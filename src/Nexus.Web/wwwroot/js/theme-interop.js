// Extracted from an inline <script> in App.razor so the Content-Security-Policy can forbid
// inline script entirely (no 'unsafe-inline' in script-src). Behaviour is unchanged: it applies
// the saved theme before first paint to avoid a flash, and exposes the toggle used by
// ThemeSwitch.razor.
(function () {
    var root = document.documentElement;
    var systemDark = window.matchMedia('(prefers-color-scheme: dark)');

    function readSaved() {
        try {
            var saved = localStorage.getItem('theme');
            return saved === 'dark' || saved === 'light' ? saved : null;
        } catch (e) {
            return null;
        }
    }

    // data-theme always carries the *effective* theme — the saved choice, or the OS setting when
    // nothing was saved. The CSS reads it for things light-dark() can't express (the theme switch
    // icon, the select chevron), so it must never be absent once this script has run.
    function apply() {
        var theme = readSaved() || (systemDark.matches ? 'dark' : 'light');
        root.setAttribute('data-theme', theme);
        return theme;
    }

    apply();

    // Following the OS only while the person hasn't picked a theme themselves.
    systemDark.addEventListener('change', function () {
        if (!readSaved()) {
            apply();
        }
    });

    // Plain JS, no Blazor circuit involved — the theme toggle needs to work
    // identically on static SSR pages (Login/Register, which have no SignalR
    // circuit for IJSRuntime to call back through) and on interactive pages.
    window.nexusToggleTheme = function () {
        var next = root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
        root.setAttribute('data-theme', next);
        try {
            localStorage.setItem('theme', next);
        } catch (e) {
            // Private mode / blocked storage: the switch still works for this page view.
        }
    };

    // Replaces the inline onclick="..." attributes these buttons used to carry. Inline event
    // handlers are inline script: the Content-Security-Policy sets script-src 'self' with no
    // 'unsafe-inline', so the browser refuses to run them and the button silently does nothing.
    //
    // Delegated from document rather than bound per element, for two reasons: these buttons also
    // render on static SSR pages that have no Blazor circuit (Login/Register), and on interactive
    // pages Blazor replaces DOM nodes on re-render, which would drop any listener bound directly
    // to the element.
    document.addEventListener('click', function (e) {
        var trigger = e.target.closest('[data-nexus-action]');
        if (!trigger) {
            return;
        }

        switch (trigger.getAttribute('data-nexus-action')) {
            case 'toggle-theme':
                window.nexusToggleTheme();
                break;
            case 'history-back':
                history.back();
                break;
        }
    });

    // "/" focuses the global search from anywhere, like most web apps — unless the person is
    // already typing somewhere (input, textarea, contenteditable such as the doc editor).
    document.addEventListener('keydown', function (e) {
        if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey) {
            return;
        }

        var target = e.target;
        var typing = target && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName));
        var search = document.getElementById('global-search');
        if (typing || !search || search.offsetParent === null) {
            return;
        }

        e.preventDefault();
        search.focus();
    });

    // Abas de páginas públicas (landing): funcionam sem circuito Blazor. Um [data-nexus-tabs]
    // com botões role="tab" controla painéis pelo aria-controls; clique e setas trocam a aba.
    function selectTab(tab, focus) {
        var list = tab.closest('[data-nexus-tabs]');
        if (!list) {
            return;
        }
        list.querySelectorAll('[role="tab"]').forEach(function (t) {
            var on = t === tab;
            t.setAttribute('aria-selected', on ? 'true' : 'false');
            t.setAttribute('tabindex', on ? '0' : '-1');
            t.classList.toggle('active', on);
            var panel = document.getElementById(t.getAttribute('aria-controls'));
            if (panel) {
                panel.hidden = !on;
            }
        });
        if (focus) {
            tab.focus();
        }
    }

    document.addEventListener('click', function (e) {
        var tab = e.target.closest('[data-nexus-tabs] [role="tab"]');
        if (tab) {
            selectTab(tab, false);
        }
    });

    document.addEventListener('keydown', function (e) {
        var tab = e.target.closest && e.target.closest('[data-nexus-tabs] [role="tab"]');
        if (!tab) {
            return;
        }
        var tabs = Array.prototype.slice.call(tab.closest('[data-nexus-tabs]').querySelectorAll('[role="tab"]'));
        var i = tabs.indexOf(tab);
        var next = e.key === 'ArrowRight' ? tabs[(i + 1) % tabs.length]
            : e.key === 'ArrowLeft' ? tabs[(i - 1 + tabs.length) % tabs.length]
            : e.key === 'Home' ? tabs[0]
            : e.key === 'End' ? tabs[tabs.length - 1]
            : null;
        if (next) {
            e.preventDefault();
            selectTab(next, true);
        }
    });

    // Campo de arquivo escondido atrás de um botão (foto de perfil): mostra o nome escolhido
    // no elemento indicado por data-file-label. Funciona em páginas SSR sem circuito Blazor.
    document.addEventListener('change', function (e) {
        var input = e.target;
        if (!input || input.type !== 'file' || !input.dataset.fileLabel) {
            return;
        }
        var label = document.getElementById(input.dataset.fileLabel);
        if (label) {
            label.textContent = input.files && input.files.length ? input.files[0].name : 'Nenhuma foto nova selecionada';
        }
    });

    // Em telas estreitas a navegação de configurações vira uma faixa rolável: traz o item
    // ativo para a vista, senão "Dados pessoais" (por exemplo) fica escondido fora da tela.
    function revealActiveSettingsTab() {
        var active = document.querySelector('.settings-nav .active');
        var nav = active && active.closest('.settings-nav');
        if (nav && nav.scrollWidth > nav.clientWidth) {
            nav.scrollLeft = active.offsetLeft - (nav.clientWidth - active.offsetWidth) / 2;
        }
    }
    document.addEventListener('DOMContentLoaded', revealActiveSettingsTab);
    document.addEventListener('enhancedload', revealActiveSettingsTab);
})();
