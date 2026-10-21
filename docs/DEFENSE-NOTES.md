# Defense Notes

A rehearsable demo script, grounded answers to the questions most likely to come up, and the
known limitations stated up front rather than discovered by the examiner.

## Demo script (~12 minutes)

Reseed immediately before presenting, so the data is in exactly the state described here:

```bash
dotnet run -- --seed-demo
```

All one-click logins below are on `/Auth/Login` (Development environment only — no password
needed). Credentials for accounts not on that page are in [README.md](../README.md#demo-credentials).

1. **Receptionist** (`reception1`, one-click)
   - Reception Operations Dashboard (`/Reception`): view today's hospital throughput at a glance —
     appointment queue status, current bed occupancy, active inpatient admissions, and outstanding bills.
   - Patients → Register a new patient. Check "Issue portal login." Note the UHID and temporary
     password shown on the confirmation banner — this is the account used in step 5.
   - Appointments → Book. Choose a doctor who is **not** the Assistant's doctor in step 2 (i.e.
     `dr2`, not `drmock`) — this is what proves the Receptionist sees the whole hospital, not one
     doctor's queue.
   - Admissions → Admit a patient, assign a bed → Discharge → Generate the bill (showing auto-calculated
     bed rent aggregated across `BedTransfers`) → record a partial payment. The bill's status updates
     to `PartiallyPaid` immediately.

2. **Assistant** (`mock-assistant`, one-click)
   - Appointments (the queue) shows **only `drmock`'s patients** — Rahim Uddin is already
     `InConsultation` with a seeded **Urgent** triage badge from the reseed.
   - Pick a `Scheduled` patient → Vitals → Create → enter observations (bounded by physiological range
     validation) → the resulting NEWS2 score/priority is computed and shown immediately, then → Send In.

3. **Doctor** (`drmock`, one-click) — *open in a second browser profile as `dr2` first, to make
   the next point land*
   - `drmock`'s dashboard shows only `drmock`'s InConsultation patients; `dr2`'s dashboard (logged
     in separately) shows only theirs. Send In the patient from step 2 and point out `drmock`'s
     screen updates **without a reload** — this is the live SignalR proof, not a screenshot claim.
   - Enter the **Clinical Cockpit** (`/DoctorDashboard/Cockpit/{id}`) — a consolidated, unified
     workspace presenting patient vitals, NEWS2 score badge, allergy contraindication flags, medical
     history, and active prescriptions side-by-side.
   - Click **Generate Case Summary** → watch it stream in live via WebSocket, synthesizing recent
     medical records, active allergies, latest vitals, and active prescriptions with citation markers.
     Accept (or Edit, or Reject — all three are real, distinct code paths, not three buttons doing the same thing).
   - Add a medical record, then write a prescription for **Mizanur Rahman** with **Napa 500mg and
     Ace 500mg together** — this fires two real safety warnings at once (duplicate Paracetamol
     therapy, and his recorded Paracetamol allergy) on data that was already seeded before this
     feature existed, not staged for the demo. Type an override reason and save — both the exact
     warnings shown and the reason are now a permanent, inspectable part of that prescription.
   - On that prescription's page, click **Generate Instructions** to produce a Bangla-language
     patient instruction sheet, watch it stream in, Accept it, then **Print Instruction Sheet** —
     the only document in the system a patient without medical training actually reads. (No
     internet at the venue? Rahim Uddin's dispensed Napa prescription already has one
     pre-accepted — open that instead and skip straight to Print.)
   - Mark the consultation Completed.

4. **Pharmacist** (`pharmacistmock`, one-click)
   - Prescriptions queue → open the one just written → Dispense. Inventory stock decrements by
     the dispensed quantity atomically within a database transaction.

5. **Patient** (the account from step 1, or `PT-202610-0001` Rahim Uddin, real username/password)
   - Portal shows only that patient's own appointments, records, prescriptions, and bills.
   - **Dependent Account Switching:** Switch active profile between primary account and linked
     pediatric dependents (e.g. Rahim Uddin managing Ayan Uddin) using the portal family switcher.
   - **Direct Self-Service Booking:** Browse doctors, inspect available 15-minute appointment slots,
     and book directly via `/Portal/Book` (or reschedule via `/Portal/Reschedule`).
   - **Online Bill Payment via bKash:** Initiate checkout on an unpaid bill → redirects through
     bKash tokenized gateway sandbox → captures payment and logs an unalterable `PaymentTransaction` row.
   - **IDOR check, live**: hand-edit the URL to another patient's id —
     `/Patients/Details/1`, `/MedicalRecords`, `/Prescriptions` — and show each one 403s or
     redirects rather than leaking another patient's chart. This is the single most examiner-proof
     five seconds in the whole demo.

6. **Admin** (`admin`, one-click)
   - Reports: the panels are populated with real numbers pulled from everything the walkthrough
     just generated, not placeholder data — revenue, occupancy, the 14-day appointment trend, the
     triage mix from step 2, the AI governance panel (token counts, latency, verdict distribution
     across both the case summary and the Bangla instruction sheet from step 3), and the
     **Prescription Safety Net** panel showing the exact duplicate-therapy and allergy-conflict
     warnings from step 3 — with an explicit "0 AI calls" badge, since all four checks behind it
     are deterministic SQL, not a model call.
   - Staff → edit a user and leave the password field blank → save → confirm they can still log
     in with the old password. (This exercises a real bug found and fixed during testing —
     see [Anticipated questions](#anticipated-questions) if asked how it was caught.)

## Delivered vs. the original AI roadmap

An earlier planning document ("Nexus Clinical AI Roadmap") sketched an 8-phase clinical-AI
product — triage co-pilot, prescription safety net, ambient consultation scribe, discharge
packet, a full evaluation chapter. What actually shipped is robustly delivered in the core clinical
pathways:

| Phase | Original scope | Status |
|---|---|---|
| P0 — Data model repair | `MedicalRecord`, `PatientAllergy`, extended vitals, structured dosing, BCrypt | **Done.** One loose end: the legacy `Patient.MedicalHistoryJson` shadow column was preserved for backward compatibility after the migration to `MedicalRecord`. |
| P1 — AI spine | `AiSuggestion` table, PHI scrubbing, review UI, typed errors, schema-enforced output, prompt caching | **Done**, and exceeded in one place: a 3-provider (Gemini/Groq/Anthropic) encrypted, priority-ordered fallback chain with live health diagnostics, versus the one hardcoded key originally planned. Schema enforcement is handled via structured prompts and regex/DTO extraction. |
| P2 — Case summary with citations | Broad context (records + vitals + meds + admissions), citation grounding, content-hash caching | **Delivered.** Context aggregates recent medical records, active allergies, latest vitals (including NEWS2 priority score), and active/recent prescriptions. Grounded with server-validated `[[rec:id]]` citations against real records. Implemented dynamic composite cache invalidation in memory keyed on entity counts and latest `UpdatedAt`/`CreatedAt` ticks across all four clinical domains to avoid redundant API billing. (Inpatient admissions are omitted from prompt context). |
| P3 — Triage co-pilot | Deterministic NEWS2 + AI narration + a priority-sorted live board | **Delivered deterministically.** `News2Calculator` is built and thoroughly tested (65 boundary tests + physiological range validation). Real-time SignalR notifications push queue status updates when an assistant sends in a patient. |
| P4 — Prescription safety net | Duplicate therapy, allergy conflict, stock substitution, pediatric dose, an override UI | **Delivered — see the demo script above.** All four checks are real and fire against real seeded traps (Napa/Ace duplicate therapy, Nasrin's and Mizan's allergies, Zmax's zero stock, and pediatric strength thresholds). Pure deterministic logic with no external AI dependency. |
| P5 — Ambient consultation scribe | Free-text intake box, tool-calling medicine lookup, form autofill, edit-diff capture | **Not built.** No audio ingestion or ambient scribe. Scoped out as future work in favor of hardening the Clinical Cockpit and financial ledger. |
| P6 — Discharge packet | Admission-level clinical summary + a Bangla patient sheet, with bed-rent reconciliation | **Substantially delivered.** Bed-rent aggregation across `BedTransfers` is fully implemented in the billing module (`BillsController.Create`). The Bangla patient instruction sheet is delivered with live streaming generation, Accept-before-print review gate, and print formatting for prescriptions. |
| P7 — Evaluation & write-up | 30–50 seeded patients, an acceptance dashboard, a cost/latency report, ablations, a failure taxonomy | **Covered via comprehensive testing & admin governance.** The Reports page provides an AI Governance dashboard (verdict distribution, token counts, and latency) and Safety Net metrics. Verified by **277 automated xUnit tests** across unit, integration, and security suites. |
| P8 — Stretch (pgvector, charge capture, NL analytics) | — | Not built; out of scope. |

## Anticipated questions

**How do you stop the AI from writing something wrong into a patient's record?**
It cannot write to a clinical table at all. `ClinicalAiService` writes exactly one thing — an
`AiSuggestion` row with `Verdict = Pending`. Accept/Edit/Reject (`AiReviewController`) only ever
update that same row's own verdict, reviewer, and timestamp — none of the three creates or
touches a `MedicalRecord` or `Prescription`. If a doctor acts on what they read, they write it
themselves through the ordinary clinical forms, completely independent of the AI subsystem. See
the sequence diagram in [ARCHITECTURE.md](ARCHITECTURE.md#2-the-ai-spine--end-to-end-sequence).

**Do you send patient data to a third party?**
`PhiScrubber` pseudonymizes the patient's name and emergency contact name (`Patient-{UHID}`,
`Contact-{UHID}`) and redacts phone numbers before any text leaves the process for an external
provider. The substitution map is built and discarded per request — it never persists — and the
streamed response is rehydrated with the real name before it reaches the doctor's screen, so the
pseudonym is genuinely never visible to the provider's counterpart. This was verified live during
testing, not assumed from the code.

**Why NEWS2 Scale 1 only, and why aren't the pediatric patients scored?**
Scale 2 (for COPD patients) requires a target-SpO2 field `PatientVital` doesn't carry;
approximating it without that field would be clinically wrong, so it's out of scope rather than
silently wrong. NEWS2 itself is validated for adult patients — the two seeded infant patients
deliberately have no vitals/triage data for the same reason: a real score implies a validity the
tool doesn't have for that population.

**How is authorization enforced, and what's the trickiest part of it?**
A global `FallbackPolicy` (`Program.cs`) inverts ASP.NET Core's default so every endpoint requires
authentication unless explicitly `[AllowAnonymous]` — before this existed, `StaffController` and
`MedicinesController` were reachable anonymously (confirmed live, not theoretical). The trickiest
part: stacked `[Authorize(Roles=...)]` attributes are **ANDed**, not overridden — a method-level
attribute adding a role does nothing if the class-level attribute doesn't already include it. This
broke the patient portal's print action during development (a patient printing their own
prescription kept getting redirected to Access Denied); the fix was widening the class-level
attribute and adding *narrowing* attributes per action, never the reverse. Full diagram in
[ARCHITECTURE.md](ARCHITECTURE.md#3-authorization-model).

**How do you prevent one patient from reading another patient's records?**
`PortalController` never trusts a route or query-string id. Every action resolves the caller's
own `Patient` row from the `UserId` claim set at login, then filters strictly on that patient's
own `Id` or authorized linked dependents. Role membership (`[Authorize(Roles = "Patient")]`)
proves *a* patient is asking, not *which* patient — identity and claims resolution is what
actually enforces ownership. Demonstrated live in the demo script above.

**How does dependent / family account management work for pediatric patients?**
`Patient` records support a self-referencing `GuardianPatientId` foreign key. When a parent logs
into the patient portal, `PortalController.ResolvePatientContextAsync` loads both the primary patient
and all linked dependents. The parent can toggle the active patient context via a secure UI banner.
Strict IDOR validation ensures a user can only select a patient ID that exists in their authorized
family set (`AllowedIds = { PrimaryPatient.Id } U Dependents`); any attempt to inject an unlinked ID
returns a 403 Forbidden.

**How does the bKash payment gateway integration work, and how does it prevent financial race conditions?**
`BkashPaymentService` connects to the bKash Tokenized Checkout API. It requests and caches OAuth ID
tokens in `IMemoryCache`, initiates payment creation correlated with `Bill.Id`, and executes the payment
upon successful callback. Once verified, it writes an immutable `PaymentTransaction` row recording the
bKash `TrxID`, payment timestamp, and amount. A database-level filtered unique index on `PaymentTransactions.TransactionId` (`WHERE "TransactionId" IS NOT NULL`) guarantees idempotency against browser refreshes and callback network races, while PostgreSQL's `xmin` concurrency token on `Bill` prevents double-payments if a cashier records cash at the same moment an online payment executes.

**How does doctor schedule and slot management work?**
`DoctorScheduleService` partitions the working day (09:00 AM to 05:00 PM) into 15-minute consultation
slots using `HospitalClock` (Asia/Dhaka UTC+6). It queries active appointments and scheduled surgical
procedures (`Operations` table) to filter out booked intervals or surgery conflicts. When a booking is submitted,
server-side pre-flight checks verify doctor and patient availability (`CheckDoctorCollisionAsync`, `CheckPatientCollisionAsync`).
To prevent simultaneous double-booking during concurrent `INSERT`s (which PostgreSQL `xmin` cannot catch because `xmin` only
operates on existing row `UPDATE`/`DELETE`), the database enforces a filtered unique index:
`CREATE UNIQUE INDEX ix_appointments_doctor_slot_active ON "Appointments" ("DoctorId", "AppointmentDatetime") WHERE "Status" <> 2;`

**Where are the AI provider API keys stored?**
Encrypted at rest via ASP.NET Data Protection (`ApiKeyProtector`), resolved fresh on every AI call
by `AiProviderResolver`, and never returned to any view — the admin UI only ever shows
"configured" / "not configured," never the key itself.

**How did you verify the NEWS2 implementation and application logic are actually correct?**
**284 automated xUnit tests** cover the entire system (executed against EF Core In-Memory for fast CI execution):
- 65 NEWS2 boundary theories testing all seven physiological parameters and the NHS "single red score" rule, plus extreme input boundary validations.
- PHI scrubbing/rehydration round trips with word-boundary regex protection and stateful lookahead streaming buffer.
- AI provider ping diagnostics and priority-ordered failover logic.
- Immutable payment ledger derivations and bill totals recalculation with null navigation guards and ledger summation.
- Prescription safety net checks: duplicate therapies, drug-class allergy cross-reactivity (Penicillin -> Amoxicillin/Ampicillin), chemical salt disambiguation (Sulfa != Sulfate), active medication windows, stock substitution, and pediatric potent drug dosing.
- Integration tests with EF Core verifying referential integrity (restricting deletion of active medicines and billed admissions) and concurrent payment collisions.
- Security access control tests verifying role gating and IDOR isolation across doctors, assistants, cashiers, and patients.

*(Note on Database Engine Testing: EF Core In-Memory validates application-layer pre-checks, entity constraints, domain events, and controllers. Production relational locks, filtered unique indexes, and PostgreSQL `xmin` rowversions are enforced by the PostgreSQL database engine via EF Core migration scripts).*

**Did testing actually find anything, or is the test suite just for show?**
Real bugs caught by testing:
1. `PhiScrubber` used a plain `string.Replace` for name substitution, causing a patient named "Ali" to corrupt the word "Alia" elsewhere in the note — caught by unit tests, fixed with a word-boundary regex.
2. `StaffController.Edit` accepted a blank password field to mean "keep current password," but `Password` was non-nullable, causing model validation to reject submissions — caught during integration verification and fixed.
3. `Bill.RecalculateTotals()` risked marking an unpaid bill as `Paid` if loaded without `.Include(b => b.BillItems)` (evaluating subtotal = 0). Fixed with an uninitialized navigation guard, automated ledger summation, and requiring `SubtotalAmount > 0` before transitioning to `Paid`.
4. Concurrent appointment booking race conditions on brand-new rows — resolved by adding filtered unique index `ix_appointments_doctor_slot_active` (`WHERE "Status" <> 2`) in PostgreSQL to stop concurrent `INSERT` double-bookings.
5. Allergy false-positives on chemical salts (e.g. `Sulfa` matching `Morphine Sulfate`) and false-negatives on beta-lactam cross-reactivity (e.g. `Penicillin` missing `Amoxicillin`) — caught and resolved via drug class mapping and chemical salt token disambiguation.

## Known limitations

Stated proactively rather than left for the examiner to find:

- **`Operation` is fully modeled in schema and cross-checked by doctor scheduling, but has no dedicated surgeon UI.** The entity, its migration, and
  its `OnDelete` configuration all exist; outpatient appointment scheduling proactively checks `Operations` to avoid double-booking surgeons, but dedicated surgical theatre console views were scoped out because none of the six active roles being defended require them.
- **NEWS2 Scale 2 (COPD) is not implemented** — requires a target-SpO2 parameter that `PatientVital` does not track.
- **No forced password change on a patient's first portal login.** The receptionist-issued
  temporary password works indefinitely unless changed voluntarily.
- **No pagination on most directory pages** (Patients, Staff, Bills, Admissions, Beds) —
  `MedicalRecordsController` is the only one with real `Skip`/`Take` paging. Fine at demo scale;
  would need addressing before a directory grew into the hundreds of rows.
- **Pediatric dosing combines a dynamic age evaluation (`IsPediatric`), weight recording (`PatientVitals.WeightKg`), and a strict 5mg threshold for potent/narrow-index pediatric drugs** alongside the 500mg adult-strength heuristic. Full pediatric pharmacological mg/kg/day dosing tables remain a candidate for future pharmacy module expansion.
- **The Bangla instruction sheet is scoped to one prescription, not a full hospital admission.**
  It explains the medicines on the prescription it was generated from, rather than synthesizing an admission course.
- **The database password was committed in plaintext in early history.** It has since been moved
  to user-secrets and `appsettings.json` holds only a placeholder (see README), but the value
  remains in old commits — rotating the actual PostgreSQL password is the real fix, and is a
  local/environment concern rather than an application code change.

