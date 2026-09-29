export function initColumn(bodyEl, dotNetRef, groupName) {
    if (!bodyEl || bodyEl._sortableInstance) {
        return;
    }

    bodyEl._sortableInstance = Sortable.create(bodyEl, {
        group: groupName || 'board',
        animation: 150,
        // Only work items move (board cards and backlog rows) — not the "drop tasks here"
        // placeholder of an empty column.
        draggable: '[data-workitem-id]',
        ghostClass: 'is-drag-ghost',
        chosenClass: 'is-drag-chosen',
        dragClass: 'is-dragging',
        // On touch, a short press-and-hold starts the drag; a plain swipe keeps scrolling the
        // board/page instead of grabbing whatever card the finger landed on.
        delay: 180,
        delayOnTouchOnly: true,
        touchStartThreshold: 6,
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
            const newIndex = typeof evt.newDraggableIndex === 'number' ? evt.newDraggableIndex : evt.newIndex;
            const allowed = await dotNetRef.invokeMethodAsync('OnCardDropped', workItemId, newDropId, newIndex);
            if (!allowed) {
                evt.from.insertBefore(evt.item, evt.from.children[evt.oldIndex] || null);
                evt.item.classList.add('is-rejected');
                setTimeout(function () { evt.item.classList.remove('is-rejected'); }, 600);
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
