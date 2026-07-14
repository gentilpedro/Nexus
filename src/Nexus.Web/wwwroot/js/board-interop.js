export function initColumn(bodyEl, dotNetRef, groupName) {
    if (!bodyEl || bodyEl._sortableInstance) {
        return;
    }

    bodyEl._sortableInstance = Sortable.create(bodyEl, {
        group: groupName || 'board',
        animation: 150,
        onEnd: function (evt) {
            const workItemId = evt.item.getAttribute('data-workitem-id');
            const targetContainer = evt.to.closest('[data-drop-id]');
            const newDropId = targetContainer ? targetContainer.getAttribute('data-drop-id') : null;
            if (!workItemId || newDropId === null) {
                return;
            }
            dotNetRef.invokeMethodAsync('OnCardDropped', workItemId, newDropId, evt.newIndex);
        }
    });
}

export function destroyColumn(bodyEl) {
    if (bodyEl && bodyEl._sortableInstance) {
        bodyEl._sortableInstance.destroy();
        bodyEl._sortableInstance = null;
    }
}
