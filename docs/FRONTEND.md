# Hospital Management System — Frontend Architecture & UI Specification

This document provides a comprehensive technical reference for the frontend architecture, design system, real-time client integrations, component interactions, and specialized workstation views of the **Hospital Management System (Nexus HMS)**.

---

## 1. Frontend Technology Stack & Architecture

The frontend is architected as an enterprise-grade, Server-Side Rendered (SSR) application built on **ASP.NET Core 10 MVC**, augmented by modular vanilla JavaScript and real-time WebSockets/SignalR streaming.

```
┌────────────────────────────────────────────────────────────────────────┐
│                        Client Presentation Layer                       │
├────────────────────────────┬─────────────────────────────┬─────────────┤
│      UI & Styling          │     Real-Time & Streaming   │  Components │
├────────────────────────────┼─────────────────────────────┼─────────────┤
│ • Bootstrap 5.3 + CSS Vars │ • SignalR 7.0.12            │ • Flatpickr │
│ • Custom Design Tokens     │ • NotificationHub           │ • Select2   │
│ • Zero-Flash Theme Engine  │ • AiStreamHub (LLM Stream)  │ • SweetAlert│
│ • Inter + Noto Sans Bengali│ • SVG Sparklines / QR SVG   │ • Razor SSR │
└────────────────────────────┴─────────────────────────────┴─────────────┘
```

### Core Technologies

| Layer / Library | Version / Source | Purpose |
|---|---|---|
| **Rendering Model** | ASP.NET Core 10 Razor (`.cshtml`) | Type-safe SSR with typed ViewModels, Anti-Forgery token validation, and component partials. |
| **CSS Framework** | Bootstrap 5.3.x | Grid layout, responsive utilities, flexbox, modal/dropdown containers. |
| **Design System** | Custom `site.css` (Design Tokens) | Clean slate/charcoal design system using CSS custom properties aliased into Bootstrap variables. |
| **Real-Time Client** | `@microsoft/signalr` 7.0.12 | Live notifications, dynamic chamber queue re-ordering, and LLM token-by-token streaming. |
| **Date & Time Picker** | Flatpickr 4.6.13 | Localized, theme-aware, mobile-disabled datepickers with custom date formatting (`Y-m-d` / `d/m/y`). |
| **Searchable Dropdowns**| Select2 4.1.0 + Bootstrap 5 Theme | AJAX-driven, keyboard-accessible select boxes for medicines, doctors, and ICD diagnoses. |
| **Clinical Alerts** | SweetAlert2 11.10.1 | Contextual confirmation dialogs for clinical decisions (walkout, discharge, safety overrides). |
| **Typography** | Google Fonts (`Inter` & `Noto Sans Bengali`) | Clean sans-serif typography for clinical cockpits and native Bengali script for patient instruction sheets. |

---

## 2. Design Tokens & Styling Architecture (`site.css`)

The application adopts a flat, calm, high-legibility clinical aesthetic designed for prolonged screen use in emergency rooms, outpatient chambers, and pharmacy counters.

### Token Hierarchy & CSS Variables

All colors, surface depths, borders, and geometries are defined as CSS Custom Properties in `:root` and re-declared under `[data-bs-theme="dark"]`. Instead of overriding Bootstrap components via nested classes, custom tokens are aliased directly onto Bootstrap's `--bs-*` variables:

```css
:root {
    /* Surfaces */
    --surface-page: #f7f8fa;
    --surface-card: #ffffff;
    --surface-sunken: #f1f3f5;

    /* Borders & Accents */
    --border-subtle: #e8eaed;
    --text-strong: #1a2027;
    --text-body: #3c4653;
    --text-muted: #6b7684;

    --accent: #0056b3;
    --accent-rgb: 0, 86, 179;
    --accent-hover: #00458f;
    --accent-soft: rgba(0, 86, 179, .08);

    --radius: 10px;
    --radius-sm: 6px;
    --card-pad: 1.75rem;
    --shadow-card: 0 1px 2px rgba(15, 23, 42, .04), 0 1px 1px rgba(15, 23, 42, .03);

    /* Bootstrap Variable Aliasing */
    --bs-body-bg: var(--surface-page);
    --bs-body-color: var(--text-body);
    --bs-border-color: var(--border-subtle);
    --bs-primary: var(--accent);
    --bs-card-bg: var(--surface-card);
    --bs-modal-bg: var(--surface-card);
}

[data-bs-theme="dark"] {
    /* Deep Slate / Charcoal — strictly avoiding pure #000000 */
    --surface-page: #12161c;
    --surface-card: #1a1f27;
    --surface-sunken: #222831;

    --border-subtle: #2b323c;
    --text-strong: #e8ebef;
    --text-body: #c3cad3;
    --text-muted: #8b95a3;

    --accent: #5a9cf8;
    --accent-rgb: 90, 156, 248;
    --accent-hover: #7bb0fa;
    --accent-soft: rgba(90, 156, 248, .14);
    --shadow-card: 0 1px 2px rgba(0, 0, 0, .28), 0 1px 1px rgba(0, 0, 0, .24);
}
```

### The Keystone `.pro-card` Component
Rather than heavy box shadows and skeuomorphic gradients, views encapsulate information inside `.pro-card`:
- **Hairline Border:** `1px solid var(--border-subtle)`.
- **Subtle Elevation:** `box-shadow: var(--shadow-card)`.
- **Geometric Consistency:** `border-radius: var(--radius)` (10px) with inner padding `var(--card-pad)` (1.75rem).

### Zero-Flash Theme Initialization
To prevent a jarring "white-to-dark flash" on page refresh, a lightweight synchronous script in `<head>` executes before the first browser paint:
```javascript
(function () {
    var stored = localStorage.getItem('hms-theme');
    var theme;
    if (stored === 'light' || stored === 'dark') {
        theme = stored;
    } else {
        var hour = new Date().getHours();
        var isNight = hour >= 19 || hour < 6;
        var prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
        theme = (prefersDark || isNight) ? 'dark' : 'light';
    }
    document.documentElement.setAttribute('data-bs-theme', theme);
})();
```

### High-Contrast Print Overrides (`@media print`)
When printing prescriptions or invoices, the print stylesheet unconditionally forces light theme surfaces, suppresses web chrome (navbars, toggles, footers, toast containers), and ensures maximum legibility for thermal and laser printers:
```css
@media print {
    :root, [data-bs-theme="dark"] {
        color-scheme: light;
        --surface-page: #ffffff;
        --surface-card: #ffffff;
        --surface-sunken: #f8f9fa;
        --border-subtle: #dee2e6;
        --text-strong: #000000;
        --text-body: #212529;
        --text-muted: #6c757d;
    }
}
```

---

## 3. Real-Time Client Architecture & SignalR Streams

The frontend maintains two specialized SignalR persistent connections for clinical responsiveness and AI integration.

```
┌────────────────────────────────────────────────────────────────────────┐
│                        SignalR Architecture                            │
├───────────────────────────────────┬────────────────────────────────────┤
│         NotificationHub           │            AiStreamHub             │
├───────────────────────────────────┼────────────────────────────────────┤
│ • Channel: /notificationHub       │ • Channel: /aiStreamHub            │
│ • Scoping: Doctor Chamber Groups  │ • Scoping: Per-Request Stream UUID │
│ • Push: Queue re-ranking, vitals, │ • Push: Token deltas, citations,   │
│   consultation completion toasts  │   failover / provider restart      │
└───────────────────────────────────┴────────────────────────────────────┘
```

### A. NotificationHub (`wwwroot/js/notifications.js`)
Mounted in `Views/Shared/_Layout.cshtml` whenever an authenticated user has the **Doctor** or **Assistant** role.

1. **Scoped Group Membership:** Upon connection, the client issues `connection.invoke("JoinDoctorGroup", doctorId)`. Doctors and their assigned assistants share an isolated communication channel, preventing notifications from leaking across chambers.
2. **Consultation Completed (`ReceiveNotification`):**
   - Triggers an audio bell chime (`#dingSound`).
   - Displays a floating Bootstrap Toast (`#notificationToast`).
   - Smoothly animates and removes the appointment row from the queue (`opacity: 0; transform: translateX(20px)`).
3. **Patient Sent In (`PatientSentIn`):**
   - Automatically appends an incoming patient consultation card to `#active-consultations-container` on the doctor's screen without a page reload.
   - Embeds color-coded NEWS2 triage badges (`Emergency`, `Urgent`, `Normal`).
   - Initializes an AI Review placeholder with a loading spinner.
4. **Background AI Summary Arrival (`ArrivalAiReady` / `ArrivalAiFailed`):**
   - Once the server finishes generating the clinical pre-visit case summary in the background, it triggers `ArrivalAiReady`.
   - The client fetches the server-rendered HTML via `/AiReview/RenderSuggestion/{id}?compact=true` and smoothly swaps the loading spinner with the reviewable card.

### B. AI Stream Hub (`wwwroot/js/ai-stream.js`)
Drives live token-by-token generation for Clinical Case Summaries and Bangla Patient Instructions.

1. **Stream ID Generation:** Uses `crypto.randomUUID()` to generate a unique per-request stream channel.
2. **Live Chunk Rendering (`ReceiveChunk`):**
   - Buffers incoming token deltas and dynamically strips and formats record citation markers:
     ```javascript
     function stripMarkers(text) {
         return text.replace(/\[\[rec:(\d+)\]\]/g, function (_, id) {
             return ' <sup class="text-muted">[' + id + ']</sup>';
         });
     }
     ```
3. **Stream Failover Handling (`StreamRestarted`):** If an upstream provider (e.g., Gemini) times out or hits a rate limit, the server seamlessly falls back to Groq or Anthropic. The client receives `StreamRestarted`, flushes the buffer to discard partial hallucinated tokens, and displays a provider-switch status notification.
4. **Completion Handoff:** On stream completion, the client requests the compiled, signed suggestion partial from `/AiReview/RenderSuggestion/{id}` and mounts it with active `Accept`, `Edit`, and `Reject` buttons.

---

## 4. Interactive Client-Side Systems

### A. Portal 15-Minute Slot Engine (`wwwroot/js/portal-booking.js`)
Provides an interactive appointment booking and rescheduling calendar in the Patient Portal (`/Portal/Book` and `/Portal/Reschedule`).

* **Dynamic AJAX Fetching:** When a patient selects a doctor and date, an asynchronous call queries `/Portal/GetAvailableSlots?doctorId={id}&date={date}`.
* **Morning vs. Afternoon Grouping:** Slots between 09:00 AM and 05:00 PM are partitioned into Morning and Afternoon flex-chip groups.
* **Conflict & Availability Visualization:**
  - **Available:** Interactive chip (`btn-outline-primary slot-chip`), bound to a hidden UTC datetime input.
  - **Unavailable / Colliding:** Strikethrough disabled button (`btn-light opacity-50 text-decoration-line-through`) with a native HTML `title` indicating the conflict reason (e.g., "Doctor already booked", "Slot in the past").
* **Reason-for-Visit Quick Chips:** Clickable symptoms (`Fever`, `Cough`, `Chest Pain`, `Follow-up`) that automatically concatenate into the clinical intake text area.

### B. Dependent & Pediatric Profile Switcher (`_Layout.cshtml`)
Supports multi-patient family account management directly from the top navigation bar.

* **Contextual State:** Primary patients with linked dependent children (`ViewBag.Dependents`) see a dedicated profile switcher dropdown.
* **Claims-Safe Profile Switch:** Selecting a child executes a POST to `/Portal/SwitchProfile`, securely switching the active patient context via claims without exposing cross-patient IDOR vulnerabilities.
* **Visual Distinction:** When managing a child's profile, the profile pill transitions from primary blue to an alert cyan-info pill (`btn-info text-dark`) with an icon (`bi-person-heart`) to prevent accidental clinical actions under the wrong identity.

---

## 5. Specialized Workstations & Clinical Views

```
┌────────────────────────────────────────────────────────────────────────┐
│                        Specialized Workstations                        │
├──────────────────────────┬──────────────────────────┬──────────────────┤
│     Doctor Cockpit       │   Assistant Triage       │ Reception Desk   │
├──────────────────────────┼──────────────────────────┼──────────────────┤
│ • 3-Column Layout        │ • Physiological Sliders  │ • Bed Grid       │
│ • ΔNEWS2 SVG Sparkline   │ • NEWS2 Real-Time Score  │ • Chamber Queues │
│ • AI Case Summary Gate   │ • Starvation-Aging Queue │ • Quick Billing  │
│ • ATC Graph Drug Alerts  │ • Rapid Triage Badges    │ • Intake Wizard  │
└──────────────────────────┴──────────────────────────┴──────────────────┘
```

### 1. The Clinical Cockpit (`/DoctorDashboard/Cockpit`)
A 3-column clinical workstation unifying consultation notes, diagnostic safety checks, and longitudinal monitoring.

```
┌────────────────────────────────────────────────────────────────────────┐
│ [SAFETY HEADER] UHID | Age | Blood Group | Severe Allergies (Red Badge)│
├───────────────────┬───────────────────────────────┬────────────────────┤
│ Left Column (30%) │ Center Column (45%)           │ Right Column (25%) │
├───────────────────┼───────────────────────────────┼────────────────────┤
│ • ΔNEWS2 Radar    │ • Consultation Encounter Notes│ • AI Pre-Visit     │
│   (SVG Sparkline) │ • ICD-10 Diagnosis Picker     │   Case Summary     │
│ • Vitals History  │ • Rx Prescription Builder     │ • Accept / Edit /  │
│ • Active Rx List  │ • ATC Graph Cross-Reactivity  │   Reject Action    │
│ • Medical Records │   & Drug-Drug Interaction     │   Gate             │
│   Timeline        │   Safety Banners              │ • Citations [1][2] │
└───────────────────┴───────────────────────────────┴────────────────────┘
```

* **Omnipresent Patient Safety Header:** Renders UHID, chronological age, blood group, and severe drug allergies in a prominent dark banner (`.cockpit-header`). Severe/life-threatening allergies pulse in crimson (`#fee2e2` / `#991b1b`).
* **Longitudinal $\Delta\text{NEWS2}$ Deterioration Radar:**
  - Displays consecutive NEWS2 readings across time.
  - Generates an inline SVG polyline sparkline charting physiological trends.
  - Highlights rapid deterioration with a SignalR warning badge if NEWS2 deteriorates by $\ge 2$ points within 12 hours:
    $$\Delta\text{NEWS2} = \text{NEWS2}_{\text{current}} - \text{NEWS2}_{\le 12\text{h}} \ge 2$$
* **ATC Classification Drug Safety Alerts:** When adding medications, the safety engine validates prescriptions against documented allergies and active drugs:
  - **Cross-Reactivity Alert:** Flags beta-lactam cross-allergies (e.g., Penicillin allergy catching Amoxicillin) via an amber warning banner.
  - **Severe DDI Alert:** Flags high-risk clinical pairs (e.g., Warfarin + Ibuprofen) via a critical danger modal.
* **AI Clinical Summary Review Gate:** Pre-visit case summaries are gated behind strict clinician review (`Accept`, `Edit`, or `Reject`), strictly enforcing human-in-the-loop validation before records are committed.

---

### 2. Assistant Triage & Chamber Queue (`/Vitals/Record`)
Enforces bedside vital signs data capture and automated acuity grading:

* **Physiological Range Boundaries:** Form controls validate 7 clinical parameters with hard boundaries (e.g., Heart Rate: 20–300 bpm, SpO2: 50–100%, Systolic BP: 40–300 mmHg).
* **Instant Client NEWS2 Score:** JavaScript evaluates vital scores across 7 parameters in real time and colors the score badge:
  - `0 - 4`: **Normal** (Green)
  - `5 - 6`: **Urgent / Medium** (Yellow/Amber)
  - `7+` or any single parameter scoring 3: **Emergency** (Red)
* **Starvation-Prevention Aging Re-ranking:** Submitting vitals invokes `AcuityQueueService`, dynamically re-ranking the doctor's queue using the aging formula:
  $$\text{PriorityScore} = (W_{\text{acuity}} \times \text{NEWS2}) + (W_{\text{wait}} \times \text{MinutesWaited})$$

---

### 3. Front-Desk Reception Console (`/Reception`)
Consolidates high-volume hospital operations on a single screen:

* **Interactive Bed Occupancy Grid:** Visualizes beds categorized by `ICU`, `Cabin`, and `General Ward`. Occupied beds display patient UHID, admission duration, and quick transfer shortcuts.
* **Chamber Waitlist Counters:** Real-time counters showing patients waiting, currently in consultation, and completed per doctor chamber.
* **Quick Intake Actions:** Modals for fast patient registration, slot booking, and cashier bill collection.

---

### 4. Pharmacy Dispensing & Inventory Workstation (`/Prescriptions/Dispense`)
* **FEFO Batch Allocation Cards:** Groups medication stock by `MedicineBatches`, showing batch number, expiry date, and remaining count.
* **Expired Stock Protection:** Expired batches are grayed out with a red `Expired` badge. The dispensing button is locked if non-expired batch stock is insufficient.
* **30-Day Expiry Warnings:** Near-expiry batches display an orange alert indicator.

---

### 5. Public Anti-Forgery Verification (`/Verify/Prescription`)
An unauthenticated (`[AllowAnonymous]`), mobile-optimized verification screen accessed by scanning the HMAC-signed QR code printed on prescriptions.

* **Decoded Clinical Verification:** Displays patient UHID, issuing doctor name, consultation timestamp, and prescribed medications.
* **Cryptographic Tamper Check:** Recomputes the server HMAC-SHA256 signature against token payload data:
  - **Valid Token:** Displays a green verified shield (`Verified Authentic Prescription`).
  - **Tampered / Expired Token:** Displays a red warning banner (`Invalid or Tampered Prescription Token`).

---

## 6. Print Engine Architecture (`/Prescriptions/Print`)

Prescription printouts and AI-generated Bangla Instruction Sheets are engineered for clinical clarity and zero formatting distortion.

```
┌────────────────────────────────────────────────────────┐
│ NEXUS HOSPITAL                        PRESCRIPTION     │
│ 123 Healthcare Ave, Dhaka             Rx #1042         │
├────────────────────────────────────────────────────────┤
│ Patient: Rahim Uddin (PT-202610-0001)  Date: 2026-10-22 │
│ Age: 45 yrs | Gender: Male             Blood: B+       │
├────────────────────────────────────────────────────────┤
│ Diagnosis: Essential Hypertension (I10)                │
│                                                        │
│ Rx:                                                    │
│ 1. Napa 500mg (Paracetamol)                            │
│    1 tablet three times daily after meals x 5 days     │
│ 2. Seclo 20mg (Omeprazole)                             │
│    1 capsule once daily before breakfast x 14 days     │
├────────────────────────────────────────────────────────┤
│ [HMAC-SHA256 QR]        Doctor: Dr. Mock (Consultant)  │
│ [SVG Stamp]             Signature: ___________________ │
└────────────────────────────────────────────────────────┘
```

1. **Inline SVG QR Code:** Generated dynamically server-side via `QRCoder` (`SvgQRCode`) and embedded directly into the HTML as `<svg>`. This avoids external HTTP asset requests, ensuring instant and pixel-perfect printing even offline.
2. **Bilingual Typography Fallback:** Prescriptions containing translated Bengali directions utilize `Noto Sans Bengali` with automatic Unicode script fallback.
3. **Print-Clean CSS Rules:**
   - Explicit page margins: `@page { margin: 15mm; size: A4 portrait; }`.
   - Stripped interactive elements (`.btn`, `.navbar`, `.toast-container`, `.theme-toggle` have `display: none !important`).
   - Forced solid black borders (`1px solid #000000`) for clear separation on monochrome clinical printers.

---

## 7. Form Security, Anti-Forgery, and Validation

All client-side form submissions adhere to strict web security principles:

* **CSRF / XSRF Protection:** Every Razor form injects `@Html.AntiForgeryToken()`. AJAX and fetch calls dynamically harvest and append `__RequestVerificationToken`:
  ```javascript
  const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
  ```
* **jQuery Unobtrusive Client Validation:** Form models annotated with `[Required]`, `[Range]`, `[StringLength]`, and `[RegularExpression]` automatically emit `data-val-*` attributes, evaluated by `jquery.validate.unobtrusive.min.js` before network submission.
* **SweetAlert Gating:** Destructive or irreversible clinical actions (cancelling appointments, patient walkouts, safety overrides) require interactive confirmation modals before submitting the underlying form.
