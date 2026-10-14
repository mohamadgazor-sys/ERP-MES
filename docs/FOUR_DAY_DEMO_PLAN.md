# Four-Day Demonstration Plan

| | |
|---|---|
| **Status** | Owner decisions of 2026-10-14 are applied (section 5, section 6, section 7). Nothing is implemented yet. The decisions are recorded as Decision Log #57; #55 (EF Core/SQLite packages, PR #11) and #56 (migration tooling, a later PR) are allocated to those changes, so numbers are sequential across PRs. |
| **Date** | 2026-10-14 |
| **Goal** | A working, presentable desktop demonstration of the core workflow for a meeting, **not** a production-ready ERP/MES. |
| **Source of truth** | The Master, Architecture and Domain/Database specifications and `DECISION_LOG.md`. This plan adds no business rules. Where a rule is unresolved the operation is either left out or labelled **temporary demonstration assumption** (TDA) on screen and in this document. |
| **Unchanged** | Architecture boundaries (`MILESTONE_1_PLAN.md` section 2), SDK pin 10.0.401, central packages and the package gate (#53), security decisions (#27, #40, #41, #47), Windows CI, small PRs reviewed and merged by the owner. |

## 1. What the demonstration will and will not be

**Will show (fictional data only):**

1. A Persian right-to-left WPF shell with navigation and a dashboard.
2. A customer list and basic customer-request entry, including one request with both MAKE and REPAIR items.
3. A management production queue: sample requests, stage statuses, priority labels (`عادی`, `فوری`, `خیلی فوری`), and estimated processing time where the specification supports it.
4. A limited stage and QC flow: an item moves through stages, and each next stage waits for QC approval of the previous one. The demo shows **QC approval only**; there is no rejection action (section 5, T3).
5. Local SQLite persistence: changes survive moving between screens and restarting the application.
6. A repeatable fictional seed and reset, and a scripted walkthrough for the meeting room.

**Will not be presented as done** (shown on a "Not in this demo" slide or screen, never faked):

- SQL Server deployment or any production deployment.
- The full role/department/operation permission matrix (unresolved, default-deny stays). The demo enforces only the few permissions already decided and is labelled as limited.
- Full Excel import (preview, validation, conflict choice). At most a labelled placeholder is shown.
- Scheduling optimisation, calendar-aware end dates, machine downtime effects, external-supply lead times.
- Contracts and contract PDFs, Drawing review, Warehouse receiving, PDF upload (20 MiB, Decision Log #54), material prices, UOM management.
- Money/Rial calculations (rounding and storage unresolved; the demo shows no monetary values).
- Reports, rankings, bonuses (Phase 2).

## 2. Ground rules specific to the demo

- **No real data.** Customers, requests, items and users are invented. The seed never reads real files. Nothing demo-related is committed that looks like real customer data.
- **No universal password and no hidden bypass.** Login uses the approved PBKDF2-HMAC-SHA256 hashing (#27, #41). The seed creates fictional users with **random, one-time passwords generated locally** on the machine that runs the seed (section 5, T6). The full one-time token bootstrap and recovery policy of Milestone 1 section 5 is **not** part of the demo, is not weakened or replaced, and the demo says so.
- **Dates** are Jalali with Persian digits, **24-hour time**, through `JalaliDateService` only. Stored timestamps UTC, date-only values `DateOnly` (#24).
- **Quantities** use the decimal fixed-scale approach of #33 (D9). No money anywhere.
- **Provider independence.** Business logic stays out of the SQLite project; only provider-specific wiring lives in `Infrastructure.Sqlite`. The demo database is a **demonstration database** at its own path and is not a migration target for production.
- **Demo database safety.** The seed and reset commands act only on a database file that carries a demo marker, so they cannot touch a real database.
- **Every unresolved choice** is handled by leaving it out or marking it TDA (section 5). Nothing is silently decided.

## 3. Dependencies before the demo track starts

| Item | Why | Status |
|---|---|---|
| PDF-size documentation PR (PR #9, Decision Log #54) | Keeps documents consistent | **Merged** |
| EF Core and SQLite packages (PR #11, Decision Log #55) | Needed for the local demo database | **Approved**; code and documentation CI passed; awaiting the owner's review and merge |
| Migration tooling (`Design` 10.0.13, `dotnet-ef` 10.0.13, Decision Log #56) | Needed to create and apply the initial SQLite migration | **Approved** 2026-10-14 (option a); separate PR, starts after PR #11 is merged because it builds on its package versions |
| JalaliDateService follow-up (Q1-Q6) | Updated date-input and time-zone behaviour | Runs in parallel; the demo uses the existing service and does **not** rely on the updated input/time-zone behaviour until the follow-up is merged |

The demo shell, navigation, local persistence setup and fictional seed data start after PR #9 is merged and do not wait for the Q1-Q6 follow-up. They use `JalaliDateService` as it exists on `main` (constructor arguments: Persian digits, Iran Standard Time) and add **no** date conversion or formatting of their own. Overlapping PRs merge one at a time, each branched from the latest `main`.

## 4. Four-day schedule

Each numbered item is its own small PR with Windows CI and meaningful tests, reviewed and merged by the owner in order. If review is slower than the plan assumes, the schedule shrinks to the "must" items in section 4.5.

### Day 1 - foundation you can see

| PR | Content | Tests / evidence |
|---|---|---|
| D1.1 | (PR #12) Persian RTL shell: main window, navigation list, dashboard with placeholder tiles, Tahoma font chain (#48), global RTL flow direction, hand-written MVVM base (no extra MVVM package) | ViewModel unit tests; manual UI checklist (#50) |
| D1.2 | Application and Domain types for Customer, Request, Item (MAKE/REPAIR), Stage, Priority; RequestNo rule (Customer + RequestNo + Jalali year, case-sensitive, no silent trim) | Unit tests for the key rules and boundary cases |
| D1.3a | Migration tooling PR: `Microsoft.EntityFrameworkCore.Design` 10.0.13 (`PrivateAssets="all"`) and `dotnet-ef` 10.0.13 in a committed local tool manifest; licence inventory and vulnerability check updated. Separate from the persistence PR | CI restore of the tool, vulnerability check |
| D1.3b | Persistence PR: demo schema for the subset, repository abstractions in `Infrastructure`, the **initial migration generated and committed** (migration files and model snapshot reviewed in the PR), one migration set per provider. **`EnsureCreated` is not used**, including for the demo database | Fresh database through migrations; startup with an existing database; data persists across a restart; repeatable reset |
| D1.4 | CI additionally publishes the Windows application as a build artifact so the owner can download and run each day's build | CI run shows artifact |

End of day 1: the app opens on the dashboard and stores a customer.

### Day 2 - data entry and login

| PR | Content | Tests |
|---|---|---|
| D2.1 | Sign-in with real password hashing, first-run administrator creation, session; fictional users from the seed | Hashing/verification tests, lock-out defaults of #40 where implemented |
| D2.2 | Customer list and customer entry (name only, #32) | ViewModel and persistence tests |
| D2.3 | Request entry: header, free-form RequestNo, items with WorkType MAKE or REPAIR per item, a request with mixed types | Business-key tests (duplicate rejected, different case accepted) |
| D2.4 | Seed and reset: `ERP.MES.Provisioning` commands to create and reset a fictional dataset in a marked demo database | Tests that reset refuses an unmarked database |

End of day 2: enter a customer and a mixed MAKE/REPAIR request, restart, and see them again.

### Day 3 - queue and production flow

| PR | Content | Tests |
|---|---|---|
| D3.1 | Management production queue: rows from sample requests, stage status, priority label indicators (`عادی`, `فوری`, `خیلی فوری`), estimated processing duration = minutes per piece x planned quantity | Ordering and calculation tests |
| D3.2 | Planning can change priority; Management override preserves Planning's underlying priority and logs who, when, old/new order and reason (decided in the specification) | Audit-record tests |
| D3.3 | Limited stage/QC flow: release first internal stage, **QC approval** of a stage, next stage unlocks only after QC approval of the preceding one (decided rule); history kept. **No rejection action** | State and history tests |
| D3.4 | Dashboard tiles populated from the same data (counts by status, urgent items) | Query tests |

End of day 3: the queue shows live data and an item can be walked through two stages with QC.

### Day 4 - reliability and rehearsal

| PR | Content |
|---|---|
| D4.1 | Bug fixes found in the owner's manual run of the previous days' builds; empty/error states; consistent Persian wording and Jalali dates |
| D4.2 | Walkthrough script (section 8), seed data tuned to it, "Not in this demo" screen, known-limitations text inside the app |
| D4.3 | Final CI-built artifact, a checklist run, and a dry-run record in `SESSION_LOG.md` |

### 4.4 Demo acceptance criteria (owner, 2026-10-14)

The demonstration must support one complete, repeatable walkthrough:

1. Create a customer and a request with mixed MAKE and REPAIR items.
2. See that request in the management production queue.
3. Advance an item through its seeded stages.
4. Record QC approval of a stage (approval only; see T3).
5. Close and reopen the application: the changes are still there (persisted through the EF Core migrations, not `EnsureCreated`).
6. Reset the fictional demo data reliably and run the walkthrough again.

Estimates and demo-only behaviour are labelled on screen. Persistence tests must cover both a fresh database created through migrations and normal startup with an existing database.

### 4.5 Must / should / cut

- **Must:** shell and dashboard, persistence with restart, customer and mixed request entry, queue with priority indicators, one stage-then-QC cycle, seed/reset, walkthrough.
- **Should:** Management override with audit trail, dashboard tiles from data, sign-in with several fictional users.
- **Cut first if time runs out:** override audit screen, dashboard polish. Indicative end dates and QC rejection are **not planned** (T3, T4).

## 5. Demonstration assumptions (owner decisions of 2026-10-14)

Each is labelled in the UI and in this document as demo-only. None replaces a production requirement or is recorded as a production decision.

| # | Area | Decision | What it means in the demo |
|---|---|---|---|
| T1 | Simplified request status | **Approved conditionally** | Only the already-approved statuses of Decision Log #29 are used (`CREATED`, `SENT_TO_MANAGEMENT`, `MGMT_APPROVED_AT_PLANNING`, `MGMT_REJECTED_AT_PLANNING`, `UNDER_DRAWING_REVIEW`, `DRAWING_RETURNED_TO_PLANNING`, `CANCELLED`, `CLOSED`); no new status is added. The seed places sample requests directly in a suitable status and the demo offers only a few simplified transitions, labelled as demo shortcuts. Production state of an item is held on the item and its stages, not as a new request status. The demo **does not claim** the request state machine is implemented; the transition table stays an unapproved draft. |
| T2 | Fixed stage routes | **Approved** | Fictional seeded routes of two or three stages per item. No route editor. |
| T3 | QC rejection | **Rejected as proposed** | The demo shows **QC approval only** and exposes **no rejection action**. Rejection is implemented only if it follows the established accepted / rework / scrap rules (Master Specification and Decision Log #5, #6); because partial-quantity, scrap and rework details are still unresolved, it is out of the four-day scope unless the owner decides otherwise. |
| T4 | Estimated duration | **Approved** | The queue shows a clearly labelled **estimate** (minutes per piece x planned quantity, with the label "تخمین" and a note that it ignores calendar, capacity and downtime). No calculated completion date while scheduling is not implemented. |
| T5 | Permissions | **Approved** | Only already-approved permissions are enforced (Decision Log #34 and Domain and Database Specification section 15.2); everything else is denied by default. |
| T6 | Demo accounts | **Approved with conditions** | Real PBKDF2 hashing and fictional accounts with **random, one-time passwords generated locally**. The credentials are never committed, never hard-coded, never a universal password, and never printed to CI logs: CI does not run the seed command in a mode that outputs them, and tests use in-memory values that are not logged. They are shown once to the person running the seed or written to a credential file **outside the repository**, created with permissions restricted to the current Windows user, and the file name pattern is excluded by `.gitignore` as a second safeguard. A resettable local demo database stands in for the production recovery flow; the approved production authentication and recovery design (#40, #41, #47) is neither weakened nor replaced. |

## 6. Packages and CI

- **Approved by the owner (2026-10-14) for the local demo database:** `Microsoft.EntityFrameworkCore` and `Microsoft.EntityFrameworkCore.Sqlite` (plus the packages they bring in). Use the aligned stable **10.0.13** versions where applicable. This happens in its own package PR, which must: verify restore and compatibility on .NET 10 and SDK 10.0.401; update `docs/DEPENDENCY_LICENCE_INVENTORY.md` for every new direct and transitive package; run the existing direct and transitive vulnerability check; and record the results in `docs/MILESTONE_1_PLAN.md` section 8.1 (#53). If 10.0.13 brings a blocking issue, it is **reported, not silently downgraded**.
- **Not part of this:** the production SQL Server provider and any production database version decision. No MVVM package is planned (hand-written base).
- **CI:** existing job unchanged except D1.4, which uploads the published application as an artifact using the already pinned `upload-artifact` action.
- **Limit of my verification:** I cannot run WPF in my environment. Windows CI proves the application builds and the unit tests pass; it does **not** prove the screens look right. Visual and interaction checks depend on the owner running each day's CI-built artifact and reporting what they see. This is the main schedule risk.

## 7. Decisions recorded

1. Assumptions T1-T6: decided as shown in section 5.
2. Demo sign-in: real hashing and fictional accounts with local, random, one-time passwords (T6); the production bootstrap/recovery design is unchanged.
3. EF Core and SQLite approved for the local demo database (section 6).
4. The demo shell, navigation, persistence setup and seed data start after the PDF-size PR is merged and do not wait for the Q1-Q6 follow-up; the updated date-input and time-zone behaviour is relied on only after that follow-up is merged.

## 8. Meeting-room walkthrough (draft, about 10 minutes)

1. Reset the demo database with the reset command, then start the application (done before the meeting; the reset is repeatable).
2. Sign in as the Central user; show the dashboard.
3. Open Customers, add a fictional customer.
4. Create a request with a free-form RequestNo and two items, one MAKE and one REPAIR.
5. Close and reopen the application; show the data is still there.
6. Switch to the Management queue; point out priority labels and the labelled estimate; change a priority as Planning, then show a Management override and its log entry.
7. Switch to QC: approve the first stage (approval only; there is no rejection action in the demo); show the next stage unlock; show the stage history.
8. Show the "Not in this demo" screen and say plainly what is simulated or missing.

**Fallbacks:** a second pre-seeded database copy; the previous day's artifact kept as a backup build; the walkthrough works offline.

## 9. Risks

| Risk | Mitigation |
|---|---|
| Visual defects I cannot see | Daily CI artifact and the owner's report; Day 4 reserved for fixes |
| Review latency across about 15 small PRs | Must/should/cut list; PRs sized for quick review; merge strictly in order |
| Scope pressure to fake unfinished features | The "Not in this demo" screen and TDA labels; nothing simulated is shown as complete |
| A blocking issue in the 10.0.13 packages | Report it; do not silently downgrade; the schedule slips instead of dropping persistence |
| Q1-Q6 follow-up changes date behaviour mid-demo-build | Dates only through `JalaliDateService`; tests catch any change |
