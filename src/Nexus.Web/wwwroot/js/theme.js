export function getTheme() {
    var explicit = document.documentElement.getAttribute('data-theme');
    if (explicit === 'dark' || explicit === 'light') {
        return explicit;
    }
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

export function toggleTheme() {
    var next = getTheme() === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-theme', next);
    localStorage.setItem('theme', next);
    return next;
}
