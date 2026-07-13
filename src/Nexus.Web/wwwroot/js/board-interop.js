export function initColumn(bodyEl, dotNetRef) {
    if (!bodyEl || bodyEl._sortableInstance) {
        return;
    }

    bodyEl._sortableInstance = Sortable.create(bodyEl, {
        group: 'board',
        animation: 150,
        onEnd: function (evt) {
            const workItemId = evt.item.getAttribute('data-workitem-id');
            const targetColumn = evt.to.closest('.board-column');
            const newStatusId = targetColumn ? targetColumn.getAttribute('data-status-id') : null;
            if (!workItemId || !newStatusId) {
                return;
            }
            dotNetRef.invokeMethodAsync('OnCardDropped', workItemId, newStatusId, evt.newIndex);
        }
    });
}

export function destroyColumn(bodyEl) {
    if (bodyEl && bodyEl._sortableInstance) {
        bodyEl._sortableInstance.destroy();
        bodyEl._sortableInstance = null;
    }
}
