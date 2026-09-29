// Geração aleatória e reprodutível de documentos e alterações no formato Delta do Quill.
// Compartilhado pelo gerador de vetores e pelos testes do cliente colaborativo.

import Delta from 'quill-delta';

/** PRNG pequeno e determinístico (mulberry32): a mesma semente gera os mesmos casos. */
export function rng(seed) {
    let a = seed >>> 0;
    const next = () => {
        a = (a + 0x6d2b79f5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
    const int = (min, max) => min + Math.floor(next() * (max - min + 1));
    const pick = (items) => items[int(0, items.length - 1)];
    const chance = (p) => next() < p;
    return { next, int, pick, chance };
}

const WORDS = ['a', 'ó', 'ção', 'Nexus', 'doc', ' ', '  ', 'x', 'olá', '😀', '\n'];

// Só formatos que a política do servidor aceita, com valores válidos, mais null (remoção).
function randomAttributes(r, { allowNull }) {
    const attrs = {};
    const count = r.int(0, 2);
    for (let i = 0; i < count; i++) {
        const key = r.pick(['bold', 'italic', 'underline', 'link', 'header', 'list', 'color', 'align']);
        let value;
        switch (key) {
            case 'bold':
            case 'italic':
            case 'underline':
                value = true;
                break;
            case 'link':
                value = r.pick(['https://nexus.example/a', 'https://nexus.example/b']);
                break;
            case 'header':
                value = r.int(1, 3);
                break;
            case 'list':
                value = r.pick(['ordered', 'bullet']);
                break;
            case 'color':
                value = r.pick(['#5b5ceb', '#e11d48']);
                break;
            case 'align':
                value = r.pick(['center', 'right']);
                break;
        }
        if (allowNull && r.chance(0.3)) {
            value = null;
        }
        attrs[key] = value;
    }
    return Object.keys(attrs).length > 0 ? attrs : undefined;
}

function randomText(r) {
    let text = '';
    const parts = r.int(1, 3);
    for (let i = 0; i < parts; i++) {
        text += r.pick(WORDS);
    }
    return text;
}

/** Documento válido: só inserts, terminando em quebra de linha. */
export function randomDocument(r) {
    const doc = new Delta();
    const parts = r.int(0, 5);
    for (let i = 0; i < parts; i++) {
        if (r.chance(0.1)) {
            doc.insert({ image: 'https://nexus.example/img.png' }, randomAttributes(r, { allowNull: false }));
        } else {
            doc.insert(randomText(r), randomAttributes(r, { allowNull: false }));
        }
    }
    doc.insert('\n', r.chance(0.3) ? { header: r.int(1, 2) } : undefined);
    return doc;
}

/** Texto plano do documento, com embed ocupando uma posição, para achar limites de caractere. */
function plainText(doc) {
    return doc.ops.map((op) => (typeof op.insert === 'string' ? op.insert : '￼')).join('');
}

/**
 * Alteração válida sobre `doc`: nunca apaga a quebra de linha final nem insere depois dela, e
 * nunca para no meio de um emoji (entre as metades do par UTF-16) — as mesmas garantias do Quill,
 * cujo cursor não para ali.
 */
export function randomChange(r, doc) {
    const text = plainText(doc);
    const length = text.length;
    const boundary = (pos) => {
        const code = text.charCodeAt(pos);
        return pos > 0 && pos < length && code >= 0xdc00 && code <= 0xdfff ? pos + 1 : pos;
    };
    const change = new Delta();
    let cursor = 0;
    const editable = length - 1; // a última posição é a quebra de linha final
    const steps = r.int(1, 3);
    for (let i = 0; i < steps && cursor <= editable; i++) {
        const skip = boundary(cursor + r.int(0, Math.max(0, editable - cursor))) - cursor;
        const kind = r.pick(['insert', 'delete', 'format', 'insert']);
        if (kind === 'format') {
            // Formatar pode incluir a quebra de linha final (ex.: virar título).
            const size = boundary(cursor + skip + r.int(1, Math.max(1, length - cursor - skip))) - cursor - skip;
            if (cursor + skip + size > length) {
                continue;
            }
            change.retain(skip);
            change.retain(size, randomAttributes(r, { allowNull: true }) ?? { bold: true });
            cursor += skip + size;
            continue;
        }
        change.retain(skip);
        cursor += skip;
        if (kind === 'insert') {
            if (r.chance(0.1)) {
                change.insert({ image: 'https://nexus.example/img.png' });
            } else {
                change.insert(randomText(r), randomAttributes(r, { allowNull: false }));
            }
        } else {
            const room = editable - cursor;
            if (room <= 0) {
                continue;
            }
            const size = Math.min(boundary(cursor + r.int(1, Math.min(room, 4))) - cursor, room);
            change.delete(size);
            cursor += size;
        }
    }
    const chopped = change.chop();
    return chopped.ops.length > 0 ? chopped : new Delta().insert('!');
}
