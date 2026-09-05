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
| Secrets | ASP.NET Data Protection (encrypted AI provider keys at rest), .NET user-secrets (local dev connection string/API keys) |
| Testing | xUnit |

## Features by role

- **Receptionist** — patient registration (with optional portal login issuance), appointment
  booking with any doctor, admissions and bed assignment, billing and payments.
- **Assistant** — a single doctor's chamber queue, vitals recording with automatic NEWS2 triage
  scoring, sending patients in to the consultation room.
- **Doctor** — a scoped consultation queue (own patients only), an AI-generated pre-visit case
  summary with Accept/Edit/Reject review, medical records, and prescriptions.
- **Pharmacist** — the prescription queue and inventory, dispensing against stock.
- **Patient** — a self-service portal: own appointments, records, prescriptions, and bills. Never
  another patient's.
- **Admin** — staff and role management, AI provider configuration, and a reporting dashboard
  (revenue, occupancy, appointment volume, triage mix, AI governance, top medicines).

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for how the AI review pipeline and the
authorization model actually work, and [docs/DEFENSE-NOTES.md](docs/DEFENSE-NOTES.md) for a
walkthrough script and anticipated questions.

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

## Running tests

```bash
dotnet test
```

65 xUnit tests cover the NEWS2 triage algorithm (boundary values on all seven parameters plus the
clinical escalation rules), the PHI-scrubbing/rehydration round trip, and bill total/status
calculations — the three pieces of pure business logic in the codebase with no database
dependency. See [docs/DEFENSE-NOTES.md](docs/DEFENSE-NOTES.md) for what each suite is actually
proving and why.

## Project structure

```
Controllers/    One controller per resource (Patients, Appointments, Bills, Reports, ...)
Models/         EF Core entities + ViewModels/ for typed view payloads
Services/       Business logic with no HTTP concerns (NEWS2, PHI scrubbing, the AI spine, the demo seeder)
Hubs/           SignalR hubs (live queue updates, streamed AI output)
Views/          Razor views, one folder per controller
Migrations/     EF Core migration history
tests/          xUnit test project (excluded from the web project's build via a Compile Remove)
docs/           ER diagram, architecture diagrams, and defense preparation notes
```

## Documentation

- [docs/ER-DIAGRAM.md](docs/ER-DIAGRAM.md) — all 18 tables and their relationships
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — layering, the AI request/response sequence, and
  the authorization model
- [docs/DEFENSE-NOTES.md](docs/DEFENSE-NOTES.md) — a rehearsable demo script, anticipated
  questions with grounded answers, and known limitations stated up front
