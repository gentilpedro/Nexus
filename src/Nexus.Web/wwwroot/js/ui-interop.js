// Small UI helpers shared by the Razor components that use the native <dialog> element
// (ConfirmDialog, TaskDetailPanel). showModal() is what gives a dialog its focus trap, Esc to
// close, inert background and top-layer rendering (so no ancestor's overflow can clip it) —
// Blazor can render the element but can't call showModal() itself.
window.nexusUi = {
    showModal: function (dialog) {
        if (!dialog || dialog.open) {
            return;
        }

        dialog.showModal();

        // Click on the backdrop (the dialog element itself, outside its content box) behaves
        // like Esc: it raises "cancel", which the component already handles. Bound once per
        // element, since Blazor keeps the same <dialog> node across renders.
        if (!dialog.dataset.nexusLightDismiss) {
            dialog.dataset.nexusLightDismiss = "1";
            dialog.addEventListener("click", function (e) {
                if (e.target !== dialog || dialog.dataset.dismissible === "false") {
                    return;
                }

                var rect = dialog.getBoundingClientRect();
                var inside = e.clientX >= rect.left && e.clientX <= rect.right
                    && e.clientY >= rect.top && e.clientY <= rect.bottom;
                if (!inside) {
                    dialog.dispatchEvent(new Event("cancel", { cancelable: true }));
                }
            });
        }
    },

    close: function (dialog) {
        if (dialog && dialog.open) {
            dialog.close();
        }
    },

    // Sumário do documento: rola até o N-ésimo título (h1–h3) do conteúdo e move o foco para
    // ele, para quem navega por teclado continuar a leitura dali.
    scrollToHeading: function (containerId, index) {
        var container = document.getElementById(containerId);
        if (!container) {
            return;
        }

        var heading = container.querySelectorAll('h1, h2, h3')[index];
        if (!heading) {
            return;
        }

        heading.setAttribute('tabindex', '-1');
        heading.scrollIntoView({ behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth', block: 'start' });
        heading.focus({ preventScroll: true });
    },

    focus: function (element) {
        if (element) {
            element.focus();
        }
    }
};
