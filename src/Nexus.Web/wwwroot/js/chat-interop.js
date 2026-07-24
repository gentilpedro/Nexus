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
