// Tiny helpers the Blazor app calls via JS interop. No external scripts, no network calls.
// Firefox only starts a drag when dragstart sets some data; Blazor's handler can't do that, so do it here.
document.addEventListener('dragstart', function (e) {
    if (e.target && e.target.closest && e.target.closest('.project-card') && e.dataTransfer) {
        e.dataTransfer.setData('text/plain', '');
        e.dataTransfer.effectAllowed = 'move';
    }
});

window.cockpit = {
    toggleTheme: function () {
        var root = document.documentElement;
        var current = root.getAttribute('data-theme')
            || (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
        var next = current === 'dark' ? 'light' : 'dark';
        root.setAttribute('data-theme', next);
        try { localStorage.setItem('cockpit.theme', next); } catch (e) { }
        return next;
    }
};
