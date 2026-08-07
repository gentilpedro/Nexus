// Extracted from an inline <script> in App.razor so the Content-Security-Policy can forbid
// inline script entirely (no 'unsafe-inline' in script-src). Behaviour is unchanged: it applies
// the saved theme before first paint to avoid a flash, and exposes the toggle used by
// ThemeSwitch.razor.
(function () {
    var saved = localStorage.getItem('theme');
    if (saved === 'dark' || saved === 'light') {
        document.documentElement.setAttribute('data-theme', saved);
    }

    // Plain JS, no Blazor circuit involved — the theme toggle needs to work
    // identically on static SSR pages (Login/Register, which have no SignalR
    // circuit for IJSRuntime to call back through) and on interactive pages.
    window.nexusToggleTheme = function () {
        var current = document.documentElement.getAttribute('data-theme');
        if (current !== 'dark' && current !== 'light') {
            current = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
        }
        var next = current === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        localStorage.setItem('theme', next);
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
})();
