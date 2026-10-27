// Teclado da lista de menções do chat (#61): testa o chat-mention-keys.js servido ao navegador.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mentionKeyAction } from '../../src/Nexus.Web/wwwroot/js/chat-mention-keys.js';

test('sem lista aberta, Enter envia e Shift+Enter quebra a linha', () => {
    assert.deepEqual(mentionKeyAction({ key: 'Enter' }, 0), { action: 'send' });
    assert.deepEqual(mentionKeyAction({ key: 'Enter', shiftKey: true }, 0), { action: 'none' });
    assert.deepEqual(mentionKeyAction({ key: 'Tab' }, 0), { action: 'none' });
    assert.deepEqual(mentionKeyAction({ key: 'ArrowDown' }, 0), { action: 'none' });
});

test('com a lista aberta, Enter escolhe a sugestão destacada em vez de enviar', () => {
    assert.deepEqual(mentionKeyAction({ key: 'Enter' }, 3), { action: 'pick', index: 0 });
    assert.deepEqual(mentionKeyAction({ key: 'Enter' }, 3, 2), { action: 'pick', index: 2 });
    assert.deepEqual(mentionKeyAction({ key: 'Tab' }, 3, 1), { action: 'pick', index: 1 });
});

test('as setas navegam e dão a volta', () => {
    assert.deepEqual(mentionKeyAction({ key: 'ArrowDown' }, 3, 0), { action: 'move', index: 1 });
    assert.deepEqual(mentionKeyAction({ key: 'ArrowDown' }, 3, 2), { action: 'move', index: 0 });
    assert.deepEqual(mentionKeyAction({ key: 'ArrowUp' }, 3, 0), { action: 'move', index: 2 });
});

test('índice fora da lista (que encolheu) cai na última sugestão', () => {
    assert.deepEqual(mentionKeyAction({ key: 'Enter' }, 2, 5), { action: 'pick', index: 1 });
});

test('Esc fecha a lista; outras teclas seguem para o campo', () => {
    assert.deepEqual(mentionKeyAction({ key: 'Escape' }, 2), { action: 'close' });
    assert.deepEqual(mentionKeyAction({ key: 'a' }, 2), { action: 'none' });
    assert.deepEqual(mentionKeyAction({ key: 'Enter', shiftKey: true }, 2), { action: 'none' });
});

test('Enter que confirma um IME não envia nem escolhe', () => {
    assert.deepEqual(mentionKeyAction({ key: 'Enter', isComposing: true }, 0), { action: 'none' });
    assert.deepEqual(mentionKeyAction({ key: 'Enter', isComposing: true }, 2), { action: 'none' });
});
