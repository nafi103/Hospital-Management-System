/**
 * Portal Booking & Interactive Slot Selection Engine
 * Handles dynamic AJAX slot fetching, conflict visualization, and form state synchronization.
 */
document.addEventListener('DOMContentLoaded', function () {
    const doctorSelect = document.getElementById('bookingDoctorSelect');
    const dateInput = document.getElementById('bookingDateInput');
    const slotTimeInput = document.getElementById('selectedSlotTimeInput');
    const morningContainer = document.getElementById('morningSlotsContainer');
    const afternoonContainer = document.getElementById('afternoonSlotsContainer');
    const slotLoadingSpinner = document.getElementById('slotLoadingSpinner');
    const noSlotsAlert = document.getElementById('noSlotsAlert');
    const submitBtn = document.getElementById('submitBookingBtn');
    const summaryDoctor = document.getElementById('summaryDoctorName');
    const summaryDate = document.getElementById('summaryDateDisplay');
    const summaryTime = document.getElementById('summaryTimeDisplay');
    const excludeApptInput = document.getElementById('excludeAppointmentId');

    if (!dateInput || !morningContainer) {
        return; // Not on booking/reschedule page
    }

    // Quick-fill chips for Reason for Visit
    document.querySelectorAll('.quick-reason-chip').forEach(function (chip) {
        chip.addEventListener('click', function () {
            const reasonArea = document.getElementById('reasonForVisitInput');
            if (reasonArea) {
                const text = this.getAttribute('data-reason');
                if (!reasonArea.value.trim()) {
                    reasonArea.value = text;
                } else if (!reasonArea.value.includes(text)) {
                    reasonArea.value = reasonArea.value.trim() + ', ' + text;
                }
                reasonArea.focus();
            }
        });
    });

    function getSelectedDoctorId() {
        if (doctorSelect) {
            return doctorSelect.value;
        }
        const hiddenDocId = document.getElementById('bookingDoctorIdHidden');
        return hiddenDocId ? hiddenDocId.value : null;
    }

    function formatDisplayDate(dateStr) {
        if (!dateStr) return 'Not selected';
        const parts = dateStr.split('-');
        if (parts.length === 3) {
            const d = new Date(parts[0], parts[1] - 1, parts[2]);
            return d.toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' });
        }
        return dateStr;
    }

    function fetchAndRenderSlots() {
        const docId = getSelectedDoctorId();
        const dateVal = dateInput.value;
        const excludeId = excludeApptInput ? excludeApptInput.value : '';

        if (!docId || !dateVal) {
            return;
        }

        // Reset state
        slotTimeInput.value = '';
        if (submitBtn) submitBtn.disabled = true;
        if (summaryTime) summaryTime.textContent = 'Select a time slot';
        if (summaryDate) summaryDate.textContent = formatDisplayDate(dateVal);
        if (summaryDoctor && doctorSelect && doctorSelect.options[doctorSelect.selectedIndex]) {
            summaryDoctor.textContent = doctorSelect.options[doctorSelect.selectedIndex].text;
        }

        slotLoadingSpinner.classList.remove('d-none');
        morningContainer.innerHTML = '';
        afternoonContainer.innerHTML = '';
        if (noSlotsAlert) noSlotsAlert.classList.add('d-none');

        const url = `/Portal/GetAvailableSlots?doctorId=${encodeURIComponent(docId)}&date=${encodeURIComponent(dateVal)}${excludeId ? '&excludeAppointmentId=' + encodeURIComponent(excludeId) : ''}`;

        fetch(url)
            .then(res => {
                if (!res.ok) throw new Error('Failed to fetch slots');
                return res.json();
            })
            .then(data => {
                slotLoadingSpinner.classList.add('d-none');

                const morning = data.morningSlots || [];
                const afternoon = data.afternoonSlots || [];
                const total = data.totalAvailable || 0;

                if (total === 0 && noSlotsAlert) {
                    noSlotsAlert.classList.remove('d-none');
                }

                renderSlotGroup(morning, morningContainer);
                renderSlotGroup(afternoon, afternoonContainer);
            })
            .catch(err => {
                slotLoadingSpinner.classList.add('d-none');
                morningContainer.innerHTML = '<p class="text-danger small"><i class="bi bi-exclamation-circle me-1"></i>Unable to load time slots. Please try another date.</p>';
            });
    }

    function renderSlotGroup(slots, container) {
        if (!slots || slots.length === 0) {
            container.innerHTML = '<span class="text-muted small">No slots scheduled for this window.</span>';
            return;
        }

        const grid = document.createElement('div');
        grid.className = 'd-flex flex-wrap gap-2';

        slots.forEach(s => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = `btn btn-sm ${s.isAvailable ? 'btn-outline-primary slot-chip' : 'btn-light text-muted border text-decoration-line-through opacity-50 disabled'}`;
            btn.textContent = s.timeDisplay;
            btn.setAttribute('data-utc', s.slotStartUtc);
            btn.setAttribute('data-display', s.timeDisplay);

            if (!s.isAvailable) {
                btn.disabled = true;
                btn.title = s.conflictReason || 'Unavailable';
            } else {
                btn.addEventListener('click', function () {
                    // Deselect previous
                    document.querySelectorAll('.slot-chip').forEach(b => {
                        b.classList.remove('btn-primary', 'text-white', 'active');
                        b.classList.add('btn-outline-primary');
                    });

                    // Select current
                    this.classList.remove('btn-outline-primary');
                    this.classList.add('btn-primary', 'text-white', 'active');

                    // Bind to hidden input
                    slotTimeInput.value = this.getAttribute('data-utc');

                    if (summaryTime) {
                        summaryTime.textContent = this.getAttribute('data-display') + ' (15 mins)';
                    }
                    if (submitBtn) {
                        submitBtn.disabled = false;
                    }
                });
            }

            grid.appendChild(btn);
        });

        container.appendChild(grid);
    }

    // Attach listeners
    if (doctorSelect) {
        doctorSelect.addEventListener('change', fetchAndRenderSlots);
    }
    dateInput.addEventListener('change', fetchAndRenderSlots);

    // Initial load
    fetchAndRenderSlots();
});
