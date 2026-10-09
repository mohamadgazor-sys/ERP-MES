# Milestone 1 — Foundation: Solution and Project Plan (for approval)

**Status:** Proposal. **No code, solution, project, package, migration or workflow file has been created.** Nothing starts until you approve this plan (§9 lists exactly what I need approved).

Basis: audit `PHASE0_SPECIFICATION_AUDIT.md` §6.2; Decision Log #22–#27 (technical conventions), #30 (roles seed), #34 (permission map); Architecture Specification §3, §5, §17, §31.

## 1. Goal and boundaries

**Goal:** a buildable, tested skeleton on which Milestone 2 can add Customers, Requests and Items — with Persian/RTL shell, login, authorization mechanism, Jalali and Rial primitives, and a provider-neutral SQLite persistence base.

**In scope**
- Solution and projects; build and CI configuration.
- Domain primitives: Jalali calendar abstraction, Rial money value object, result/error type.
- `Department`, `Role`, `User`, `AuditLog` entities; seed of the development departments/roles (Decision Log #30).
- Authentication (PBKDF2), first-run Admin creation, default-deny authorization service carrying the matrix of Domain spec §15.2 and §15.4.
- SQLite persistence base with the Decision Log #23–#25 conventions; initial migration for the tables above.
- WPF shell: Persian RTL login window and an empty main window; bidi (LTR identifiers inside RTL) check.

**Out of scope:** customers, requests, items, workflow, documents, Excel, any business screen, SQL Server migrations, installer.

## 2. Solution layout (proposed)

```text
ERP-MES.sln
global.json                      (pins the SDK; exact version chosen when the SDK is installed)
Directory.Build.props            (shared: nullable enabled, warnings as errors for Domain/Application, analyzers)
.editorconfig
src/
  ERP.MES.Domain/                net10.0
  ERP.MES.Application/           net10.0
  ERP.MES.Infrastructure/        net10.0         (EF Core context, repositories, security, shared mapping)
  ERP.MES.Infrastructure.Sqlite/ net10.0         (SQLite provider setup + SQLite migrations only)
  ERP.MES.Desktop/               net10.0-windows (WPF, MVVM)
tests/
  ERP.MES.Domain.Tests/          net10.0
  ERP.MES.Application.Tests/     net10.0
  ERP.MES.Infrastructure.Tests/  net10.0
  fixtures/                      (already exists; synthetic fixtures only)
.github/workflows/ci.yml         (proposed, §7)
```

Differences from Architecture Specification §3: one extra project, `Infrastructure.Sqlite`, so that "one migration set per provider" (Decision Log #25) is structural; a later `Infrastructure.SqlServer` sits beside it. No UI-test project in M1 (audit X49): UI behaviour is covered by a written manual checklist (§6) until a UI-automation tool is chosen.

Dependency direction: Desktop → Application → Domain; Infrastructure(.Sqlite) → Application/Domain contracts; Domain depends on nothing. Namespaces `ERP.MES.*`.

## 3. Components and decisions inside the plan

**3.1 Domain primitives**
- `IJalaliCalendar` (pure, no dependencies) with: Gregorian date ↔ Jalali date, Jalali year of a date, Jalali range → half-open Gregorian range. Implementation behind it is chosen in §9-A4. Needed in the Domain because `JalaliRequestYear` is part of the Request business key (audit X31).
- `Money` (Rial): whole-Rial or decimal storage and rounding are **not** decided (Decision Log Unresolved); M1 provides formatting/parsing only (thousand separators, "ریال" label) and no arithmetic rules beyond addition/comparison.
- `Result<T>` / structured business errors (Architecture Specification §30).

**3.2 Persistence conventions (Decision Log #23–#25)**
- `long` identity keys; `long Version` column on mutable entities, incremented by the application and checked on update; conflicting update raises a concurrency error.
- Timestamps in UTC; calendar dates as `DateOnly`; no `DateTimeOffset`.
- Foreign-key enforcement turned on for every SQLite connection; migrations only in `Infrastructure.Sqlite`; the dev database is disposable and its path comes from configuration, outside source control (`.gitignore` already covers `*.db`).
- A short written "provider-neutral subset" note is added to the repository docs as part of M1 (list in #25).

**3.3 Entities (M1 only)**
- `Department` (id, code, Persian name, active), `Role` (id, code, name), `User` (id, username, password hash/salt/iteration metadata, display name, department, role, active, last login, version), `AuditLog` (append-only: id, UTC time, user, action code, target type/id, before/after summary).
- Seed: Departments Central, Management, Planning, Drawing, Warehouse, Production, QC, Maintenance/PM, System; Roles Admin, Manager, Staff.

**3.4 Authentication**
- PBKDF2 via the built-in .NET implementation, per-user random salt, iteration count stored with the hash (Decision Log #27). Proposed parameters: PBKDF2-HMAC-SHA256, 16-byte salt, 32-byte hash, iteration count taken from current published guidance at implementation time (OWASP's 2023 figure for SHA-256 was 600,000 — to be re-checked, not assumed). Hashes are upgraded transparently on login when the stored count is lower than the configured one.
- **First-run Admin:** if no user exists, the app shows a one-time "create Admin account" screen (Admin role, System department). No default or shared password exists anywhere in the repository.
- Proposed minimal policy (needs approval, audit U49): minimum 8 characters, lock after 5 failed attempts for 5 minutes (counter stored per user), no password in logs, login success/failure written to `AuditLog`. Password reset by the Admin only.
- Session: in-memory current-user object; no auto-lock in M1.

**3.5 Authorization**
- Action codes (for example `Request.ApproveInitial`, `Request.Reopen`, `Item.Import`) and a rule set mapping each to allowed department/role, built from Domain spec §15.2 (13 rows + reopen) and §15.4. Anything not mapped is denied. The service is called at the use-case boundary; the UI only mirrors it.
- Rule set is code plus tests in M1 (no database-edited permissions yet).

**3.6 Desktop shell**
- Login window, main window with empty navigation area; `FlowDirection=RightToLeft` at the root; Persian resource file (`fa`) for all strings; no business logic in code-behind.
- Font: needs your choice (§9-A6); the licence must allow bundling.
- Jalali date picker: not in M1. M1 only proves display/parsing through the calendar abstraction; the picker control is chosen with the first form that needs dates (Milestone 2).

## 4. Packages and tools (proposed, **none installed**)

| Purpose | Candidate | Note |
|---|---|---|
| ORM | `Microsoft.EntityFrameworkCore` + `...Sqlite` + `...Design` | EF Core candidate named in the Architecture Specification §4.2 |
| Hosting/DI/config/logging | `Microsoft.Extensions.*` (DependencyInjection, Configuration, Logging) | Standard |
| MVVM helpers | `CommunityToolkit.Mvvm` | Source generators for view models; optional |
| Tests | `xunit`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `Microsoft.EntityFrameworkCore.Sqlite` (in-memory/file) | Plain xUnit assertions; no assertion-library dependency (some assertion libraries changed licence terms — checked before any is added) |
| Jalali | Built-in `System.Globalization.PersianCalendar` behind `IJalaliCalendar` | Accuracy (leap years, boundaries) is **verified by golden tests** before use; if it fails, a vetted library replaces it behind the same interface |

Every package's licence and maintenance state is checked and reported to you before it is added. Versions are pinned (central package management).

## 5. Work breakdown (each step is a reviewable commit group; build and tests run after each)

| Step | Content | Verified by |
|---|---|---|
| M1.1 | Solution, projects, `global.json`, `Directory.Build.props`, `.editorconfig`, empty test projects | Build succeeds on Windows CI; tests run (zero tests allowed here) |
| M1.2 | Domain primitives: `IJalaliCalendar` + golden tests, `Money` formatting, `Result` | Unit tests |
| M1.3 | Persistence base, entities, seed, initial SQLite migration, version column, audit log | Integration tests on SQLite (migration from empty, FK enforcement, concurrency conflict, audit append-only) |
| M1.4 | Password hashing + authentication service + first-run Admin | Unit + integration tests (hash differs from input and between users, lockout, no plaintext) |
| M1.5 | Authorization rule set from the matrix | One allowed and one denied test per action; unlisted action denied |
| M1.6 | WPF shell, login, resources, RTL, bidi check | Windows build; manual checklist with screenshots |
| M1.7 | CI workflow, provider-neutral-subset note, session log | CI green on a pull request |

## 6. Acceptance criteria (from audit §6.2, restated)

1. Clean checkout builds in Debug and Release on a Windows agent; non-WPF projects build and test on any OS.
2. All automated tests pass; CI log attached to the milestone report.
3. App starts; RTL Persian login; valid login works; wrong password and inactive user rejected.
4. No plaintext password anywhere; same password for two users stores differently; no default credential in the repository.
5. Every authorization matrix row has an allowed and a denied test; unlisted action denied.
6. Jalali golden table passes (Nowruz boundaries, leap years, month ends); a Jalali range converts to a half-open Gregorian range; no string comparison of dates.
7. Rial values format with separators and the Rial label and parse back unchanged; no Toman unit exists.
8. Database created from migrations on SQLite; foreign keys enforced; concurrent update conflict detected.
9. A grid shows `P-565`, `1.10`, `wsj-898` readable in RTL; screenshot attached.
10. No secrets or real data committed; `.gitignore` check passes.

**Manual UI checklist** (kept in `docs/` once M1.6 starts): RTL alignment, Persian typing, tab order, window resize, mixed LTR/RTL text, high-DPI.

## 7. Build and verification environment

- Authoritative build: GitHub Actions on `windows-latest` (restore, build Release, test, upload results). I can only write the workflow file; I cannot run it from this session — results are read from the pull request checks.
- This cloud session has **no .NET SDK installed** and runs Linux. To build and test the non-WPF projects here I would need to install the .NET 10 SDK; compiling the WPF project on Linux may be possible with Windows targeting enabled (to be verified), but it cannot be run. I will not install anything without your approval (§9-A9). Without it, M1 verification relies on CI, and every statement I make about build/test results will say which environment produced it.

## 8. Risks

- **Jalali accuracy:** built-in calendar behaviour is unverified; mitigated by golden tests and the abstraction.
- **Bidi rendering:** LTR identifiers inside RTL grids can display reordered; mitigated by criterion 9 and a text-direction helper in the shell.
- **Windows 10 support:** .NET 10 lists only certain Windows 10 builds (Decision Log #22); development machines should be Windows 11 or a listed build. Factory validation is later.
- **Provider drift:** only SQLite exists in M1; the SQL Server test pass is scheduled before the stage/QC milestone (Decision Log #25).
- **Scope creep:** any M2 entity found "needed" in M1 is reported, not added.

## 9. Approvals I need from you

| # | Item | My recommendation |
|---|---|---|
| A1 | Project layout in §2, including the extra `Infrastructure.Sqlite` project | Approve |
| A2 | Package candidates in §4 (each still licence-checked before adding) | Approve candidates |
| A3 | Central package management and pinned versions; warnings as errors in Domain/Application | Approve |
| A4 | Jalali approach: built-in calendar behind `IJalaliCalendar`, golden-tested, replaceable | Approve |
| A5 | Authentication policy and first-run Admin flow (§3.4), including lockout and Admin-only reset | Approve or amend the numbers |
| A6 | Persian UI font (needs a font whose licence allows bundling; you may name one) | Name a preference or let me propose two for choice |
| A7 | Recording technical parameters (final .NET SDK version, PBKDF2 iterations) as Decision Log entries rather than ADR files, matching your earlier choice | Approve |
| A8 | CI: GitHub Actions workflow on `windows-latest` | Approve |
| A9 | Permission to install the .NET 10 SDK in this cloud session for local build/test of non-WPF projects | Approve (otherwise CI-only verification) |
| A10 | Answers to the open questions in `REQUEST_STATUS_TRANSITIONS_DRAFT.md` §4 (needed before Milestone 2, not before M1) | Review when convenient |

When you reply, tell me which items you approve or change; I will then record them in the Decision Log and begin M1.1 only.
