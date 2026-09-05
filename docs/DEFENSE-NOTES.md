# Defense Notes

A rehearsable demo script, grounded answers to the questions most likely to come up, and the
known limitations stated up front rather than discovered by the examiner.

## Demo script (~10 minutes)

Reseed immediately before presenting, so the data is in exactly the state described here:

```bash
dotnet run -- --seed-demo
```

All one-click logins below are on `/Auth/Login` (Development environment only — no password
needed). Credentials for accounts not on that page are in [README.md](../README.md#demo-credentials).

1. **Receptionist** (`reception1`, one-click)
   - Patients → Register a new patient. Check "Issue portal login." Note the UHID and temporary
     password shown on the confirmation banner — this is the account used in step 6.
   - Appointments → Book. Choose a doctor who is **not** the Assistant's doctor in step 2 (i.e.
     `dr2`, not `drmock`) — this is what proves the Receptionist sees the whole hospital, not one
     doctor's queue.
   - Admissions → Admit a patient, assign a bed → Discharge → Generate the bill → record a
     partial payment. The bill's status updates to `PartiallyPaid` immediately.

2. **Assistant** (`mock-assistant`, one-click)
   - Appointments (the queue) shows **only `drmock`'s patients** — Rahim Uddin is already
     `InConsultation` with a seeded **Urgent** triage badge from the reseed.
   - Pick a `Scheduled` patient → Vitals → Create → enter observations → the resulting NEWS2
     score/priority is computed and shown immediately, then → Send In.

3. **Doctor** (`drmock`, one-click) — *open in a second browser profile as `dr2` first, to make
   the next point land*
   - `drmock`'s dashboard shows only `drmock`'s InConsultation patients; `dr2`'s dashboard (logged
     in separately) shows only theirs. Send In the patient from step 2 and point out `drmock`'s
     screen updates **without a reload** — this is the live SignalR proof, not a screenshot claim.
   - Open the patient's chart → Generate Case Summary → watch it stream in → Accept (or Edit, or
     Reject — all three are real, distinct code paths, not three buttons doing the same thing).
   - Add a medical record, write a prescription, Mark Completed.

4. **Pharmacist** (`pharmacistmock`, one-click)
   - Prescriptions queue → open the one just written → Dispense. Inventory stock decrements by
     the dispensed quantity.

5. **Patient** (the account from step 1, real username/password)
   - Portal shows only that patient's own appointments, records, prescriptions, and bills.
   - **IDOR check, live**: hand-edit the URL to another patient's id —
     `/Patients/Details/1`, `/MedicalRecords`, `/Prescriptions` — and show each one 403s or
     redirects rather than leaking another patient's chart. This is the single most examiner-proof
     five seconds in the whole demo.

6. **Admin** (`admin`, one-click)
   - Reports: the six panels are populated with real numbers pulled from everything the last five
     minutes just generated, not placeholder data — revenue, occupancy, the 14-day appointment
     trend, the triage mix from step 2, and the AI governance panel (token counts, latency,
     verdict distribution) covering the case summary from step 3.
   - Staff → edit a user and leave the password field blank → save → confirm they can still log
     in with the old password. (This exercises a real bug found and fixed during Phase 6 testing —
     see [Anticipated questions](#anticipated-questions) if asked how it was caught.)

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
own `Id`. Role membership (`[Authorize(Roles = "Patient")]`) proves *a* patient is asking, not
*which* patient — the identity resolution is what actually enforces ownership. Demonstrated live
in the demo script above, not just asserted.

**Where are the AI provider API keys stored?**
Encrypted at rest via ASP.NET Data Protection (`ApiKeyProtector`), resolved fresh on every AI call
by `AiProviderResolver`, and never returned to any view — the admin UI only ever shows
"configured" / "not configured," never the key itself.

**How did you verify the NEWS2 implementation is actually correct?**
65 xUnit tests, most of them boundary-value theories on all seven NEWS2 parameters (the `switch`
statements in `News2Calculator` are all `<=` comparisons, so off-by-one at each boundary is the
realistic failure mode) plus explicit tests for the NHS "single red score" rule — a patient with
one severely abnormal reading escalates to Urgent even when the aggregate score alone wouldn't.
Run `dotnet test` to see all of them pass.

**Did testing actually find anything, or is the test suite just for show?**
Two real bugs, both found by testing rather than code review. First, `PhiScrubber` used a plain
`string.Replace` for name substitution, which meant a patient named "Ali" would corrupt the
unrelated word "Alia" elsewhere in the same note — caught by a test written specifically for that
scenario, fixed with a word-boundary regex, now green. Second, `StaffController.Edit` accepted a
blank password field to mean "keep the current password," but `Password` is a non-nullable
string, so ASP.NET Core's implicit-required validation rejected the whole submission before that
logic could ever run — a genuinely dead code path that only a live end-to-end submission (not a
unit test, not a code read) surfaced. Both are fixed and verified.

## Known limitations

Stated proactively rather than left for the examiner to find:

- **`Operation` is fully modeled but has no controller or views.** The entity, its migration, and
  its `OnDelete` configuration all exist; surgical scheduling was scoped out because none of the
  six roles being defended need it, and building an unused feature to look complete would be worse
  than naming the gap.
- **NEWS2 Scale 2 (COPD) is not implemented** — see the Q&A above.
- **No forced password change on a patient's first portal login.** The receptionist-issued
  temporary password works indefinitely unless changed voluntarily.
- **No pagination on most directory pages** (Patients, Staff, Bills, Admissions, Beds) —
  `MedicalRecordsController` is the only one with real `Skip`/`Take` paging. Fine at demo scale;
  would need addressing before a directory grew into the hundreds of rows.
- **`Bill.RecalculateTotals()` can't reach `Paid` for a fully-discounted bill.** The status guard
  is `PaidAmount >= NetTotal && NetTotal > 0` — a bill discounted to exactly zero has nothing owed
  but never transitions out of `Unpaid`, because the `NetTotal > 0` guard was written for the
  ordinary case and never revisited for a discount covering the full amount. Caught and documented
  by `BillTotalsTests`, deliberately left as a documented edge case rather than patched under
  time pressure this close to the defense — it doesn't affect any of the demo's seeded bills.
- **The database password was committed in plaintext in early history.** It has since been moved
  to user-secrets and `appsettings.json` holds only a placeholder (see README), but the value
  remains in old commits — rotating the actual PostgreSQL password is the real fix, and is a
  local/environment concern rather than an application code change.
