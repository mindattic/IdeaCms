// IdeaCms.Plugin.ThemeToggle — flips html[data-theme-mode] and remembers the choice.
// The server (App.razor) sets the initial data-theme-mode from the site's default; an early inline
// script in <head> corrects it from localStorage before first paint if the visitor already chose one.
// This file only needs to handle the click.
(function () {
    'use strict';
    if (window.MaThemeToggle) return;
    window.MaThemeToggle = true;

    var STORAGE_KEY = 'mindattic.theme-mode';

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('.ma-tt-btn');
        if (!btn) return;

        var html = document.documentElement;
        var next = html.getAttribute('data-theme-mode') === 'dark' ? 'light' : 'dark';
        html.setAttribute('data-theme-mode', next);
        try { localStorage.setItem(STORAGE_KEY, next); } catch (e) { }
    });
})();
