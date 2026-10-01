# Architecture

Three diagrams: the overall layering, the AI spine's actual request/response sequence, and the
authorization model. All three are traced from the running code, not idealized.

## 1. Layers

```mermaid
flowchart TB
    Browser["Browser<br/>(Razor views, Bootstrap, vanilla JS)"]

    subgraph App["ASP.NET Core 10 MVC"]
        Controllers["Controllers<br/>(one per resource: Patients, Appointments, Bills, Reports, ...)"]
        Services["Services<br/>(PhiScrubber, AiProviderResolver, ClinicalAiService,<br/>News2Calculator, ApiKeyProtector, DemoDataSeeder)"]
        Hubs["SignalR Hubs<br/>(NotificationHub, AiStreamHub)"]
    end

    EFCore["EF Core 10<br/>(ApplicationDbContext)"]
    DB[("PostgreSQL 18")]

    AiProviders["External AI providers<br/>(Gemini / Groq / Anthropic)"]

    Browser -- "HTTP (forms, fetch)" --> Controllers
    Browser <-- "WebSocket (live updates, AI token stream)" --> Hubs
    Controllers --> Services
    Controllers --> EFCore
    Services --> EFCore
    Hubs --> Services
    EFCore --> DB
    Services -- "scrubbed text only" --> AiProviders
```

Every controller talks to `ApplicationDbContext` directly for straightforward CRUD — there is no
repository layer, which is a deliberate scope decision, not an oversight: with one DbContext and no
second data source, a repository would only add indirection. The `Services/` layer exists for logic
that either has real business rules (`News2Calculator`, `PhiScrubber`), coordinates multiple
concerns (`ClinicalAiService`), or is cross-cutting (`ApiKeyProtector`, `DemoDataSeeder`).

## 2. The AI spine — end-to-end sequence

This is the flow behind the "Generate Case Summary" button on a patient's chart, traced from
`Services/ClinicalAiService.cs`, `Controllers/AiReviewController.cs`, and `Hubs/AiStreamHub.cs`.
The critical property it's designed to demonstrate: **the AI never writes to a clinical table.**
It produces exactly one thing — an `AiSuggestion` row — and every path out of that row back into
clinical data requires a human action taken through the *ordinary* clinical forms, not an
automated conversion.

```mermaid
sequenceDiagram
    actor Doctor
    participant Browser
    participant Hub as AiStreamHub
    participant Ctrl as AiReviewController
    participant Svc as ClinicalAiService
    participant Scrub as PhiScrubber
    participant Resolver as AiProviderResolver
    participant Provider as AI Provider (Gemini/Groq/Anthropic)
    participant DB as PostgreSQL

    Doctor->>Browser: Click "Generate Case Summary"
    Browser->>Hub: JoinStream(streamId)
    Browser->>Ctrl: POST GenerateCaseSummary(patientId, streamId)
    Ctrl->>Svc: GenerateCaseSummaryAsync(patientId, userId, streamId)

    Svc->>DB: Load patient + last 10 MedicalRecords
    Svc->>Scrub: Scrub(recordText, patient)
    Scrub-->>Svc: pseudonymized text + reversal map
    Svc->>Resolver: GetOrderedProvidersAsync()
    Resolver->>DB: Load enabled AiProviderSettings (by Priority)
    Resolver-->>Svc: ordered [(provider, decrypted key, model)]

    loop until a provider succeeds or all are exhausted
        Svc->>Provider: StreamAsync(scrubbed prompt)
        Provider-->>Svc: token chunks
        Svc->>Scrub: Rehydrate(chunk buffer, map)
        Svc->>Hub: ReceiveChunk(rehydrated delta)
        Hub-->>Browser: live token stream (real patient name, not the pseudonym)
    end

    Svc->>Svc: Validate [[rec:id]] citations against real record ids<br/>(drop any hallucinated id)
    Svc->>DB: INSERT AiSuggestion (Verdict = Pending)
    Svc->>Hub: StreamComplete(suggestionId)
    Ctrl-->>Browser: { suggestionId }

    Doctor->>Ctrl: POST Accept / Edit / Reject
    Ctrl->>DB: UPDATE AiSuggestion.Verdict (+ ReviewedById, ReviewedAt)
    Note over DB: Accept/Edit/Reject touch ONLY the AiSuggestion row.<br/>No MedicalRecord or Prescription is created automatically -<br/>if the doctor acts on what they read, they write it<br/>themselves through the normal clinical forms.
```

Two details worth being able to explain unprompted:

- **Rehydration runs over the full raw buffer every time**, not a truncated prefix — a pseudonym
  occurrence can straddle a streaming chunk boundary, and `Replace` needs every character of a
  match to recognize it. What's withheld from the browser instead is a trailing margin of output
  characters sized to the longest pseudonym in play, so a match that's still incomplete can never
  cause already-sent text to be silently wrong.
- **Provider fallback is a priority-ordered list, not a hardcoded pair.** `AiProviderResolver`
  reads `AiProviderSettings` (admin-managed, encrypted keys) in priority order; the first provider
  gets one retry on a transient failure, every other provider in the chain gets one attempt before
  the service gives up and reports "all providers unavailable." Adding a new vendor is
  `IAiTextProvider` + a DI registration — no branching on provider type anywhere else.

This whole sequence — scrub, resolve providers, stream, rehydrate, persist as a Pending
`AiSuggestion` — is factored into one shared private method (`RunProviderStreamAsync`) that both
`GenerateCaseSummaryAsync` and `GeneratePatientInstructionsAsync` call. The two callers differ only
in what context they build beforehand (a patient's medical records vs. one prescription's
medicines) and what they do with the finished text afterward (citation validation and a
`MedicalRecord`-scoped payload vs. a plain narrative with no citations, targeting a `Prescription`
via `AiSuggestion.TargetEntityId`). Adding a third AI feature means writing a third caller, not
reimplementing the streaming/fallback logic again.

## 3. Authorization model

```mermaid
flowchart TD
    Request["Incoming request"] --> Auth{"Authenticated?"}
    Auth -- "No" --> Fallback["Global FallbackPolicy:<br/>RequireAuthenticatedUser()"]
    Fallback --> AllowAnon{"[AllowAnonymous]?<br/>(Auth controller, Home/Error)"}
    AllowAnon -- "Yes" --> Serve["Serve request"]
    AllowAnon -- "No" --> Redirect["302 -> /Auth/Login"]

    Auth -- "Yes" --> RoleCheck{"[Authorize(Roles=...)]<br/>on class AND action?"}
    RoleCheck -- "Both must match -<br/>ASP.NET Core ANDs them" --> RoleOk{"Role in every<br/>attribute's list?"}
    RoleOk -- "No" --> Denied["302 -> /Auth/AccessDenied"]
    RoleOk -- "Yes" --> Identity{"Controller needs the<br/>caller's own identity?<br/>(Portal, Vitals, ...)"}

    Identity -- "No" --> Serve
    Identity -- "Yes" --> Resolve["Resolve Patient via<br/>UserId claim - never a route id"]
    Resolve --> Serve
```

Three things this diagram is standing in for:

- **The fallback policy inverts the framework default.** ASP.NET Core is opt-in by default — a
  controller with no `[Authorize]` is anonymous. `Program.cs` sets a global `FallbackPolicy` of
  `RequireAuthenticatedUser()`, so the default flips to opt-out: every endpoint requires a signed-in
  user unless explicitly marked `[AllowAnonymous]`. Before this existed, `StaffController` and
  `MedicinesController` were reachable by an anonymous `GET` — confirmed live, not theoretical.
- **Stacked `[Authorize(Roles=...)]` attributes are ANDed, not overridden.** A class-level
  attribute of `Doctor,Pharmacist,Admin` and a method-level attribute adding `Patient` do **not**
  combine into a wider set — the method's attribute is evaluated in addition to the class's, so
  `Patient` is still excluded unless the class-level attribute itself is widened. This surfaced as
  a real bug while building the patient portal (`PrescriptionsController.Print` kept 302-ing a
  patient trying to print their own prescription) and is now the reason narrowing happens with
  *extra* per-action attributes added to an already-wide class attribute, never the reverse.
- **Role membership is necessary but not sufficient for patient-scoped data.** `[Authorize(Roles =
  "Patient")]` proves the caller is *a* patient, not *which* patient. `PortalController` (and
  `VitalsController`'s appointment lookups) resolve the actual `Patient` row from the `UserId`
  claim set at login (`AuthController.SignInUserAsync`) on every single action, and every query is
  then scoped to that patient's own `Id`. A route or query-string `id` is never trusted for
  ownership — see the IDOR walkthrough in
  [DEFENSE-NOTES.md](DEFENSE-NOTES.md#anticipated-questions).
- **SignalR Hubs enforce strict identity isolation and role gating.**
  - `AiStreamHub`: Annotated with `[Authorize(Roles = "Doctor")]`. Clients can only join a stream group when the doctor's authenticated identity matches the clinician initiating the AI streaming pipeline. This completely blocks unauthenticated or unauthorized users from intercepting live rehydrated PHI tokens over WebSockets.
  - `NotificationHub`: Enforces `[Authorize]`. On connection, callers are strictly assigned to their personal user group `user_{userId}` derived from authentication claims, preventing users from arbitrarily subscribing to other users' private notification channels.

## 4. Financial Integrity & Optimistic Concurrency

```mermaid
flowchart LR
    Payment["Cashier / Patient Payment"] --> ConcurrencyCheck{"PostgreSQL xmin<br/>Concurrency Check"}
    ConcurrencyCheck -- "Conflict Detected" --> ConcurrencyException["DbUpdateConcurrencyException<br/>(Transaction Rolled Back)"]
    ConcurrencyCheck -- "Token Match" --> BalanceVerify{"Dynamic Balance Check<br/>(NetTotal - Sum(Tx))"}
    BalanceVerify -- "Exceeds Balance" --> OverpayAbort["Reject Over-Payment<br/>(Transaction Rolled Back)"]
    BalanceVerify -- "Valid Amount" --> LedgerInsert["INSERT PaymentTransaction<br/>(Immutable Ledger Row)"]
    LedgerInsert --> BillUpdate["Update Bill.PaidAmount<br/>& Recalculate Totals"]
```

- **Immutable `PaymentTransaction` Ledger:** Financial payments are never recorded as simple in-place mutations on `Bill.PaidAmount`. Every collection writes an immutable `PaymentTransaction` row (recording the transaction timestamp, payment method, cashier `UserId`, and amount). The bill's `PaidAmount` and status (`Paid`, `PartiallyPaid`, `Unpaid`) are derived atomically from the sum of all recorded transactions, preserving an unalterable audit trail.
- **PostgreSQL `xmin` Concurrency Tokens:** `Bill` and `Appointment` entities utilize PostgreSQL's internal `xmin` system column mapped via EF Core's `IsRowVersion()`. If two cashiers record payments simultaneously or two receptionists attempt to book the same doctor's slot, the second commit fails with a `DbUpdateConcurrencyException`, triggering an immediate transaction rollback and alerting the user before any balance or schedule corruption occurs.

## 5. Clinical Safety Net & Referential Integrity

- **Preventing Clinical History Data Loss (Restrict Deletions):**
  - `PrescriptionItem.Medicine` is configured with `DeleteBehavior.Restrict` in EF Core and enforced with pre-flight checks in `MedicinesController.DeleteConfirmed`. A formulary medicine cannot be deleted if historical prescription records reference it, eliminating cascade deletion hazards that could silently purge historic patient charts.
  - Inpatient admissions linked to billing invoices cannot be deleted (`_context.Bills.AnyAsync(b => b.AdmissionId == id)`), preventing unhandled foreign key `500 Internal Server Error` crashes and protecting accounting audit trails.
- **AI Context Expansion & Dynamic Cache Invalidation:**
  - The clinical AI case summary pipeline aggregates comprehensive patient context: recent medical records, active allergies, latest vitals (including NEWS2 priority score), and active prescriptions.
  - Dynamic cache invalidation computes a composite hash from the latest `UpdatedAt` timestamps across all four clinical domains, ensuring clinician edits immediately invalidate cached prompts without redundant database queries.
  - Review verdicts (`Accept`, `Reject`, `Edit`) execute atomic database transitions via `ExecuteUpdateAsync` conditioned on `s.Verdict == AiSuggestionVerdict.Pending`, preventing terminal decision race conditions.
