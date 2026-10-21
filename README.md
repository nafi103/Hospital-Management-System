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
| Testing | xUnit (284 tests across unit algorithms, integration concurrency, security access control, and portal workflows) |

## Features by role

- **Receptionist** — front-desk operational overview dashboard (`/Reception`), patient registration (with optional portal login issuance), appointment booking with any doctor, admissions and bed assignment (with automatic bed-charge calculation across transfers), billing and payment collection.
- **Assistant** — a single doctor's chamber queue, vitals recording with automatic NEWS2 triage scoring and physiological range validation, sending patients in to the consultation room with real-time SignalR notifications.
- **Doctor** — a scoped consultation queue (own patients only) and an integrated **Clinical Cockpit** (`/DoctorDashboard/Cockpit`), an AI-generated pre-visit case summary (synthesizing medical records, active allergies, latest vitals + NEWS2 score, and active prescriptions with citation markers and cache invalidation) with Accept/Edit/Reject review, medical records, and prescriptions — with a deterministic safety net on every prescription (duplicate therapy, allergy conflict, stock substitution, pediatric dose) and an AI-generated Bangla patient instruction sheet, gated behind the same Accept-before-print review.
- **Pharmacist** — the prescription queue and inventory, dispensing against stock with atomic inventory decrement.
- **Patient** — a self-service portal: own appointments, records, prescriptions, and bills. Direct doctor appointment self-booking and rescheduling with 15-minute slot calendar (`/Portal/Book`), doctor directory (`/Portal/Doctors`), bKash online bill payment (`/Bkash/Pay`), and pediatric dependent family account management (switching between primary patient and children). Never another patient's (guaranteed by claims-based IDOR isolation).
- **Admin** — staff and role management, AI provider configuration, and a reporting dashboard (revenue, occupancy, appointment volume, triage mix, AI governance, prescription safety net, top medicines).

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

- **Immutable Financial Ledger:** Billing updates record unalterable `PaymentTransaction` rows with cashier/gateway attribution. Total paid amounts and invoice statuses are derived dynamically from transaction rows, preventing accounting drift and race conditions.
- **PostgreSQL `xmin` Concurrency Tokens:** `Bill` and `Appointment` models map PostgreSQL's native `xmin` rowversion token, protecting against double-payments, concurrent cashier/online checkouts, and doctor schedule slot double-bookings.
- **Strict SignalR Authorization:** `AiStreamHub` restricts connection to doctors, and stream subscription is gated to the initiating clinician. `NotificationHub` restricts connection to authenticated users and automatically scopes broadcasts to isolated user groups.
- **Clinical Data Protection:** Formulary medicines reference `DeleteBehavior.Restrict` to eliminate cascade deletion of clinical prescription history. Admissions with active billing invoices cannot be deleted, avoiding broken accounting trails.
- **Expanded AI Context & Invalidation:** Clinical AI summaries aggregate medical records, allergies, vitals, and active prescriptions, using dynamic composite hashes for cache invalidation and atomic state transitions on clinical reviews.
- **Doctor Scheduling Engine:** `DoctorScheduleService` computes available 15-minute consultation slots between 09:00 AM and 05:00 PM (17:00) using `HospitalClock` (Asia/Dhaka UTC+6), with pre-flight collision checks for both doctors and patients.

## Running tests

```bash
dotnet test
```

**277 automated xUnit tests** cover:
- **NEWS2 Triage & Boundaries:** 65 parameter boundary tests and physiological input range validation rules.
- **Privacy & Sanitization:** PHI scrubbing and rehydration round trips with whole-word regex boundaries, AI stream sanitization, citation deduplication, and hallucinated citation stripping.
- **AI Infrastructure:** Priority-ordered fallback chains and provider connectivity ping diagnostics (Gemini, Groq, Anthropic).
- **Prescription Safety Net:** Deterministic checks for duplicate therapies, generic/brand allergy contraindications, active medication windows, stock substitutions, and pediatric doses.
- **Billing & Accounting:** Subtotals, net totals, zero-net discounts, and immutable `PaymentTransaction` ledger derivations.
- **Hospital Clock & Timezones:** Asia/Dhaka UTC+6 timezone conversions, start/end of day boundaries, and slot alignment.
- **EF Core & Database Concurrency:** PostgreSQL `xmin` optimistic concurrency tokens on appointments and bills, referential integrity restrictions on medicines and admissions.
- **Clinical Workflows:** Doctor Clinical Cockpit integration and pharmacy atomic inventory dispensing.
- **Patient Portal & Scheduling:** 15-minute slot generation, direct patient booking, rescheduling, cancellations, and pediatric dependent family account IDOR isolation.
- **bKash Payment Gateway:** Token grant caching, payment creation, callback verification, payment execution, and transaction ledger logging.
- **Access Control & Security:** Strict role-based and claims-based authorization across Doctor, Assistant, Receptionist, Patient, and Admin endpoints.

See [docs/DEFENSE-NOTES.md](docs/DEFENSE-NOTES.md) for what each suite is actually proving and why.

## Project structure

```
Controllers/    One controller per resource (Patients, Appointments, Bills, Bkash, DoctorDashboard, Portal, ...)
Models/         EF Core entities + ViewModels/ and Dto/ for typed view payloads
Services/       Business logic with no HTTP concerns (NEWS2, PHI scrubbing, AI spine, DoctorScheduleService, BkashPaymentService)
Hubs/           SignalR hubs (live queue updates, streamed AI output)
Views/          Razor views, one folder per controller
Migrations/     EF Core migration history
tests/          xUnit test project (excluded from the web project's build via a Compile Remove)
docs/           ER diagram, architecture diagrams, and defense preparation notes
```

## Documentation

- [docs/SYSTEM-GUIDE.md](docs/SYSTEM-GUIDE.md) — master comprehensive system specification, workflows, and module reference (the single all-in-one document)
- [docs/ER-DIAGRAM.md](docs/ER-DIAGRAM.md) — all 19 database tables, schema attributes, and relationships
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — system layering, AI spine sequence, authorization model, payment gateway, and scheduling concurrency
- [docs/DEFENSE-NOTES.md](docs/DEFENSE-NOTES.md) — rehearsable demo script, anticipated viva questions with grounded technical answers, and known limitations stated up front
