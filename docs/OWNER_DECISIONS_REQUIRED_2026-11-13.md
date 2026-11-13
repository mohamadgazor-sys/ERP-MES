# Owner decisions required - consolidated report (2026-11-13, second round)

| | |
|---|---|
| **Source** | The exact rows of `docs/OWNER_REVIEW_PACKET_2.md` (A1, A2, A4, B1, B2, B6, B8, B9, B10, B13, E1, E4, E5), quoted verbatim, read together with the Decision Log entry each row names and the approved specifications. Nothing was inferred from an identifier alone. |
| **Not repeated** | Everything answered by the owner messages of 2026-11-05 and 2026-11-13 (external supply, downtime, overtime inside regular hours, contractor-lost pieces, assembly waiting state #140) and the rows already resolved in `docs/OWNER_REVIEW_REPORT_2026-11-13.md` (A3, B3, B7 in part, B14, E2, E3, E6). |
| **How to answer** | For each decision write "keep", "accept the recommendation", or the replacement. Section 4 lists every recommendation in one line each so a single reply can accept them all. |
| **Rule** | The application keeps following the labelled current implementation until you answer. Nothing below is implemented as an approved business rule. No decision below stops independent development. |

## 1. Rows that need your business judgment

### A1 - Overtime limits (Decision Log #103 D5, changed by #122)
- **Exact original wording (packet):** "Overtime limits: how many minutes per operator per day and per week?" Current default in the packet: "None configured; overtime cannot be approved until Planning enters them". Packet recommendation: "Planning enters them in the overtime screen; no application change".
- **Current implementation:** the limits row does not exist until someone creates it; without it no overtime can be approved (#103 (2)). Since #122 (4) **Management** (not Planning) configures the limits; within the daily and weekly limits Management OR the Production Manager approves alone; beyond either limit only Management. The packet's recommendation text ("Planning enters them") is therefore out of date. The demonstration database carries EXAMPLE limits of 120 minutes per day and 600 per week (#139), which are not a business decision.
- **Business context:** the limits decide when the Production Manager may approve alone and when Management must decide.
- **Alternatives:** (a) you give the two numbers now and I set them as the real starting values; (b) you leave the numbers to Management, who enters them in the overtime screen before go-live; (c) a different rule (for example per-unit limits) - not built.
- **Recommended default:** (b). The numbers are operational settings with versioning and audit; no code change is needed whichever number you choose.
- **Consequences:** (a)/(b) none for the code. Until a real number exists real overtime approval is impossible; the demo is unaffected. (c) needs a design change.
- **Blocks development?** No. Blocks real use of overtime approval only.
- **Related:** #103, #122 (4), #131, #139.

### A2 - Where the contract amount lives (Decision Log #104 D4)
- **Exact original wording:** "Where does the contract amount live? Contracts store number, date, title, note and PDF but no amount". Current default: "Management types the amount and the contract number when it approves the price of a contract item". Recommendation: "Add a contract amount that Planning enters and Management approves with the contract; the price screen then proposes it".
- **Current implementation:** the contract record has no amount. For a MAKE item of a request whose contract is approved the price decision has basis CONTRACT; Management types the contract amount and the contract reference at price approval (#104 (4)).
- **Business context:** the amount customers signed should be traceable from the contract itself, and typing it later is an error risk.
- **Alternatives:** (a) keep; (b) a contract amount entered by Planning with the contract and approved by Management with it; the price screen proposes it (Management can still confirm or change with a reason); (c) amount per item rather than per contract (items of one contract may carry different prices).
- **Recommended default:** (b), because it keeps one source of truth and keeps Management's approval. If prices differ per item choose (c).
- **Consequences:** (a) none. (b) adds a contract field, a small change to the contract and price screens, and one migration. (c) adds a per-item amount and more screens.
- **Blocks development?** No.
- **Related:** #104, Master Specification 19.0 (amounts in Rial), #111 (item-scoped contract decisions).

### A4 - Production SQL Server (Decision Log #117)
- **Exact original wording:** "SQL Server: technical pass done (provider, committed migration, integration tests against a throw-away SQL Server 2022 container in CI). Still yours: which edition/instance is the production database, its collation, backups and connection-string handling, and when the provider package is approved for production". Current default: "The test pass is green on main (3 tests, 0 skipped); production use needs your approval of the instance". Recommendation: "Name the instance and edition; nothing else is blocked".
- **Current implementation:** SQLite for the demonstration and development; the SQL Server provider, a committed migration and a CI integration pass on SQL Server 2022 exist. The company's SQL Server 2008 R2 system is never touched (CLAUDE.md).
- **Business context:** deployment, backups and who administers the database.
- **Alternatives:** the instance and edition IT chooses (a supported modern version on its own server or instance, not the 2008 R2 system); collation; who owns backups; how the connection string is supplied (not stored in the repository).
- **Recommended default (technical, mine to propose, yours to approve):** a dedicated database on a supported SQL Server version, case-insensitive accent-sensitive collation with Persian support, nightly full plus frequent log backups owned by IT, connection string in a per-machine protected setting.
- **Consequences:** the choice sets the go-live plan; no code change except configuration.
- **Blocks development?** No; blocks only production go-live.
- **Related:** #117, CLAUDE.md "Canonical business rules already decided".

### B1 - Late job reports (Decision Log #102)
- **Exact original wording:** "Late job reports - No limit; no report is refused as late (you gave no number of working days)".
- **Current implementation:** a report may be entered any time after the work date (not in the future); lateness is not flagged or limited.
- **Business context:** very late reports distort KPIs and costing (only APPROVED reports are costed, #104).
- **Alternatives:** (a) keep no limit; (b) flag reports older than N working days for the supervisor and Management but accept them; (c) refuse reports older than N working days (Planning or the Production Manager can enter an exception with a reason).
- **Recommended default:** (b) with N chosen by you; refusing real work records invites off-system corrections.
- **Consequences:** (a) none. (b) a flag and a report column, no workflow change. (c) a refusal rule plus an exception path.
- **Blocks development?** No.
- **Related:** #102, #104.

### B2 - Amending an approved report (Decision Log #102)
- **Exact original wording:** "Amending an approved report - Not built; an approved report is final".
- **Current implementation:** an approved daily job report can never be changed; corrections of work-session time exist separately (Planning/Production Manager, with a reason, #113).
- **Business context:** operators make mistakes after approval; only APPROVED reports feed costing and KPIs.
- **Alternatives:** (a) keep final; (b) the Production Manager (or Planning) reopens an approved report with a mandatory reason, the report is corrected and approved again as a new revision, the earlier revision stays visible; (c) Management only.
- **Recommended default:** (b), because it keeps history and avoids off-system fixes.
- **Consequences:** (a) none. (b)/(c) add a revision workflow and make existing cost snapshots stale; the stale-snapshot rule is the same question as QC correction decision Q5 in section 2.
- **Blocks development?** No.
- **Related:** #102, #104, Master Specification 1.10 / 15.

### B6 - Material price date, scrap factor, adjustment lines (Decision Log #104)
- **Exact original wording:** "Material price date - The day the item's first stage was released; no scrap factor; no Management adjustment lines".
- **Current implementation:** material cost = nominal weight per base unit x base units that entered production x the price per kg in force on the day the first stage was released; scrap and rework are absorbed by the accepted units; Management can change only the final price (with a reason), not add cost lines.
- **Business context:** cost accuracy and price disputes.
- **Alternatives:** price date = release day (now), or the cost-calculation day, or the day material was issued (no such record exists); add a scrap/waste factor per material; allow Management adjustment lines (extra material, tooling) with a reason.
- **Recommended default:** accept for v1 (all three as they are); they change formulas, not the workflow.
- **Consequences:** each alternative changes the cost formula, adds fields and tests; older snapshots stay immutable.
- **Blocks development?** No.
- **Related:** #104 (3), Master Specification 19.3, 19.4.

### B8 - Finished/sent (Decision Log #105; Master Specification 17, open question U-38)
- **Exact original wording:** "Finished/sent - Also requires the Warehouse receipt (your text names only the price approval)".
- **Current implementation:** an item can be marked finished/sent only after the Warehouse recorded its receipt AND the latest cost snapshot has a Management-approved price. The Master Specification's own lifecycle diagram orders costing, price approval, then Warehouse receipt, then COMPLETE / CLOSED (section 38), and annotates the relation to the final receipt as the open question U-38.
- **Business context:** which events make an item "finished/sent".
- **Alternatives:** (a) keep both required; (b) price approval alone is enough; the receipt is recorded but not required; (c) receipt first, price approval later, both still required (an ordering rule).
- **Recommended default:** (a); a receipt is physical evidence that the item exists.
- **Consequences:** (b) allows an item to be finished/sent without ever being received (a hole for mistakes). (c) adds an ordering rule.
- **Blocks development?** No.
- **Related:** #105, #104, Master Specification 17.2, 38, U-38.

### B9 - Reversing a mistaken receipt, mark or dispatch (Decision Log #105)
- **Exact original wording:** "Mistaken receipt, mark or dispatch - Cannot be reversed in v1".
- **Current implementation:** the three Warehouse records are append-only; there is no reversal action. (A QC correction will need one for cost and Warehouse records: see section 2, Q5.)
- **Business context:** Warehouse typing errors.
- **Alternatives:** (a) keep; (b) the Warehouse proposes a reversal with a reason and Management approves it; (c) Management alone reverses with a reason. In every case the original row is kept and marked reversed, never deleted.
- **Recommended default:** (b) or (c); (c) is simpler. The mechanism can be shared with the QC-correction invalidation of Q5.
- **Consequences:** (a) mistakes need a database fix by a developer. (b)/(c) add a reversal record, status and screen.
- **Blocks development?** No; best decided together with Q5.
- **Related:** #105, Master Specification 1.10.

### B10 - Closing the request (Decision Log #105)
- **Exact original wording:** "Request status - Not closed automatically when all items are finished/sent".
- **Current implementation:** closing a request is not automatic; the CLOSED status exists in the specification (section 10, 38) but is reached by a separate step that is not part of a normal flow today.
- **Business context:** whether the request becomes CLOSED by itself.
- **Alternatives:** (a) manual close by Management after review; (b) automatic close when every item is finished/sent (customer dispatch stays optional, #105); (c) automatic close when every item is finished/sent AND has a customer dispatch.
- **Recommended default:** (a), because a closed request blocks further changes (#99).
- **Consequences:** (b)/(c) are status-transition rules; a wrong automatic close needs a reopen action that does not exist.
- **Blocks development?** No.
- **Related:** #105, Master Specification 38.

### B13 - Planned-end forecast (Decision Log #100, #128; Master Specification 13.3)
- **Exact original wording:** "Planned end forecast - One lane per unit (capacity does not multiply throughput); machine downtime is included since #128 (see B15); no external lead time yet; regular week not effective-dated; "supervisor approval" of an extra working day is a mandatory reason text". (The "no external lead time yet" part was done by the external-stage series, #135; the downtime part is approved, #130.)
- **Current implementation:** the unit works through its queue one lane at a time in working intervals; the number of machines or operators does not shorten the plan; the regular week applies from today (not effective-dated); an extra working day needs a reason text, not an approval workflow.
- **Business context:** accuracy of planned end dates for units with several machines or shifts.
- **Alternatives:** (a) keep; (b) multiply throughput by the number of machines or by staffed capacity; (c) plan per machine; (d) effective-dated regular week; (e) a real approval step for an extra working day.
- **Recommended default:** (a) until real data exists; (b) and (c) need a rule for how machines share the queue and how stages bind to machines.
- **Consequences:** (b)/(c) change forecast results and need scheduling design work; (d)/(e) are smaller.
- **Blocks development?** No.
- **Related:** #100, #123, #128, #130, #135, Master Specification 13.3.

### E1 - Who approves session time (Decision Log #112-#116, #122)
- **Exact original wording:** "Who approves session time - Planning, and never the person who dispatched or started the session".
- **Current implementation:** Planning and the **Production Manager** (#122 (2)) approve session time; nobody approves a session they dispatched or started. Only time-approved sessions reach the cost and KPIs.
- **Business context:** segregation of duties on time that is later costed.
- **Alternatives:** (a) keep both roles with the "not the dispatcher/starter" rule; (b) Planning only; (c) allow the Production Manager to approve sessions the Production Manager dispatched (no separation).
- **Recommended default:** (a). The code already matches; the only open point is that the Production Manager dispatches too, so on the Production Manager's own sessions Planning must approve.
- **Consequences:** (b) removes a role; (c) removes the separation of duties.
- **Blocks development?** No.
- **Related:** #113, #114, #122 (2).

### E4 - Labour cost (Decision Log #114)
- **Exact original wording:** "Labour cost - No labour-cost rule; labour hours are reported separately".
- **Current implementation:** item cost = machine cost (approved session machine runtime x the machine rate of that date) + material. Labour hours are reported (metrics, KPI) but never priced.
- **Business context:** whether operators' pay should enter the item cost. The performance reward formula is Phase 2 (CLAUDE.md) and is not part of this.
- **Alternatives:** (a) keep labour out of cost; (b) a labour rate per hour (per operator or per department, effective-dated) multiplied by labour hours; (c) a fixed labour overhead percentage.
- **Recommended default:** (a) for v1; if labour must be priced, (b) with one department-level rate.
- **Consequences:** (b)/(c) change the cost formula and add master data; previous snapshots stay immutable.
- **Blocks development?** No.
- **Related:** #104, #114.

### E5 - Pause reasons: scopes and billable ones (Decision Log #112-#115)
- **Exact original wording:** "Pause reasons: scopes and which are billable - Pauses are never machine runtime for costing; a missing operator qualification is a warning kept on the session, not a refusal; an operator outside the scheduled shift needs a recorded reason". (In the packet the row reads "Pause reasons: scopes and which are billable" in the topic column and the three rules in the default column.)
- **Current implementation:** seven pause reasons (power outage, planned maintenance, breakdown, operator absence or handover, material unavailable, waiting for assistance, other with a note); every pause is excluded from machine runtime for costing; a missing qualification is a warning; work outside the scheduled shift needs a recorded reason.
- **Business context:** whether some pauses (for example a power outage or a machine breakdown) should still be charged as machine time.
- **Alternatives:** (a) keep all pauses unbilled; (b) mark selected reasons as billable (a flag per reason that Planning maintains); (c) refuse unqualified operators.
- **Recommended default:** (a); (b) later if costing disputes appear (the flag is a small change).
- **Consequences:** (b) changes costing for sessions with those pauses; (c) can stop work when the qualification register is incomplete.
- **Blocks development?** No.
- **Related:** #112, #113, #114.

## 2. Approved requirement not yet built: QC correction (Master Specification 1.10, 15, 15.1)

**What the approved text says (quoted from §1.10 and §15):** QC may correct a previous QC decision; the downstream case is not handled by rebuilding all downstream stages. Rule: "return the item to the affected stage (for the confirmed example, Stage 1)"; "retain historical QC records"; "mark the QC decision as corrected/superseded"; "downstream stages are then handled manually according to the actual production situation"; "previous cost approval and warehouse transactions are not treated as authoritative after the correction and must not silently remain the current outcome"; "The exact mechanics for invalidating/reopening a previously closed cost or warehouse record must be explicit in the transaction implementation and must never delete historical evidence". The correction records: correction user, time, reason, relationship to the original decision, resulting current decision/status. "The item should carry a visible indicator/history that a QC correction occurred." CLAUDE.md adds: QC alone performs QC correction.

**What exists today (inspected):** quantity QC (`QcInspectionLine` rows are append-only; a stage is QC_APPROVED when everything is decided), shortfall decisions, work sessions with Planning-only corrections before QC submission (#113), Planning's undo of a start or submission only while QC decided nothing (#102), cost snapshots (immutable; a recalculation adds the next one; the older decision is superseded, #104), Warehouse receipt / finished-sent / dispatch (append-only, no reversal, #105). There is **no** correction, no "superseded" marker on QC decisions, no "corrected" marker on an item, and no way to invalidate a cost or Warehouse record. Packet B7 ("A later QC correction ... Does not invalidate an existing cost snapshot") and B12 ("QC correction of an earlier decision is not built") are labelled defaults that the approved text of §1.10 / §15.1 supersedes.

**What the approved text already determines (I will implement exactly this):** QC alone; a mandatory reason; an append-only correction record (user, time, reason, link to the corrected stage and its decisions, resulting status); no QC line, approval, work session or cost row is deleted or edited; the corrected QC decisions are marked superseded; the item returns to the corrected stage and carries a visible "corrected" indicator and history; downstream stages are NOT rebuilt or reset automatically; cost approval and Warehouse outcomes are no longer shown as current truth.

**What the text does NOT determine - each needs your answer (recommended default after each; "accept all" is a valid answer):**

| # | Question | Options | Recommended default |
|---|---|---|---|
| Q1 | Which stage can be corrected, and when? | (a) any stage whose QC decision is final (QC_APPROVED); (b) also stages still waiting for the shortfall decision; (c) only the last approved stage | (a) QC_APPROVED stages only (the corrected stage is "the affected stage"; Stage 1 is the confirmed example, not a limit) |
| Q2 | How many units does the correction cover? | (a) the whole stage (everything that stage received is redone); (b) QC names a quantity of the previously accepted units | (b) with the whole stage as the maximum: QC states how many of the previously accepted units are now to be reworked or scrapped; the rest stay accepted |
| Q3 | What is the "resulting current decision" and how does the stage continue? | (a) the stage reopens as executable (back in its unit queue, WAITING) for the corrected quantity and returns to QC after the work; (b) QC immediately records a new decision (rework or scrap) in the same step | (b): the correction records the new decision (rework or scrap, with the reason) and reopens the stage as executable for rework units; scrapped units follow the existing shortfall decision |
| Q4 | What happens to downstream stages that already progressed? | (a) nothing: they stay as they are and are only marked "affected by a QC correction" for Planning and the Production Manager to handle; (b) not-yet-started downstream stages are held (stopped) until the corrected stage is approved again; (c) all downstream stages are reset (rejected by the specification) | (a) plus a visible flag; a downstream stage is never started on units the correction rejected without a person deciding (the specification says "handled manually") |
| Q5 | Cost and Warehouse records after a correction | (a) mark the latest cost snapshot, its price decision, the Warehouse receipt and the finished/sent mark as "invalidated by QC correction" (rows stay); the item shows "not current" until Warehouse recalculates, Management re-approves and the Warehouse records the receipt again; a customer dispatch that already happened is flagged only, it cannot be undone; (b) only the cost/price records, not the receipt | (a), using one shared "invalidated by correction" marker; it is the same mechanism a reversal (B9) and an amended report (B2) would need |
| Q6 | Work sessions and machine time of the original run | (a) keep all; the rerun adds new sessions; costing then counts every time-approved session of the stage (the failed run was real machine time); (b) the original run's sessions are excluded from cost | (a): history is preserved and the cost shows what was really spent; Management sees the correction on the price screen |
| Q7 | Assembly parents of a corrected child | (a) flag only; (b) a parent whose first stage is not yet started returns to the waiting state (#140) until the child is QC approved again; (c) nothing | (b): consistent with the approved dependency rule; a parent already in work is flagged only |
| Q8 | Authorization and timing | QC alone, any time before the request is closed; Management is informed through the visible flag; or Management must approve a correction after a customer dispatch | QC alone; a correction after a customer dispatch needs the Management reason text to be read on the item (flag) - no extra approval step |

**Why it is not built yet:** Q2, Q3, Q5 and Q6 decide what counts as the item's current quantities, cost and Warehouse state; guessing them would change production, cost and Warehouse results. Everything else is ready. As soon as you answer (or accept the defaults) it is implemented in one focused pull request with the schema change (`QcCorrections` plus a "superseded"/"invalidated" marker), the service, the screen action, and tests of the complete workflow.
**Blocks development?** This one item only.

## 3. Other approved-but-unbuilt requirements found in the audit

| # | Requirement | Finding | Plan |
|---|---|---|---|
| G1 | Master Specification 12.5 / 12.5.1 request and item progress visibility | No request-level progress view exists: no counts of completed / active / waiting-blocked / rework items and no per-item "2 / 5 stages". The specification requires the counts and the stage progress regardless; the headline percentage formula is "not yet approved" | I implement the counts and per-item stage progress now (no invented rule); the percentage waits for R1 below |
| G2 | Master Specification 12.4 Management live overview | The production queue page exists; the home page's four tiles are placeholders showing "—" | I fill the four tiles from the database (open requests, items in production, waiting for QC, urgent items) in the same pull request as G1 |
| R1 | Headline request percentage (owner question) | The specification proposes "completed items divided by total items" as a default but marks it not approved | Recommended: accept that formula for v1 and show it next to the counts |

## 4. Quick answer sheet

Reply with one line per item, for example "A1 (b); A2 (b); A4 IT will name it; B1 (b) N=7; B2 (b); B6 keep; B8 (a); B9 (c); B10 (a); B13 keep; E1 keep; E4 keep; E5 keep; Q1-Q8 accept defaults; R1 accept".

| Row | Recommended answer |
|---|---|
| A1 | (b) Management enters the real numbers before go-live |
| A2 | (b) contract amount on the contract |
| A4 | IT names the instance; my technical proposal above |
| B1 | (b) flag after N working days (you choose N) |
| B2 | (b) reopen with reason, new revision |
| B6 | keep for v1 |
| B8 | (a) both receipt and price approval required |
| B9 | (c) Management reverses with a reason; row kept |
| B10 | (a) manual close by Management |
| B13 | keep until real data exists |
| E1 | keep |
| E4 | keep labour out of cost |
| E5 | keep; billable flag later if needed |
| Q1-Q8 | defaults in section 2 |
| R1 | item-completion percentage |

## 5. What continues without your answers
The progress view and dashboard tiles (G1, G2), the verification and hand-off updates, and any approved work that does not depend on the answers above.
