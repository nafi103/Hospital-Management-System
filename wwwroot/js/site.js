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

    // 6. Global Medical Record Quick-View Modal Interceptor
    var activeCitationList = [];
    var currentCitationIndex = -1;

    function updateCitationNav() {
        var navContainer = document.getElementById('citationNavControls');
        var navBadge = document.getElementById('citationNavBadge');
        var prevBtn = document.getElementById('btnPrevCitation');
        var nextBtn = document.getElementById('btnNextCitation');
        if (!navContainer || !navBadge) return;

        if (activeCitationList.length > 1 && currentCitationIndex >= 0) {
            navContainer.classList.remove('d-none');
            navContainer.classList.add('d-flex');
            navBadge.textContent = (currentCitationIndex + 1) + ' / ' + activeCitationList.length;
            if (prevBtn) prevBtn.disabled = currentCitationIndex <= 0;
            if (nextBtn) nextBtn.disabled = currentCitationIndex >= activeCitationList.length - 1;
        } else {
            navContainer.classList.add('d-none');
            navContainer.classList.remove('d-flex');
        }
    }

    window.openMedicalRecordQuickView = function (recordId) {
        if (!recordId) return;

        // Collect all citations present on the active page to enable seamless cycling without closing
        var foundIds = [];
        document.querySelectorAll('.ai-citation-badge, [data-record-id]').forEach(function (el) {
            var cid = el.getAttribute('data-record-id');
            if (cid && foundIds.indexOf(cid) === -1) {
                foundIds.push(cid);
            }
        });
        activeCitationList = foundIds;
        currentCitationIndex = activeCitationList.indexOf(String(recordId));
        updateCitationNav();

        var modalEl = document.getElementById('medicalRecordQuickViewModal');
        if (!modalEl) {
            window.open('/MedicalRecords/Details/' + recordId, '_blank');
            return;
        }

        var modal = bootstrap.Modal.getOrCreateInstance(modalEl);

        // Update target link for new tab
        var fullUrl = '/MedicalRecords/Details/' + recordId;
        var newTabLink = document.getElementById('btnModalOpenRecordFull');
        if (newTabLink) {
            newTabLink.setAttribute('href', fullUrl);
        }

        var titleEl = document.getElementById('medicalRecordQuickViewModalTitle');
        if (titleEl) {
            titleEl.textContent = 'Medical Record #' + recordId;
        }

        var contentEl = document.getElementById('medicalRecordQuickViewModalContent');
        if (contentEl) {
            contentEl.innerHTML =
                '<div class="text-center py-5">' +
                '  <div class="spinner-border text-primary" role="status"><span class="visually-hidden">Loading...</span></div>' +
                '  <p class="text-muted mt-2 small mb-0">Loading clinical record #' + recordId + '...</p>' +
                '</div>';
        }

        var leftCol = document.querySelector('.cockpit-col-left');
        var savedLeftScroll = leftCol ? leftCol.scrollTop : 0;
        var savedWindowScroll = window.scrollY || window.pageYOffset;

        modal.show();

        // Subtly highlight local timeline card without scrolling or disrupting doctor's view
        var localCard = document.getElementById('record-card-' + recordId);
        if (localCard) {
            document.querySelectorAll('.record-highlight-target').forEach(function (c) {
                c.classList.remove('highlighted');
            });
            localCard.classList.add('highlighted');
            // Explicitly lock scroll position so clicking references never jumps or scrolls
            if (leftCol) leftCol.scrollTop = savedLeftScroll;
            window.scrollTo(0, savedWindowScroll);
        }

        // Restore scroll position when modal is dismissed
        $(modalEl).one('hidden.bs.modal', function () {
            if (leftCol) leftCol.scrollTop = savedLeftScroll;
            window.scrollTo(0, savedWindowScroll);
        });

        // Fetch record partial via AJAX
        fetch('/MedicalRecords/QuickView/' + recordId, {
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
        .then(function (res) {
            if (!res.ok) {
                if (res.status === 403) throw new Error('Access denied: You do not have an active care relationship to view this record.');
                if (res.status === 404) throw new Error('Medical Record #' + recordId + ' was not found.');
                throw new Error('Could not load record (Status ' + res.status + ').');
            }
            return res.text();
        })
        .then(function (html) {
            if (contentEl) {
                contentEl.innerHTML = html;
            }
        })
        .catch(function (err) {
            if (contentEl) {
                contentEl.innerHTML =
                    '<div class="alert alert-warning m-4 d-flex align-items-center gap-2">' +
                    '  <i class="bi bi-exclamation-triangle-fill fs-5 text-warning flex-shrink-0"></i>' +
                    '  <div>' + (err.message || 'Error loading medical record.') + '</div>' +
                    '</div>';
            }
        });
    };

    // Citation Next / Prev Click Handlers
    document.addEventListener('click', function (e) {
        if (e.target.closest('#btnPrevCitation')) {
            e.preventDefault();
            if (currentCitationIndex > 0) {
                window.openMedicalRecordQuickView(activeCitationList[currentCitationIndex - 1]);
            }
        } else if (e.target.closest('#btnNextCitation')) {
            e.preventDefault();
            if (currentCitationIndex < activeCitationList.length - 1) {
                window.openMedicalRecordQuickView(activeCitationList[currentCitationIndex + 1]);
            }
        }
    });

    // Intercept clicks on any citation pill / link across the entire app
    document.addEventListener('click', function (e) {
        var citationEl = e.target.closest('.ai-citation-badge, [data-record-id], a[href*="/MedicalRecords/Details/"]');
        if (!citationEl) return;

        // Skip the explicit "Open in New Tab" link inside the modal itself or if explicitly bypassed
        if (citationEl.id === 'btnModalOpenRecordFull' || citationEl.getAttribute('data-bypass-quickview') === 'true') {
            return;
        }

        var recordId = citationEl.getAttribute('data-record-id');
        if (!recordId) {
            var href = citationEl.getAttribute('href') || '';
            var match = href.match(/\/MedicalRecords\/Details\/(\d+)/i);
            if (match) {
                recordId = match[1];
            }
        }

        if (recordId) {
            e.preventDefault();
            e.stopPropagation();
            window.openMedicalRecordQuickView(recordId);
        }
    });
})();

