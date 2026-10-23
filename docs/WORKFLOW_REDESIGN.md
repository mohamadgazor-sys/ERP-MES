# Workflow Redesign - Lifecycle, Authorization, Staging and Decision Register

| | |
|---|---|
| **Status** | **DESIGN.** Not implemented. The pull request that adds or changes this document is documentation-only: no code, schema, migration or package. |
| **Basis** | The owner's requirements R1-R9 (Decision Log #82, #83), the owner's confirmed decisions #84-#92 (2026-10-22), the Master, Architecture and Domain/Database specifications, Decision Log #1-#81, and the working demonstration (PRs #18-#27). |
| **Companion documents** | `OWNER_DECISION_PACKET.md`, `RAW_MATERIAL_INQUIRY_DESIGN.md`, `PLANNING_RESOURCES_AND_SCHEDULING_DESIGN.md`, `QC_QUANTITY_MODEL_DESIGN.md`, `COSTING_DESIGN.md`, `JOB_REPORTS_KPI_AND_OVERTIME_DESIGN.md`. |

## 0. Legend

| Marker | Meaning |
|---|---|
| **[APPROVED #n]** | Approved by the owner; Decision Log entry given. |
| **[EXISTING #n]** | Approved earlier and not in conflict with the newer requirements. |
| **[DEMO]** | Approved for the demonstration only (#58, #79-#81). |
| **[DEFAULT]** | A deterministic default I recommend for a low-risk implementation detail. **Not approved.** It may be implemented and is changed by a later decision without rework of the business meaning. |
| **[OPEN Dn]** | A genuinely unresolved business decision; see `OWNER_DECISION_PACKET.md`. Not implemented until answered. |

Nothing marked DEFAULT or OPEN is a business rule.

## 1. Approved requirements and decisions in force

| Id | Statement | Decision |
|---|---|---|
| R1-R9 | Owner requirements: raw-material inquiry, Planning resources, QC quantity model, costing, job reports, KPIs, shifts, overtime, 12-step lifecycle | #82 |
| | Individual-unit QC dispositions for the intended production system | #83, #92 |
| | One authoritative work type per request; mixed requests prohibited | #84 |
| | Contract determination for MAKE requests only | #85 |
| | Planning defines all items and stages after the gates; Drawing owns feasibility and inquiries | #86 |
| | Request PDF mandatory before submission | #87 |
| | Warehouse answers Yes/No per inquiry line; no reservation or deduction | #88 |
| | Planning owns resource master data and planned machine assignment; supervisors only overtime and unit-report review; no silent substitution | #89 |
| | QC owns UOM and quantity types; controlled mapping to a base counting unit; set composition explicit | #90 |
| | Final machining cost = approved actual job-report time x applicable machine rate; plan only for labelled estimates | #91 |

## 2. Reconciliation with earlier decisions

Every conflict found is now resolved by a newer decision. History is preserved: each superseded entry received a note, not an edit.

| Old rule | Newer decision | Handling |
|---|---|---|
| #2, Master 1.1, Domain 2.3: WorkType per item; mixed requests | #84 | Annotated; model change in lifecycle PR |
| #4, Master 1.3: contract determination for every request | #85 | Annotated |
| #34, #75(4), Master 6: Drawing defines MAKE items | #86 | Annotated |
| Master 6.4/17.1, Domain 13.1: AVAILABLE/PARTIAL/UNAVAILABLE with approved quantity | #88 | Annotated |
| Master 9, Domain 19.3: supervisor assigns operator and machine | #89 | Annotated |
| Master 20, Domain 16: UOM with Order/Tracking UOM on the item | #90 | Annotated; model change in the QC masters PR |
| #58, #79: whole-quantity QC | #83, #92 | Marked demo-only |
| Master 19.1: cost routes (actual sessions, planned estimates) | #91 | Annotated; Contract Make still [OPEN D4] |
| Master 22.2 / #21: 24-day period; ranking formula Phase 2 | R6 (#82) | 24-day period kept as one preset; reward formula stays Phase 2 |
| #13: shift patterns configurable | R7 (#82) | Consistent: A-D are data |

Confirmed consistent and unchanged: Management approves/rejects the request (#29); Planning forwards to Drawing; production units see assigned stages in their own queues; later stages are locked until QC approves the preceding one (#5, #78); QC owns UOM (#9); Warehouse owns material price per kg, Planning owns machine rate (#10); the inquiry does not deduct stock (#17).

## 3. End-to-end lifecycle

Notation: Req = request status [EXISTING #29 unless marked]; statuses after `DRAWING_RETURNED_TO_PLANNING` are [DEFAULT].

| Step | Actor | Action | Result | Gate or rule | Basis |
|---|---|---|---|---|---|
| 1 | Central | Creates the request, **selects exactly one type (MAKE or REPAIR)**, records details, attaches the request PDF, submits to Management | `CREATED` -> `SENT_TO_MANAGEMENT` | PDF mandatory; type fixed after submission | #84, #87 |
| 2 | Management (Manager role) | Approves or rejects (reason mandatory on rejection). Rejected request may be reopened by Planning or Management with a reason | `MGMT_APPROVED_AT_PLANNING` / `MGMT_REJECTED_AT_PLANNING` | | #29 |
| 3 | Planning | Reviews, forwards to Drawing | `UNDER_DRAWING_REVIEW` | | #29 |
| 4 | Drawing | Determines technical feasibility; may request clarification; may open **raw-material inquiries** to Warehouse | request status unchanged; inquiry has its own status | Availability never decides feasibility. Clarification routing and the effect of "No" are [OPEN D1] with a labelled default | #82, #88 |
| 5 | Drawing | Returns the technical result to Planning (fully accepted / partially accepted / rejected / more information needed / ordinary return, Master 5.2) | `DRAWING_RETURNED_TO_PLANNING` | The result shows the material picture (not inquired / pending / all yes / some no) as information | #82 |
| 6 | Planning (then Management) | **MAKE:** Planning decides whether a contract is required; if so creates/submits the contract + PDF; Management approves or rejects; rejection returns to Planning; Warehouse read-only; approval notifies Planning and Warehouse. **REPAIR:** skips this step | MAKE with contract: `CONTRACT_PENDING_MANAGEMENT` -> approved: `READY_FOR_DEFINITION`; rejected: `CONTRACT_REJECTED_AT_PLANNING`. No contract needed, or REPAIR: `READY_FOR_DEFINITION` | | #85 |
| 7 | Planning | After the Management, Drawing and contract gates: defines **all items** (quantity type, dimensions, raw-material weight, material) and **their stages**, with production unit and machine per stage; by hand or by **Excel import** (section 12) | Items `DEFINED`; first stage `PLANNED` | Gates of section 5 | #86, #89 |
| 8 | Production units | See assigned stages in their own queues; later stages visible but cannot start until the preceding stage meets the QC gate. Planning releases the first stage | Stage `PLANNED` -> `WAITING`; later stages locked | Queue order #78 | #5, #78 |
| 9 | QC | Records accepted / rework / scrap at individual-unit granularity; accepted quantity unlocks the next stage | Stage `QC_APPROVED` for the accepted quantity | `QC_QUANTITY_MODEL_DESIGN.md` | #83, #92 |
| 10 | Warehouse | After final-stage QC, calculates item cost from approved actual job-report time x applicable machine rate and raw-material cost | Item `COST_PROPOSED` | `COSTING_DESIGN.md`; Contract Make pricing [OPEN D4] | #91, R4 |
| 11 | Management | Approves or changes the proposed price; decision, author, time and reason recorded; the proposal is kept | Item `PRICE_APPROVED` | | R4 |
| 12 | Warehouse | Marks the item finished/sent only after the price approval | Item `FINISHED_SENT` | Receipt vs finished/sent vs customer dispatch [OPEN D7] | R9 |

### 3.1 State machines

**Request:**

```text
CREATED --submit(Central; type chosen; PDF attached)--> SENT_TO_MANAGEMENT
SENT_TO_MANAGEMENT --approve(Manager)--> MGMT_APPROVED_AT_PLANNING
SENT_TO_MANAGEMENT --reject(Manager, reason)--> MGMT_REJECTED_AT_PLANNING
MGMT_REJECTED_AT_PLANNING --reopen(Planning|Management, reason)--> SENT_TO_MANAGEMENT
MGMT_APPROVED_AT_PLANNING --forward(Planning)--> UNDER_DRAWING_REVIEW
UNDER_DRAWING_REVIEW --return result(Drawing)--> DRAWING_RETURNED_TO_PLANNING
DRAWING_RETURNED_TO_PLANNING --send back(Planning, reason)--> UNDER_DRAWING_REVIEW
[DEFAULT] DRAWING_RETURNED_TO_PLANNING --MAKE, contract required: submit contract(Planning)--> CONTRACT_PENDING_MANAGEMENT
[DEFAULT] CONTRACT_PENDING_MANAGEMENT --approve(Management)--> READY_FOR_DEFINITION
[DEFAULT] CONTRACT_PENDING_MANAGEMENT --reject(Management, optional reason)--> CONTRACT_REJECTED_AT_PLANNING
[DEFAULT] CONTRACT_REJECTED_AT_PLANNING --resubmit(Planning)--> CONTRACT_PENDING_MANAGEMENT
[DEFAULT] DRAWING_RETURNED_TO_PLANNING --MAKE, no contract needed, or REPAIR--> READY_FOR_DEFINITION
[DEFAULT] READY_FOR_DEFINITION --Planning defines items and releases the first stage--> IN_PRODUCTION
[DEFAULT] IN_PRODUCTION --all items FINISHED_SENT or closed--> CLOSED
CANCELLED/CLOSED as in #29.
```

A Drawing result of "rejected" or "more information needed" does not by itself close the request; Planning decides (send back, cancel, or proceed on the accepted part) [DEFAULT]. The drawn-out clarification path is [OPEN D1].

**Item pipeline [DEFAULT]:** `DEFINED -> RELEASED -> IN_PRODUCTION -> PRODUCTION_COMPLETED -> COST_PROPOSED -> PRICE_APPROVED -> FINISHED_SENT`. A QC correction returns the item to Stage 1 (#7); cost and price stop being "current" and history stays (Master 15.1, Domain 12.5).

**Stage:** the existing vocabulary (Master 10) is unchanged; quantity is tracked in buckets (`QC_QUANTITY_MODEL_DESIGN.md`).

**Raw-material inquiry:** `RAW_MATERIAL_INQUIRY_DESIGN.md` section 4.

## 4. Where the inquiry sits

The inquiry is an informational sub-process owned by Drawing, anchored at the request (#86). It does not change the request status, reserve stock or approve feasibility. What follows an unavailable answer is [OPEN D1].

## 5. Gates before production release

| Gate | Status |
|---|---|
| Request approved by Management | [APPROVED #29, #86] |
| Drawing technical result returned (fully or partially accepted for the items in scope) | [APPROVED #86] |
| Contract approved when required (MAKE only) | [APPROVED #85] |
| Item fully defined (quantity type, dimensions, raw weight, material, stages with unit and machine) | [DEFAULT] |
| Every inquiry line answered and every "No" line dispositioned | [OPEN D1] |

The demonstration checks none of these (#80, #81).

## 6. Per-unit queues (relation to #78)

The overview is item-level and read-only. The authoritative per-unit queue is approved as a target but not built [EXISTING #78]: released stages of a unit; IN_PROGRESS, then STOPPED/problem, then WAITING; then Planning priority, Planning position (first come first served on ties), planned end, stable ids; Management overrides stay active until cleared; a later Planning priority change flags the override for review. Stages now carry a planned machine (#89), so a unit queue can also be filtered by machine. The remaining details in the Decision Log "Queue ordering" item stay open and are settled with sensible defaults only when that PR is built; they will be listed in that PR.

## 7. Authorization matrix

**Y** = may perform; **R** = read-only; blank = denied by default. Basis: **A** approved (decision number), **E** earlier approved matrix (15.2/15.4), **DEMO** #80, **D** [DEFAULT], **OPEN D8** pending. The "Supervisor" is a Production user designated for a unit [DEFAULT, U-16].

| Action | Cen | Mgmt | Plan | Draw | Whse | Prod op | Prod sup | QC | PM | Basis |
|---|---|---|---|---|---|---|---|---|---|---|
| Create request, choose type, attach PDF, submit | Y | | | | | | | | | A #84, #87; E |
| Approve / reject request (Manager role) | | Y | | | | | | | | E |
| Reopen a rejected request | | Y | Y | | | | | | | E |
| Forward to Drawing / send back | | | Y | | | | | | | E |
| Record technical result; request clarification | | | | Y | | | | | | E, A #82 |
| Create / send raw-material inquiry | | | R | Y | R | | | | | A #86; R rows OPEN D8 |
| Answer inquiry lines (Yes/No + notes) | | | | R | Y | | | | | A #88 |
| Decide contract required (MAKE); create / submit contract | | | Y | | R | | | | | A #85 |
| Approve / reject contract | | Y | | | R | | | | | A #85 |
| Define / edit items and stages; Excel import | | | Y | | | | | | | A #86 |
| Register operators, machine codes, operator-machine master assignments | | | Y | | | | | | | A #89 |
| Machine hourly rates | | Y | Y | | | | | | | A #10, #89 |
| Assign a stage to unit and machine; change a planned machine | | | Y | | | | | | | A #89 |
| Material price per kg | | Y | | | Y | | | | | A #10 |
| Maintain UOM and quantity types | | | | | | | | Y | | A #90 |
| Select UOM / quantity type for an item | | | Y | | | | | | | A #90 |
| Shift templates, unit shifts, OFF days, capacity, worker schedule | | | Y | | | | | | | A #89 |
| Overtime assignment (create) | | | | | | | Y | | | A #82 R8, #89; limits OPEN D5 |
| Overtime approval | | | | | | | | | | OPEN D5 |
| Release first stage | | | Y | | | | | | | DEMO #80 |
| Start stage; submit for QC | | | | | | Y | | | | DEMO #80; supervisor acting OPEN D8 |
| QC disposition (accepted / rework / scrap); QC correction | | | | | | | | Y | | E, #92 |
| Create and submit own daily job report | | | | | | Y | Y | | | A #82 R5 |
| Review / approve / return job reports of the unit | | | | | | | Y | | | D (OPEN D8 for Planning fallback) |
| View KPIs, rankings, job reports for any period | | R | Y | | | | R (own unit) | | | Planning A #82 R6; others OPEN D8 |
| Report machine downtime | | | R | | | | | | Y | E |
| Calculate item cost (propose) | | | R | | Y | | | | | A #82 R4 |
| Approve / change price | | Y | | | | | | | | A #82 R4 |
| Mark item finished / sent | | | | | Y | | | | | A #82 R9; definition OPEN D7 |
| Administer users and roles | Admin only | | | | | | | | | E |

Warehouse cannot edit machine rates; Planning and Management cannot edit raw-material costs (R4). No row widens a permission the approved matrix denies; every OPEN row is denied until decided.

## 8. Data flow

```text
Request(type) --(Drawing)--> RawMaterialInquiry(lines) <--(Warehouse: Yes/No)
Request --(Planning, after gates)--> RequestItem(spec) --> ItemStage(unit, machine)
QC masters: UnitOfMeasure, QuantityType(base mapping)    Planning masters: Operator, Machine(code), RateHistory, Shift*, Assignment, Qualification
Production: DailyJobReport(lines) --(supervisor approval)--> approved lines
QC: inspection lines (ACCEPTED/REWORK/SCRAP per base unit) --> StageQuantityBalance --> next-stage eligibility
Approved lines x MachineRate(work date) + Weight x MaterialPrice --> ItemCostCalculation(snapshot) --> Management price --> Warehouse FINISHED_SENT
Approved lines + QC records --> KPI figures (Planning), drill-down to reports
```

## 9. Classification of U-01 .. U-40

Class: **A** answered by the owner's requirements/decisions; **B** resolved by an existing decision with no conflict; **C** low-risk detail with a deterministic [DEFAULT]; **D** genuinely open, in `OWNER_DECISION_PACKET.md`.

| Id | Question | Class | Resolution |
|---|---|---|---|
| U-01 | Request type per request or per item | A | #84 |
| U-02 | Contract determination scope | A | #85 |
| U-03 | Who defines items; when | A | #86 |
| U-04 | PDF mandatory before submission | A | #87 |
| U-05 | Request statuses after Drawing return; gates; inquiry status | C | Section 3.1 and 5 defaults; inquiry gets no request status |
| U-06 | Clarification route; result with open inquiry | D | D1 |
| U-07 | Inquiry anchor | A | Request level, from #86 (items do not exist yet) |
| U-08 | Material attributes; material master | C | Grade, form, diameter mm, length mm, quantity+unit, standard, remarks; Warehouse-owned material list; free text flagged |
| U-09 | Yes/No vs partial | A | #88 (Yes/No + optional notes) |
| U-10 | Effect of "No"; what returns to Drawing | D | D1 |
| U-11 | Who may cancel/close/see an inquiry | C | Drawing cancels/closes; Planning and Warehouse read |
| U-12 | Machine assignment authority | A | #89 |
| U-13 | Qualification and reassignment rules | C | Qualification is a separate informational record; scheduling uses the Planning master assignment; no qualification gives a warning, not a block; reassignment only by Planning with history |
| U-14 | Machine code standard, immutability | C | Unique, never reused, immutable once referenced, retired not deleted; typo fix before first use is an audited Planning action |
| U-15 | Item spec fields, optionality, scale | C | Weight kg (decimal 12,4) per base unit, length/diameter mm (decimal 10,2); weight, quantity type and material mandatory to define a MAKE item; optional for REPAIR |
| U-16 | Operator accounts; supervisor identification | C | Operators who report need user accounts linked to Operator; a supervisor is a Production-department user designated for a unit by Planning |
| U-17 | UOM vs quantity type; base mapping; sets | A | #90; field-level design in the QC document |
| U-18 | Physical-piece identity | D | D2 |
| U-19 | Rework/scrap effect on totals; shortfall; over-production | D | D3 |
| U-20 | QC correction scope | B | #7: send the item back to Stage 1; downstream handled manually |
| U-21 | Pair/set pricing unit | C | Cost per base piece, shown per ordered unit |
| U-22 | Cost on actual or planned time | A | #91 |
| U-23 | Time recording and rounding | C | Integer minutes; hours = minutes/60; no rounding of time |
| U-24 | Setup, downtime, rework/scrap hours, overtime premium | D (rework/scrap hours, overtime premium: D3, D5); C (setup included if reported; downtime excluded) | |
| U-25 | Which rate/price applies | C | Rate on the report line's work date; material price on the item's release date |
| U-26 | Weight vs consumption; extra material costs | C | Nominal weight x price; extra costs only as explicit adjustment lines |
| U-27 | Quantity basis; authoritative figure | D (quantity basis: D3); C (total authoritative, per-unit derived) | |
| U-28 | Rounding; who approves adjustments | C | Whole Rial, half away from zero per line; Management approves adjustments (R4); Rial storage rules of #33 stay open |
| U-29 | Contract Make vs calculated cost | D | D4 |
| U-30 | Job-report field set | C | Section 3 of the job-report document |
| U-31 | Report authority, amendments, late window, duplicates | C | Section 5 of the job-report document; late window length configurable (default 3 working days) |
| U-32 | Valid reported hours | C | Section 4 of the job-report document |
| U-33 | KPI set; ranking; period | A (operator ranking by hours, date range, no composite, rewards external: R6); C (tie-break hours desc, distinct days desc, OperatorCode asc; 24-day preset); D (unit performance metric definitions: D6) | |
| U-34 | Shift storage; midnight attribution; breaks | C | Templates as data; a shift occurrence belongs to its start date; breaks for A-C unspecified until Planning enters them |
| U-35 | Overlap of D with A/B; overlapping assignments | D | D5 |
| U-36 | Off-day conflicts; capacity semantics | C | Block creating a schedule on an OFF day/off-day; flag existing schedules when a day becomes OFF; MaxOperators is a hard headcount limit for normal shifts |
| U-37 | Overtime approval, limits, premium | D | D5 |
| U-38 | Receipt vs finished/sent vs dispatch | D | D7 |
| U-39 | Who triggers cost; invalidation | C | Warehouse triggers after the last stage's QC; a QC correction or an amended report marks the snapshot not current |
| U-40 | Remaining authorization rows | D | D8 |

Summary: 9 answered (A), 1 existing decision (B), 18 low-risk defaults (C), 12 with a genuinely open part (D: U-06, U-10, U-18, U-19, U-24, U-27, U-29, U-33, U-35, U-37, U-38, U-40; U-24, U-27 and U-33 are partly A/C). The open parts are consolidated into packet items D1-D8.

## 10. The working demonstration: a known simplified implementation

The demonstration (PRs #18-#27) stays as it is until each gap below is closed by an implementation PR. It is **not** the target process.

| Area | Demonstration | Intended process | Closed by phase |
|---|---|---|---|
| Request lifecycle | *(Implemented: one type per request #84; PDF before submission #87; Management approve/reject, reopen, forward to Drawing, Drawing result, send back, with workflow history and audit.)* The MAKE contract gate is implemented (#93: own contract status, request status unchanged, readiness derived). Still missing: item definition after the gates, production release gates, clarification threads (D1) | Section 3 | 2b, 4a |
| Items and stages | Fixed seeded routes; no unit or machine on stages | Planning defines stages with unit and machine; Excel import | 3, 4 |
| Stage actions | Release/start/submit/approve with no request-status, contract or Management gate [DEMO] | Gates of section 5 | 1, 2, 5 |
| QC | Whole quantity only | Per-unit accepted / rework / scrap | 5 |
| Units / machines / operators | *(Production units, machines with stable codes, the rate history, operators, assignments and qualifications are implemented.)* Materials and the Warehouse price history are implemented too | Master data with history | 3b (partly done) |
| Queue | Item-level read-only overview, provisional order | Per-unit queue, Planning position, overrides | 5 |
| Costing / price | None | R4 | 7 |
| Job reports / KPI / shifts / overtime | None | R5-R8 | 6, 8 |
| Material inquiry | None | R1 | 2 |
| Quantities | A single decimal on the item. *(QC masters — units, conversions, quantity types — are implemented; selecting them on items is phase 4a.)* | Quantity types | 3a done, 4a |

## 11. Staged implementation plan

Each PR is small, keeps the demo working until replaced, carries tests, a Decision Log entry where a decision is made, and documentation. A PR does not start before its prerequisites are merged. The phases are the owner's list (2026-10-22) refined into PRs.

| Phase | PR | Needs | Depends on | Blocked by open decisions |
|---|---|---|---|---|
| 1a | Single-type requests (model, mapping, migration, Excel/demo adjustments), PDF-before-submit | #84, #87 | - | none |
| 1b | Request lifecycle: Management decision, forward to Drawing, Drawing result, status history, reopen | #29, #82 | 1a | none (clarification route uses [DEFAULT]) |
| 2a | Raw-material inquiry (Drawing, Warehouse Yes/No, history, clarification) | #86, #88 | 1b | D1 for the routing after "No"; build entities and answers first |
| 2b | MAKE contract gate (Planning decides, submit/approve/reject, Warehouse read-only, notification) | #85, #93 | 1b | none | done; rejection routing PROVISIONAL, notification derived from state |
| 3a | QC masters: UOM, conversions, quantity types with base mapping and explicit set composition | #90 | - | none |
| 3b | Planning masters: operators, machine codes, rate history, master assignments, qualifications; Warehouse material master and price history | #89 | - | none |
| 4a | Item definition and stage planning (spec fields, stage->unit/machine, release gates) | #86, #89 | 1b, 2b, 3a, 3b | none |
| 4b | Excel import (template, preview, validation, transactional commit, conflict choice) | #16 | 4a | none |
| 5a | Per-unit queues (Planning position, categories, overrides with review flag) | #78 | 4a | none |
| 5b | Quantity-level QC (inspection lines, balances, rework loop, next-stage eligibility) | #92 | 3a, 4a, 5a | D2, D3 |
| 6a | Planning calendar: shift templates A-D, unit shifts, OFF days, capacity, worker schedule | R7 | 3b | D5 (overlap) |
| 6b | Daily job reports and supervisor review | R5 | 3b, 4a, 6a | D8 |
| 6c | Overtime assignments | R8 | 6a, 6b | D5 |
| 7 | Costing, Management price approval, Warehouse completion | #91, R4 | 3b, 5b, 6b | D3, D4, D7 |
| 8 | KPI reporting | R6 | 6b, 5b | D6 |
| 9 | End-to-end integration, demo hardening, SQL Server test pass (#25), retire the demo stage flow | | all | none |

Order: 1a, 1b, 3a and 3b (independent), 2b, 2a, 4a, 4b, 5a, 6a, 5b, 6b, 6c, 7, 8, 9. A PR that needs an open decision builds only the parts not affected, or waits.

## 12. Excel import

[EXISTING #16] plus #84 and #86. One worksheet, Persian headers, about 1,000 items, preview, validation, transactional import, manual conflict choice.

1. **Who/when:** Planning, for a request in `READY_FOR_DEFINITION` (items exist only after the gates). The request's type is fixed; a Work Type column, if present, must match the request or the row is rejected.
2. **Template:** item number, description, quantity type code, ordered quantity, raw-material weight (kg), length (mm), diameter (mm), material code, priority, planned minutes per piece per stage, stage routes as columns (stage name, unit code, machine code). Machine and unit codes resolve to existing masters; an unknown code is a row error (nothing is auto-created).
3. **Preview:** parse and validate the whole file with no write; per-row messages (error / warning / conflict) with row numbers; counts of new, changed, unchanged, invalid rows.
4. **Conflicts:** an existing ItemNo whose values differ is a conflict. Per row the user chooses skip, update (field-level view of old and new), or cancel the import. Updating an item that is released, in progress or completed is **blocked** [DEFAULT; the Decision Log item "Excel conflict-resolution choices" lists it as open]. Nothing is overwritten silently.
5. **Commit:** one transaction; any failure rolls back everything; an import log row records the file hash, user, time, and the per-row outcome; large files are processed asynchronously with cancellation and progress.
6. **Idempotence:** importing the same file again produces no changes; unchanged rows are reported as such.
7. **Audit:** the audit log gets one entry for the import and one per created/updated item.

## 13. Migration strategy

1. Migrations only (EF Core migrations; pinned dotnet-ef 10.0.13); never edit a shipped migration; each schema PR adds its own migration.
2. **Dev/demo databases** are disposable: the demo is reset by the existing guarded `DemoDatabase.ResetAsync` and re-seeded with fictional data conforming to the new model.
3. **Any database that is not a marked demonstration database** is upgraded only by an explicit, logged migration step. Data that cannot be mapped automatically stops the migration with a report; nothing is silently dropped or reassigned. For #84: a request with items of both types is listed and the migration refuses until Planning splits it.
4. Each migrating PR tests (a) a fresh database from the first migration, (b) an upgrade from a database built at the previous release with representative data, (c) provider-neutral behaviour (no SQLite-only SQL), (d) the SQL Server pass required before the stage/QC milestone (#25).
5. Backups before upgrade are the operator's procedure (production backup procedure remains open in the Decision Log).

## 14. Concurrency

[EXISTING #23, #77] Per-row expected versions via `RowVersionExpectations` and `UpdateRowsAsync`. Rules for all new entities: every mutable row has a version; a command names the versions of exactly the rows it changes (and reads it relies on) and fails with a conflict if any is stale; append-only histories (responses, messages, QC lines, report events, price/rate rows) need no version; guarantees are per row, not aggregate; new operations write their audit entries in the same transaction. Hot spots to test: two users answering the same inquiry line; two QC dispositions on one stage; an amended job report against a cost snapshot; a rate change against a calculation.

## 15. Safe demo seed and reset

[EXISTING] Seeding and reset work only on a database carrying the positive demonstration marker (#59, #60); production databases are never touched. Each phase that changes the model extends the seeder with fictional data for the new entities (machines FCC-06 etc., operators, a shift calendar, an inquiry, a job report), keeps accounts fictional with no real passwords, and keeps the seed repeatable. The seeder, not the migration, creates demo data. The demonstration stays labelled as a simplified implementation until phase 9.

## 16. Change to existing documents

Annotated, not rewritten, with notes in the Decision Log: Master, Architecture and Domain/Database specifications, the Decision Log entries #2, #4, #34, #58, #75, #79, and this redesign's companions.
