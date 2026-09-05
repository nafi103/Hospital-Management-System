// Drives the "Generate Instructions" button on the prescription details page: opens a
// SignalR connection to AiStreamHub, joins a per-request group, kicks off generation of a
// Bangla-language patient instruction sheet, and renders it as it streams in. On
// completion it swaps the live text for the server-rendered, reviewable suggestion card
// (with working Accept/Edit/Reject) - the same pattern as ai-stream.js for case summaries.
$(document).ready(function () {
    const btn = document.getElementById("generatePatientInstructionsBtn");
    if (!btn) return;

    const feed = document.getElementById("patientInstructionsStreamFeed");
    const placeholder = document.getElementById("patientInstructionsStreamPlaceholder");
    const errorBox = document.getElementById("patientInstructionsStreamError");
    const listContainer = document.getElementById("patientInstructionsList");
    const prescriptionId = btn.dataset.prescriptionId;

    function resetUi() {
        btn.disabled = false;
        btn.innerHTML = '<i class="bi bi-translate me-1"></i> Generate Instructions';
    }

    btn.addEventListener("click", function () {
        const tokenEl = document.querySelector('input[name="__RequestVerificationToken"]');
        const token = tokenEl ? tokenEl.value : "";
        const streamId = (window.crypto && crypto.randomUUID) ? crypto.randomUUID() :
            "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, function (c) {
                const r = Math.random() * 16 | 0;
                const v = c === "x" ? r : (r & 0x3 | 0x8);
                return v.toString(16);
            });

        if (typeof signalR === "undefined") {
            errorBox.textContent = "The live-update library didn't load - refresh the page and try again.";
            errorBox.classList.remove("d-none");
            return;
        }

        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> Generating&hellip;';
        errorBox.classList.add("d-none");
        feed.classList.add("d-none");
        feed.textContent = "";
        placeholder.classList.remove("d-none");

        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/aiStreamHub")
            .build();

        let buffer = "";
        connection.on("ReceiveChunk", function (delta) {
            buffer += delta;
            placeholder.classList.add("d-none");
            feed.classList.remove("d-none");
            feed.textContent = buffer;
        });

        connection.on("StreamRestarted", function (message) {
            buffer = "";
            feed.classList.add("d-none");
            placeholder.classList.remove("d-none");
            placeholder.innerHTML = '<span class="spinner-border spinner-border-sm me-1"></span> ' + message;
        });

        connection.on("StreamError", function (message) {
            errorBox.textContent = message;
            errorBox.classList.remove("d-none");
            placeholder.classList.add("d-none");
            feed.classList.add("d-none");
            resetUi();
            connection.stop();
        });

        connection.start()
            .then(() => connection.invoke("JoinStream", streamId))
            .then(() => {
                const body = new URLSearchParams();
                body.set("prescriptionId", prescriptionId);
                body.set("streamId", streamId);
                body.set("__RequestVerificationToken", token);

                return fetch("/AiReview/GeneratePatientInstructions", {
                    method: "POST",
                    headers: { "Content-Type": "application/x-www-form-urlencoded" },
                    body: body.toString()
                });
            })
            .then(response => response.json().then(data => ({ ok: response.ok, data })))
            .then(({ ok, data }) => {
                if (!ok) throw new Error(data.error || "The AI service failed.");

                // returnUrl keeps Accept/Edit/Reject on this page - without it,
                // RenderSuggestion's default fallback sends the doctor to Patients/Details
                // instead of back here.
                const returnUrl = encodeURIComponent(window.location.pathname);
                return fetch("/AiReview/RenderSuggestion/" + data.suggestionId + "?returnUrl=" + returnUrl)
                    .then(r => r.text())
                    .then(html => {
                        placeholder.classList.add("d-none");
                        feed.classList.add("d-none");
                        const wrapper = document.createElement("div");
                        wrapper.style.fontFamily = "'Noto Sans Bengali', 'Inter', sans-serif";
                        wrapper.innerHTML = html;
                        listContainer.prepend(wrapper);
                        const emptyState = document.getElementById("noPatientInstructionsEmptyState");
                        if (emptyState) emptyState.remove();
                    });
            })
            .catch(err => {
                errorBox.textContent = err.message || "Couldn't reach the AI service.";
                errorBox.classList.remove("d-none");
                placeholder.classList.add("d-none");
                feed.classList.add("d-none");
            })
            .finally(() => {
                resetUi();
                connection.stop();
            });
    });
});
