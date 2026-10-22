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

// ==========================================================================
// Staff Shell Interactions (Shortcuts, Mobile Sidebar, Quick Search)
// ==========================================================================
(function () {
    document.addEventListener('DOMContentLoaded', function () {
        // 1. Mobile Sidebar Toggle
        var toggleBtn = document.getElementById('sidebarToggleBtn');
        var backdrop = document.getElementById('sidebarBackdrop');

        if (toggleBtn) {
            toggleBtn.addEventListener('click', function () {
                document.body.classList.toggle('sidebar-open');
            });
        }

        if (backdrop) {
            backdrop.addEventListener('click', function () {
                document.body.classList.remove('sidebar-open');
            });
        }

        // Close sidebar on navigation
        document.querySelectorAll('.sidebar-nav-link').forEach(function (link) {
            link.addEventListener('click', function () {
                document.body.classList.remove('sidebar-open');
            });
        });

        // 2. Quick Patient / Doctor Jump Input
        var quickSearchInput = document.getElementById('quickPatientSearchInput');
        if (quickSearchInput) {
            quickSearchInput.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    var query = this.value.trim();
                    if (query.length > 0) {
                        window.location.href = '/Patients?searchQuery=' + encodeURIComponent(query);
                    }
                }
            });
        }

        var quickDocSearch = document.getElementById('quickDoctorSearchInput');
        if (quickDocSearch) {
            quickDocSearch.addEventListener('keydown', function (e) {
                if (e.key === 'Enter') {
                    e.preventDefault();
                    var query = this.value.trim();
                    if (query.length > 0) {
                        window.location.href = '/Portal/Doctors?specialty=' + encodeURIComponent(query);
                    }
                }
            });
        }

        // 3. Global Shortcut: Ctrl+K or Cmd+K
        document.addEventListener('keydown', function (e) {
            if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
                var input = document.getElementById('quickPatientSearchInput') || document.getElementById('quickDoctorSearchInput');
                if (input) {
                    e.preventDefault();
                    input.focus();
                    input.select();
                }
            }
        });
    });

    // 4. SignalR Status Global Helper
    window.updateSignalRStatus = function (status) {
        var indicator = document.getElementById('signalrStatusIndicator');
        if (!indicator) return;
        var dot = indicator.querySelector('.signalr-dot');
        var label = indicator.querySelector('.signalr-label');
        if (dot) {
            dot.className = 'signalr-dot ' + status;
        }
        if (label) {
            if (status === 'connected') label.textContent = 'Live';
            else if (status === 'reconnecting') label.textContent = 'Reconnecting';
            else label.textContent = 'Offline';
        }
    };

    // 5. Global Modal Input Auto-Focus
    document.addEventListener('shown.bs.modal', function (event) {
        var focusTarget = event.target.querySelector('textarea, input:not([type="hidden"]):not([disabled])');
        if (focusTarget) {
            focusTarget.focus();
        }
    });
})();

