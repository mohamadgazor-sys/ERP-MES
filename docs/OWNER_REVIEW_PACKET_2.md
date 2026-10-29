# Owner Review Packet 2 - defaults chosen during phases 4b to 9 that need your decision

| | |
|---|---|
| **Status** | Open questions. Nothing here is approved. Each row is a default the application currently follows, labelled in the Decision Log entry named in the first column. Where a row is answered, the application changes only in the place named. |
| **How to answer** | Say "keep" or give the replacement for each row (a number, a rule or a name). Answers go into the Decision Log; the application follows in a small pull request. |
| **Earlier packet** | `docs/OWNER_DECISION_PACKET.md` (D1-D8, answered in Decision Log #95). |

## A. Questions that block real use

| # | Entry | Question | Current default | Recommendation |
|---|---|---|---|---|
| A1 | #103 (D5) | Overtime limits: how many minutes per operator per day and per week? | None configured; overtime cannot be approved until Planning enters them | Planning enters them in the overtime screen; no application change |
| A2 | #104 (D4) | Where does the contract amount live? Contracts store number, date, title, note and PDF but no amount | Management types the amount and the contract number when it approves the price of a contract item | Add a contract amount that Planning enters and Management approves with the contract; the price screen then proposes it |
| A3 | #102 (D8) | Who is the supervisor of each production unit? | No unit has one; Planning reviews job reports as a flagged fallback | Planning designates one supervisor per unit in the designation screen |
| A4 | #117 | SQL Server: **technical pass done** (provider, committed migration, integration tests against a throw-away SQL Server 2022 container in CI). **Still yours:** which edition/instance is the production database, its collation, backups and connection-string handling, and when the provider package is approved for production | The test pass is green on `main` (3 tests, 0 skipped); production use needs your approval of the instance | Name the instance and edition; nothing else is blocked |

## B. Business rules I filled with a labelled default

| # | Entry | Topic | Current default |
|---|---|---|---|
| B1 | #102 | Late job reports | No limit; no report is refused as late (you gave no number of working days) |
| B2 | #102 | Amending an approved report | Not built; an approved report is final |
| B3 | #103 | Week for overtime limits | Saturday to Friday |
| B4 | #103 | Overtime proposals | A proposal does not reserve limit; only approved and waiting overtime counts. A supervisor cannot propose overtime for their own operator record. A missing machine qualification is not shown |
| B5 | #103 / #106 | Report lines and overtime | Lines are not linked to an overtime assignment; the overtime share in the unit metrics is an estimate by time overlap |
| B6 | #104 | Material price date | The day the item's first stage was released; no scrap factor; no Management adjustment lines |
| B7 | #104 | A later QC correction or amended report | Does not invalidate an existing cost snapshot; a new calculation is always possible |
| B8 | #105 | Finished/sent | Also requires the Warehouse receipt (your text names only the price approval) |
| B9 | #105 | Mistaken receipt, mark or dispatch | Cannot be reversed in v1 |
| B10 | #105 | Request status | Not closed automatically when all items are finished/sent |
| B11 | #106 | Unit metrics | Rates count every QC decision of the period (re-inspected rework is decided again); a stage's completion is its last QC decision; days are Iran local days; only reports waiting for approval are counted as pending (not drafts); no export or drill-down |
| B12 | #101 | Quantity QC | Submission is always of everything still to be produced or reworked; a stage whose quantity is not a whole number of base units cannot be inspected by quantity; QC correction of an earlier decision is not built |
| B13 | #100 | Planned end forecast | One lane per unit (capacity does not multiply throughput); no downtime or external lead time yet; regular week not effective-dated; "supervisor approval" of an extra working day is a mandatory reason text |
| B14 | #98 | Excel import | At most 20 stages per item; the template shows 6 stage columns |

## C. Things only a person can do

| # | Item |
|---|---|
| C1 | Walk through `docs/DEMO_WALKTHROUGH.md` on Windows in the WPF application with the nine demonstration accounts (the automated end-to-end test performs the same steps at the service level, and the UI smoke test opens every page of the real application on a Windows runner - `docs/UI_SMOKE.md` - but no person has clicked through the business steps yet). Report what looks wrong. |
| C2 | The manual checklist sections O to X in `docs/MANUAL_UI_CHECKLIST.md` (phases 5a to 8) have been covered by automated tests only; the pages themselves are opened by the UI smoke test, buttons are not clicked. |
| C3 | ~~Decide what to do with the six `DEMO-` sample requests~~ **Resolved by Decision Log #118:** the four built on the retired unit-less stages are no longer seeded; two valid pre-approval requests remain. Tell me if you want them back as converted examples. |

## D. State of the roadmap

Phases 1a to 9c, the test-speed work 9a and 9e, the work-session phases 10a to 10g (item-scoped contract decisions, sessions, dispatch, actual-time costing, work board, end-to-end test, SQL Server pass) and the demo-readiness work 11 are merged (pull requests #37 to #59 and later). The SQL Server pass (formerly 9d) is done technically; see A4. The Windows CI job now takes about 17 minutes (it was about 54 minutes before 9a) against a 90-minute limit.

## E. Work sessions: defaults in force that need your decision (`docs/WORK_SESSIONS_DESIGN.md` section 7, Decision Log #112-#116)

| # | Topic | Current default |
|---|---|---|
| E1 | Who approves session time | Planning, and never the person who dispatched or started the session |
| E2 | Legacy `Start` by a plain Production operator for stages tracked by job reports | Allowed until the legacy path is retired |
| E3 | Overlapping shift handover on one machine | Not allowed; exact handover only |
| E4 | Labour cost | No labour-cost rule; labour hours are reported separately |
| E5 | Pause reasons: scopes and which are billable | Pauses are never machine runtime for costing; a missing operator qualification is a warning kept on the session, not a refusal; an operator outside the scheduled shift needs a recorded reason |
| E6 | The demonstration-only supervisor account `demo.supervisor` | A demonstration choice, not a business decision |

## F. Presentation day (offline demo; 2026-10-29)

| # | Item | State |
|---|---|---|
| F1 | **The presentation computer's Windows version.** Windows 11 **22H2 is not on Microsoft's .NET 10 supported list**; 24H2 or 25H2 is. Check with `winver`; if it is 22H2 (or older), update it **before** the day, with internet and time (`docs/OFFLINE_DEMO_GUIDE.md` section 1). | needs a person / IT |
| F2 | The demo passphrase for the nine fictional accounts is a demo-only value in `DEMO-ACCOUNTS.txt` (Decision Log #120, a labelled default). Change it if you prefer another; it protects nothing real. | owner may change |
| F3 | Antivirus / AppLocker / SmartScreen on the presentation computer may block an unsigned application: ask IT to allow the extracted folder, or test it on that computer early. The application and the package are not code-signed. | needs IT |
| F4 | One real offline rehearsal on that computer: Wi-Fi off, `Check-Demo`, `Start-Demo`, the steps you will show, then `Reset-Demo` (guide section 13). | needs a person |
