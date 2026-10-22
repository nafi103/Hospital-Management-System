# Hospital Management System

A role-based hospital management system built with ASP.NET Core 10 MVC, EF Core 10, and
PostgreSQL, covering the full patient journey — registration, the front-desk queue, vitals and
triage, consultation with an AI-assisted case summary, prescription and dispensing, billing, and
a patient-facing portal — across six roles: **Admin, Doctor, Assistant, Pharmacist,
Receptionist, and Patient.**

## Tech stack

| Layer | Technology |
|---|---|
| Framework | ASP.NET Core 10 MVC (.NET 10) |
| ORM | Entity Framework Core 10 |
| Database | PostgreSQL (developed against 18; Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2 targets PostgreSQL 12+) |
| Auth | Cookie authentication, BCrypt password hashing, a global authorization fallback policy |
| Real-time | SignalR (live queue updates, streamed AI token output) |
| AI | Gemini / Groq / Anthropic, selectable and priority-ordered per admin configuration |
| Payments | bKash Payment Gateway (Tokenized Checkout API v1.2.0-beta sandbox, OAuth token caching, online billing) |
| Operational Clock | HospitalClock (Asia/Dhaka UTC+6 operational timezone, 15-minute appointment slot calculations) |
| Secrets | ASP.NET Data Protection (encrypted AI provider keys at rest), .NET user-secrets (local dev connection string/API keys) |
| Testing | xUnit (310 tests across cryptographic chains, graph algorithms, multi-resource scheduling, FEFO inventory, concurrency, and security) |

## Features by role

- **Receptionist** — front-desk operational overview dashboard (`/Reception`), patient registration (with optional portal login issuance), appointment booking with any doctor, admissions and bed assignment (with automatic bed-charge calculation across transfers), surgical operation scheduling (`/Operations`), billing and payment collection.
- **Assistant** — a single doctor's chamber queue, vitals recording with automatic NEWS2 triage scoring and physiological range validation, live acuity-weighted dynamic queue re-ranking with starvation prevention, sending patients in to the consultation room with real-time SignalR notifications.
- **Doctor** — a scoped consultation queue (own patients only) with live Estimated Wait Times (EWT), an integrated **Clinical Cockpit** (`/DoctorDashboard/Cockpit`) featuring an inline SVG $\Delta\text{NEWS2}$ longitudinal deterioration sparkline (with $\ge 2$ pt / 12h rapid deterioration alerts), an AI-generated pre-visit case summary with Accept/Edit/Reject review, surgical operation bookings (`/Operations/Create`), and prescriptions — with an Anatomical Therapeutic Chemical (ATC) classification graph and deterministic safety net (beta-lactam cross-reactivity, high-risk drug-drug interaction matrix, duplicate therapy, stock substitution, pediatric dose), and HMAC-signed tamper-proof prescriptions & AI-translated Bangla patient instruction sheets.
- **Pharmacist** — the prescription queue and inventory with First-Expired, First-Out (FEFO) batch allocation (`MedicineBatches`), automatic deduction from earliest-expiring stock, hard lockout against dispensing expired drugs, and instant QR verification of HMAC-signed prescriptions.
- **Patient** — a self-service portal: own appointments, records, prescriptions, and bills. Direct doctor appointment self-booking and rescheduling with 15-minute slot calendar (`/Portal/Book`), doctor directory (`/Portal/Doctors`), bKash online bill payment (`/Bkash/Pay`), and pediatric dependent family account management (switching between primary patient and children). Never another patient's (guaranteed by claims-based IDOR isolation). Public `/Verify/Prescription` endpoint allows anti-forgery validation of printed prescriptions via HMAC-SHA256 QR scan.
- **Admin** — staff and role management, AI provider configuration, and a reporting dashboard (revenue, occupancy, appointment volume, triage mix, AI governance, prescription safety net, top medicines, FEFO 30-day near-expiry batch monitor, and a one-click SHA-256 Ledger Integrity verification tool).

See [docs/SYSTEM-GUIDE.md](docs/SYSTEM-GUIDE.md) for the all-in-one comprehensive system specification, [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for architecture and data flows, and [docs/DEFENSE-NOTES.md](docs/DEFENSE-NOTES.md) for a demo walkthrough script and viva defense questions.

## Setup (clean clone)

**Prerequisites:** .NET 10 SDK, PostgreSQL running locally (or reachable), `dotnet-ef` tool
(`dotnet tool install --global dotnet-ef` if not already installed).

1. **Create the database.** Any empty PostgreSQL database works — the migrations create every
   table.

2. **Configure the connection string via user-secrets** (never commit it to
   `appsettings.json` — see [Configuration and secrets](#configuration-and-secrets) below):

   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=HospitalDB;Username=postgres;Password=<your-password>"
   ```

3. **Apply migrations:**

   ```bash
   dotnet ef database update
   ```

4. **Seed demo data.** This is a CLI flag, not an HTTP endpoint — it can't be triggered by a
   stray click during a live demo, and it's idempotent (safe to re-run any time to reset to a
   known-good state):

   ```bash
   dotnet run -- --seed-demo
   ```

5. **Run the app:**

   ```bash
   dotnet run
   ```

   Open the URL printed on startup (`http://localhost:5004` by default) and go to `/Auth/Login`.

### Configuration and secrets

- The Postgres connection string belongs in user-secrets (step 2 above), not
  `appsettings.json` — the checked-in value there is a placeholder.
- AI provider API keys (Gemini/Groq) can likewise be set via user-secrets
  (`Gemini:ApiKey`, `Groq:ApiKey`) before first run, **or** configured after the fact through the
  Admin → AI Providers screen, which stores them encrypted in the database via ASP.NET Data
  Protection. Either path works; nothing needs both.
- bKash checkout configuration lives in `appsettings.json` (`Bkash` section, preconfigured with
  sandbox credentials for development/testing) and can optionally be overridden via user-secrets
  (`Bkash:AppKey`, `Bkash:AppSecret`, `Bkash:Username`, `Bkash:Password`).
- Without any AI provider configured, every feature still works except live AI generation — the
  seeded demo data includes one pre-generated case summary specifically so the Accept/Edit/Reject
  review workflow can still be demonstrated offline.

## Demo credentials

**One-click login** (Development environment only, no password — `/Auth/Login` has a button per
role):

| Role | Username |
|---|---|
| Admin | `admin` |
| Doctor | `drmock` |
| Assistant | `mock-assistant` |
| Pharmacist | `pharmacistmock` |
| Receptionist | `reception1` |
| Patient | `PT-202610-0001` |

**Real username/password accounts** — needed to demonstrate two doctors (or two assistants)
signed in simultaneously, and to log in as a patient:

| Role | Username | Password |
|---|---|---|
| Doctor (2nd) | `dr2` | `Passw0rd!` |
| Assistant (2nd, assigned to dr2) | `assistant2` | `Passw0rd!` |
| Pharmacist (2nd) | `pharmacist2` | `Passw0rd!` |
| Receptionist (2nd) | `reception2` | `Passw0rd!` |
| Patient | *(see Patients list for exact UHID)* | `Patient@123` |

Six seeded patients have a portal login issued: Rahim Uddin, Fatema Begum, Karim Sheikh, Nasrin
Akter, Abdul Kader, and Rupa Chakma. Their username is their UHID (format `PT-yyyyMM-NNNN`,
month-dependent) — look it up in the Patients directory after seeding, or re-run
`dotnet run -- --seed-demo` and read it off the console summary.

Additionally, pediatric dependent accounts are linked to family accounts:
- Rahim Uddin (`PT-202610-0001`): Father and guardian of Ayan Uddin (`baby1`).
- Fatema Begum: Mother and guardian of Zara Begum (`baby2`).

## Security & Concurrency Guarantees

- **Cryptographic Hash-Chained Financial Ledger:** Billing updates record unalterable `PaymentTransaction` rows linked via SHA-256 (`PreviousHash` + `RowHash`), creating a tamper-evident blockchain-style ledger. A one-click audit tool on `/Reports` re-evaluates the chain in $O(N)$ time, flagging exact manipulated rows.
- **Hierarchical ATC Classification Graph:** Models medicines and pharmacological classes in a directed acyclic graph (DAG) structure, enabling $O(\text{depth})$ detection of beta-lactam cross-reactivity (e.g., penicillin allergy catching amoxicillin/ampicillin) and high-risk drug-drug interaction matrix checks with zero AI latency.
- **Dynamic Acuity-Weighted Queue with Aging:** Combines NEWS2 clinical triage scoring with starvation-prevention waiting age ($\text{PriorityScore} = W_{\text{acuity}} \times \text{NEWS2} + W_{\text{wait}} \times \text{MinutesWaited}$), pushing live queue re-ranking and Estimated Wait Times (EWT) via SignalR.
- **Longitudinal $\Delta\text{NEWS2}$ Deterioration Radar:** Tracks rate of physiological change across consecutive vitals readings, alerting clinicians if NEWS2 deteriorates by $\ge 2$ points within 12 hours, visualized via an inline SVG sparkline in the Clinical Cockpit.
- **FEFO Pharmacy Batch Allocation & Expiry Control:** Allocates inventory using First-Expired, First-Out (`MedicineBatches`), automatically drawing from earliest-expiring stock, locking out expired medicines, and alerting the Admin dashboard on batches expiring within 30 days.
- **HMAC-SHA256 Signed QR Prescriptions:** Digitally signs issued prescriptions and patient instruction sheets using server-secret HMAC signatures, rendering anti-forgery QR codes verifiable anonymously at `/Verify/Prescription`.
- **Multi-Resource Surgical Scheduling Engine:** `OperationsController` schedules Operating Theatres by simultaneously enforcing three constraints: surgeon availability (outpatient & surgical collision checks), OT room vacancy, and automated post-op ICU/Cabin bed allocation.
- **PostgreSQL Concurrency Guarantees:** Double-booking prevention via filtered unique indexes (`idx_appointments_doctor_slot_active`) and optimistic concurrency tokens (`xmin`) across `Bills` and `Appointments`.
- **Strict SignalR Authorization:** `AiStreamHub` restricts connection to doctors, and stream subscription is gated to the initiating clinician. `NotificationHub` restricts connection to authenticated users and automatically scopes broadcasts to isolated user groups.
- **Clinical Data Protection:** Formulary medicines reference `DeleteBehavior.Restrict` to eliminate cascade deletion of clinical prescription history. Admissions with active billing invoices cannot be deleted, avoiding broken accounting trails.
- **Expanded AI Context & Invalidation:** Clinical AI summaries aggregate medical records, allergies, vitals, and active prescriptions, using dynamic composite hashes for cache invalidation and atomic state transitions on clinical reviews.

## Running tests

```bash
dotnet test
```

**310 automated xUnit tests** cover:
- **Cryptographic Ledger Integrity:** Genesis SHA-256 block calculation, chain hash propagation, valid chain verification, and tamper detection flagging modified historical transactions.
- **Hierarchical ATC Graph & DDI:** Beta-lactam tree traversal (penicillin allergy catching amoxicillin and ampicillin) and drug-drug interaction matrix checks (Warfarin + Ibuprofen/Naproxen).
- **Dynamic Acuity Queue & $\Delta\text{NEWS2}$:** Priority score aging formula, starvation prevention, live EWT estimation, and longitudinal $\Delta\text{NEWS2} \ge 2$ deterioration detection within 12 hours.
- **FEFO Inventory & Expiry Protection:** Earliest-expiry batch consumption, partial multi-batch deductions, expired batch lockout, and near-expiry alerting.
- **HMAC Anti-Forgery & Verification:** Cryptographic token signing, tamper rejection, expired token rejection, and valid token payload decoding.
- **Multi-Resource Surgical Operations:** Surgeon schedule collision detection, operating theatre double-booking prevention, and post-op ICU/Cabin bed reservation.
- **NEWS2 Triage & Boundaries:** 65 parameter boundary tests and physiological input range validation rules.
- **Privacy & Sanitization:** PHI scrubbing and rehydration round trips with whole-word regex boundaries, AI stream sanitization, citation deduplication, and hallucinated citation stripping.
- **AI Infrastructure:** Priority-ordered fallback chains and provider connectivity ping diagnostics (Gemini, Groq, Anthropic).
- **Prescription Safety Net:** Deterministic checks for duplicate therapies, generic/brand allergy contraindications, active medication windows, stock substitutions, and pediatric doses.
- **Billing & Accounting:** Subtotals, net totals, zero-net discounts, and immutable `PaymentTransaction` ledger derivations.
- **Hospital Clock & Timezones:** Asia/Dhaka UTC+6 timezone conversions, start/end of day boundaries, and slot alignment.
- **EF Core & Database Concurrency:** Filtered unique indexes and PostgreSQL `xmin` optimistic concurrency tokens on appointments and bills.
- **Clinical Workflows:** Doctor Clinical Cockpit integration, $\Delta\text{NEWS2}$ radar, and pharmacy atomic inventory dispensing.
- **Patient Portal & Scheduling:** 15-minute slot generation, direct patient booking, rescheduling, cancellations, and pediatric dependent family account IDOR isolation.
- **bKash Payment Gateway:** Token grant caching, payment creation, callback verification, payment execution, and transaction ledger logging.
- **Access Control & Security:** Strict role-based and claims-based authorization across Doctor, Assistant, Receptionist, Patient, and Admin endpoints.

See [docs/DEFENSE-NOTES.md](docs/DEFENSE-NOTES.md) for what each suite is actually proving and why.

## Project structure

```
Controllers/    One controller per resource (Patients, Appointments, Bills, Bkash, DoctorDashboard, Operations, Portal, Verify, ...)
Models/         EF Core entities (20 tables) + ViewModels/ and Dto/ for typed view payloads
Services/       Business logic (NEWS2, PHI scrubbing, AI spine, DoctorScheduleService, AtcClassificationGraph, AcuityQueueService, PrescriptionSecurityService, BkashPaymentService)
Hubs/           SignalR hubs (live queue updates, streamed AI output)
Views/          Razor views, one folder per controller
Migrations/     EF Core migration history
tests/          xUnit test project (310 tests, excluded from the web project's build via a Compile Remove)
docs/           ER diagram, architecture diagrams, comprehensive system guide, and defense preparation notes
```

## Documentation

- [docs/SYSTEM-GUIDE.md](docs/SYSTEM-GUIDE.md) — master comprehensive system specification, workflows, and module reference (the single all-in-one document)
- [docs/FRONTEND.md](docs/FRONTEND.md) — comprehensive frontend architecture, design tokens, SignalR real-time hubs, interactive booking engines, specialized clinical workstations, and print media rules
- [docs/ER-DIAGRAM.md](docs/ER-DIAGRAM.md) — all 20 database tables (including `MedicineBatches` and activated `Operations`), schema attributes, and relationships
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — system layering, cryptographic ledger, graph algorithms, dynamic queues, AI spine sequence, and scheduling concurrency
- [docs/DEFENSE-NOTES.md](docs/DEFENSE-NOTES.md) — rehearsable demo script, anticipated viva questions with grounded technical answers, and known limitations stated up front
