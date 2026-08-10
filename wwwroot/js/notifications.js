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
                    col.className = "col-md-6 fade-in-card";
                    
                    // Safely get token
                    const tokenElement = document.querySelector('input[name="__RequestVerificationToken"]');
                    const tokenValue = tokenElement ? tokenElement.value : '';

                    col.innerHTML = `
                        <div class="card border-primary border-2 h-100 shadow-sm">
                            <div class="card-header bg-primary bg-opacity-10 border-bottom-0 pt-3 pb-2">
                                <div class="d-flex justify-content-between align-items-center">
                                    <span class="badge bg-primary">In Consultation</span>
                                    <small class="text-muted"><i class="bi bi-clock"></i> Sent in ${payload.time}</small>
                                </div>
                            </div>
                            <div class="card-body">
                                <h4 class="fw-bold text-primary mb-1">${payload.patientName}</h4>
                                <p class="text-muted mb-3"><i class="bi bi-person-badge"></i> UHID: ${payload.uhid}</p>
                                
                                <div class="bg-light p-3 rounded mb-4">
                                    <h6 class="fw-bold mb-2 text-muted">Reason for Visit:</h6>
                                    <p class="mb-0">${payload.reason}</p>
                                </div>

                                <div class="d-grid gap-2">
                                    <a href="/MedicalRecords/Create?patientId=${payload.patientId}&doctorId=${payload.doctorId}" class="btn btn-outline-primary text-start">
                                        <i class="bi bi-file-medical me-2"></i> Add Medical Record
                                    </a>
                                    <a href="/Prescriptions/Create?patientId=${payload.patientId}&doctorId=${payload.doctorId}" class="btn btn-outline-primary text-start">
                                        <i class="bi bi-capsule me-2"></i> Write Prescription
                                    </a>
                                </div>
                            </div>
                            <div class="card-footer bg-white border-top-0 pb-3 text-end">
                                <form action="/DoctorDashboard/MarkCompleted/${payload.id}" method="post" style="display:inline;">
                                    <input name="__RequestVerificationToken" type="hidden" value="${tokenValue}">
                                    <button type="submit" class="btn btn-success px-4" title="Finish Consultation">
                                        <i class="bi bi-check2-circle me-1"></i> Mark Completed
                                    </button>
                                </form>
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
