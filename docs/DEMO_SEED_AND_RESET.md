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

Demonstration-only data is recognisable: customer names end with `(نمونه آزمایشی)`, request numbers start with `DEMO-` and request titles start with `[نمونه] `. **Created-by and approved-by values are real users since the sign-in PR**: the seed takes two user ids, the `demo.central` account (creator of customers, requests and items) and the `demo.qc` account (approver of the stages), which `DemoAccountProvisioner` creates before the seed runs. The earlier placeholder `1` is gone, and creators and approvers are real foreign keys (`docs/AUTHENTICATION.md` section 9). A demonstration database from before sign-in existed cannot be upgraded (its records name a user that never existed): `demo-init` refuses it and `demo-reset <file> --yes` replaces it.

| Request (customer) | Jalali date | Status | Items (type, priority) and route state |
|---|---|---|---|
| DEMO-1405-001 (A) | 1405/07/05 | `MGMT_APPROVED_AT_PLANNING` | 1.10 MAKE very urgent: 3 stages, stage 1 QC-approved, stage 2 waiting for QC, stage 3 waiting for previous QC. 1.20 REPAIR urgent: 2 stages, stage 1 in progress |
| DEMO-1405-002 (A) | 1405/07/12 | `MGMT_APPROVED_AT_PLANNING` | 2.10 MAKE normal: 2 stages, stage 1 waiting for QC. 2.20 REPAIR normal: 2 stages, stage 1 released (waiting) |
| DEMO-1405-014 (B) | 1405/07/15 | `SENT_TO_MANAGEMENT` | 3.10 MAKE very urgent (3 stages), 3.20 REPAIR normal (2 stages); nothing released |
| DEMO-1405-015 (B) | 1405/07/18 | `CREATED` | 4.10 MAKE urgent, 4.20 REPAIR normal (2 stages each); nothing released |
| DEMO-1405-003 (C) | 1405/06/28 | `MGMT_APPROVED_AT_PLANNING` | 5.10 MAKE normal: all 3 stages approved (route completed). 5.20 REPAIR urgent: stage 1 approved, stage 2 released |
| DEMO-1404-087 (C) | 1404/12/20 | `CLOSED` | 6.10 MAKE, 6.20 REPAIR (normal): both routes completed; a request of the previous Jalali year |

Totals: 3 customers, 6 requests, 12 items (6 MAKE, 6 REPAIR), 27 stages, 9 QC approvals. Stage states: 9 QC approved, 2 waiting for QC, 1 in progress, 2 waiting, 4 planned, 9 waiting for previous QC.

Honest limits of the sample, all inherited from the Domain model (Decision Log #57, #58):

- **Request status is placed directly** by `Request.PlaceInStatusForDemonstration` (Decision Log #60), which checks no transition and does not approve the transition table (T1). It is **`internal`** to the Domain assembly, visible only to `ERP.MES.Infrastructure` (where `DemoDataSeeder` lives) and the Domain tests via `InternalsVisibleTo`; there is no public status setter. `InternalsVisibleTo` is assembly-wide, so a test also proves that Infrastructure uses no other internal Domain method, constructor or property setter. Tests prove the method is not public, the Domain shares internals only with those two assemblies, and the seed is the single production call site.
- Stage progress uses only the Domain's demonstration transitions (release, start, submit for QC, whole-quantity QC approval), so the QC gate runs while the data is built. There are no rejected, rework, scrap, partial or external-supply stages (G10); those are not modelled yet.
- Routes are fixed seeded routes of two or three stages (T2). The estimated minutes per piece are invented.
- No machine rates, queue positions or Management overrides exist yet.

## 3a. Demonstration accounts and credentials

A new or reset demonstration database also gets eight fictional accounts (including `demo.production` for the stage walkthrough), created through the production bootstrap and account use cases, each with a random one-time password written only to a protected credential file outside the repository (`--credentials <file>`, or a default under the user's local application data). `demo-init` on an initialised database creates none; `demo-reset` creates new accounts with new passwords. The commands never print a password. Details, the file protection and the checks: `docs/AUTHENTICATION.md` section 9.

## 4. Concurrency (M8) - resolved by Decision Log #77

This PR changed **no persistence contract**. At that time a request-level `expectedVersion` did not detect a competing change to an item's priority or a stage's status. M8 was resolved afterwards by Decision Log #77: queue and QC actions must use `UpdateRowsAsync` with the expected versions of the rows they intend to change (`PERSISTENCE_MAPPING.md` section 5). Concurrency is per row, not aggregate-level. Seed and reset do not depend on this.

## 5. Verification

Tests (`tests/ERP.MES.Infrastructure.Tests/DemoDatabaseTests.cs`, `DemoGuardTests.cs`; `RequestTests` in the Domain tests) run on real SQLite files and cover: creation with marker and migrations; zero-length file; repeated initialise and repeated seeding adding nothing; reset refusing an unmarked database, a non-database file, a folder and a missing path with the file bytes unchanged; initialise refusing an unmarked database without marking it; reset removing extra data and reproducing a row-identical snapshot of every table, also twice in a row; expected counts, mixed MAKE/REPAIR, statuses, priorities, stage states, the QC gate and approval consistency in the stored data; Persian text, Jalali year and UTC kinds after reopening with new repositories; no temporary files left; source guards (entry points only in Provisioning, the Desktop project does not seed or reset, the internal Domain method with exactly one call site in the seed and no other internal Domain member used by Infrastructure, no credential code).

The CI workflow additionally runs the real `Provisioning` commands on a temporary file: init, a repeated init, status, reset without `--yes` (exit 2), reset with `--yes`, and reset on an unrecognised file (exit 1, file unchanged). Nothing in it is a credential.
