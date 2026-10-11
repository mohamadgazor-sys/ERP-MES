# Milestone 1 — Technical Foundation: Solution and Project Plan (revision 2, for approval)

**Status:** Proposal, revision 2. **No code, solution, project, package, migration, workflow file or SDK has been created or installed.** Nothing starts until you approve this plan (§12 lists every proposed decision).

Basis: audit `PHASE0_SPECIFICATION_AUDIT.md` §6.2; Decision Log #22–#27 (technical conventions), #30 (roles seed), #34 (permission map); Architecture Specification §3, §5, §17, §31 and the revised §38.

## 1. Scope

**Milestone 1 is the technical foundation only.** It deliberately contains no business workflow.

**In scope**
- Solution, projects, build and CI configuration, SDK pin.
- Domain primitives: `JalaliDateService`, Rial `Money` formatting/parsing, result/error type.
- Entities `Department`, `Role`, `User`, `AuditLog`, `SystemSetup`; development seed of departments and roles (Decision Log #30).
- Secure one-time Administrator initialization (§5).
- Authentication (PBKDF2, lockout) and a default-deny authorization service (§6, §7).
- SQLite persistence base and the initial SQLite migration for the tables above.
- WPF shell: Persian RTL login window and an empty main window; check that LTR identifiers display correctly inside RTL text.

**Explicitly out of scope (must not be implemented in Milestone 1)**
- Customer, Request, RequestItem, `RepairItemDetails`, Material, UOM.
- Request workflow engine and request-status transitions.
- Excel import.
- MAKE/REPAIR functionality.
- Documents/PDF storage.
- Stages, production, QC, external work, contracts, costing, warehouse, scheduling, dashboards.
- SQL Server code or migrations; the production SQL Server version/provider stays unresolved.

This replaces the earlier Architecture Specification §38 list, which bundled Request/RequestItem, the request workflow, Excel import and MAKE/REPAIR into the first milestone. The Architecture Specification has been revised to match (§38.1–38.4).

## 2. Projects and dependency direction

```text
ERP-MES.sln
global.json
Directory.Build.props
Directory.Packages.props          (central package versions)
.editorconfig
.config/dotnet-tools.json         (local tool manifest for the EF CLI; tool not installed now)
src/
  ERP.MES.Domain/                  net10.0
  ERP.MES.Application/             net10.0
  ERP.MES.Infrastructure/          net10.0   (shared, provider-neutral)
  ERP.MES.Infrastructure.Sqlite/   net10.0   (SQLite only; contains SQLite migrations)
  ERP.MES.Desktop/                 net10.0-windows   (WPF; composition root)
  ERP.MES.Provisioning/            net10.0   (console tool for IT; never shipped to factory PCs)
tests/
  ERP.MES.Domain.Tests/            net10.0
  ERP.MES.Application.Tests/       net10.0
  ERP.MES.Infrastructure.Tests/    net10.0
  fixtures/                        (exists; synthetic fixtures only)
.github/workflows/ci.yml
```

**Dependency direction (an arrow means "references")**

```text
Desktop ───────────────┐
Provisioning ──────────┤
                       ├──► Application ──► Domain
Infrastructure.Sqlite ─┤        ▲
        │              │        │
        └──► Infrastructure ────┘   (Infrastructure also references Domain)
```

- `Domain` references nothing (no EF Core, no WPF, no database).
- `Application` references `Domain` only.
- `Infrastructure` references `Application` and `Domain`, plus EF Core (relational, provider-neutral). It references **no database provider package**.
- `Infrastructure.Sqlite` references `Infrastructure` and the EF Core SQLite provider.
- `Desktop` and `Provisioning` are composition roots: they reference `Application`, `Infrastructure` and the provider project(s) they use, and nothing in the lower layers references them.
- Test projects reference only what they test. `Infrastructure.Tests` references `Infrastructure` and `Infrastructure.Sqlite`.

### 2.1 What belongs where

| Project | Belongs here | Does not belong here |
|---|---|---|
| **Domain** | Entities, value objects, domain rules, `JalaliDateService` (built on the base-library `PersianCalendar`, no extra package), `Money`, result/error types, enumerations and stable codes. | Anything that knows about EF Core, SQL, files, WPF, configuration or the clock source. |
| **Application** | Use cases (login, bootstrap, create/deactivate user), abstractions the use cases need (`IUserRepository`, `IUnitOfWork`, `IPasswordHasher`, `IAuditWriter`, `IClock`, `IAuthorizationService`), the permission rule set, policy options classes. | Persistence code, UI code. |
| **Infrastructure** (shared) | `ErpMesDbContext`; provider-neutral entity configuration (Fluent API only); repository and unit-of-work implementations; the `PasswordHasher` (PBKDF2); audit writing; the clock; option binding; the extension method that registers all of this; a small `IProviderConventions` contract (§2.3). Everything that must behave identically on every provider. | Any `UseSqlite`/`UseSqlServer` call, connection strings, provider package references, migrations, provider-specific SQL. |
| **Infrastructure.Sqlite** | `UseSqlite(...)` setup and its extension method; SQLite connection settings (for example foreign-key enforcement on every connection); the SQLite implementation of `IProviderConventions`; **all SQLite migrations** and the design-time context factory; SQLite-only workarounds with a comment saying why. | Business rules, repositories, anything another provider would also need. |
| **Infrastructure.SqlServer** *(future, not created now)* | The same four things for SQL Server: setup extension, conventions implementation, **its own separate migrations**, SQL Server-specific tuning. | Business rules and shared mapping. |
| **Desktop** | WPF views, view models, resources (Persian strings), navigation, startup/composition, selection of the provider from configuration. | Authorization decisions, SQL, business rules. |
| **Provisioning** | One-time and maintenance commands for IT: issue bootstrap token (§5), hash-time benchmark (§6.3). Uses the same Application/Infrastructure code as the app. | Anything that ordinary factory users need; end-user screens. |

### 2.2 How provider independence is preserved without duplicating business logic

1. All business rules live in Domain and Application, which cannot reference EF Core or any provider; the compiler enforces this through project references.
2. There is **one** `ErpMesDbContext` and **one** set of entity configurations, written with provider-neutral Fluent API calls only. A provider project does not re-describe the model; it supplies connection setup and migrations for the same model.
3. Differences between providers are expressed through one small contract, `IProviderConventions` (for example: the name of the case-sensitive binary collation for `RequestNo` in Milestone 2, how to enforce foreign keys). Each provider project implements it; shared code calls it. Nothing else differs.
4. Migrations are generated per provider, into that provider's project, from the same model (EF Core migrations are provider-specific). The SQLite development database is disposable (Decision Log #25).
5. Repository and persistence behaviour is tested by **provider-agnostic test classes** in `Infrastructure.Tests` (an abstract base plus a provider fixture). Today only a SQLite fixture exists; when a SQL Server project is added, a SQL Server fixture is added and the same tests run against it. Persistence tests are therefore written once.
6. The desktop application picks the provider from configuration (`Database:Provider`). Adding SQL Server later adds one project reference and one `case` in the composition root; Domain and Application are untouched.

### 2.3 Future SQL Server

A future `ERP.MES.Infrastructure.SqlServer` project is **not** created in Milestone 1. Which SQL Server version and which client provider package to use is **unresolved** (Decision Log, Unresolved) and is decided when the deployment environment is checked. Nothing in this plan assumes a specific version.

## 3. Components

**3.1 `JalaliDateService`**
- Interface `IJalaliDateService` and implementation `JalaliDateService` live in Domain (the implementation uses only `System.Globalization.PersianCalendar`; no package is added). The Request business key will need the Jalali year in Domain code (Milestone 2).
- Operations: Gregorian ↔ Jalali date; Jalali year of a date; Jalali date-range → half-open Gregorian range; format/parse for display and input (24-hour time).
- Decision: **use `PersianCalendar` unless a demonstrated requirement appears.** A requirement counts as demonstrated only if a golden test (known Jalali ↔ Gregorian pairs across several leap years and the end/start of years, supplied by you or taken from a source you accept) fails against it. If that happens, a library is proposed to you, behind the same interface.

**3.2 `Money`**
- Rial formatting (thousand separators, "ریال" label) and parsing only. Rounding and storage type are unresolved (Decision Log, Unresolved), so Milestone 1 adds no arithmetic beyond equality/compare and no database column of type money.

**3.3 Persistence conventions** (Decision Log #23–#25)
- `long` identity keys; `long Version` column on mutable entities, incremented by the application and checked on update.
- Timestamps in UTC; calendar dates as `DateOnly`; no `DateTimeOffset`.
- Foreign keys enforced on every SQLite connection.
- Database path comes from configuration, outside source control (default under the user's local application-data folder; `.gitignore` already excludes `*.db`).
- A short "provider-neutral subset" note is added to `docs/` in step M1.3.

**3.4 Entities (Milestone 1 only)**
- `Department`, `Role`; seed values from Decision Log #30.
- `User`: username, display name, department, role, active flag, failed-login count, lockout-end (UTC), last login, password fields (§6.2), version.
- `AuditLog` (append-only): UTC time, user (nullable), machine name, action code, target type/id, summary.
- `SystemSetup` (single row): initialization state and bootstrap token data (§5.3).

## 4. Out-of-scope guard

Any entity, table or use case beyond §3 that looks "needed" during Milestone 1 is reported to you and not added. In particular no request/item schema or migration is created.

## 5. Secure administrator initialization (one-time bootstrap)

**Requirements:** no default password or credential anywhere in the repository; an ordinary user must not be able to claim the Administrator account by launching the application first; safe when several PCs connect to the same database at once. This section is a proposed mechanism for review, **not implemented**.

### 5.1 Principles
1. The application's first-run screen **never** creates an Administrator by itself.
2. Creating the first Administrator requires a **one-time secret (bootstrap token)** that is issued by IT with a separate tool and delivered out-of-band (read aloud, written note, secure message). The token is not stored in plaintext anywhere.
3. Initialization is **atomic and happens at most once**, enforced by the database, not by the client.
4. After success the bootstrap path is **permanently closed**.

### 5.2 Flow
1. A freshly migrated database has `SystemSetup.State = NotProvisioned`. There are no users (the seed creates departments and roles only).
2. IT runs the Provisioning tool: `issue-admin-token`. It checks that `State = NotProvisioned` (or `AwaitingBootstrap`, to re-issue), generates 32 random bytes from the operating-system cryptographic generator, stores `SHA-256(token)` plus an expiry (default 24 hours, configurable by IT at issue time), sets `State = AwaitingBootstrap` and `FailedAttempts = 0`, writes an audit entry (OS user name, machine name), and prints the token **once** to the console. A high-entropy random token is hashed with SHA-256 and compared in constant time; PBKDF2 is not needed for it.
3. The desktop application, at start-up, reads `SystemSetup.State`:
   - `NotProvisioned`: shows "The system has not been initialised. Contact your system administrator." No input fields.
   - `AwaitingBootstrap`: shows the first-run screen with *token*, *new administrator username*, *display name*, *password* (twice).
   - `Initialized`: shows the normal login screen; the first-run screen can never appear again.
4. On submit, one **database transaction**:
   1. Atomically increment `FailedAttempts` in its own short transaction **before** checking the token, so failed guesses persist even if the main transaction rolls back. If `FailedAttempts` has reached the limit (proposed 5) or the token has expired, set `State = NotProvisioned`, audit the lock, and stop; IT must issue a new token.
   2. Compare `SHA-256(entered token)` with the stored hash in constant time. If different: audit, show a generic error.
   3. Validate the new password against the password policy (§6.1).
   4. Claim the setup row: `UPDATE SystemSetup SET State = 'Initialized', TokenHash = NULL, InitializedAtUtc = …, Version = Version + 1 WHERE Id = 1 AND State = 'AwaitingBootstrap' AND Version = @read`. If the affected-row count is not exactly 1, roll back and tell the user the system was already initialised.
   5. Insert the Administrator user (role Admin, department System) in the same transaction, write the audit entry, commit.
5. After commit the token hash is gone, `State = Initialized`, and every code path that could create an Administrator without an existing Administrator is closed.

### 5.3 `SystemSetup` columns (single row, `Id = 1`)
`Id`, `State` (`NotProvisioned` | `AwaitingBootstrap` | `Initialized`), `TokenHash`, `TokenExpiresAtUtc`, `FailedAttempts`, `InitializedAtUtc`, `Version`. No filtered/partial unique index is used (not provider-neutral); the single-row conditional update is the arbiter.

### 5.4 Concurrent initialization (several PCs, same database)
- Two PCs with a valid token submit at the same moment: both transactions try step 4.4; the conditional `UPDATE … WHERE State = 'AwaitingBootstrap' AND Version = @read` can match only once because the database serialises writers on that row. The loser sees 0 affected rows, rolls back, and is told the system is already initialised. Exactly one Administrator exists.
- A PC that launched earlier and still shows the first-run screen after another PC finished gets the same "already initialised" result.
- Guesses from several PCs share one persistent `FailedAttempts` counter, so the limit applies to all PCs together.
- A test runs two claim attempts in parallel against SQLite; the same test class is re-run against SQL Server when that provider project exists (§2.2 point 5).

### 5.5 What this does and does not protect
- **Protects against:** an ordinary user starting the app first; token guessing (256-bit token, 5 attempts, expiry); double initialization; a default credential existing (none exists).
- **Does not protect against:** a person who can write to the database directly (database credentials are the trust boundary), or a person holding both the Provisioning tool and a database connection. The Provisioning tool is therefore not distributed with the factory install, and production database permissions (separate privileged login for provisioning) are designed when the SQL Server environment is known.
- **Not designed here:** recovery if the only Administrator is lost or deactivated (a "break-glass" procedure). It needs your decision; see §12 item P12.

### 5.6 Audit and logging
`AdminTokenIssued`, `BootstrapFailed`, `BootstrapLocked`, `BootstrapCompleted` (machine name, OS user name). The token and the password are never logged.

### 5.7 Tests (acceptance)
No credential in the repository (a test scans tracked files for the seed users and known weak passwords); wrong token rejected and counted; expired token rejected; limit reached locks bootstrap; correct token creates exactly one Administrator; second claim fails; parallel claims produce one winner; state `Initialized` never shows the first-run screen; token and password never appear in log or audit output; password policy enforced at bootstrap.

## 6. Authentication

### 6.1 Policy defaults (all configurable; proposals for your review)

| Setting | Proposed default | Notes |
|---|---|---|
| `MinimumPasswordLength` | **12** | Counted in characters; no composition rules proposed |
| `MaxFailedAttempts` | **5** | Counter stored in the database per user (shared across PCs) |
| `LockoutMinutes` | **5** | Lock ends automatically; Administrator can unlock |
| `Pbkdf2Iterations` | **600000** | See §6.2 |

Configuration comes from `appsettings.json` (non-secret defaults committed; `appsettings.Local.json` ignored). Values are validated at start-up and the application refuses to start if a value is below a documented safety floor (proposed floors: length ≥ 8, attempts 3–10, lockout ≥ 1 minute, iterations ≥ 310,000). Because several PCs will share one database later, policy should eventually live in the database; that is deferred to when multi-user deployment is designed and is not done in Milestone 1.

Password changes and resets: the Administrator resets passwords (no self-service e-mail reset exists); a user may change their own password. Login success and failure are written to `AuditLog`.

### 6.2 Password hashing (Decision Log #27)
- Algorithm: **PBKDF2-HMAC-SHA256**, starting work factor **600,000** iterations (OWASP's Password Storage Cheat Sheet lists 600,000 for PBKDF2-HMAC-SHA256; checked 2026-10-11), 16-byte random salt per hash, 32-byte derived key, built-in .NET implementation, constant-time comparison.
- Stored with every user: `PasswordHash`, `PasswordSalt`, `PasswordAlgorithm` (`PBKDF2-HMAC-SHA256`), `PasswordHashVersion` (integer, starts at 1), `PasswordWorkFactor` (iterations). Nothing is inferred from a global setting when verifying: the stored values are used.
- Transparent upgrade: after a successful login, if the stored algorithm/version/work factor is below the configured one, the password is re-hashed and stored.

### 6.3 Benchmark on the lowest-spec supported PC
- The Provisioning tool has a `benchmark-hash` command. It measures the time for one PBKDF2 operation at the configured work factor on the machine where it runs, and prints the result.
- Proposed procedure: run it on the lowest-spec PC that must be supported, which you identify (needs the factory inventory). Proposed target: one hash ≤ about 500 ms on that PC. If it is slower, the options are reported to you; the work factor is **not** lowered below 600,000 without your approval.
- Until that PC is identified, 600,000 is a starting value only.

## 7. Authorization

Action codes and a rule set mapping each to allowed department/role, built from Domain spec §15.2 and §15.4, are implemented as code with tests. Anything not mapped is denied. Only the actions that exist in Milestone 1 are wired (administer users, bootstrap); the other matrix rows are encoded and tested as rules but have no screens. The service is called at the use-case boundary; the UI only mirrors it.

## 8. Packages and tools

**Rules:** versions are managed centrally in `Directory.Packages.props` (`ManagePackageVersionsCentrally`); only stable (non-preview) releases; exact versions pinned; each package's licence, .NET 10 compatibility and maintenance status is re-checked on NuGet and reported to you before it is added; transitive vulnerabilities are checked with the SDK's NuGet audit. **No package has been installed.**

| Package | Project | Purpose (why needed) | .NET 10 compatibility | Licence |
|---|---|---|---|---|
| `Microsoft.EntityFrameworkCore.Relational` | Infrastructure | ORM and relational model configuration; the persistence approach named in the Architecture Specification §4.2 | Use the 10.x release line that matches .NET 10 | MIT (expected; verify on NuGet) |
| `Microsoft.EntityFrameworkCore.Sqlite` | Infrastructure.Sqlite | SQLite provider (development database) | Same 10.x line | MIT (expected; verify) |
| `Microsoft.EntityFrameworkCore.Design` | Infrastructure.Sqlite (build-time only, `PrivateAssets=all`) | Required by the EF command-line tool to create migrations | Same 10.x line | MIT (expected; verify) |
| `dotnet-ef` (local tool in `.config/dotnet-tools.json`) | tooling | Generate migrations | Matches EF 10.x | MIT (expected; verify) |
| `Microsoft.Extensions.DependencyInjection` (+ `.Abstractions`) | Desktop, Provisioning (+ Abstractions in Application/Infrastructure) | Composition root and registration methods | 10.x | MIT (expected; verify) |
| `Microsoft.Extensions.Options` + `...Options.ConfigurationExtensions` + `...Configuration.Json` | Application, Infrastructure, Desktop | Typed, validated settings from `appsettings.json` (policy, database) | 10.x | MIT (expected; verify) |
| `Microsoft.Extensions.Logging.Abstractions` | Application, Infrastructure | Logging calls without choosing a sink; sink decided later | 10.x | MIT (expected; verify) |
| `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio` | all test projects | Test host and framework | Current stable release supporting net10.0 (xUnit v2 or v3 chosen at pin time) | Apache-2.0 / MIT (expected; verify) |

**Deliberately not added in Milestone 1** (to keep dependencies minimal): `CommunityToolkit.Mvvm` (a few lines of hand-written `ObservableObject`/`RelayCommand` suffice for one login window; revisit in Milestone 2), assertion libraries, mocking libraries (hand-written fakes), a Jalali library (`PersianCalendar` is used), a logging sink, a PBKDF2/hashing package (built into .NET), Hosting meta-package, a font package.

**Compatibility and licence status is an expectation from package metadata I know of; none of it was re-verified in this session.** That check is step M1.1's first task, with results listed for you before any `PackageReference` is added.

## 9. SDK pin

- Proposed `global.json` (not created yet):

```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "latestPatch",
    "allowPrerelease": false
  }
}
```

- Source of the version: Microsoft's .NET 10.0 download page, checked 2026-10-11, shows SDK **10.0.401** with runtime 10.0.12 (security patch, released 2026-09-08) as the latest .NET 10 (LTS) release. I read only the first part of that long page (the latest-release block); the other .NET 10 supported-OS and support-date facts are in Decision Log #22.
- Before the pin is committed, step M1.1 confirms on Windows CI that the pinned SDK installs (`actions/setup-dotnet` with `global-json-file`) and that `dotnet --version` reports it. If a newer stable patch exists then, I propose the update to you rather than assuming.
- No SDK is installed in the current Claude environment for this task; none is needed for documentation.

## 10. Persian UI font

- Proposed: a font-family chain **"Segoe UI, Tahoma, Arial"** (first available is used). These ship with Windows 10/11 as far as I know; that and their rendering of Persian letters (پ چ ژ گ ک ی), the zero-width non-joiner and digits must be checked on real Windows 10 and 11 machines in the manual checklist (§11) before the choice is final.
- **No font is bundled** in Milestone 1. Bundling (for example an open-licence Persian font) is considered only after licensing terms and deployment needs are established, and would be a separate decision.

## 11. Verification environment

- **Authoritative verification is Windows CI** (GitHub Actions, `windows-latest`): restore, build Release, test with TRX output uploaded as an artifact. This covers the WPF project, which can only be built on Windows. I can write the workflow file; I cannot run it from this session, so every build/test statement I make will state that it comes from the CI run and link the run.
- Proposed workflow hygiene: third-party actions limited to the official `actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`, each pinned to a full commit SHA; read-only token permissions; no secrets used; CI does not run on forks with write access.
- Optional later: a second job on a Linux runner for the non-WPF test projects, to keep Domain/Application/Infrastructure portable.
- The WPF UI is verified by a manual checklist until a UI-automation approach is chosen: RTL alignment, Persian typing, tab order, resize, mixed LTR/RTL text, high-DPI, first-run screen states.

## 12. Work breakdown, acceptance criteria, risks

**Steps** (each a reviewable pull request; build and tests run after each)

| Step | Content | Verified by |
|---|---|---|
| M1.1 | Solution, projects, `global.json`, central packages (after the package check in §8), `Directory.Build.props`, `.editorconfig`, empty test projects, CI workflow | CI run: build succeeds; pinned SDK confirmed |
| M1.2 | `JalaliDateService` + golden tests, `Money` formatting, result type | Unit tests |
| M1.3 | Persistence base, entities, seed, `SystemSetup`, initial SQLite migration, version column, audit log, provider-neutral-subset note | Provider-agnostic integration tests on SQLite (migration from empty, FK enforcement, concurrency conflict, append-only audit) |
| M1.4 | Password hasher, authentication service, lockout, policy options and validation | Unit + integration tests |
| M1.5 | Provisioning tool: `issue-admin-token`, `benchmark-hash`; bootstrap use case and first-run screen logic | Bootstrap tests (§5.7), including the parallel-claim test |
| M1.6 | Authorization rule set | One allowed and one denied test per action; unlisted action denied |
| M1.7 | WPF shell: login, first-run screen, RTL, resources, font chain, bidi check | CI build; manual checklist with screenshots |

**Acceptance criteria**
1. A clean checkout builds Debug and Release on the Windows CI agent; the CI log is linked.
2. All automated tests pass in CI.
3. The app starts; the three `SystemSetup` states show the correct screen; valid login works; wrong password, inactive user and locked user are rejected.
4. No default credential, token or real data in the repository; no plaintext password stored; same password for two users stores differently; every stored hash records algorithm, version and work factor.
5. Bootstrap: only a valid, unexpired token creates the Administrator; exactly one Administrator results from parallel attempts; the bootstrap path is closed afterwards.
6. Every authorization matrix row has an allowed and a denied test; unlisted actions are denied.
7. Jalali golden table passes; a Jalali range converts to a half-open Gregorian range; no date comparison by formatted string.
8. Rial values format and parse correctly; no Toman unit exists.
9. The database is created from the SQLite migration; foreign keys enforced; concurrent-update conflict detected.
10. A grid shows `P-565`, `1.10`, `wsj-898` readable inside RTL; screenshot attached.
11. **No** Customer, Request, RequestItem, Excel, MAKE/REPAIR, document or workflow code, table or migration exists.

**Risks:** `PersianCalendar` accuracy (mitigated by golden tests and the interface); bidi rendering of LTR identifiers; lowest-spec PC unknown until the factory inventory (hash time); Windows 10 coverage of .NET 10 is partial (Decision Log #22); provider drift until a second provider exists (SQL Server test pass scheduled before the stage/QC milestone, Decision Log #25); bootstrap residual risk in §5.5.

## 13. Proposed decisions requiring your approval

| # | Proposed decision | Where |
|---|---|---|
| P1 | Milestone 1 is the technical foundation only; Architecture Specification §38 is revised accordingly (Request/RequestItem, workflow, Excel, MAKE/REPAIR moved to Milestones 2–3) | §1; Architecture §38 |
| P2 | Project structure and dependency direction, including the shared `Infrastructure`, `Infrastructure.Sqlite`, a future `Infrastructure.SqlServer`, and the `IProviderConventions` contract | §2 |
| P3 | A separate `ERP.MES.Provisioning` console project, not shipped to factory PCs | §2, §5 |
| P4 | Administrator bootstrap mechanism: one-time token (256-bit, SHA-256 stored, expiry 24 h, 5 attempts), `SystemSetup` state machine, atomic conditional claim | §5 |
| P5 | Authentication defaults: 12-character minimum, 5 failed attempts, 5-minute lockout, configurable with safety floors | §6.1 |
| P6 | PBKDF2-HMAC-SHA256, 600,000 iterations, 16-byte salt, stored algorithm/version/work factor, transparent upgrade | §6.2 |
| P7 | Hash benchmark on the lowest-spec supported PC with a ≈500 ms target; you identify that PC | §6.3 |
| P8 | Package list and the explicit exclusions; per-package licence/compatibility check before adding | §8 |
| P9 | Central package version management; stable releases only | §8 |
| P10 | `PersianCalendar` behind `JalaliDateService` (in Domain), with a library only after a demonstrated golden-test failure | §3.1 |
| P11 | SDK pin `10.0.401` with `rollForward: latestPatch`, to be confirmed on CI before commit | §9 |
| P12 | **Question for you:** how should recovery work if the only Administrator is lost or deactivated? (Not designed here.) | §5.5 |
| P13 | Font chain "Segoe UI, Tahoma, Arial", nothing bundled, to be checked on real machines | §10 |
| P14 | Windows CI as the authoritative verification; action pinning and permissions; no SDK installed in the Claude environment | §11 |
| P15 | Manual UI checklist in Milestone 1 instead of UI automation | §11 |

**Not decided here (stay unresolved):** production SQL Server version/provider; minimum Windows edition/version; the request-status transition table and its open questions (see `REQUEST_STATUS_TRANSITIONS_DRAFT.md`, deferred until before the request-workflow milestone); Rial rounding and storage; central storage of authentication policy; the break-glass recovery procedure.

When you reply, tell me which items you approve or change; I will then record the approved items in the Decision Log and begin step M1.1 only.
