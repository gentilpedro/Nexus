export function initColumn(bodyEl, dotNetRef, groupName) {
    if (!bodyEl || bodyEl._sortableInstance) {
        return;
    }

    bodyEl._sortableInstance = Sortable.create(bodyEl, {
        group: groupName || 'board',
        animation: 150,
        onEnd: async function (evt) {
            const workItemId = evt.item.getAttribute('data-workitem-id');
            const targetContainer = evt.to.closest('[data-drop-id]');
            const newDropId = targetContainer ? targetContainer.getAttribute('data-drop-id') : null;
            if (!workItemId || newDropId === null) {
                return;
            }

            // Sortable already moved the card's DOM node before this handler runs — if the
            // server rejects the move (workflow rule, IDOR guard), undo that move here so the
            // card visually snaps back. Blazor's own re-render can't do this on its own: its
            // diff is based on what IT last rendered, which never learned about Sortable's
            // direct DOM manipulation, so a rejected move that leaves the model unchanged
            // produces no patches and the card stays stranded in the wrong column.
            const allowed = await dotNetRef.invokeMethodAsync('OnCardDropped', workItemId, newDropId, evt.newIndex);
            if (!allowed) {
                evt.from.insertBefore(evt.item, evt.from.children[evt.oldIndex] || null);
            }
        }
    });
}

export function destroyColumn(bodyEl) {
    if (bodyEl && bodyEl._sortableInstance) {
        bodyEl._sortableInstance.destroy();
        bodyEl._sortableInstance = null;
    }
}
