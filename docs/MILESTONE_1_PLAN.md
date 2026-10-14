# Milestone 1 — Technical Foundation: Solution and Project Plan (revision 3)

**Status:** **Approved with conditions on 2026-10-11** (P1–P15 recorded as Decision Log #36–#50). The M1.1 skeleton was approved on 2026-10-11 subject to the confirmations in Decision Log #51–#53 (exact SDK pin, xUnit v3, package checks). M1.1 is merged. **M1.2A (`JalaliDateService` only) is authorized and in review; M1.2B and later steps start only on your instruction.** P8 (packages) stays conditional: each batch of packages is added only after its dependency graph, restore and transitive vulnerability results are recorded in §8.1 and addressed.

Basis: audit `PHASE0_SPECIFICATION_AUDIT.md` §6.2; Decision Log #22–#27 and #36–#50 (technical conventions and Milestone 1 approvals), #30 (roles seed), #34 (permission map); Architecture Specification §3, §5, §17, §31, §38.

## 1. Scope

**Milestone 1 is the technical foundation only.** It deliberately contains no business workflow.

**In scope**
- Solution, projects, build and CI configuration, SDK pin.
- Domain primitives: `JalaliDateService`, Rial `Money` formatting/parsing, result/error type.
- Entities `Department`, `Role`, `User`, `AuditLog`, `SystemSetup`; development seed of departments and roles (Decision Log #30).
- Secure one-time Administrator initialization and the Administrator recovery mechanism (§5).
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
| **Provisioning** | Restricted commands for authorized operators: issue the bootstrap token and the recovery token (§5), hash-time benchmark (§6.3). Uses the same Application/Infrastructure code as the app. The operator supplies the database connection at run time; **no privileged credential is embedded, committed or stored in application configuration** (Decision Log #38). Excluded from the normal factory-PC distribution. | Anything that ordinary factory users need; end-user screens; stored credentials. |

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
- Operations: Gregorian ↔ Jalali date; Jalali year of a date; Jalali date-range → half-open Gregorian range; format/parse for display and input (24-hour time). Input parsing accepts Persian digits (۰–۹) as well as Latin digits.
- Required tests (Decision Log #45): Nowruz and year-boundary dates (last day of Esfand to 1 Farvardin, including leap years), leap-year handling across several years, month ends, and Persian-digit input.
- **Status (M1.2A):** implemented in `src/ERP.MES.Domain/Calendar/` with tests; behaviour, invalid-input rules, golden data and open questions are in [`JALALI_DATE_SERVICE.md`](JALALI_DATE_SERVICE.md).
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

**Requirements:** no default password or credential anywhere in the repository; an ordinary user must not be able to claim the Administrator account by launching the application first; safe when several PCs connect to the same database at once. The mechanism is approved (Decision Log #39, #47) and **not yet implemented**; it is built in steps M1.3–M1.5.

### 5.1 Principles
1. The application's first-run screen **never** creates an Administrator by itself.
2. Creating the first Administrator requires a **one-time secret (bootstrap token)** that is issued by IT with a separate tool and delivered out-of-band (read aloud, written note, secure message). The token is not stored in plaintext anywhere.
3. Initialization is **atomic and happens at most once**, enforced by the database, not by the client.
4. After success the bootstrap path is **permanently closed**.
5. Only a **digest** of the token is stored, never the plaintext; the token is never logged and never committed.
6. **Re-issuing a token invalidates the previous one** (the stored digest, expiry and attempt counter are replaced).
7. The attempt limit is held in the database and therefore enforced **centrally across all application instances**.

### 5.2 Flow
1. A freshly migrated database has `SystemSetup.State = NotProvisioned`. There are no users (the seed creates departments and roles only).
2. IT runs the Provisioning tool: `issue-admin-token`. It checks that `State = NotProvisioned` (or `AwaitingBootstrap`, to re-issue), generates 32 random bytes from the operating-system cryptographic generator, stores `SHA-256(token)` plus an expiry (default 24 hours, configurable by IT at issue time), sets `State = AwaitingBootstrap` and `FailedAttempts = 0` (this replaces any earlier digest, so a previously issued token stops working), writes an audit entry (OS user name, machine name), and prints the token **once** to the console; it is never written to a file or log. The tool takes the database connection from the operator at run time (§2.1). A high-entropy random token is hashed with SHA-256 and compared in constant time; PBKDF2 is not needed for it.
3. The desktop application, at start-up, reads `SystemSetup.State`:
   - `NotProvisioned`: shows "The system has not been initialised. Contact your system administrator." No input fields.
   - `AwaitingBootstrap`: shows the first-run screen with *token*, *new administrator username*, *display name*, *password* (twice).
   - `Initialized`: shows the normal login screen; the first-run screen can never appear again.
4. On submit, one **database transaction**:
   1. Atomically increment `FailedAttempts` in its own short transaction **before** checking the token, so failed guesses persist even if the main transaction rolls back. If `FailedAttempts` has reached the limit (5, Decision Log #39) or the token has expired, set `State = NotProvisioned`, audit the lock, and stop; IT must issue a new token.
   2. Compare `SHA-256(entered token)` with the stored hash in constant time. If different: audit, show a generic error.
   3. Validate the new password against the password policy (§6.1).
   4. Claim the setup row: `UPDATE SystemSetup SET State = 'Initialized', TokenHash = NULL, InitializedAtUtc = …, Version = Version + 1 WHERE Id = 1 AND State = 'AwaitingBootstrap' AND Version = @read`. If the affected-row count is not exactly 1, roll back and tell the user the system was already initialised.
   5. Insert the Administrator user (role Admin, department System) in the same transaction, write the audit entry, commit.
5. After commit the token hash is gone, `State = Initialized`, and every code path that could create an Administrator without an existing Administrator is closed.

### 5.3 `SystemSetup` columns (single row, `Id = 1`)
`Id`, `State` (`NotProvisioned` | `AwaitingBootstrap` | `Initialized`), `TokenHash`, `TokenExpiresAtUtc`, `FailedAttempts`, `InitializedAtUtc`, `AdminChangeStamp` (§5.7), recovery columns `RecoveryTokenHash`, `RecoveryTokenExpiresAtUtc`, `RecoveryFailedAttempts`, `RecoveryTargetUserId` (§5.7), `Version`. No filtered/partial unique index is used (not provider-neutral); the single-row conditional update is the arbiter.

### 5.4 Concurrent initialization (several PCs, same database)
- Two PCs with a valid token submit at the same moment: both transactions try step 4.4; the conditional `UPDATE … WHERE State = 'AwaitingBootstrap' AND Version = @read` can match only once because the database serialises writers on that row. The loser sees 0 affected rows, rolls back, and is told the system is already initialised. Exactly one Administrator exists.
- A PC that launched earlier and still shows the first-run screen after another PC finished gets the same "already initialised" result.
- Guesses from several PCs share one persistent `FailedAttempts` counter, so the limit applies to all PCs together.
- A test runs two claim attempts in parallel against SQLite; the same test class is re-run against SQL Server when that provider project exists (§2.2 point 5).

### 5.5 What this does and does not protect
- **Protects against:** an ordinary user starting the app first; token guessing (256-bit token, 5 attempts, expiry); double initialization; a default credential existing (none exists).
- **Does not protect against:** a person who can write to the database directly (database credentials are the trust boundary), or a person holding both the Provisioning tool and a database connection. The Provisioning tool is therefore not distributed with the factory install, and production database permissions (separate privileged login for provisioning) are designed when the SQL Server environment is known.
- Recovery if all Administrator access is lost is defined in §5.7.

### 5.6 Audit and logging
`AdminTokenIssued`, `BootstrapFailed`, `BootstrapLocked`, `BootstrapCompleted`, `RecoveryTokenIssued`, `RecoveryFailed`, `RecoveryLocked`, `RecoveryCompleted`, `LastAdminProtectionRefused` (machine name, OS user name; for operator actions also the database login name as reported by the database). Tokens and passwords are never logged.

### 5.7 Administrator recovery (Decision Log #47)

**Rule 1 — normal operations cannot remove the last Administrator.** User-management use cases refuse to deactivate or demote (change the role of) the last active Administrator. The check and the change run in one transaction that also advances `SystemSetup.AdminChangeStamp` with a conditional update (`WHERE AdminChangeStamp = @read`), so two concurrent changes cannot both pass; the loser re-reads and is refused. Users are never physically deleted.

**Rule 2 — recovery needs an authorized operator.** If all Administrator access is lost, recovery requires an authorized operator using the restricted Provisioning tool **with authorized database access**. There is no universal recovery password, hidden master key, or backdoor, and none may be added; a review test searches the tracked source for such constructs.

**Mechanism.**
1. The operator runs `issue-recovery-token --username <existing Administrator account> --reason <text>` with their own database credentials supplied at run time (integrated authentication or an interactive prompt; never stored in a file, in the repository or in application configuration).
2. The tool generates a 256-bit random token, stores only its SHA-256 digest with a 24-hour expiry and an attempt counter in the recovery columns of `SystemSetup`, binds it to the target account, and prints the token once. Issuing again replaces the digest, so the previous recovery token stops working. Reason, OS user, machine name and database login are audited.
3. Out-of-band, the token is given to the person who will recover the account. The login screen offers "use recovery token" only while a recovery token is pending; without a valid token it does nothing.
4. The person enters the token and a **new password** that satisfies the policy. In one transaction: the attempt counter is incremented centrally first (limit 5); the digest is compared in constant time; the recovery claim is an atomic conditional update (`WHERE RecoveryTokenHash IS NOT NULL AND Version = @read`) so only one redemption can succeed; then the target account gets the new password hash (fresh salt, current work factor), is reactivated if inactive, is set to the Administrator role, has its failed-login counter and lockout cleared, and the token digest is erased. The new password must differ from the old one (checked against the stored hash). The old password no longer works.
5. Everything is audited (§5.6). The account is therefore forced to set a new password before it can be used again.

**SQL Server deployment (process only; version and provider not decided).** (1) A named, authorized operator holds a database login limited to what Provisioning needs; it is separate from the application's login and is not present on factory PCs. (2) The operator runs Provisioning from a controlled administration PC with their own credentials. (3) The operator issues a recovery token and hands it over out-of-band. (4) The designated person redeems it in the application. (5) The audit record names the operator, their database login, the machine and the reason; database-level auditing, if available, supplements it. (6) The incident is reviewed afterwards. The exact permission scripts are designed once the SQL Server environment is known.

### 5.8 Tests (acceptance)
No credential in the repository (a test scans tracked files for seed users, known weak passwords and recovery backdoor patterns); wrong token rejected and counted; expired token rejected; limit reached locks bootstrap; correct token creates exactly one Administrator; second claim fails; parallel claims produce one winner; re-issuing invalidates the previous token (bootstrap and recovery); state `Initialized` never shows the first-run screen; token and password never appear in log or audit output; password policy enforced at bootstrap and recovery; last active Administrator cannot be deactivated or demoted, including two concurrent attempts; recovery requires a valid token, forces a different new password, reactivates the target, clears lockout and audits the event.

## 6. Authentication

### 6.1 Policy defaults (approved provisionally, Decision Log #40; all configurable)

| Setting | Proposed default | Notes |
|---|---|---|
| `MinimumPasswordLength` | **12** | Counted in characters; no composition rules proposed |
| `MaxFailedAttempts` | **5** | Counter stored in the database per user (shared across PCs) |
| `LockoutMinutes` | **5** | Lock ends automatically; Administrator can unlock |
| `Pbkdf2Iterations` | **600000** | See §6.2 |

Configuration comes from `appsettings.json` (non-secret defaults committed; `appsettings.Local.json` ignored). Values are validated at start-up and the application refuses to start if a value is below a documented safety floor (proposed floors: length ≥ 8, attempts 3–10, lockout ≥ 1 minute, iterations ≥ 310,000). Because several PCs will share one database later, policy should eventually live in the database; that is deferred to when multi-user deployment is designed and is not done in Milestone 1.

Security events are audited: login success and failure, lockout and unlock, password change and reset, bootstrap and recovery events.

Password changes and resets: the Administrator resets passwords (no self-service e-mail reset exists); a user may change their own password. Login success and failure are written to `AuditLog`.

### 6.2 Password hashing (Decision Log #27, #41)
- Algorithm: **PBKDF2-HMAC-SHA256**, starting work factor **600,000** iterations (OWASP's Password Storage Cheat Sheet lists 600,000 for PBKDF2-HMAC-SHA256; checked 2026-10-11), 16-byte random salt per hash, 32-byte derived key, built-in .NET implementation, constant-time comparison.
- Stored with every user: `PasswordHash`, `PasswordSalt`, `PasswordAlgorithm` (`PBKDF2-HMAC-SHA256`), `PasswordHashVersion` (integer, starts at 1), `PasswordWorkFactor` (iterations). Nothing is inferred from a global setting when verifying: the stored values are used.
- Transparent upgrade: after a successful login, if the stored algorithm/version/work factor is below the configured one, the password is re-hashed and stored.
- The baseline (algorithm, version, work factor) is never changed silently: a change is a reviewed change recorded in the Decision Log.

### 6.3 Benchmark on the lowest-spec supported PC
- The Provisioning tool has a `benchmark-hash` command. It measures the time for one PBKDF2 operation at the configured work factor on the machine where it runs, and prints the result.
- Proposed procedure: run it on the lowest-spec PC that must be supported, which you identify (needs the factory inventory). Approved target (Decision Log #42): one hash ≈ 500 ms on that PC. If it is slower, the options are reported to you; the work factor is **never** lowered automatically, and not below 600,000 without your approval.
- Until that PC is identified, 600,000 is a starting value only.

## 7. Authorization

Action codes and a rule set mapping each to allowed department/role, built from Domain spec §15.2 and §15.4, are implemented as code with tests. Anything not mapped is denied. Only the actions that exist in Milestone 1 are wired (administer users, bootstrap, recovery); the last-Administrator protection (§5.7) belongs to the user-management use cases; the other matrix rows are encoded and tested as rules but have no screens. The service is called at the use-case boundary; the UI only mirrors it.

## 8. Packages and tools

**Approval status: conditional (Decision Log #43, #53).** A package batch is added only after its dependency graph has been checked and restore plus vulnerability checks (including transitive dependencies) are recorded in §8.1 and any relevant advisory is addressed. **Batch 1 (the xUnit v3 test packages, rows marked "batch 1")** is in M1.1; its results are in §8.1. All other rows are **not yet approved** and add nothing until their own batch is checked and you accept it.

**Rules:** versions are managed centrally in `Directory.Packages.props` (`ManagePackageVersionsCentrally`, Decision Log #44); related EF Core components use the same version; only stable (non-preview) releases; exact versions pinned; the SDK's NuGet audit is enabled to flag vulnerable packages at restore; no package is added that is not in the approved table.

**Source of the data below:** the NuGet package metadata (api.nuget.org) read on 2026-10-11: latest stable version, the licence expression and the target frameworks declared in each package's `.nuspec`. Vulnerability status of these versions has not been checked yet; the first restore in CI will run NuGet audit and the result will be reported.

| Package | Version | Project(s) | First needed | Purpose and why it is needed | Compatibility (declared target frameworks) | Licence |
|---|---|---|---|---|---|---|
| `Microsoft.EntityFrameworkCore.Relational` | 10.0.12 | Infrastructure | M1.3 | ORM and relational model configuration; the persistence approach in Architecture Specification §4.2 | `net10.0` | MIT |
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.12 | Infrastructure.Sqlite | M1.3 | SQLite provider for the development database | `net10.0` | MIT |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.12 | Infrastructure.Sqlite (`PrivateAssets=all`, build-time only) | M1.3 | Required by the EF command-line tool to create migrations; not shipped | `net10.0` | MIT |
| `dotnet-ef` (local .NET tool, `.config/dotnet-tools.json`) | 10.0.12 | repository tooling | M1.3 | Generates migrations; pinned in a tool manifest so CI and developers use the same version | Tool package (no target framework listed); runs on the .NET 10 runtime | MIT |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | 10.0.12 | Application, Infrastructure | M1.3 | Registration extension methods without depending on a container | `net10.0` (also net8.0, net9.0, netstandard2.0/2.1) | MIT |
| `Microsoft.Extensions.DependencyInjection` | 10.0.12 | Desktop, Provisioning | M1.5 | The container used by the two composition roots | `net10.0` (and earlier) | MIT |
| `Microsoft.Extensions.Options` | 10.0.12 | Application | M1.4 | Typed policy settings (`IOptions<T>`) for authentication defaults | `net10.0` (and earlier) | MIT |
| `Microsoft.Extensions.Options.ConfigurationExtensions` | 10.0.12 | Infrastructure, Desktop, Provisioning | M1.4 | Binds `appsettings.json` sections to the options classes | `net10.0` (and earlier) | MIT |
| `Microsoft.Extensions.Configuration.Json` | 10.0.12 | Desktop, Provisioning | M1.4 | Reads `appsettings.json` / `appsettings.Local.json` | `net10.0` (and earlier) | MIT |
| `Microsoft.Extensions.Logging.Abstractions` | 10.0.12 | Application, Infrastructure | M1.4 | Logging calls in authentication/bootstrap code without choosing a sink (tokens and passwords are never logged) | `net10.0` (and earlier) | MIT |
| `Microsoft.NET.Test.Sdk` | 18.10.1 | all test projects (via `tests/Directory.Build.props`) | **batch 1 (M1.1)** | Test host required by `dotnet test` in the VSTest runner combination of the official xUnit v3 template | `net8.0`, `net462` (the `net8.0` asset is used on `net10.0`) | MIT |
| `xunit.runner.visualstudio` | 4.0.1 | all test projects | **batch 1 (M1.1)** | Lets `dotnet test` and Visual Studio discover and run xUnit tests | `net8.0`, `net472` (the `net8.0` asset is used on `net10.0`) | Apache-2.0 |
| `xunit.v3.mtp-off` | 4.0.2 | all test projects | **batch 1 (M1.1)** | xUnit v3 framework (test attributes, assertions, analyzers) configured for the VSTest runner, as the official template does for `TestRunner=vstest` | `net8.0` and later; test projects are executables (`OutputType=Exe`) | Apache-2.0 |

**Test framework (Decision Log #52):** xUnit v3, set up as in the official `xunit.v3` project template 4.0.2 (read from the `xunit.v3.templates` package on NuGet). For a `net10.0` project the template's default is the Microsoft.Testing.Platform runner (one package, `xunit.v3.mtp-v2`). I used the template's **VSTest** variant (the three packages above) because it keeps the standard `dotnet test` command and `--logger trx` working unchanged; Microsoft's `dotnet test` documentation (MTP mode) shows `--solution` and an additional TRX extension package are needed instead, and says the opt-in must be made in `global.json`, but it did not state the exact JSON. Moving to MTP later is possible as a separate, reviewed change. The three test projects are kept; the settings are shared in `tests/Directory.Build.props`.

**Version alignment:** the EF Core and `Microsoft.Extensions.*` packages share the 10.0.12 servicing line, which matches the current .NET 10 runtime patch (Decision Log #22).

**Deliberately not added** (to keep dependencies minimal): `CommunityToolkit.Mvvm` (a few lines of hand-written `ObservableObject`/`RelayCommand` suffice for one login window; revisit in Milestone 2), assertion libraries, mocking libraries (hand-written fakes), a Jalali library (`PersianCalendar` is used), a logging sink, any hashing package (built into .NET), the Hosting meta-package, a font package.

**Not verified in this session:** transitive dependency licences beyond the packages' own metadata, and vulnerability advisories (see above).

### 8.1 Batch 1 results: dependency graph, restore and vulnerability checks

Source: GitHub Actions run [38175638879](https://github.com/mohamadgazor-sys/ERP-MES/actions/runs/38175638879) on commit `cbb36da` (PR #6, base `main`), Windows runner, SDK 10.0.401. Everything below is copied from that run's log.

- **Restore:** succeeded for all 9 projects.
- **Resolved dependency graph** (`dotnet list package --include-transitive`; identical for the three test projects; the six `src` projects have no packages):

  | Kind | Package | Resolved version |
  |---|---|---|
  | Direct | `Microsoft.NET.Test.Sdk` | 18.10.1 |
  | Direct | `xunit.runner.visualstudio` | 4.0.1 |
  | Direct | `xunit.v3.mtp-off` | 4.0.2 |
  | Transitive | `Microsoft.Bcl.AsyncInterfaces` | 6.0.0 |
  | Transitive | `Microsoft.CodeCoverage` | 18.10.1 |
  | Transitive | `Microsoft.TestPlatform.ObjectModel` | 18.10.1 |
  | Transitive | `Microsoft.TestPlatform.TestHost` | 18.10.1 |
  | Transitive | `Microsoft.Win32.Registry` | 5.0.0 |
  | Transitive | `System.Security.AccessControl` | 6.0.1 |
  | Transitive | `xunit.analyzers` | 2.2.0 |
  | Transitive | `xunit.v3.assert` | 4.0.2 |
  | Transitive | `xunit.v3.common` | 4.0.2 |
  | Transitive | `xunit.v3.core.mtp-off` | 4.0.2 |
  | Transitive | `xunit.v3.extensibility.core` | 4.0.2 |
  | Transitive | `xunit.v3.runner.common` | 4.0.2 |
  | Transitive | `xunit.v3.runner.inproc.console` | 4.0.2 |

  This is 3 direct and 13 transitive packages. My earlier pre-CI approximation listed 21 because it picked older dependency groups; the table above is the resolved graph of record.
- **Vulnerability check** (`dotnet list package --vulnerable --include-transitive`, source `https://api.nuget.org/v3/index.json`): "has no vulnerable packages given the current sources" for all 9 projects, including transitive packages. No advisory needed to be addressed. A pre-CI read of NuGet's public vulnerability feed (updated 2026-10-11) agreed.
- **Build:** Debug and Release, 0 warnings, 0 errors.
- **Tests:** 11 discovered and 11 passed (Domain.Tests 3, Application.Tests 3, Infrastructure.Tests 5); the CI step fails when any project discovers zero tests or any test fails.
- **Licences:** all 16 packages declare an SPDX licence expression (7 MIT, 9 Apache-2.0); none is missing, unclear, copyleft or proprietary. Per-package inventory, flags (including `requireLicenseAcceptance=true` on four Microsoft packages and bundled third-party notices on five) and method are in [`DEPENDENCY_LICENCE_INVENTORY.md`](DEPENDENCY_LICENCE_INVENTORY.md). The packages are used by the test projects only. The inventory is an engineering record, not a legal approval of the package terms.
- **Status:** batch 1 is checked and awaits your acceptance. The advisory feed changes over time, so CI re-runs the check on every build.

## 9. SDK pin (approved, Decision Log #46)

`global.json` pins SDK **10.0.401**, and CI uses the same SDK through `actions/setup-dotnet` with `global-json-file`. A future SDK update is a reviewed change (a pull request that edits `global.json` and states the reason).

```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "disable",
    "allowPrerelease": false
  }
}
```

- Source of the version: Microsoft's .NET 10.0 download page, checked 2026-10-11, shows SDK **10.0.401** with runtime 10.0.12 (security patch, released 2026-09-08) as the latest .NET 10 (LTS) release. I read only the first part of that long page (the latest-release block).
- `rollForward: disable` makes the pin exact: a machine without SDK 10.0.401 cannot build the solution until that SDK (or a reviewed change to `global.json`) is installed. The earlier proposal used `latestPatch`; I implemented the exact pin because the approval says future SDK updates require a reviewed change. If you prefer `latestPatch`, say so; it is a one-word change.
- **The exact SDK 10.0.401 must be installed on every developer machine and in CI** (Decision Log #51). `global.json` disables roll-forward, so a machine with a different .NET 10 SDK, or none, cannot build until 10.0.401 is installed. CI installs it with `actions/setup-dotnet` from `global.json` and fails if `dotnet --version` differs. Changing the SDK is a reviewed update to `global.json` (and the matching note here), never an automatic one.
- No SDK is installed in the Claude environment; build and test results come from GitHub Actions. CI run for commit `7dc9419` confirmed SDK 10.0.401 was selected.

## 10. Persian UI font (approved provisionally, Decision Log #48)

- Primary font **Tahoma**, with the system fallback chain **"Tahoma, Segoe UI, Arial"** (the first available font is used). Tahoma and the fallbacks ship with Windows 10/11 as far as I know; rendering of Persian letters (پ چ ژ گ ک ی), the zero-width non-joiner and digits must be checked on real Windows 10 and 11 machines in the manual checklist (§11) before the choice is final.
- **No font is bundled.** Bundling a font is considered only after licensing terms and deployment needs are established, and would be a separate decision.

## 11. Verification environment (approved, Decision Log #49, #50)

- **Authoritative verification is Windows CI** (GitHub Actions, `windows-latest`): restore, build Release, test, with TRX results uploaded as an artifact when tests exist. This covers the WPF project, which can only be built on Windows. Build and test statements in reports state which environment produced them and link the CI run.
- **Workflow hygiene (approved):**
  - Only three official actions are used, each **pinned to a full commit SHA** (a comment names the release). Resolved on 2026-10-11 with `git ls-remote` against the action repositories (the tags are lightweight, so the SHA is the commit):
    - `actions/checkout` v7.0.1 → `3d3c42e5aac5ba805825da76410c181273ba90b1`
    - `actions/setup-dotnet` v6.0.0 → `a98b56852c35b8e3190ac28c8c2271da59106c68`
    - `actions/upload-artifact` v7.0.2 → `cf430e030ddbb5b0abf93d22962f4752f3646cd9`
  - Least-privilege token: `permissions: contents: read` for the whole workflow; `persist-credentials: false` on checkout.
  - Triggers: `pull_request` and `push` to `main`. Not `pull_request_target`.
  - No secrets, no deployment, no self-hosted runner. A job timeout is set. **Amendment proposed 2026-10-14 (awaiting the owner's approval):** a final step publishes the desktop application self-contained for win-x64 and uploads it as a 14-day artifact for download, so the owner can run each demonstration build. Nothing is deployed; the same pinned `upload-artifact` action is used.
  - Updating a pinned SHA is a reviewed change.
- Optional later: a Linux job for the non-WPF test projects, to keep Domain/Application/Infrastructure portable.
- **Manual UI checklist** (approved) verifies the initial shell until a UI-automation approach is chosen: RTL alignment, Persian typing, tab order, resize, mixed LTR/RTL text, high-DPI, first-run screen states. Automated build and test remain required.

## 12. Work breakdown, acceptance criteria, risks

**Steps** (each a reviewable pull request; build and tests run after each)

| Step | Content | Verified by |
|---|---|---|
| M1.1 | **Authorized.** Solution (`ERP-MES.sln`), six `src` projects and three test projects with the §2 project references, `global.json` (10.0.401, exact pin), `Directory.Build.props`, `Directory.Packages.props` (central management; xUnit v3 batch 1 only), `tests/Directory.Build.props` (shared xUnit v3 setup), `.editorconfig`, CI workflow. `Desktop` and `Provisioning` contain only a placeholder entry point. Each test project has real, discoverable tests that guard the approved dependency direction (11 tests); a run that discovers no tests fails CI. | Local structure checks; CI: pinned SDK, restore, dependency listing, transitive vulnerability gate, Debug/Release build, tests run and counted |
| M1.2A | `JalaliDateService` + golden tests (**in review**; no `Money`, no Rial rules) | Unit tests; Windows CI |
| M1.2B | `Money` formatting and result type (**not started**; Rial rounding and storage stay unresolved) | Unit tests |
| M1.3 | Persistence base, entities, seed, `SystemSetup`, initial SQLite migration, version column, audit log, provider-neutral-subset note | Provider-agnostic integration tests on SQLite (migration from empty, FK enforcement, concurrency conflict, append-only audit) |
| M1.4 | Password hasher, authentication service, lockout, policy options and validation | Unit + integration tests |
| M1.5 | Provisioning tool: `issue-admin-token`, `issue-recovery-token`, `benchmark-hash`; bootstrap and recovery use cases; last-Administrator protection; first-run screen logic | Bootstrap and recovery tests (§5.8), including the parallel-claim test |
| M1.6 | Authorization rule set | One allowed and one denied test per action; unlisted action denied |
| M1.7 | WPF shell: login, first-run screen, RTL, resources, font chain, bidi check | CI build; manual checklist with screenshots |

**Acceptance criteria**
1. A clean checkout builds Debug and Release on the Windows CI agent; the CI log is linked.
2. All automated tests pass in CI.
3. The app starts; the three `SystemSetup` states show the correct screen; valid login works; wrong password, inactive user and locked user are rejected.
4. No default credential, token or real data in the repository; no plaintext password stored; same password for two users stores differently; every stored hash records algorithm, version and work factor.
5. Bootstrap and recovery: only a valid, unexpired, current token works; re-issuing invalidates the previous token; exactly one Administrator results from parallel bootstrap attempts; the bootstrap path is closed afterwards; the last active Administrator cannot be deactivated or demoted; recovery forces a new password and is audited; no backdoor exists.
6. Every authorization matrix row has an allowed and a denied test; unlisted actions are denied.
7. Jalali golden table passes; a Jalali range converts to a half-open Gregorian range; no date comparison by formatted string.
8. Rial values format and parse correctly; no Toman unit exists.
9. The database is created from the SQLite migration; foreign keys enforced; concurrent-update conflict detected.
10. A grid shows `P-565`, `1.10`, `wsj-898` readable inside RTL; screenshot attached.
11. **No** Customer, Request, RequestItem, Excel, MAKE/REPAIR, document or workflow code, table or migration exists.

**Risks:** `PersianCalendar` accuracy (mitigated by golden tests and the interface); bidi rendering of LTR identifiers; lowest-spec PC unknown until the factory inventory (hash time); Windows 10 coverage of .NET 10 is partial (Decision Log #22); provider drift until a second provider exists (SQL Server test pass scheduled before the stage/QC milestone, Decision Log #25); bootstrap residual risk in §5.5.

## 13. Approval status

All of P1–P15 were answered by you on 2026-10-11 and are recorded in the Decision Log.

| # | Decision | Status | Decision Log |
|---|---|---|---|
| P1 | Milestone 1 limited to the technical foundation; Architecture §38 matches | Approved | #36 |
| P2 | Project structure, responsibilities, dependency direction | Approved (documented in §2) | #37 |
| P3 | Separate `Provisioning` project, outside the factory distribution, no embedded privileged credentials | Approved | #38 |
| P4 | Administrator bootstrap with the listed safeguards | Approved | #39 |
| P5 | Password length 12, 5 attempts, 5-minute lockout, configurable, audited | Approved provisionally | #40 |
| P6 | PBKDF2-HMAC-SHA256, 600,000, per-password salt, stored parameters, upgrade on verify | Approved | #41 |
| P7 | Benchmark on lowest-spec supported PC, ≈500 ms, never auto-reduce | Approved | #42 |
| P8 | Package list | **Conditional** — batch 1 (xUnit v3) checked in §8.1; remaining rows await their own batch | #43, #53 |
| P9 | Central package version management | Approved | #44 |
| P10 | `PersianCalendar` behind `JalaliDateService`, with boundary, leap-year and Persian-digit tests | Approved | #45 |
| P11 | SDK 10.0.401 in `global.json`, same SDK in CI | Approved | #46 |
| P12 | Administrator recovery policy | Approved (documented in §5.7) | #47 |
| P13 | Tahoma primary font with system fallback, nothing bundled | Approved provisionally | #48 |
| P14 | Windows CI, SHA-pinned actions, least privilege, no secrets or deployment | Approved | #49 |
| P15 | Manual UI checklist for the initial shell | Approved | #50 |

**Confirmed by you for M1.1:** exact SDK pin with `rollForward: disable` (#51); xUnit v3 (#52); package batches checked before acceptance (#53). **Waiting for you (needed before M1.2):** acceptance of the batch 1 results in §8.1, and of the remaining rows of the §8 table batch by batch as each is needed.

**Still unresolved (not decided here):** production SQL Server version/provider; minimum supported Windows edition/version; Rial rounding rules and storage type; central storage of the authentication policy; every request-status transition question (see `REQUEST_STATUS_TRANSITIONS_DRAFT.md`, deferred until before the request-workflow milestone).
