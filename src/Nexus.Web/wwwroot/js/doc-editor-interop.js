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
    instances.delete(elementId);
}
