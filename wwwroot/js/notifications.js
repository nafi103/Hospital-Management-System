$(document).ready(function () {
    if (window.MySignalRGroupId && window.MySignalRGroupId !== "") {
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/notificationHub")
            .withAutomaticReconnect()
            .build();

        connection.on("ReceiveNotification", function (appointmentId, patientName) {
            // 1. Play Ding Sound
            const ding = document.getElementById("dingSound");
            if (ding) {
                // We use catch to prevent uncaught exceptions if browser blocks autoplay
                ding.play().catch(e => console.log("Audio play blocked by browser."));
            }

            // 2. Set Message and Show Toast
            document.getElementById("notificationMessage").innerHTML = `<b>${patientName}</b> has finished their consultation.<br>Please prepare the next patient!`;
            
            const toastEl = document.getElementById("notificationToast");
            const toast = new bootstrap.Toast(toastEl);
            toast.show();

            // 3. Dynamically remove the row instead of reloading
            const row = document.getElementById("appointment-row-" + appointmentId);
            if (row) {
                row.style.transition = "opacity 0.5s ease, transform 0.5s ease";
                row.style.opacity = "0";
                row.style.transform = "translateX(20px)";
                setTimeout(() => row.remove(), 500);
            }
        });

        connection.on("PatientSentIn", function (payload) {
            console.log("PatientSentIn received!", payload);
            
            if (window.UserRole === "Doctor") {
                const emptyState = document.getElementById("empty-state-message");
                if (emptyState) emptyState.style.display = "none";

                const container = document.getElementById("active-consultations-container");
                if (container) {
                    const col = document.createElement("div");
                    col.className = "col-12 fade-in-card";
                    col.id = "consultation-card-" + payload.id;
                    col.dataset.patientId = payload.patientId;

                    // Safely get token
                    const tokenElement = document.querySelector('input[name="__RequestVerificationToken"]');
                    const tokenValue = tokenElement ? tokenElement.value : '';

                    // Mirrors the triage badge DoctorDashboard/Index.cshtml renders server-side
                    // for vitals recorded before this patient was sent in - kept here too so a
                    // dashboard that's already open shows it without needing a reload.
                    let triageHtml = '';
                    if (payload.triage) {
                        const triageClass = payload.triage === 'Emergency' ? 'danger' : (payload.triage === 'Urgent' ? 'warning' : 'success');
                        triageHtml = `
                            <div class="mb-3">
                                <span class="badge bg-${triageClass} bg-opacity-10 text-${triageClass} border border-${triageClass}-subtle mb-2">
                                    <i class="bi bi-clipboard2-pulse me-1"></i> Triage: ${payload.triage}
                                </span>
                                <div class="small text-muted">${payload.vitalsSummary || ''}</div>
                            </div>`;
                    }

                    // The AI review panel starts in its loading state - GenerateArrivalSummary
                    // runs in the background on the server and reports back over this same
                    // connection (ArrivalAiReady / ArrivalAiFailed) once it's done.
                    col.innerHTML = `
                        <div class="pro-card h-100 border-start border-4 border-primary">
                            <div class="row g-0">
                                <div class="col-md-7 pe-md-4 mb-4 mb-md-0">
                                    <div class="d-flex justify-content-between align-items-center mb-3">
                                        <span class="badge bg-primary">In Consultation</span>
                                        <small class="text-muted"><i class="bi bi-clock"></i> Sent in ${payload.time}</small>
                                    </div>

                                    <h4 class="fw-bold text-primary mb-1">${payload.patientName}</h4>
                                    <p class="text-muted mb-2"><i class="bi bi-person-badge"></i> UHID: ${payload.uhid}</p>
                                    ${triageHtml}

                                    <div class="bg-light p-3 rounded mb-4">
                                        <h6 class="fw-bold mb-2 text-muted">Reason for Visit:</h6>
                                        <p class="mb-0">${payload.reason}</p>
                                    </div>

                                    <div class="d-grid gap-2 mb-3">
                                        <a href="/MedicalRecords/Create?patientId=${payload.patientId}&doctorId=${payload.doctorId}" class="btn btn-outline-primary text-start">
                                            <i class="bi bi-file-medical me-2"></i> Add Medical Record
                                        </a>
                                        <a href="/Prescriptions/Create?patientId=${payload.patientId}&doctorId=${payload.doctorId}" class="btn btn-outline-primary text-start">
                                            <i class="bi bi-capsule me-2"></i> Write Prescription
                                        </a>
                                    </div>

                                    <div class="d-flex justify-content-start pt-3 border-top">
                                        <form action="/DoctorDashboard/MarkCompleted/${payload.id}" method="post" style="display:inline;">
                                            <input name="__RequestVerificationToken" type="hidden" value="${tokenValue}">
                                            <button type="submit" class="btn btn-success px-4" title="Finish Consultation">
                                                <i class="bi bi-check2-circle me-1"></i> Mark Completed
                                            </button>
                                        </form>
                                    </div>
                                </div>

                                <div class="col-md-5 ps-md-4 border-start-md ai-review-panel">
                                    <h6 class="text-muted text-uppercase small fw-bold mb-3"><i class="bi bi-robot me-1"></i> AI Review</h6>
                                    <div class="ai-review-loading text-center text-muted py-4">
                                        <div class="spinner-border spinner-border-sm mb-2"></div>
                                        <p class="small mb-0">Generating summary&hellip;</p>
                                    </div>
                                    <div class="ai-review-empty text-center text-muted py-4 small d-none">No AI draft available for this visit.</div>
                                    <div class="ai-review-content d-none"></div>
                                </div>
                            </div>
                        </div>
                    `;
                    // CSS animation
                    col.style.opacity = "0";
                    col.style.transform = "translateY(20px)";
                    col.style.transition = "opacity 0.5s ease, transform 0.5s ease";
                    container.appendChild(col);

                    setTimeout(() => {
                        col.style.opacity = "1";
                        col.style.transform = "translateY(0)";
                    }, 50);
                } else {
                    console.error("active-consultations-container not found in DOM");
                }
            } else {
                console.log("User is not Doctor, ignoring PatientSentIn");
            }
        });

        // The arrival-triggered AI draft finished - swap the spinner in that patient's
        // card for the real, reviewable content (Accept/Edit/Reject included).
        connection.on("ArrivalAiReady", function (payload) {
            if (window.UserRole !== "Doctor") return;

            const panel = document.querySelector('[data-patient-id="' + payload.patientId + '"] .ai-review-panel');
            if (!panel) return;

            fetch("/AiReview/RenderSuggestion/" + payload.suggestionId + "?compact=true&returnUrl=" + encodeURIComponent(window.location.pathname))
                .then(r => r.text())
                .then(html => {
                    panel.querySelector(".ai-review-loading").classList.add("d-none");
                    panel.querySelector(".ai-review-empty").classList.add("d-none");
                    const content = panel.querySelector(".ai-review-content");
                    content.innerHTML = html;
                    content.classList.remove("d-none");
                })
                .catch(err => console.error("Failed to load AI review:", err));
        });

        // The arrival-triggered AI draft failed (no history yet, or the AI service is
        // down) - stop spinning and say so plainly instead of spinning forever.
        connection.on("ArrivalAiFailed", function (payload) {
            if (window.UserRole !== "Doctor") return;

            const panel = document.querySelector('[data-patient-id="' + payload.patientId + '"] .ai-review-panel');
            if (!panel) return;

            panel.querySelector(".ai-review-loading").classList.add("d-none");
            panel.querySelector(".ai-review-content").classList.add("d-none");
            panel.querySelector(".ai-review-empty").classList.remove("d-none");
        });

        connection.start().then(function () {
            console.log("Connected to SignalR Notification Hub.");
            // Join the specific doctor's group
            connection.invoke("JoinDoctorGroup", window.MySignalRGroupId.toString())
                .catch(function (err) {
                    console.error("Error joining doctor group: ", err.toString());
                });
        }).catch(function (err) {
            console.error("SignalR Connection Error: ", err.toString());
        });
    }
});
