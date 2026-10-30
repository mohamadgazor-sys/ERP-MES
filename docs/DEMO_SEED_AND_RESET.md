# Fictional Demonstration Data, Safe Reset and Repeatable Seed

| | |
|---|---|
| **Status** | Implemented; the marker (Decision Log #59) and the restricted status placement (#60) are owner-approved. |
| **Scope** | A fictional dataset for the demonstration walkthrough, an explicit reset, and the guard that keeps both away from any database that is not a marked demonstration database. |
| **Basis** | `docs/FOUR_DAY_DEMO_PLAN.md` (D1.3b, D2.4, T1, T2, T6), Decision Log #55-#58, `docs/PERSISTENCE_MAPPING.md`, `docs/DOMAIN_MODEL_DEMO_SCOPE.md`. |
| **Not included** | Screens, a SQL Server provider, any new production rule. (Sign-in and the demonstration accounts were added by the sign-in PR; see `docs/AUTHENTICATION.md`.) |

## 1. Pieces

| Piece | Project | Role |
|---|---|---|
| `DemoDataSeeder` | `ERP.MES.Infrastructure/Demo` (provider-neutral) | Builds the fixed fictional dataset through `ICustomerRepository`, `IRequestRepository` and the Domain methods. Repeatable: a customer is found by its exact name, a request by its business key; what exists is left unchanged. |
| `DemoDatabase` | `ERP.MES.Infrastructure.Sqlite/Demo` | The only entry point that creates, seeds or resets a demonstration database file: `Inspect`, `InitializeAsync`, `ResetAsync`. Holds the marker check. |
| Commands | `ERP.MES.Provisioning` | `demo-status`, `demo-init`, `demo-reset ... --yes`. Never run at application start-up; the Desktop project does not reference them (a test checks it). |

Usage (from the repository root, with the pinned SDK):

```
dotnet run --project src/ERP.MES.Provisioning -- demo-status <file>
dotnet run --project src/ERP.MES.Provisioning -- demo-init   <file>
dotnet run --project src/ERP.MES.Provisioning -- demo-reset  <file> --yes
```

Exit codes: 0 done, 1 refused (the reason is printed; nothing changed), 2 usage error (including `demo-reset` without `--yes`). The schema always comes from the committed EF Core migrations; `EnsureCreated` is not used.

## 2. How a demonstration database is positively identified

A database is a demonstration database only if the SQLite file header field `application_id` equals `0x45524D44` (ASCII "ERMD"). Decisions in this design:

- **The marker is written only by `InitializeAsync`, and only on a database it creates itself** (no file, or a zero-length file). No existing database can be turned into a demonstration database by this code.
- **Inspection is read-only** and never creates a file.
- **Initialise** accepts: missing path, zero-length file, or a marked database (then it applies pending migrations and adds missing sample data). **Reset** accepts only a marked database. Everything else is refused with the reason and nothing is changed: an unmarked SQLite database ("may be a real database"), a file SQLite cannot open, a folder, and (for reset) a missing path - reset never creates a database.
- **Reset builds the replacement first.** It migrates, marks and seeds a temporary file next to the target and moves it over the old file only when that succeeded; a failure leaves the previous database as it was. Journal files of the replaced demonstration database are removed so they cannot be applied to the new file.
- **Reset is explicit.** There is no start-up hook; the command needs `--yes`.

**Approved design (Decision Log #59).** **Why a header field and not a marker table.** A marker table would need a new migration, and the accepted initial migration is not to be touched without reporting an issue. The header field leaves the schema and the migration unchanged and cannot be mistaken for business data. The cost: the marker is SQLite-specific, which is acceptable because the demonstration database and the reset are SQLite-only (Decision Log #55, #57) and the code lives in the provider project. No marker table and no migration are added for this purpose.

## 3. The sample data

Fixed (no clock, no randomness, no machine setting), so a reset followed by a seed always yields the same rows and ids. Dates are given as Jalali dates and converted with `JalaliDateService`; timestamps are 05:00 UTC on the Jalali day.

Demonstration-only data is recognisable: customer names end with `(نمونه آزمایشی)`, request numbers start with `DEMO-` and request titles start with `[نمونه] `. **Created-by and approved-by values are real users since the sign-in PR**: the seed takes two user ids, the `demo.secretary` account (creator of customers, requests and items) and the `demo.qc` account (approver of the stages), which `DemoAccountProvisioner` creates before the seed runs. The earlier placeholder `1` is gone, and creators and approvers are real foreign keys (`docs/AUTHENTICATION.md` section 9). A demonstration database from before sign-in existed cannot be upgraded (its records name a user that never existed): `demo-init` refuses it and `demo-reset <file> --yes` replaces it.

| Request (customer) | Jalali date | Status | Items (type, priority) |
|---|---|---|---|
| DEMO-1405-014 (B) | 1405/07/15 | `SENT_TO_MANAGEMENT` | **MAKE request**, with a fictional PDF. 3.10 very urgent (quantity 100), 3.20 normal (quantity 3). No stages. |
| DEMO-1405-015 (B) | 1405/07/18 | `CREATED` | **REPAIR request**, no PDF (shows the rule "attach the PDF before submitting", Decision Log #87). 4.10 urgent (200), 4.20 normal (1). No stages. |

**Changed 2026-10-29 (demo-readiness, Decision Log #118).** The earlier dataset had six requests (12 items, 27 stages, 9 QC approvals) whose stage progress was built with the unit-less demonstration transitions. That flow was retired (Decision Log #98, #116): production stages now belong to a production unit, are released per unit after the approval, contract and Drawing gates, and are executed through work sessions. Sample stages without a unit, queue position or session cannot be shown in the unit queues or the work board and cannot be continued by the current workflow, so the four requests that depended on them (`DEMO-1405-001`, `-002`, `-003`, `DEMO-1404-087`) are no longer seeded. The two requests that remain are valid examples under the current workflow: they precede Management's decision, so they carry items but no stages. Customers (3, one per fictional company, one of them unused by a request) and the master data of section 4 are unchanged. Totals: 3 customers, 2 requests (1 PDF), 4 items, 0 stages, 0 QC approvals. Everything after the approval (contract decision, Drawing, item definition with stages and units, release, sessions, QC, costing, dispatch) is performed by `docs/DEMO_WALKTHROUGH.md` through the real services, and `EndToEndDemoTests` runs that path on a fresh seeded database.

Honest limits of the sample, all inherited from the Domain model (Decision Log #57, #58):

- **Request status is placed directly** by `Request.PlaceInStatusForDemonstration` (Decision Log #60), which checks no transition and does not approve the transition table (T1). It is **`internal`** to the Domain assembly, visible only to `ERP.MES.Infrastructure` (where `DemoDataSeeder` lives) and the Domain tests via `InternalsVisibleTo`; there is no public status setter. `InternalsVisibleTo` is assembly-wide, so a test also proves that Infrastructure uses no other internal Domain method, constructor or property setter. Tests prove the method is not public, the Domain shares internals only with those two assemblies, and the seed is the single production call site.
- No stage progress is seeded any more (see the change note above).

## 3a. Demonstration accounts and credentials

A new or reset demonstration database also gets ten fictional accounts (including `demo.production` for the operator and `demo.supervisor` for the unit supervisor in the work-session walkthrough), created through the production bootstrap and account use cases, each with a random one-time password written only to a protected credential file outside the repository (`--credentials <file>`, or a default under the user's local application data). `demo-init` on an initialised database creates none; `demo-reset` creates new accounts with new passwords. The commands never print a password. Details, the file protection and the checks: `docs/AUTHENTICATION.md` section 9.

## 4. Concurrency (M8) - resolved by Decision Log #77

This PR changed **no persistence contract**. At that time a request-level `expectedVersion` did not detect a competing change to an item's priority or a stage's status. M8 was resolved afterwards by Decision Log #77: queue and QC actions must use `UpdateRowsAsync` with the expected versions of the rows they intend to change (`PERSISTENCE_MAPPING.md` section 5). Concurrency is per row, not aggregate-level. Seed and reset do not depend on this.

## 5. Verification

Tests (`tests/ERP.MES.Infrastructure.Tests/DemoDatabaseTests.cs`, `DemoGuardTests.cs`; `RequestTests` in the Domain tests) run on real SQLite files and cover: creation with marker and migrations; zero-length file; repeated initialise and repeated seeding adding nothing; reset refusing an unmarked database, a non-database file, a folder and a missing path with the file bytes unchanged; initialise refusing an unmarked database without marking it; reset removing extra data and reproducing a row-identical snapshot of every table, also twice in a row; expected counts, mixed MAKE/REPAIR, statuses, priorities, stage states, the QC gate and approval consistency in the stored data; Persian text, Jalali year and UTC kinds after reopening with new repositories; no temporary files left; source guards (entry points only in Provisioning, the Desktop project does not seed or reset, the internal Domain method with exactly one call site in the seed and no other internal Domain member used by Infrastructure, no credential code).

The CI workflow additionally runs the real `Provisioning` commands on a temporary file: init, a repeated init, status, reset without `--yes` (exit 2), reset with `--yes`, and reset on an unrecognised file (exit 1, file unchanged). Nothing in it is a credential.

## 4. Master data for the end-to-end walkthrough (Decision Log #108)

`DemoMasterDataSeeder` (Infrastructure/Demo) adds, after the sample requests, the fictional master data that `docs/DEMO_WALKTHROUGH.md` needs: units `MILLING` and `TURNING`, machines `FCC-06` and `TCC-12` with rates, the day shift `D` for both units, the material `ST37-R30` with a price, the operator `OP-DEMO` linked to `demo.production`, and that account's designation to MILLING. It is written through the repositories and the Domain with real users (`demo.planning`, `demo.warehouse`) and one audit entry per row, like the sample requests; it does NOT go through the application services because those refuse a demonstration account until it has replaced its one-time password, and the seed does not go around that rule. The time is fixed (1405/08/01 05:00 UTC), it is repeatable (an existing row is left unchanged) and a reset gives exactly the same rows. Owner decisions are left empty on purpose: no overtime limits, no unit supervisor, no worker schedule.


## 5. Work-session additions (Decision Log #112-#115)

The master-data seed also assigns `OP-DEMO` to `FCC-06`, records its qualification on it, designates the new `demo.supervisor` account to MILLING and marks it supervisor (all with audit entries under `demo.planning`). The supervisor is a demonstration choice, not an owner decision. Overtime limits and the worker schedule stay empty. The work-session tables themselves start empty; the seven pause reasons come from the migration.

## 6. Presentation mode and the offline package (2026-10-29, Decision Log #120)

`demo-init` and `demo-reset` accept `--presentation`. After the ten accounts have been created with random one-time passwords (unchanged behaviour), each account signs in with its one-time password and replaces it, **through the real sign-in and change-password use cases**, with a demo passphrase read from the environment variable `ERPMES_DEMO_PASSPHRASE`; the one-time credential file is then deleted. Nothing is hard-coded in the seed or the commands; without the variable the option is refused and nothing is changed. If a step fails, the accounts keep their one-time passwords and the credential file is left in place.

The offline package (`tools/offline-demo`, `docs/OFFLINE_DEMO_GUIDE.md`) uses this: `Start-Demo` creates `data\erpmes.demo.db` on first run, `Reset-Demo` replaces it. The passphrase is the fictional, demo-only value in the package's `DEMO-ACCOUNTS.txt` (it exists in the repository as `tools/offline-demo/package/DEMO-ACCOUNTS.txt` because it protects nothing real: the database holds only fictional data and exists only inside the package folder). A reset gives the same rows as before (the earlier determinism tests) with new password hashes. The provisioning tool shipped inside the package is the same binary as for developers; the launcher only ever calls `demo-status`, `demo-init` and `demo-reset` (Decision Log #38/#47 say the provisioning tool is not part of the factory-PC distribution; the offline package is a presentation package, not the factory distribution).
