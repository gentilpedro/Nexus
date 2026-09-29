// Núcleo do cliente da edição colaborativa dos Docs (#32).
//
// Sem Quill, sem DOM e sem Blazor: recebe a classe Delta e um "transporte" por injeção, para que
// o mesmo código rode no navegador e nos testes em Node (tests/collab-js), onde é verificado
// contra o quill-delta oficial numa rede simulada que atrasa, perde e duplica mensagens.
//
// O protocolo (ver DocSequencer.cs): o servidor só aceita uma alteração escrita sobre a revisão
// atual. Este cliente mantém no máximo UMA alteração em voo; o que for digitado enquanto isso vai
// para um buffer. Quando chega uma operação de outra pessoa, as duas pendências são transformadas
// sobre ela — a operação remota tem prioridade, porque o servidor já a ordenou antes — e a versão
// transformada da operação remota é aplicada no editor. Invariante mantido o tempo todo:
//
//     documento no editor = documento do servidor na `revision` ∘ inFlight ∘ buffer
//
// É esse invariante que faz todos convergirem, e é o que os testes checam a cada passo.

export const SyncStatus = Object.freeze({
    Synced: 'synced',
    Syncing: 'syncing',
    Offline: 'offline',
    Error: 'error',
});

// Esperas entre novas tentativas depois de falha de rede, "ocupado" ou limite de taxa.
const RETRY_DELAYS = [500, 1000, 2000, 4000, 8000, 15000];

export class CollabClient {
    /**
     * @param {object} options
     * @param {typeof import('quill-delta').default} options.Delta
     * @param {{ submit: Function, catchUp: Function }} options.transport
     *   submit({ clientId, seq, baseRevision, change, isSeed }) → Promise<{ status, revision?, ops?, error? }>
     *   catchUp(sinceRevision) → Promise<{ status, ops? }>
     * @param {string} options.clientId
     * @param {number} options.revision  revisão do servidor que o documento local reflete
     * @param {(delta) => void} options.onRemote  aplica no editor uma alteração vinda de outra pessoa
     * @param {() => void} [options.onChange]  estado mudou (status, pendências, revisão)
     * @param {object} [options.pending]  pendências restauradas de uma sessão anterior desta aba
     * @param {(fn: Function, ms: number) => any} [options.setTimeout]
     */
    constructor({ Delta, transport, clientId, revision, onRemote, onChange, pending, setTimeout: schedule }) {
        this.Delta = Delta;
        this.transport = transport;
        this.clientId = clientId;
        this.revision = revision;
        this.onRemote = onRemote;
        this.onChange = onChange ?? (() => {});
        this.schedule = schedule ?? ((fn, ms) => setTimeout(fn, ms));

        this.inFlight = null; // { seq, change: Delta, baseRevision }
        this.buffer = null; // Delta
        this.nextSeq = 1;
        this.sending = false;
        this.catchingUp = false;
        this.catchUpAgain = false;
        this.offline = false;
        this.fatal = null; // { kind: 'resync' | 'rejected' | 'forbidden' | 'seeded', message }
        this.retryIndex = 0;
        this.retryTimer = null;
        this.stopped = false;

        if (pending) {
            this.nextSeq = pending.nextSeq;
            this.inFlight = pending.inFlight
                ? { seq: pending.inFlight.seq, change: new Delta(pending.inFlight.change), baseRevision: pending.inFlight.baseRevision }
                : null;
            this.buffer = pending.buffer ? new Delta(pending.buffer) : null;
        }
    }

    get status() {
        if (this.fatal) return SyncStatus.Error;
        if (this.offline) return SyncStatus.Offline;
        if (this.inFlight || this.buffer) return SyncStatus.Syncing;
        return SyncStatus.Synced;
    }

    /** Número de alterações ainda não confirmadas pelo servidor (para o indicador na tela). */
    get pendingCount() {
        return (this.inFlight ? 1 : 0) + (this.buffer ? 1 : 0);
    }

    get hasPending() {
        return this.inFlight !== null || this.buffer !== null;
    }

    /** O que precisa sobreviver a uma recarga da página para nada se perder. */
    pendingState() {
        if (!this.hasPending) return null;
        return {
            clientId: this.clientId,
            revision: this.revision,
            nextSeq: this.nextSeq,
            inFlight: this.inFlight
                ? { seq: this.inFlight.seq, change: this.inFlight.change.ops, baseRevision: this.inFlight.baseRevision }
                : null,
            buffer: this.buffer ? this.buffer.ops : null,
        };
    }

    /** Alteração feita pela pessoa no editor (já aplicada nele). */
    localChange(delta) {
        if (this.stopped || this.fatal) return;
        this.buffer = this.buffer ? this.buffer.compose(delta) : delta;
        if (this.buffer.ops.length === 0) {
            this.buffer = null; // ex.: digitou e apagou antes de enviar
        }
        this.changed();
        this.flush();
    }

    /** O servidor avisou que o documento chegou à `revision`. */
    notify(revision) {
        if (revision > this.revision) {
            this.catchUp();
        }
    }

    /** Recomeça depois de uma queda de conexão: reenvia o que está em voo e busca o que perdeu. */
    resume() {
        if (this.stopped || this.fatal) return;
        this.clearRetry();
        this.retryIndex = 0;
        this.send();
        this.catchUp();
    }

    stop() {
        this.stopped = true;
        this.clearRetry();
    }

    flush() {
        if (this.inFlight || !this.buffer || this.stopped || this.fatal) return;
        this.inFlight = { seq: this.nextSeq++, change: this.buffer, baseRevision: this.revision };
        this.buffer = null;
        this.changed();
        this.send();
    }

    async send() {
        if (this.sending || !this.inFlight || this.stopped || this.fatal) return;
        this.sending = true;
        const sent = this.inFlight;
        const sentBase = sent.baseRevision;
        let result;
        try {
            result = await this.transport.submit({
                clientId: this.clientId,
                seq: sent.seq,
                baseRevision: sentBase,
                change: JSON.stringify(sent.change.ops),
                isSeed: false,
            });
        } catch {
            this.sending = false;
            this.setOffline(true);
            this.retryLater();
            return;
        }
        this.sending = false;
        if (this.stopped) return;
        this.setOffline(false);
        this.handleSubmit(result, sent.seq);

        // Enquanto esperávamos, um catch-up pode ter confirmado esta alteração pelo log e o
        // buffer ter virado uma nova alteração em voo — cujo send() voltou na hora, porque este
        // ainda estava em andamento. A resposta que acabou de chegar é da antiga e não dispara
        // nada; sem isto a nova ficaria parada para sempre, com a tela em "Salvando…".
        if (this.inFlight && !this.sending && (this.inFlight.seq !== sent.seq || this.inFlight.baseRevision !== sentBase)) {
            this.send();
        }
    }

    handleSubmit(result, seq) {
        switch (result.status) {
            case 'ack':
                this.retryIndex = 0;
                // Resposta de algo que o log já confirmou (a operação apareceu num catch-up antes).
                if (!this.inFlight || this.inFlight.seq !== seq) {
                    break;
                }
                if (result.revision !== this.revision + 1) {
                    // O servidor só aceita sobre a revisão em que a alteração foi escrita; outro
                    // número quer dizer que o estado local não é o que achamos. Não dá para seguir.
                    this.fail('resync', 'O documento saiu de sincronia.');
                    return;
                }
                this.revision = result.revision;
                this.inFlight = null;
                this.changed();
                this.flush();
                break;

            case 'behind':
                this.retryIndex = 0;
                this.applyOps(result.ops ?? []);
                // Reenvia a pendência já transformada sobre o que chegou (mesmo número).
                this.send();
                this.flush();
                break;

            case 'busy':
            case 'ratelimited':
                this.retryLater();
                break;

            case 'resync':
            case 'seeded':
            case 'rejected':
            case 'forbidden':
            default:
                this.fail(result.status, result.error ?? 'Não foi possível salvar a alteração.');
                break;
        }
    }

    async catchUp() {
        if (this.stopped || this.fatal) return;
        if (this.catchingUp) {
            this.catchUpAgain = true; // um aviso chegou no meio: busca de novo no fim
            return;
        }
        this.catchingUp = true;
        try {
            do {
                this.catchUpAgain = false;
                let result;
                try {
                    result = await this.transport.catchUp(this.revision);
                } catch {
                    this.setOffline(true);
                    this.retryLater();
                    return;
                }
                if (this.stopped) return;
                this.setOffline(false);
                if (result.status !== 'ok') {
                    this.fail('resync', 'O histórico que faltava já foi descartado.');
                    return;
                }
                const ops = result.ops ?? [];
                this.applyOps(ops);
                // Resposta cheia: pode haver mais depois do limite por resposta.
                if (ops.length >= 500) this.catchUpAgain = true;
            } while (this.catchUpAgain && !this.stopped && !this.fatal);
        } finally {
            this.catchingUp = false;
        }
        this.send();
        this.flush();
    }

    /** Aplica, em ordem, as operações do log que ainda não foram vistas. */
    applyOps(ops) {
        const sorted = [...ops].sort((a, b) => a.revision - b.revision);
        let changed = false;
        for (const op of sorted) {
            if (op.revision <= this.revision) continue;
            if (op.revision !== this.revision + 1) {
                // Buraco (resposta fora de ordem): o resto vem no próximo catch-up.
                this.catchUpAgain = true;
                break;
            }

            if (op.clientId === this.clientId && this.inFlight && op.seq === this.inFlight.seq) {
                // A nossa própria alteração, vista no log antes da resposta do envio.
                this.revision = op.revision;
                this.inFlight = null;
                changed = true;
                continue;
            }

            let remote = new this.Delta(JSON.parse(op.change));
            if (this.inFlight) {
                const mine = this.inFlight.change;
                const rebased = remote.transform(mine, true);
                remote = mine.transform(remote, false);
                // Pode sumir no rebase (apagava o que a outra pessoa já apagou). Vazia não vai.
                this.inFlight = rebased.ops.length === 0 ? null : { ...this.inFlight, change: rebased, baseRevision: op.revision };
            }
            if (this.buffer) {
                const mine = this.buffer;
                const rebased = remote.transform(mine, true);
                remote = mine.transform(remote, false);
                this.buffer = rebased.ops.length === 0 ? null : rebased;
            }

            this.revision = op.revision;
            if (remote.ops.length > 0) {
                this.onRemote(remote);
            }
            changed = true;
        }
        if (changed) this.changed();
    }

    fail(kind, message) {
        this.fatal = { kind, message };
        this.clearRetry();
        this.changed();
    }

    setOffline(offline) {
        if (this.offline !== offline) {
            this.offline = offline;
            this.changed();
        }
    }

    /**
     * Agenda uma nova tentativa com espera crescente. Uma só por vez, e ela refaz as duas coisas
     * (reenviar e buscar o que falta): assim uma falha de catch-up durante a espera de um reenvio
     * não fica esquecida.
     */
    retryLater() {
        if (this.stopped || this.retryTimer) return;
        const delay = RETRY_DELAYS[Math.min(this.retryIndex, RETRY_DELAYS.length - 1)];
        this.retryIndex++;
        const timer = { cancelled: false };
        this.retryTimer = timer;
        this.schedule(() => {
            if (timer.cancelled) return;
            this.retryTimer = null;
            this.send();
            this.catchUp();
        }, delay);
    }

    clearRetry() {
        if (this.retryTimer) {
            this.retryTimer.cancelled = true;
            this.retryTimer = null;
        }
    }

    changed() {
        this.onChange();
    }
}
