const instances = new Map();

export function initEditor(elementId, initialHtml) {
    const quill = new Quill('#' + elementId, { theme: 'snow' });
    if (initialHtml) {
        quill.clipboard.dangerouslyPasteHTML(initialHtml);
    }
    instances.set(elementId, quill);
}

export function getContent(elementId) {
    const quill = instances.get(elementId);
    return quill ? quill.root.innerHTML : '';
}

export function destroyEditor(elementId) {
    // Quill inserts its toolbar as a sibling BEFORE the target element, not inside it —
    // Blazor never rendered that sibling, so when it later removes the target div (the
    // @if block around it turning false) the orphaned toolbar is left behind in the DOM.
    // Removing it explicitly here is what actually prevents the duplicated-toolbar bug.
    const quill = instances.get(elementId);
    if (quill) {
        const toolbar = quill.getModule('toolbar');
        if (toolbar && toolbar.container) {
            toolbar.container.remove();
        }
    }
    instances.delete(elementId);
}
