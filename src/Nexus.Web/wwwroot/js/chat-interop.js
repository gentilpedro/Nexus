import { mentionKeyAction } from './chat-mention-keys.js';

export function scrollToBottom(element) {
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
}

export function getScrollMetrics(element) {
    return element ? { scrollHeight: element.scrollHeight, scrollTop: element.scrollTop } : null;
}

// After prepending older messages above the currently visible ones, the browser keeps the
// scroll position anchored to the top of the container by default — visually this yanks
// whatever the user was reading down past the bottom of the screen. Restoring scrollTop
// relative to how much taller the container just got keeps the same content in view.
export function restoreScrollAfterPrepend(element, previousScrollHeight, previousScrollTop) {
    if (element) {
        element.scrollTop = element.scrollHeight - previousScrollHeight + previousScrollTop;
    }
}

// One native listener sending value + caret position together on every keystroke, instead of
// Blazor's @bind (one round trip for the value) plus a separate JS interop call for the caret
// (a second round trip) — same "JS owns the event, calls back into .NET" shape as
// board-interop.js's Sortable integration.
export function initComposeMentions(element, dotNetRef) {
    // An @ref whose element hasn't rendered yet still arrives here as a truthy object, so a
    // plain null check isn't enough — without the addEventListener probe that case throws,
    // killing the circuit and leaving mentions dead. Returning quietly instead lets the
    // caller retry on a later render, once the compose box actually exists.
    if (!element || typeof element.addEventListener !== 'function' || element._mentionsInitialized) {
        return;
    }
    element._mentionsInitialized = true;
    element.addEventListener('input', () => {
        // New text means a new suggestion list: the highlight goes back to the first option.
        element._mentionIndex = 0;
        highlightMention(mentionOptions(element), -1);
        dotNetRef.invokeMethodAsync('OnComposeInput', element.value, element.selectionStart);
    });

    // <textarea> never submits its form on Enter by itself — only <input> does that natively.
    // Shift+Enter still falls through to the default behavior (a newline), matching the usual
    // chat-app convention. While the mention list is open, the keys drive the list instead
    // (see chat-mention-keys.js).
    element.addEventListener('keydown', (e) => {
        const options = mentionOptions(element);
        const result = mentionKeyAction(e, options.length, element._mentionIndex ?? 0);
        switch (result.action) {
            case 'send':
                e.preventDefault();
                element.closest('form')?.requestSubmit();
                break;
            case 'move':
                e.preventDefault();
                element._mentionIndex = result.index;
                highlightMention(options, result.index);
                break;
            case 'pick':
                e.preventDefault();
                element._mentionIndex = 0;
                options[result.index].click();
                break;
            case 'close':
                // Without stopPropagation, Esc would also reach an enclosing <dialog>.
                e.preventDefault();
                e.stopPropagation();
                dotNetRef.invokeMethodAsync('DismissMentions');
                break;
        }
    });
}

function mentionOptions(element) {
    const dropdown = element.parentElement?.querySelector('.chat-mention-dropdown');
    return dropdown ? [...dropdown.querySelectorAll('[role="option"]')] : [];
}

// index -1 clears the highlight; CSS then marks the first option, which Enter picks.
function highlightMention(options, index) {
    options.forEach((option, i) => {
        option.classList.toggle('is-active', i === index);
        option.setAttribute('aria-selected', i === index ? 'true' : 'false');
    });
    options[index]?.scrollIntoView({ block: 'nearest' });
}

// Sets the textarea's DOM value directly. Needed anywhere the C# side wants to force what's
// on screen (inserting a mention token, clearing the box after a send) — Blazor's own
// re-render only pushes "value" down when its *previous* rendered value differs from the new
// one, but this textarea is deliberately uncontrolled (see ChatComposeBox.razor) so its actual
// on-screen content moves via this function, not via Blazor's diff, and can otherwise go stale.
export function setComposeValue(element, newValue, caretPosition) {
    if (!element) {
        return;
    }
    element.value = newValue;
    element.focus();
    element.setSelectionRange(caretPosition, caretPosition);
}
