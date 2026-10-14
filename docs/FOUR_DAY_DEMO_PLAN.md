# Four-Day Demonstration Plan

| | |
|---|---|
| **Status** | Proposal for owner review. Nothing here is implemented or approved until the owner says so. |
| **Date** | 2026-10-14 |
| **Goal** | A working, presentable desktop demonstration of the core workflow for a meeting, **not** a production-ready ERP/MES. |
| **Source of truth** | The Master, Architecture and Domain/Database specifications and `DECISION_LOG.md`. This plan adds no business rules. Where a rule is unresolved the operation is either left out or labelled **temporary demonstration assumption** (TDA) on screen and in this document. |
| **Unchanged** | Architecture boundaries (`MILESTONE_1_PLAN.md` section 2), SDK pin 10.0.401, central packages and the package gate (#53), security decisions (#27, #40, #41, #47), Windows CI, small PRs reviewed and merged by the owner. |

## 1. What the demonstration will and will not be

**Will show (fictional data only):**

1. A Persian right-to-left WPF shell with navigation and a dashboard.
2. A customer list and basic customer-request entry, including one request with both MAKE and REPAIR items.
3. A management production queue: sample requests, stage statuses, priority labels (`عادی`, `فوری`, `خیلی فوری`), and estimated processing time where the specification supports it.
4. A limited stage and QC flow: an item moves through stages, and each next stage waits for QC approval of the previous one.
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
- **No universal password and no hidden bypass.** Login uses the approved PBKDF2-HMAC-SHA256 hashing (#27, #41). The first administrator is created through the first-run flow; the seed creates fictional users with **randomly generated passwords shown once to the person running the seed** and never stored in Git. The full one-time token bootstrap and recovery policy of Milestone 1 section 5 is **not** complete in the demo and the demo says so. (Decision requested in section 7, item 2.)
- **Dates** are Jalali with Persian digits, **24-hour time**, through `JalaliDateService` only. Stored timestamps UTC, date-only values `DateOnly` (#24).
- **Quantities** use the decimal fixed-scale approach of #33 (D9). No money anywhere.
- **Provider independence.** Business logic stays out of the SQLite project; only provider-specific wiring lives in `Infrastructure.Sqlite`. The demo database is a **demonstration database** at its own path and is not a migration target for production.
- **Demo database safety.** The seed and reset commands act only on a database file that carries a demo marker, so they cannot touch a real database.
- **Every unresolved choice** is handled by leaving it out or marking it TDA (section 5). Nothing is silently decided.

## 3. Dependencies before the demo track starts

| Item | Why | Status |
|---|---|---|
| PDF-size documentation PR (#54) | Keeps documents consistent | Open, awaiting review |
| JalaliDateService follow-up (Q1-Q6) | Persian-digit, Iran-time display used on every screen | Not started; starts after the PDF PR is merged, per instructions |
| Package approvals (section 6) | EF Core and SQLite are not yet approved packages | To be approved by the owner before Day 1 coding that uses them |

The first shell work (Day 1 morning) can proceed without the date follow-up by using the service already on `main` with explicit constructor arguments (Persian digits, Iran Standard Time); the follow-up then lands its behaviour changes underneath.

## 4. Four-day schedule

Each numbered item is its own small PR with Windows CI and meaningful tests, reviewed and merged by the owner in order. If review is slower than the plan assumes, the schedule shrinks to the "must" items in section 4.5.

### Day 1 - foundation you can see

| PR | Content | Tests / evidence |
|---|---|---|
| D1.1 | Persian RTL shell: main window, navigation list, dashboard with placeholder tiles, Tahoma font chain (#48), global RTL flow direction, hand-written MVVM base (no extra MVVM package) | ViewModel unit tests; manual UI checklist (#50) |
| D1.2 | Application and Domain types for Customer, Request, Item (MAKE/REPAIR), Stage, Priority; RequestNo rule (Customer + RequestNo + Jalali year, case-sensitive, no silent trim) | Unit tests for the key rules and boundary cases |
| D1.3 | SQLite persistence in `Infrastructure.Sqlite` (EF Core, after package approval), schema for the demo subset, repository abstractions in `Infrastructure`, one migration set per provider (approved convention) | Round-trip and uniqueness tests against SQLite |
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
| D3.3 | Limited stage/QC flow: release first internal stage, QC approve/reject on a stage, next stage unlocks only after QC approval of the preceding one (decided rule); history kept | State and history tests |
| D3.4 | Dashboard tiles populated from the same data (counts by status, urgent items) | Query tests |

End of day 3: the queue shows live data and an item can be walked through two stages with QC.

### Day 4 - reliability and rehearsal

| PR | Content |
|---|---|
| D4.1 | Bug fixes found in the owner's manual run of the previous days' builds; empty/error states; consistent Persian wording and Jalali dates |
| D4.2 | Walkthrough script (section 8), seed data tuned to it, "Not in this demo" screen, known-limitations text inside the app |
| D4.3 | Final CI-built artifact, a checklist run, and a dry-run record in `SESSION_LOG.md` |

### 4.5 Must / should / cut

- **Must:** shell and dashboard, persistence with restart, customer and mixed request entry, queue with priority indicators, one stage-then-QC cycle, seed/reset, walkthrough.
- **Should:** Management override with audit trail, dashboard tiles from data, sign-in with several fictional users.
- **Cut first if time runs out:** indicative end dates, override audit screen, QC rejection/rework branches, dashboard polish.

## 5. Temporary demonstration assumptions (TDA)

These are used only if the owner accepts them for the demo. Each is labelled in the UI. None is a production decision and none is recorded in the Decision Log as approved.

| # | Area | Assumption | Why it is needed | Real status |
|---|---|---|---|---|
| T1 | Request status | The demo moves a sample request from creation straight to "ready for production" with a simple status field, **without** the Management/Planning/Drawing transitions | The transition table is a draft awaiting approval (`REQUEST_STATUS_TRANSITIONS_DRAFT.md`) | Unresolved |
| T2 | Stages | Each demo item has a short fixed list of 2-3 internal stages chosen in the seed | Routing/stage-revision semantics are unresolved | Unresolved |
| T3 | QC rejection | A rejected stage returns to its own stage in the demo; no scrap/rework quantities | Partial quantity, scrap and rework detail are unresolved | Unresolved |
| T4 | Estimated end | Shown as an estimated duration (minutes per piece x quantity), not a calendar date | The calendar, shifts, capacity and downtime are configurable and unresolved | Duration formula is decided; calendar-aware end is not implemented |
| T5 | Permissions | Only a few fictional users, with the decided permissions (for example QC alone approves QC, Planning and Management edit priorities); everything else default-deny | The full matrix is unresolved | Partly decided |
| T6 | Sign-in | Fictional users with random one-time passwords; no recovery flow | Milestone 1 bootstrap/recovery are not built | Approved design, not implemented |

## 6. Packages and CI

- **Likely new packages (need owner approval and the #53 gate, including a refreshed licence inventory and vulnerability check):** Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Sqlite (versions to be verified current and supported on .NET 10 at the time), and, only if the owner prefers it, an MVVM helper. The plan assumes **no MVVM package** (hand-written base) to keep the dependency list short.
- **CI:** existing job unchanged except D1.4, which uploads the published application as an artifact using the already pinned `upload-artifact` action.
- **Limit of my verification:** I cannot run WPF in my environment. Windows CI proves the application builds and the unit tests pass; it does **not** prove the screens look right. Visual and interaction checks therefore depend on the owner running each day's CI-built artifact and reporting what they see. This is the main schedule risk.

## 7. Decisions requested from the owner

1. Approve the section 5 temporary assumptions (or name any to drop).
2. Sign-in approach for the demo (section 2): real hashing and fictional users with random one-time passwords, with the full bootstrap flow explicitly not part of the demo. Alternative: build the bootstrap now, which costs about half a day.
3. Package approval for EF Core and SQLite, as described in section 6.
4. Whether the Q1-Q6 follow-up must be merged before Day 1 starts, or may land in parallel.

## 8. Meeting-room walkthrough (draft, about 10 minutes)

1. Reset the demo database with the reset command, then start the application (done before the meeting; the reset is repeatable).
2. Sign in as the Central user; show the dashboard.
3. Open Customers, add a fictional customer.
4. Create a request with a free-form RequestNo and two items, one MAKE and one REPAIR.
5. Close and reopen the application; show the data is still there.
6. Switch to the Management queue; point out priority labels and estimated duration; change a priority as Planning, then show a Management override and its log entry.
7. Switch to QC: approve the first stage; show the next stage unlock; show the stage history.
8. Show the "Not in this demo" screen and say plainly what is simulated or missing.

**Fallbacks:** a second pre-seeded database copy; the previous day's artifact kept as a backup build; the walkthrough works offline.

## 9. Risks

| Risk | Mitigation |
|---|---|
| Visual defects I cannot see | Daily CI artifact and the owner's report; Day 4 reserved for fixes |
| Review latency across about 15 small PRs | Must/should/cut list; PRs sized for quick review; merge strictly in order |
| Scope pressure to fake unfinished features | The "Not in this demo" screen and TDA labels; nothing simulated is shown as complete |
| Package approval delay blocks persistence | Ask for approval on Day 0; fall back to in-memory only is **not** acceptable because persistence is a stated goal, so the schedule slips instead |
| Q1-Q6 follow-up changes date behaviour mid-demo-build | Dates only through `JalaliDateService`; tests catch any change |
