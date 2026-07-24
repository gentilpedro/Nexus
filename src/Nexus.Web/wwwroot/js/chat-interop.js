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
    if (!element || element._mentionsInitialized) {
        return;
    }
    element._mentionsInitialized = true;
    element.addEventListener('input', () => {
        dotNetRef.invokeMethodAsync('OnComposeInput', element.value, element.selectionStart);
    });
}

export function insertMentionText(element, newValue, caretPosition) {
    if (!element) {
        return;
    }
    element.value = newValue;
    element.focus();
    element.setSelectionRange(caretPosition, caretPosition);
}
