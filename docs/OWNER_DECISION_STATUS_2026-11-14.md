# Owner decision status after the 2026-11-14 message

Source rows: `docs/OWNER_REVIEW_PACKET_2.md` (exact original questions, quoted), `docs/OWNER_DECISIONS_REQUIRED_2026-11-13.md`, Decision Log #142 (the approvals) and #143 (QC correction implementation).
**Result: none of the thirteen rows is unresolved.** The owner's message of 2026-11-14 answers every one of them (recorded in Decision Log #142), so B9, B2 and A2 each have an explicitly approved decision and implementation can proceed without further input. "Recommended default" below is what Packet 2 proposed; the decision column says what was approved.

| # | Exact original question / default (Packet 2) | Current implementation | Unresolved? | Decision recorded | Recommended default (Packet 2) | Safe to implement without further input? |
|---|---|---|---|---|---|---|
| A1 | "Overtime limits: how many minutes per operator per day and per week?" Default: none configured; overtime cannot be approved until Planning enters them | Overtime screen; demo values 120/600 minutes are illustrative | No | #142: Management enters real limits before go-live; no code change | Planning enters them; no application change | Nothing to implement |
| A2 | "Where does the contract amount live? Contracts store number, date, title, note and PDF but no amount." Default: Management types amount and contract number when approving the price of a contract item | **Not built** (price screen asks Management) | No | #142: contract gets an amount in Rial; Planning enters, Management approves with the contract; price screen proposes it; Management may change the price with a mandatory reason and audit; approved contract amount stays distinct from the later item price decision; no per-item contract price | Add a contract amount Planning enters and Management approves | **Yes** (PR-E, schema change, both providers) |
| A4 | "SQL Server: ... which edition/instance is the production database, its collation, backups and connection-string handling, and when the provider package is approved for production" | Provider, migrations, CI container tests, collation test (PR #86), `docs/SQL_SERVER_PRODUCTION_ARCHITECTURE.md` | Instance/edition is IT's, not an application blocker | #142: architecture approved; dedicated supported SQL Server, never the 2008 R2 system; backups owned by IT; protected per-machine connection strings | Name the instance and edition | Yes, nothing blocked |
| B1 | "Late job reports. Default: no limit; no report is refused as late" | Done in PR #86: warning after 5 working days, never refused | No | #142: N = 5 working days, visible late warning to supervisor and Management, counted on the production calendar, work date separate from submission date | (none proposed) | Done |
| B2 | "Amending an approved report. Default: not built; an approved report is final" | **Built in PR-D** (Decision Log #145; revision rows, reopen permission, stale cost marker) | No | #142: Planning or the Production Manager reopens an approved report with a mandatory reason; new revision; original and full history kept; only the latest valid approved revision feeds KPIs and costing; a changed costing input makes the cost snapshot and price approval stale until recalculated and re-approved; a physically accurate Warehouse receipt is not invalidated; not conflated with QC correction, stale-cost mechanism reused | (none proposed) | Done (PR-D) |
| B6 | "Material price date. Default: the day the item's first stage was released; no scrap factor; no Management adjustment lines" | As default | No | #142: keep | keep | Nothing to implement |
| B8 | "Finished/sent. Default: also requires the Warehouse receipt (your text names only the price approval)" | Both required; dispatch optional | No | #142: both required; customer dispatch optional | keep | Done |
| B9 | "Mistaken receipt, mark or dispatch. Default: cannot be reversed in v1" | **Built in PR-C** (Decision Log #144; `WarehouseReversal`, permissions, screen); the QC correction remains a separate mechanism sharing the marker table | No | #142: Warehouse proposes a reversal with a reason, Management approves; original never deleted, marked reversed only after approval; link to original with proposer, approver, timestamps, reasons; physical movements corrected by compensating transactions; architecture reused for QC correction where appropriate | (none proposed) | Done (PR-C) |
| B10 | "Request status. Default: not closed automatically when all items are finished/sent" | Done in PR #86: manual Management closure with blockers | No | #142: manual closure by Management only; confirmation, audit, read-only afterwards; blocked by unresolved items, external quantities, pending QC decisions, unapproved shortfalls | keep manual | Done |
| B13 | "Planned end forecast. Default: one lane per unit; downtime included; no external lead time yet (now added); regular week not effective-dated; extra-day 'supervisor approval' is a mandatory reason text" | As default (external lead time added by #129) | No | #142: keep v1 rules | keep | Nothing to implement |
| E1 | "Who approves session time. Default: Planning, never the person who dispatched or started the session" | As default | No | #142: keep | keep | Nothing to implement |
| E4 | "Labour cost. Default: no labour-cost rule; labour hours reported separately" | As default | No | #142: keep | keep | Nothing to implement |
| E5 | "Pause reasons: scopes and which are billable. Default: pauses never machine runtime for costing; missing qualification is a warning; off-shift operator needs a reason" | As default | No | #142: keep | keep | Nothing to implement |

## Rows of Packet 2 outside the thirteen you listed (not silently dropped)

| # | State |
|---|---|
| A3 supervisors | Superseded by Decision #122: the Production Manager designates supervisors; operational data to enter, not a rule |
| B3, B4, B14, E2, E3, E6 | Classified in `docs/OWNER_REVIEW_REPORT_2026-11-13.md` as keep / technical / demo-only; no decision requested |
| B5, B11 | Recommended after management feedback (report-line-to-overtime link, metric export/drill-down); not requested |
| B7 | **Superseded** by Q5/B2 (#142): a QC correction now invalidates the cost snapshot and price approval; an amended report will make them stale |
| B12 | QC correction "not built" is now built (PR #86, #143) |

## Implementation order after PR #86
1. B9 Warehouse reversal (PR-C), 2. B2 job-report revision (PR-D), 3. A2 contract amount (PR-E), then the remaining approved but unimplemented items (seeded dispatched-item demo scenario; Warehouse "corrected" reconciliation helpers if needed). No business question is open for any of them.

## Traceability: every "resolved" row to its owner-approved decision

Rule used: a row counts as resolved only when `docs/DECISION_LOG.md` entry **#142** (the owner's message of 2026-11-14) contains an explicit approval for it. A Packet 2 "current default" or a "recommendation" is **not** an approval. Anchors are the bold labels inside entry #142.

| Row | Anchor in Decision Log #142 | Chosen option as recorded | Owner-approved or labelled interpretation? |
|---|---|---|---|
| A1 | **A1:** | option (b): Management enters real limits before go-live; 120/600 illustrative only | Owner-approved |
| A2 | **A2:** | option (b): contract amount in Rial; Planning enters, Management approves; price screen proposes; Management may change price with mandatory reason and audit; distinct from item price decision; no per-item contract price | Owner-approved (implementation details such as field names are mine) |
| A4 | **A4:** | architecture approved; instance/edition IT's, not blocking | Owner-approved |
| B1 | **B1:** | option (b), N = 5 working days, warning, never refuse | Owner-approved |
| B2 | **B2:** | option (b): reopen with reason, new revision, only latest valid approved revision feeds KPIs/costing, stale cost/price, Warehouse receipt not invalidated | Owner-approved |
| B6 | **B6, B13, E1, E4, E5:** | keep v1 rules | Owner-approved ("keep") |
| B8 | **B8:** | option (a): both price approval and Warehouse receipt; dispatch optional | Owner-approved |
| B9 | **B9:** | option (b): Warehouse proposes with reason, Management approves; original never deleted; compensating transaction for physical movements | Owner-approved |
| B10 | **B10:** | option (a): manual closure by Management only | Owner-approved; the blocker "an item whose last stage accepted nothing needs no finished/sent mark" is a **labelled interpretation** of mine |
| B13, E1, E4, E5 | **B6, B13, E1, E4, E5:** | keep | Owner-approved ("keep") |
| QC correction (B7/B12 superseded) | **QC correction Q1-Q8:** | Q1-Q8 as recorded | Owner-approved |
| R1 | **R1:** | completed / total x 100 | Owner-approved; "rounded down, whole number" is my presentation choice |

Items I decided without an owner decision and that are therefore only labelled defaults: the working-day counting detail of B1 (production calendar of the unit), the exact blocker list of B10, the table/field names, and the demo values. They are marked as such in the code comments and Decision Log entries #142/#143 and can be changed by the owner.
