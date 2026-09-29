// Testa o doc-collab-core.js de verdade (o arquivo servido ao navegador) com o quill-delta
// oficial: vários editores, um servidor que segue a mesma regra do DocSequencer.cs, e uma rede
// que atrasa, reordena, perde e duplica mensagens. Complementa a simulação em C#
// (ConvergenceSimulationTests), que prova o protocolo; esta prova a implementação que roda no
// navegador.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import Delta from 'quill-delta';
import { CollabClient } from '../../src/Nexus.Web/wwwroot/js/doc-collab-core.js';
import { rng, randomChange } from './random-delta.mjs';

const EMPTY = () => new Delta().insert('\n');
const settle = () => new Promise((resolve) => setImmediate(resolve));

class Server {
    constructor() {
        this.snapshots = [EMPTY()];
        this.log = [];
        this.accepted = new Map();
        this.behind = 0;
        this.duplicates = 0;
    }

    get head() {
        return this.snapshots.length - 1;
    }

    get document() {
        return this.snapshots[this.head];
    }

    submit({ clientId, seq, baseRevision, change }) {
        const key = `${clientId}:${seq}`;
        if (this.accepted.has(key)) {
            this.duplicates++;
            return { status: 'ack', revision: this.accepted.get(key) };
        }
        if (baseRevision < this.head) {
            this.behind++;
            return { status: 'behind', ops: this.since(baseRevision) };
        }
        assert.equal(baseRevision, this.head, 'revisão do futuro');
        const delta = new Delta(JSON.parse(change));
        assert.ok(delta.ops.length > 0, 'cliente mandou alteração vazia');
        const baseLength = delta.ops.reduce((n, op) => n + (op.retain ?? op.delete ?? 0), 0);
        assert.ok(baseLength <= this.document.length(), 'alteração além do fim do documento');
        this.snapshots.push(this.document.compose(delta));
        const revision = this.head;
        this.log.push({ revision, clientId, seq, change });
        this.accepted.set(key, revision);
        return { status: 'ack', revision };
    }

    since(revision) {
        return this.log.filter((op) => op.revision > revision);
    }
}

class Network {
    constructor(r, server) {
        this.r = r;
        this.server = server;
        this.queue = [];
        this.timers = [];
    }

    request(handler) {
        return new Promise((resolve, reject) => {
            this.queue.push({ handler, resolve, reject });
        });
    }

    schedule(fn) {
        this.timers.push(fn);
    }

    /** Entrega uma mensagem ao acaso: às vezes perde o pedido, às vezes perde só a resposta. */
    deliverOne(lossChance) {
        if (this.queue.length === 0) return false;
        const index = this.r.int(0, this.queue.length - 1);
        const [message] = this.queue.splice(index, 1);
        if (this.r.chance(lossChance)) {
            message.reject(new Error('pedido perdido'));
            return true;
        }
        const response = message.handler();
        if (this.r.chance(lossChance)) {
            message.reject(new Error('resposta perdida')); // o servidor processou, o cliente não soube
            return true;
        }
        if (this.r.chance(0.05)) {
            message.handler(); // chegou duplicado no servidor; a segunda resposta se perde
        }
        message.resolve(response);
        return true;
    }

    fireTimers() {
        const timers = this.timers.splice(0);
        timers.forEach((fn) => fn());
    }
}

function createEditor(id, network, server) {
    const editor = { id, document: EMPTY(), client: null };
    editor.client = new CollabClient({
        Delta,
        clientId: `cliente-${id}`,
        revision: 0,
        transport: {
            submit: (request) => network.request(() => server.submit(request)),
            catchUp: (since) => network.request(() => ({ status: 'ok', ops: server.since(since) })),
        },
        onRemote: (delta) => {
            editor.document = editor.document.compose(delta);
        },
        setTimeout: (fn) => network.schedule(fn),
    });
    return editor;
}

function assertInvariant(editor, server) {
    const { client } = editor;
    let expected = server.snapshots[client.revision];
    if (client.inFlight) {
        assert.equal(client.inFlight.baseRevision, client.revision);
        expected = expected.compose(client.inFlight.change);
    }
    if (client.buffer) expected = expected.compose(client.buffer);
    assert.deepEqual(editor.document.ops, expected.ops, `invariante quebrado no editor ${editor.id}`);
}

async function runScenario(seed) {
    const r = rng(seed);
    const server = new Server();
    const network = new Network(r, server);
    const editors = Array.from({ length: r.int(2, 4) }, (_, i) => createEditor(i + 1, network, server));

    for (let step = 0; step < 250; step++) {
        const editor = r.pick(editors);
        const roll = r.int(0, 9);
        if (roll <= 2) {
            const change = randomChange(r, editor.document);
            editor.document = editor.document.compose(change);
            editor.client.localChange(change);
        } else if (roll <= 6) {
            network.deliverOne(0.1);
        } else if (roll === 7) {
            editor.client.notify(server.head); // aviso do servidor de que o documento mudou
        } else if (roll === 8) {
            network.fireTimers();
        } else {
            editor.client.resume(); // reconectou
        }
        await settle();
        for (const e of editors) assertInvariant(e, server);
    }

    // Rede estabiliza: entrega tudo, sem perda, até ninguém ter pendência.
    for (let round = 0; round < 500; round++) {
        while (network.deliverOne(0)) await settle();
        network.fireTimers();
        await settle();
        if (editors.every((e) => !e.client.hasPending && e.client.revision === server.head) && network.queue.length === 0) break;
        // Só o que o servidor faria: avisar que o documento mudou. Nada de chamar resume() aqui —
        // isso destravaria por fora um cliente que tivesse ficado parado, e o teste de vivacidade
        // passaria sem provar nada.
        for (const e of editors) {
            e.client.notify(server.head);
        }
        await settle();
    }

    for (const e of editors) {
        assert.equal(e.client.fatal, null, `editor ${e.id} caiu em erro: ${JSON.stringify(e.client.fatal)}`);
        assert.equal(e.client.revision, server.head, `editor ${e.id} não alcançou o servidor`);
        assert.deepEqual(e.document.ops, server.document.ops, `editor ${e.id} divergiu`);
    }
    // Nenhuma alteração aplicada duas vezes.
    const keys = server.log.map((op) => `${op.clientId}:${op.seq}`);
    assert.equal(new Set(keys).size, keys.length);

    return { accepted: server.log.length, behind: server.behind, duplicates: server.duplicates };
}

test('editores concorrentes numa rede instável convergem para o documento do servidor', async () => {
    const totals = { accepted: 0, behind: 0, duplicates: 0 };
    for (let seed = 1; seed <= 40; seed++) {
        const stats = await runScenario(seed);
        totals.accepted += stats.accepted;
        totals.behind += stats.behind;
        totals.duplicates += stats.duplicates;
    }
    // Os caminhos difíceis precisam ter acontecido, senão a convergência seria trivial.
    assert.ok(totals.accepted > 300, `aceitas: ${totals.accepted}`);
    assert.ok(totals.behind > 100, `atrasados: ${totals.behind}`);
    assert.ok(totals.duplicates > 20, `reenvios reconhecidos: ${totals.duplicates}`);
});

test('as pendências salvas numa recarga voltam e são enviadas sem duplicar', async () => {
    const r = rng(7);
    const server = new Server();
    const network = new Network(r, server);
    const ana = createEditor(1, network, server);

    const change = new Delta().insert('texto não sincronizado');
    ana.document = ana.document.compose(change);
    ana.client.localChange(change);
    const saved = ana.client.pendingState();
    const savedDocument = ana.document;
    ana.client.stop(); // a página recarregou antes da resposta

    // Enquanto isso, o pedido original chegou ao servidor e foi aceito.
    network.deliverOne(0);
    await settle();

    const restored = { id: 2, document: savedDocument, client: null };
    restored.client = new CollabClient({
        Delta,
        clientId: saved.clientId,
        revision: saved.revision,
        pending: saved,
        transport: {
            submit: (request) => network.request(() => server.submit(request)),
            catchUp: (since) => network.request(() => ({ status: 'ok', ops: server.since(since) })),
        },
        onRemote: (delta) => {
            restored.document = restored.document.compose(delta);
        },
        setTimeout: (fn) => network.schedule(fn),
    });
    restored.client.resume();
    while (network.deliverOne(0)) await settle();

    assert.equal(server.log.length, 1, 'a alteração foi aplicada duas vezes');
    assert.equal(restored.client.hasPending, false);
    assert.deepEqual(restored.document.ops, server.document.ops);
});
