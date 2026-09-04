// Theme toggle: cycles auto -> light -> dark -> auto. "Auto" follows the OS
// dark-mode preference, or local time (night = 7pm-6am) when the OS expresses
// no preference either way. The actual light/dark resolution for first paint
// happens in the inline <head> script in _Layout.cshtml (to avoid a flash) -
// this file only handles the toggle button and keeping an already-open page
// in sync if the OS theme changes while in auto mode.
(function () {
    var STORAGE_KEY = 'hms-theme';

    function resolveAutoTheme() {
        var hour = new Date().getHours();
        var isNight = hour >= 19 || hour < 6;
        var prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
        return (prefersDark || isNight) ? 'dark' : 'light';
    }

    function getMode() {
        var stored = localStorage.getItem(STORAGE_KEY);
        return (stored === 'light' || stored === 'dark') ? stored : 'auto';
    }

    function applyTheme(mode) {
        var resolved = mode === 'auto' ? resolveAutoTheme() : mode;
        document.documentElement.setAttribute('data-bs-theme', resolved);
    }

    function updateButton(btn, mode) {
        var icons = { auto: 'bi-circle-half', light: 'bi-sun', dark: 'bi-moon-stars' };
        var icon = btn.querySelector('i');
        if (icon) icon.className = 'bi ' + icons[mode];
        btn.title = 'Theme: ' + mode;
    }

    document.addEventListener('DOMContentLoaded', function () {
        var btn = document.getElementById('themeToggleBtn');
        if (!btn) return;

        var mode = getMode();
        updateButton(btn, mode);

        btn.addEventListener('click', function () {
            var order = ['auto', 'light', 'dark'];
            mode = order[(order.indexOf(mode) + 1) % order.length];
            if (mode === 'auto') {
                localStorage.removeItem(STORAGE_KEY);
            } else {
                localStorage.setItem(STORAGE_KEY, mode);
            }
            applyTheme(mode);
            updateButton(btn, mode);
        });

        if (window.matchMedia) {
            window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () {
                if (getMode() === 'auto') applyTheme('auto');
            });
        }
    });
})();
