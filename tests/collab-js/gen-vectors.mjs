// Gera tests/Nexus.Domain.Tests/Collab/delta-vectors.json a partir do quill-delta oficial.
//
// O servidor compõe e valida Deltas em C#, e o navegador compõe e transforma com o quill-delta
// embutido no Quill. Se as duas implementações discordarem em um único caso, o documento salvo
// no servidor deixa de ser o que os editores mostram. Os vetores fixam o comportamento de
// referência; o C# precisa reproduzir cada um exatamente.
//
// Uso: npm install && npm run vectors (a semente é fixa, então o arquivo só muda se o gerador mudar)

import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import Delta from 'quill-delta';
import { rng, randomDocument, randomChange } from './random-delta.mjs';

const SEED = 20260929;
const CASES = 250;
const r = rng(SEED);
const ops = (delta) => delta.ops;

const compose = [];
const transform = [];

// Casos escritos à mão: as bordas que o aleatório demora a sortear.
const manual = [
    [new Delta().insert('abc\n'), new Delta().retain(1).delete(1)],
    [new Delta().insert('abc\n'), new Delta().retain(4, { header: 1 })],
    [new Delta().insert('abc', { bold: true }).insert('\n'), new Delta().retain(3, { bold: null })],
    [new Delta().retain(2, { bold: null }), new Delta().retain(2, { italic: true })],
    [new Delta().insert('ab').delete(2), new Delta().retain(1).insert('x')],
    [new Delta().delete(3), new Delta().insert('y')],
    [new Delta().insert({ image: 'https://nexus.example/i.png' }).insert('\n'), new Delta().retain(1, { link: 'https://nexus.example' })],
];
for (const [a, b] of manual) {
    compose.push({ a: ops(a), b: ops(b), expected: ops(a.compose(b)) });
}

for (let i = 0; i < CASES; i++) {
    // Documento + alteração: é o que o servidor faz a cada operação aceita.
    const doc = randomDocument(r);
    const c1 = randomChange(r, doc);
    compose.push({ a: ops(doc), b: ops(c1), expected: ops(doc.compose(c1)) });

    // Duas alterações em sequência: é o que o cliente faz ao juntar o buffer.
    const after = doc.compose(c1);
    const c2 = randomChange(r, after);
    compose.push({ a: ops(c1), b: ops(c2), expected: ops(c1.compose(c2)) });

    // Duas alterações concorrentes sobre o mesmo documento, nas duas prioridades.
    const other = randomChange(r, doc);
    for (const priority of [true, false]) {
        transform.push({ a: ops(c1), b: ops(other), priority, expected: ops(c1.transform(other, priority)) });
    }
}

const target = fileURLToPath(new URL('../Nexus.Domain.Tests/Collab/delta-vectors.json', import.meta.url));
writeFileSync(target, JSON.stringify({ source: 'quill-delta@5.1.0', seed: SEED, compose, transform }) + '\n');
console.log(`${compose.length} casos de compose e ${transform.length} de transform em ${target}`);
