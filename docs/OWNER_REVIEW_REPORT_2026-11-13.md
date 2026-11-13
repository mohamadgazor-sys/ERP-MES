# Consolidated owner-review report (2026-11-13)

Source: the actual text of `docs/OWNER_REVIEW_PACKET_2.md` sections A, B and E at main `965987a`, checked against the Decision Log (#1-#135) and the owner messages of 2026-11-05 and 2026-11-13. Nothing below is implemented as an approved rule: where a row says "current implementation", that is a labelled default that stays in force until you answer. Answers go into the Decision Log; the application changes only where stated.

Already answered (not repeated below): B15 downtime (#130); overtime inside regular hours (#131); external supply decisions (#129); contractor-lost pieces (#136, 2026-11-13).

## 1. Essential business decisions (only you can answer)

| Field | A1 - Overtime limits | A2 - Contract amount |
|---|---|---|
| Section and row | A, A1 (Decision Log #103, D5) | A, A2 (#104, D4) |
| Original question | "Overtime limits: how many minutes per operator per day and per week?" | "Where does the contract amount live? Contracts store number, date, title, note and PDF but no amount" |
| Current implementation | Management (since #122) configures a daily and a weekly limit in minutes; none is configured, so no overtime can be approved. | Management types the amount and the contract number when it approves the price of a contract item. |
| Business context | Within the limits Management or the Production Manager approves alone; beyond them only Management. Without numbers the approval rule cannot work. | A contract item's price must trace back to the contract the customer signed. |
| Available choices | Give the two numbers; or leave to Management to enter them in the overtime screen. | (a) keep as is; (b) add a contract amount that Planning enters and Management approves together with the contract, and the price screen proposes it. |
| Recommended default | Management enters them in the screen before go-live (e.g. you tell us, we configure the demo with example values only). | (b): fewer typing errors, one source of truth. |
| Consequences | A number change needs no code. | (b) adds a field and a small screen change; (a) none. |
| Implementation blocker | Blocks real use of overtime approval, not development; the demo uses example limits. | Blocks nothing now. |

| Field | A4 - Production SQL Server | B1 - Late job reports | B2 - Amending an approved report |
|---|---|---|---|
| Section and row | A, A4 (#117) | B, B1 (#102) | B, B2 (#102) |
| Original question | "Which edition/instance is the production database, its collation, backups and connection-string handling, and when the provider package is approved for production" | "Late job reports: no limit; no report is refused as late (you gave no number of working days)" | "Amending an approved report: not built; an approved report is final" |
| Current implementation | SQLite for demo; SQL Server provider and migrations are tested against a throw-away SQL Server 2022 container in CI. | Any report is accepted whenever it is entered. | An approved report can never be changed. |
| Business context | Real deployment and backups. | Late entry affects KPIs and costing. | Operators make mistakes after approval. |
| Available choices | Name edition/instance, collation, backup owner. | A number of working days after which a report is refused or flagged. | Keep final; or allow Planning/Production Manager to reopen with a reason and re-approval. |
| Recommended default | Name the instance when IT decides. | Flag (not refuse) reports later than N working days, N chosen by you. | Reopen with reason and audit, because refusing all corrections invites off-system fixes. |
| Consequences | Deployment plan. | Reports/KPIs. | New workflow state, cost snapshots interplay (B7). |
| Implementation blocker | Blocks production go-live only. | No. | No. |

| Field | B6 - Material price date and cost lines | B8 - Finished/sent | B9 - Reversing a mistaken receipt/mark/dispatch | B10 - Closing the request |
|---|---|---|---|---|
| Section and row | B, B6 (#104) | B, B8 (#105) | B, B9 (#105) | B, B10 (#105) |
| Original question | "Material price date: the day the item's first stage was released; no scrap factor; no Management adjustment lines" | "Finished/sent: also requires the Warehouse receipt (your text names only the price approval)" | "Mistaken receipt, mark or dispatch: cannot be reversed in v1" | "Request status: not closed automatically when all items are finished/sent" |
| Current implementation | As quoted. | Both the price approval and the Warehouse receipt are required. | No reversal. | Closing is manual/not automatic. |
| Business context | Cost accuracy and price disputes. | Which events make an item "finished/sent". | Warehouse typing errors. | Whether the request becomes CLOSED by itself. |
| Available choices | Accept; or add scrap factor / adjustment lines. | Keep both; or price approval alone. | Keep; or Warehouse/Management reversal with reason. | Auto-close when every item is finished/sent and dispatched; or manual. |
| Recommended default | Accept for v1. | Keep both (physical receipt is real evidence). | Allow Management-approved reversal with reason (audit kept). | Manual close by Management after review. |
| Consequences | Costing formulas. | Workflow gate. | Audit-heavy new action. | Status transitions. |
| Implementation blocker | No. | No. | No. | No. |

| Field | B13 - Parallel machine capacity | E1 - Approver of session time | E4 - Labour cost | E5 - Pause reasons and billing |
|---|---|---|---|---|
| Section and row | B, B13 (#100); Master Spec §13.3 | E, E1 (#112-#116) | E, E4 | E, E5 |
| Original question | "Planned end forecast: one lane per unit (capacity does not multiply throughput) ... regular week not effective-dated" | "Who approves session time: Planning, and never the person who dispatched or started the session" | "Labour cost: no labour-cost rule; labour hours are reported separately" | "Pause reasons: scopes and which are billable" |
| Current implementation | One lane per unit; machines are not parallel capacity. | Planning **and the Production Manager** (since #122) approve; never the dispatcher/starter. | Machine-rate cost only. | Pauses are never machine runtime for costing; missing qualification = warning; outside-shift work needs a reason. |
| Business context | Planned end dates for units with several machines. | Segregation of duties. | Whether payroll cost enters item cost. | Costing precision. |
| Available choices | Rule for how headcount/machines share a queue. | Confirm both roles; or one. | Keep out (reward formulas are Phase 2); or define a rate. | Accept; or mark some pauses billable. |
| Recommended default | Postpone until real data exists. | Confirm both roles with the no-self-approval rule. | Keep out of v1. | Accept. |
| Consequences | Forecast accuracy only. | None (code already matches). | Costing scope. | Costing. |
| Implementation blocker | No. | No. | No. | No. |

## 2. Optional improvements (not questions)

- B5 / B11: link report lines to overtime assignments; export and drill-down for unit metrics. Recommended: after management feedback.
- B4: a proposal does not reserve limit; a missing machine qualification is not shown. Recommended: keep.

## 3. Resolved by existing approved requirements or technical choices (no owner input needed)

| Row | Why it is resolved |
|---|---|
| A3 supervisors | Decision #122: the **Production Manager** designates unit supervisors. Who the supervisor of each unit is, is operational data to enter, not a rule. The packet's "Planning designates" is superseded. |
| B3 week for overtime limits | Saturday-to-Friday matches the approved week (Saturday-Thursday regular, Friday exception, #13, #123). |
| B7 cost snapshot after correction | A new calculation is always possible; depends on the (unbuilt) QC correction below. |
| B14 Excel stages | 20 stages is a safety limit, documented in the template guide, not a business rule. |
| E2 legacy Start | Technical transition choice; retire when the work board is the only path. |
| E3 handover overlap | Decision #95 D5 already allows an explicitly recorded handover overlap on a worker schedule and forbids two operators on one machine otherwise; no further decision. |
| E6 demo supervisor | Demo-only account, not a business decision. |

## 4. Approved requirement not yet built (a gap, not a question)

- **B12 / Master Spec §15 / CLAUDE.md:** "A later QC correction preserves history and marks the item as corrected; send the item back to Stage 1 and let downstream stages be manually handled." The packet says "QC correction of an earlier decision is not built". This needs no decision from you; it is on the implementation list after the management demo.

## 5. What I need from you (short form)

1. Overtime limits: the two numbers (daily/weekly minutes) - A1.
2. Contract amount: option (a) or (b) - A2.
3. Late reports: a number of working days, flag or refuse - B1.
4. Amending approved reports: keep final, or reopen with reason - B2.
5. Reversal of Warehouse mistakes (B9) and auto-closing requests (B10).
6. Production SQL Server instance (A4) - only for go-live.
Everything else proceeds on the defaults above and none of it blocks the management demo.
