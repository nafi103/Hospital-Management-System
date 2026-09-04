// Drives the "Generate Case Summary" button on the patient details page: opens a
// SignalR connection to AiStreamHub, joins a per-request group, kicks off generation,
// and renders the narrative as it streams in. On completion it swaps the live text for
// the server-rendered, reviewable suggestion card (with working Accept/Edit/Reject).
$(document).ready(function () {
    const btn = document.getElementById("generateCaseSummaryBtn");
    if (!btn) return;

    const feed = document.getElementById("caseSummaryStreamFeed");
    const placeholder = document.getElementById("caseSummaryStreamPlaceholder");
    const errorBox = document.getElementById("caseSummaryStreamError");
    const listContainer = document.getElementById("aiSuggestionsList");
    const patientId = btn.dataset.patientId;

    function stripMarkers(text) {
        return text.replace(/\[\[rec:(\d+)\]\]/g, function (_, id) {
            return ' <sup class="text-muted">[' + id + ']</sup>';
        });
    }

    function resetUi() {
        btn.disabled = false;
        btn.innerHTML = '<i class="bi bi-magic me-1"></i> Generate Case Summary';
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
            feed.innerHTML = stripMarkers(buffer);
        });

        connection.on("StreamRestarted", function (message) {
            // The server is retrying or switching providers - anything shown so far
            // belonged to the failed attempt, so drop it rather than appending to it.
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
                body.set("patientId", patientId);
                body.set("streamId", streamId);
                body.set("__RequestVerificationToken", token);

                return fetch("/AiReview/GenerateCaseSummary", {
                    method: "POST",
                    headers: { "Content-Type": "application/x-www-form-urlencoded" },
                    body: body.toString()
                });
            })
            .then(response => response.json().then(data => ({ ok: response.ok, data })))
            .then(({ ok, data }) => {
                if (!ok) throw new Error(data.error || "The AI service failed.");

                return fetch("/AiReview/RenderSuggestion/" + data.suggestionId)
                    .then(r => r.text())
                    .then(html => {
                        placeholder.classList.add("d-none");
                        feed.classList.add("d-none");
                        const wrapper = document.createElement("div");
                        wrapper.innerHTML = html;
                        listContainer.prepend(wrapper.firstElementChild);
                        const emptyState = document.getElementById("noAiSuggestionsEmptyState");
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
