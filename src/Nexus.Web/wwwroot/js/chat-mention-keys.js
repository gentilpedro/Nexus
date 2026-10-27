// Teclado do compose do chat (#61). Com a lista de menções aberta, Enter e Tab escolhem a
// sugestão destacada, as setas trocam o destaque e Esc fecha a lista; só com a lista fechada o
// Enter envia. Antes, qualquer Enter enviava: "@Luc" + Enter mandava "@Luc" sem menção.
//
// Função pura (sem DOM) para rodar em Node: tests/collab-js/chat-mention-keys.test.mjs.
export function mentionKeyAction({ key, shiftKey = false, isComposing = false }, optionCount, activeIndex = 0) {
    // Enter que confirma a composição de um IME (acentos, japonês...) não é envio nem escolha.
    if (isComposing) {
        return { action: 'none' };
    }

    if (optionCount <= 0) {
        return key === 'Enter' && !shiftKey ? { action: 'send' } : { action: 'none' };
    }

    const current = Math.min(Math.max(activeIndex, 0), optionCount - 1);
    switch (key) {
        case 'ArrowDown':
            return { action: 'move', index: (current + 1) % optionCount };
        case 'ArrowUp':
            return { action: 'move', index: (current - 1 + optionCount) % optionCount };
        case 'Enter':
        case 'Tab':
            return shiftKey ? { action: 'none' } : { action: 'pick', index: current };
        case 'Escape':
            return { action: 'close' };
        default:
            return { action: 'none' };
    }
}
