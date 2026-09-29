// Liga o núcleo colaborativo (doc-collab-core.js) ao Quill e ao circuito do Blazor.
//
// O componente DocCollabEditor.razor chama start/notify/finish/stop; este módulo chama de volta
// Submit/CatchUp/SaveHtml/Reload/OnSyncState no componente. Aqui ficam só as partes que dependem
// do navegador: o editor, a persistência das pendências na aba e o HTML do modo leitura.

import { CollabClient, SyncStatus } from './doc-collab-core.js';

// Precisa bater com DocDeltaPolicy.AllowedFormats no servidor: um formato fora da lista seria
// recusado lá. Com a lista aqui, o Quill descarta o formato já na colagem.
const FORMATS = [
    'bold', 'italic', 'underline', 'strike', 'code', 'link', 'color', 'background', 'script',
    'header', 'list', 'blockquote', 'code-block', 'indent', 'align', 'image',
];

const STORAGE_PREFIX = 'nexus-doc-pending:';
const HTML_DEBOUNCE_MS = 1200;
const PERSIST_DEBOUNCE_MS = 300;

// Com a rede caída, o WebSocket do circuito nem sempre fecha na hora: a chamada fica pendurada
// sem erro, e a tela mostraria "Salvando…" para sempre. Passado esse tempo, a chamada conta como
// falha de rede — o núcleo marca "sem conexão" e reenvia depois; se a original ainda chegar, o
// servidor reconhece o reenvio pelo número da alteração.
const CALL_TIMEOUT_MS = 8000;

function withTimeout(promise) {
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => reject(new Error('sem resposta do servidor')), CALL_TIMEOUT_MS);
        promise.then(
            (value) => { clearTimeout(timer); resolve(value); },
            (error) => { clearTimeout(timer); reject(error); });
    });
}

const sessions = new Map(); // elementId → sessão

function newClientId() {
    return crypto.randomUUID();
}

// sessionStorage, e não localStorage: sobrevive a uma recarga da mesma aba, que é o caso a
// cobrir, mas não é compartilhado entre abas. Duas abas do mesmo documento restaurando as mesmas
// pendências usariam o mesmo clientId e seq para alterações diferentes, e o servidor trataria a
// segunda como reenvio da primeira.
function readPending(docId) {
    try {
        const raw = sessionStorage.getItem(STORAGE_PREFIX + docId);
        return raw ? JSON.parse(raw) : null;
    } catch {
        return null;
    }
}

function writePending(session) {
    try {
        const pending = session.client?.pendingState();
        if (pending) {
            pending.document = session.quill.getContents().ops;
            sessionStorage.setItem(STORAGE_PREFIX + session.docId, JSON.stringify(pending));
        } else {
            sessionStorage.removeItem(STORAGE_PREFIX + session.docId);
        }
    } catch {
        // Armazenamento cheio ou bloqueado: segue sem a proteção contra recarga.
    }
}

/** Há alterações deste documento guardadas na aba, esperando para ir ao servidor? */
export function hasPending(docId) {
    const saved = readPending(docId);
    return Boolean(saved && (saved.inFlight || saved.buffer));
}

export async function start(elementId, dotNetRef, options) {
    // Outra instância do editor para o mesmo documento nesta aba (a página foi recriada): a
    // antiga grava as pendências e para, e a nova as restaura.
    for (const other of sessions.values()) {
        if (other.docId === options.docId) {
            stopSession(other);
        }
    }

    const Delta = Quill.import('delta');
    const quill = new Quill('#' + elementId, {
        theme: 'snow',
        formats: FORMATS,
        // userOnly: desfazer volta só o que a própria pessoa fez, não o texto dos outros.
        modules: { history: { userOnly: true } },
    });

    // Imagem colada vira data: URL de centenas de KB; o servidor só aceita imagem por endereço.
    quill.clipboard.addMatcher('IMG', (node, delta) =>
        /^https?:/i.test(node.getAttribute('src') || '') ? delta : new Delta());

    const session = {
        elementId,
        docId: options.docId,
        dotNetRef,
        quill,
        Delta,
        client: null,
        htmlRevision: 0,
        htmlTimer: null,
        persistTimer: null,
        lastReported: '',
        textHandler: null,
        stopped: false,
        listeners: [],
    };
    sessions.set(elementId, session);

    listen(session, window, 'pagehide', () => writePending(session));
    listen(session, window, 'beforeunload', (event) => {
        writePending(session);
        if (session.client?.hasPending) {
            // O navegador pergunta antes de sair com texto não salvo.
            event.preventDefault();
            event.returnValue = '';
        }
    });
    listen(session, window, 'online', () => session.client?.resume());
    const reconnect = document.getElementById('components-reconnect-modal');
    if (reconnect) {
        listen(session, reconnect, 'components-reconnect-state-changed', (event) => {
            if (event.detail?.state === 'hide') session.client?.resume();
        });
    }

    await begin(session, options.snapshot);
}

function listen(session, target, type, handler) {
    target.addEventListener(type, handler);
    session.listeners.push(() => target.removeEventListener(type, handler));
}

async function begin(session, snapshot) {
    const { quill, Delta } = session;
    quill.disable();
    report(session, SyncStatus.Syncing, 0, null);

    if (!snapshot) {
        report(session, SyncStatus.Error, 0, 'Documento não encontrado.');
        return;
    }

    let clientId = newClientId();

    if (snapshot.needsSeed) {
        snapshot = await seed(session, snapshot, clientId);
        if (!snapshot || session.stopped) return;
    }

    const serverDocument = snapshot.document ? new Delta(JSON.parse(snapshot.document)) : new Delta().insert('\n');
    let localDocument = serverDocument;
    let revision = snapshot.revision;
    let pending = null;

    // Pendências de antes de uma recarga desta aba: o documento local era o do servidor naquela
    // revisão mais as pendências, então basta recolocá-lo e seguir o protocolo normal — o
    // catch-up traz o que mudou desde então e rebaseia as pendências.
    const saved = readPending(session.docId);
    if (saved && saved.document && saved.revision <= snapshot.revision && (saved.inFlight || saved.buffer)) {
        localDocument = new Delta(saved.document);
        revision = saved.revision;
        pending = saved;
        clientId = saved.clientId;
    }

    quill.setContents(localDocument, 'silent');
    quill.history.clear();
    session.htmlRevision = snapshot.revision;

    const client = new CollabClient({
        Delta,
        clientId,
        revision,
        pending,
        transport: {
            submit: (request) => withTimeout(session.dotNetRef.invokeMethodAsync('Submit', request.clientId, request.seq, request.baseRevision, request.change, request.isSeed)),
            catchUp: (since) => withTimeout(session.dotNetRef.invokeMethodAsync('CatchUp', since)),
        },
        onRemote: (delta) => quill.updateContents(delta, 'api'),
        onChange: () => stateChanged(session),
    });
    session.client = client;

    if (session.textHandler) {
        quill.off('text-change', session.textHandler);
    }
    session.textHandler = (delta, _old, source) => {
        if (source === 'user') {
            client.localChange(delta);
        }
    };
    quill.on('text-change', session.textHandler);

    quill.enable();
    stateChanged(session);

    // O documento pode ter andado entre o carregamento e agora; com pendências, reenvia também.
    if (pending) {
        client.resume();
    } else {
        client.catchUp();
    }
}

/**
 * Documento de antes da edição colaborativa: converte o HTML no próprio Quill e manda como
 * primeira revisão. Se outra pessoa semeou antes, descarta a conversão e usa a dela.
 */
async function seed(session, snapshot, clientId) {
    const { quill } = session;
    quill.setContents(quill.clipboard.convert({ html: snapshot.html ?? '' }), 'silent');
    const document = quill.getContents();

    let result;
    try {
        result = await session.dotNetRef.invokeMethodAsync('Submit', clientId, 0, 0, JSON.stringify(document.ops), true);
    } catch {
        report(session, SyncStatus.Offline, 0, 'Sem conexão para abrir o documento.');
        return null;
    }

    if (result.status === 'ack') {
        return { revision: result.revision, document: JSON.stringify(document.ops), html: null, needsSeed: false };
    }
    if (result.status === 'seeded' || result.status === 'resync') {
        const fresh = await session.dotNetRef.invokeMethodAsync('Reload');
        if (fresh && !fresh.needsSeed) return fresh;
    }
    report(session, SyncStatus.Error, 0, result.error ?? 'Não foi possível preparar o documento para edição.');
    return null;
}

function stateChanged(session) {
    const client = session.client;
    if (!client || session.stopped) return;

    schedulePersist(session);

    if (client.fatal) {
        handleFatal(session);
        return;
    }

    report(session, client.status, client.pendingCount, null);

    if (client.status === SyncStatus.Synced && client.revision > session.htmlRevision) {
        scheduleHtml(session);
    }
}

async function handleFatal(session) {
    const { client, quill } = session;
    quill.disable();

    // Sem nada pendente, não há o que perder: recomeça do documento atual do servidor.
    if (!client.hasPending && (client.fatal.kind === 'resync' || client.fatal.kind === 'seeded')) {
        client.stop();
        session.client = null;
        const fresh = await session.dotNetRef.invokeMethodAsync('Reload');
        if (!session.stopped) {
            await begin(session, fresh);
        }
        return;
    }

    // Com pendências que não dá mais para juntar: mostra o texto que a pessoa tinha, para ela
    // copiar, em vez de descartar em silêncio.
    writePending(session);
    report(session, SyncStatus.Error, client.pendingCount, client.fatal.message, quill.getText());
}

function report(session, status, pending, message, recovery = null) {
    const key = `${status}|${pending}|${message}|${recovery ? recovery.length : ''}`;
    if (key === session.lastReported || session.stopped) return;
    session.lastReported = key;
    session.dotNetRef.invokeMethodAsync('OnSyncState', status, pending, message, recovery).catch(() => {});
}

function schedulePersist(session) {
    clearTimeout(session.persistTimer);
    session.persistTimer = setTimeout(() => writePending(session), PERSIST_DEBOUNCE_MS);
}

function scheduleHtml(session) {
    clearTimeout(session.htmlTimer);
    session.htmlTimer = setTimeout(() => saveHtml(session), HTML_DEBOUNCE_MS);
}

/**
 * HTML do modo leitura. getSemanticHTML e não root.innerHTML: o innerHTML do Quill 2 escreve
 * toda lista como <ol> com data-list e marcadores de interface, e o sanitizador remove o
 * data-list — lista com marcadores virava lista numerada na leitura. Mas o 2.0.3 troca todo
 * espaço por &nbsp;, o que impede a quebra de linha; volta a ser espaço comum (dentro de <pre>,
 * espaço comum já é preservado).
 */
function readModeHtml(quill) {
    return quill.getSemanticHTML().replace(/&nbsp;/g, ' ');
}

/** Grava o HTML do modo leitura, só quando o editor está em dia com o servidor. */
async function saveHtml(session) {
    const client = session.client;
    if (!client || session.stopped || client.status !== SyncStatus.Synced) return false;
    const revision = client.revision;
    if (revision <= session.htmlRevision) return true;
    try {
        const saved = await session.dotNetRef.invokeMethodAsync('SaveHtml', revision, readModeHtml(session.quill));
        if (saved) session.htmlRevision = Math.max(session.htmlRevision, revision);
        return true;
    } catch {
        return false;
    }
}

export function notify(elementId, revision) {
    sessions.get(elementId)?.client?.notify(revision);
}

/**
 * "Concluir edição": espera as pendências chegarem ao servidor (até `timeoutMs`) e grava o HTML
 * do modo leitura. Devolve false se ainda houver algo sem sincronizar.
 */
export async function finish(elementId, timeoutMs) {
    const session = sessions.get(elementId);
    const client = session?.client;
    if (!client) return true;
    if (client.fatal) return false;

    client.flush();
    const deadline = Date.now() + timeoutMs;
    while (client.hasPending && Date.now() < deadline && !client.fatal) {
        await new Promise((resolve) => setTimeout(resolve, 100));
    }
    if (client.hasPending || client.fatal) return false;

    clearTimeout(session.htmlTimer);
    return await saveHtml(session);
}

export function stop(elementId) {
    const session = sessions.get(elementId);
    if (session) stopSession(session);
}

function stopSession(session) {
    writePending(session);
    session.stopped = true;
    session.client?.stop();
    clearTimeout(session.htmlTimer);
    clearTimeout(session.persistTimer);
    session.listeners.forEach((remove) => remove());

    // O Quill põe a barra de ferramentas como irmã do elemento, fora da árvore do Blazor; sem
    // remover aqui, ela fica órfã na página (ver doc-editor-interop.js).
    session.quill.getModule('toolbar')?.container?.remove();
    sessions.delete(session.elementId);
}
