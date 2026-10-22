# Workflow Redesign - Lifecycle, Authorization, Staging and Decision Register

| | |
|---|---|
| **Status** | **DESIGN ONLY.** Nothing in this document or its companion documents is implemented. No code, schema, migration or package is changed by the pull request that adds it. |
| **Basis** | The owner's business requirements of 2026-10-22 (recorded as requirements in Decision Log #82 and #83), the Master, Architecture and Domain/Database specifications, Decision Log #1-#81, and the working demonstration (PRs #18-#27). |
| **Companion documents** | `RAW_MATERIAL_INQUIRY_DESIGN.md`, `PLANNING_RESOURCES_AND_SCHEDULING_DESIGN.md`, `QC_QUANTITY_MODEL_DESIGN.md`, `COSTING_DESIGN.md`, `JOB_REPORTS_KPI_AND_OVERTIME_DESIGN.md`. |

## 0. How to read this document

Every statement carries one of these markers. Nothing marked PROPOSED or UNRESOLVED is a business rule yet.

| Marker | Meaning |
|---|---|
| **[REQ]** | A requirement the owner supplied on 2026-10-22. It is approved as a requirement (WHAT the system must do). Its detailed design is still a proposal unless stated otherwise. Decision Log #82, #83. |
| **[EXISTING]** | Already approved earlier (Decision Log number or specification section given). |
| **[DEMO]** | Approved for the demonstration only (Decision Log #58, #79-#81). Not a production rule. |
| **[PROPOSED]** | My recommendation. **Not approved.** It becomes a rule only after the owner approves it and a Decision Log entry records it. |
| **U-nn** | A genuinely unresolved decision that needs the owner (section 9). |

Nothing was invented as a silent default: where the requirements and the existing approved decisions disagree, the existing decision stays in force until the owner decides (section 2).

## 1. What the owner has required (approved as requirements)

| Id | Requirement (owner, 2026-10-22) | Design document |
|---|---|---|
| R1 | Drawing sends Warehouse a structured raw-material availability inquiry after the technical-feasibility determination; material lines (grade, diameter mm, length mm, other identifying attributes); Warehouse answers each line Yes/No with optional notes; everything recorded and traceable. Availability does not by itself approve feasibility or authorize production. | `RAW_MATERIAL_INQUIRY_DESIGN.md` |
| R2 | Planning registers operators, their machines and assignments; defines stable machine codes (for example FCC-06, TCC-12, HUB-01) and machine hourly rates; assigns each stage to its unit and machine; records each item's raw-material weight (kg), length and diameter (mm) and its quantity type. Rates and operator-machine assignments keep history. An assignment does not imply qualification. | `PLANNING_RESOURCES_AND_SCHEDULING_DESIGN.md` |
| R3 | QC maintains units of measure and quantity types (distinct concepts). QC results are recorded at individual-unit / sub-quantity level (a pair is not indivisible): accepted, rework, scrap, traceable to item, stage, decision and user. **Supersedes the whole-quantity-only QC limitation for the future production workflow.** | `QC_QUANTITY_MODEL_DESIGN.md` |
| R4 | Costing: per-unit total = machining cost of one item + raw-material cost of one item; total = per-unit total x produced quantity. Planning owns machine rates and stage specifications; Warehouse owns raw-material costs; Warehouse may not edit machine rates. Calculation inputs and the final approved price are auditable. | `COSTING_DESIGN.md` |
| R5 | Operators fill in daily job reports inside the application; Planning uses them for workforce and production KPIs. | `JOB_REPORTS_KPI_AND_OVERTIME_DESIGN.md` |
| R6 | Planning compares production units over a period X-Y and ranks operators by total valid reported working hours, with a date filter, deterministic tie-breaking and drill-down to the job reports. Rewards are decided outside the application; the application supplies auditable figures only. | same |
| R7 | Planning defines four shift templates (A 06:00-14:00, B 14:00-22:00, C 22:00-06:00 crossing midnight, D 08:00-17:00), marks a unit's shift working/OFF, sets staffing capacity, schedules workers against shifts/units/machines, tracks off-days; no hard-coded Monday-Friday week. | `PLANNING_RESOURCES_AND_SCHEDULING_DESIGN.md` |
| R8 | A unit supervisor selects operators for overtime and assigns them to a machine code, on an explicit overtime assignment record. An assignment does not prove the work was done. | `JOB_REPORTS_KPI_AND_OVERTIME_DESIGN.md` |
| R9 | The 12-step lifecycle of section 3. | this document |

## 2. Conflicts with existing approved decisions

These are the places where the new requirements and an already approved decision say different things. **The existing decision stays in force until the owner decides.** Each is also a decision in section 9.

| # | Requirement text | Existing approved text | Decision |
|---|---|---|---|
| X1 | Step 1: Central "selects exactly one type (MAKE or REPAIR)" for the request. | Decision Log #2, Master 1.1, Domain 2.3: WorkType is **per item**; one customer request may contain both. The approved Excel format is one mixed sheet with a Work Type column. | U-01 |
| X2 | Step 6: "For MAKE requests, Planning decides whether a contract is required." | Decision Log #4, Master 1.3: **every** customer request passes the contract determination after Drawing; the requirement applies to the whole request. | U-02 |
| X3 | Step 7: after the gates, **Planning** defines item details, stages, unit and machine. Inquiry is sent by Drawing after feasibility. | Decision Log #34, Master 5/6: **Drawing defines MAKE items**, Planning defines REPAIR items (and may edit any); MAKE flow is Drawing item definition, supply decision, Drawing finalization, Planning stage definition, contract gate, release. | U-03 |
| X4 | Warehouse answers each line **Yes/No**. | Master 6.4, 17.1, Domain 13.1: response AVAILABLE / PARTIAL / UNAVAILABLE with approved quantity. | U-09 |
| X5 | R2: Planning assigns each stage to the unit **and machine**. | Master 9: Planning assigns the unit; the **unit supervisor** assigns operator and machine. Domain 19.3 says a supervisor may assign the machine later. | U-12 |
| X6 | R3: QC dispositions per individual unit; quantity types distinct from units of measure. | Master 20, Domain 5.1/16: UOM with conversions (PAIR to PCS = 2) and Order/Tracking UOM on the item; QC partial results at stage-quantity level (accepted/rework/scrap). Demo: whole quantity only (#58, #79). | U-17..U-21; #83 supersedes the demo limitation |
| X7 | R4: one cost formula for the produced quantity. | Master 19.1: Repair = actual work sessions x rates; non-contract Make = raw material + machine cost; **Contract Make follows the contract route, with no duplicate contract-value calculation**. | U-29 |
| X8 | R6: any period X-Y; operator ranking by hours; unit comparison metrics. | Master 22.2: the 24-day (Jalali day 1-24) period; 22.3 and Decision Log #21: reward/ranking formula is Phase 2 and must not be invented. | U-33 (the 24-day period becomes one preset) |
| X9 | R7: four named shift templates. | Decision Log #13: shift patterns configurable (08:00-17:00 with 12:00-13:00 break; two- or three-shift examples). | None: A-D are configurable data; consistent. Break definitions U-34 |
| X10 | R8: supervisor-selected overtime. | Decision Log #13: Friday / extra hours need **supervisor approval** and must be recorded. | U-37 |
| X11 | Step 12: Warehouse "marks the item finished/sent". | Master 17.2: Warehouse **final receipt** is the physical completion; 19.4: cost record has `SentToWarehouse`. | U-38 |
| X12 | R5: operators fill reports "within the application". | Domain 14.4: `Operator.UserID` is optional. | U-16 |

Not in conflict (confirmed consistent): Management approves/rejects the request (#29, 15.2); Planning forwards to Drawing; Drawing returns the technical result; a rejected contract returns to Planning (#4); production units see their assigned stages in their own queues and later stages stay locked until QC approves the preceding one (#5, #78); QC owns UOM (#9); Warehouse owns material price per kg and Planning owns machine rate (#10); Warehouse inquiry does not deduct stock (#17).

## 3. End-to-end lifecycle (the owner's 12 steps, reconciled)

Notation: **Req** = request status, **Item** = item pipeline state, **Stage** = stage status (the existing vocabulary of Master 10 / `ItemStageStatus`).

| Step | Actor | Action | Result / status | Gate or rule | Basis |
|---|---|---|---|---|---|
| 1 | Central | Records request details, attaches the request PDF, submits to Management. The type (MAKE/REPAIR) question is U-01. | Req `CREATED` -> `SENT_TO_MANAGEMENT` | Header complete; PDF mandatory or optional is U-04 | [REQ] + [EXISTING] #29, #34 |
| 2 | Management (Manager role) | Approves or rejects (rejection reason mandatory). A rejected request can be reopened by Planning or Management with a reason. | Req `MGMT_APPROVED_AT_PLANNING` or `MGMT_REJECTED_AT_PLANNING` | Only the Manager role in Management | [EXISTING] #29, 15.2 |
| 3 | Planning | Reviews, forwards to Drawing. | Req `UNDER_DRAWING_REVIEW` | | [REQ] + [EXISTING] |
| 4 | Drawing | Determines technical feasibility; may request clarification; **may start a raw-material inquiry to Warehouse** while reviewing (section 4). | Req stays `UNDER_DRAWING_REVIEW`; the inquiry has its own state | Inquiry never decides feasibility | [REQ]; clarification route U-06 |
| 5 | Drawing | Returns the technical result to Planning (outcome: fully accepted, partially accepted, rejected, more information needed, ordinary return - Master 5.2). | Req `DRAWING_RETURNED_TO_PLANNING` | The result records the material-availability picture (pending / yes / no / not inquired) as information | [REQ] + [EXISTING]; open inquiry at this moment: U-06 |
| 6 | Planning, then Management | Planning decides whether a contract is required (scope: U-02). If required: creates and submits the contract + PDF; **Management approves or rejects**; a rejection returns to Planning for correction and resubmission. **Warehouse has read-only access** to the contract. | Contract status (Domain 11); Req statuses for the contract phase are PROPOSED (section 5) | A required contract that is absent or unapproved blocks production release | [REQ] + [EXISTING] #4 |
| 7 | Planning | Defines the item details: quantity type (from QC's list), dimensions (mm), raw-material weight (kg), the material, the production stages, and for each stage the production unit and machine. **Excel import is required** (preview, validation, transactional commit, manual conflict choice). | Item `DEFINED`; Stage 1 `PLANNED` | Gates of step 6 satisfied; who defines MAKE items when: U-03; unit vs machine authority: U-12 | [REQ]; import rules [EXISTING] #16 |
| 8 | Production units | See the stages assigned to them in their own queue. Later stages may be visible but cannot start before the preceding stage meets the QC gate. First stage released by Planning. | Stage `PLANNED` -> `WAITING` (released); later stages `WAITING_PREVIOUS_QC` | Queue order: #78 target (section 6) | [EXISTING] #5, #78; [DEMO] #79 |
| 9 | QC | Records results at the correct quantity granularity: accepted, rework, scrap, per individual unit / sub-quantity; the accepted quantity unlocks the next stage. | Stage `QC_APPROVED` for the accepted quantity; rework returns; scrap is permanent | `QC_QUANTITY_MODEL_DESIGN.md`; **supersedes the demo's whole-quantity-only approval** | [REQ] R3, #83; [EXISTING] #5-#7 |
| 10 | Warehouse | After the final stage's QC, calculates the item cost from approved machine time, machine rates and raw-material cost. | Item `COST_PROPOSED` | `COSTING_DESIGN.md`; trigger and invalidation: U-39 | [REQ] |
| 11 | Management | Approves or changes the proposed price; the decision, the author, the time and the reason are recorded. | Item `PRICE_APPROVED` | Only Management; adjustments keep the original proposal | [REQ] + [EXISTING] Master 19.6 |
| 12 | Warehouse | Marks the item finished/sent **only after the price approval**. | Item `FINISHED_SENT` | Meaning of "finished/sent" vs final receipt: U-38 | [REQ]; U-38 |

### 3.1 State machines

**Request (existing set of eight [EXISTING] #29, plus PROPOSED statuses for later phases):**

```text
CREATED --submit(Central)--> SENT_TO_MANAGEMENT
SENT_TO_MANAGEMENT --approve(Manager)--> MGMT_APPROVED_AT_PLANNING
SENT_TO_MANAGEMENT --reject(Manager, reason)--> MGMT_REJECTED_AT_PLANNING
MGMT_REJECTED_AT_PLANNING --reopen(Planning|Management, reason)--> SENT_TO_MANAGEMENT
MGMT_APPROVED_AT_PLANNING --forward(Planning)--> UNDER_DRAWING_REVIEW
UNDER_DRAWING_REVIEW --return result(Drawing)--> DRAWING_RETURNED_TO_PLANNING
DRAWING_RETURNED_TO_PLANNING --send back(Planning)--> UNDER_DRAWING_REVIEW
DRAWING_RETURNED_TO_PLANNING --contract decision(Planning)--> [PROPOSED] below
```

Statuses and transitions after `DRAWING_RETURNED_TO_PLANNING` are **PROPOSED** (nothing after it is approved except the contract rules of #4):

| Proposed request status | Entered when | Left when |
|---|---|---|
| `CONTRACT_PENDING_MANAGEMENT` | Planning submitted a required contract | Management approves -> `READY_FOR_DEFINITION`; rejects -> `CONTRACT_REJECTED_AT_PLANNING` |
| `CONTRACT_REJECTED_AT_PLANNING` | Management rejected the contract | Planning resubmits -> `CONTRACT_PENDING_MANAGEMENT` |
| `READY_FOR_DEFINITION` | Drawing result accepted (fully or partly) and the contract gate is satisfied or not required | Planning defines items and releases -> `IN_PRODUCTION` |
| `IN_PRODUCTION` | The first item is released | All items finished/sent or closed -> `CLOSED` |
| `CANCELLED`, `CLOSED` | existing | existing |

Request-level progress stays derived from item states (counts by state); a headline percentage is still unapproved (Architecture 39).

**Item pipeline [PROPOSED]** (distinct from the stage statuses and from the request status):

```text
DEFINED --release first stage--> RELEASED --first stage starts--> IN_PRODUCTION
IN_PRODUCTION --last stage QC-approved for all eligible quantity--> PRODUCTION_COMPLETED
PRODUCTION_COMPLETED --Warehouse calculates--> COST_PROPOSED
COST_PROPOSED --Management approves/changes--> PRICE_APPROVED
PRICE_APPROVED --Warehouse marks--> FINISHED_SENT
(any time) QC correction --> item returns to Stage 1; COST_PROPOSED / PRICE_APPROVED / FINISHED_SENT stop being "current truth" (Master 15.1, Domain 12.5) and the history stays.
```

**Stage:** the existing vocabulary (Master 10) is unchanged. What changes is that a stage's quantity is tracked in buckets (`QC_QUANTITY_MODEL_DESIGN.md` section 5); the stage status is a summary of them.

**Raw-material inquiry:** see `RAW_MATERIAL_INQUIRY_DESIGN.md` section 4.

## 4. Where the inquiry sits in the lifecycle

Drawing may open an inquiry once it has a technical view of the request (step 4). The inquiry is an informational sub-process; it does not move the request between statuses (U-05 recommends no new request status for it). Warehouse answers; Drawing reads the answer and may start a revised inquiry round. **Material availability alone neither approves feasibility nor authorizes production**: the Drawing technical outcome and the production-release gates remain separate decisions (`RAW_MATERIAL_INQUIRY_DESIGN.md` section 6 lists the routing options for "unavailable").

## 5. Gates before production release [PROPOSED - U-05]

Production release of an item requires all of the following. **Only the contract gate is approved today** ([EXISTING] #4). The rest is proposed.

| Gate | Approved? |
|---|---|
| Request approved by Management (`MGMT_APPROVED_AT_PLANNING` reached) | proposed |
| Drawing outcome fully or partially accepted for that item | proposed |
| Contract approved when required | **[EXISTING] #4** |
| Item fully defined: quantity type, dimensions, raw weight, material, stages with unit (and machine per U-12) | proposed |
| Material inquiry lines resolved (answered; "No" lines dispositioned by Drawing/Planning) | proposed (U-10) |

The demonstration checks none of these (Decision Log #80, #81).

## 6. Production-unit queues (relation to the approved #78 target)

The overview built so far is item-level and read-only. The authoritative per-unit queue is approved as a target but not built [EXISTING] #78: released stages of the unit; category IN_PROGRESS, then STOPPED/problem, then WAITING; then Planning priority, then Planning position (first come first served on ties), then planned end, then stable identifiers; Management overrides stay active until cleared and a later Planning priority change flags them for review. This redesign adds the machine to the stage assignment (U-12) and the operational categories remain as approved. The details still open under "Queue ordering" in the Decision Log are unchanged.

## 7. Authorization matrix by department and action

Legend: **Y** = may perform; **R** = read-only; blank = denied by default. Basis: **REQ** owner requirement; **EX** already approved (section 15.2 / 15.4 / Decision Log); **DEMO** demo-only #80; **PROP** proposed and **not approved** (U-40). Roles: only Management's initial request decision requires the Manager role (#30); all other rows are department-based. "Supervisor" is a Production-department user designated for a unit (U-16); until U-16 is decided no supervisor permission can be implemented.

| Action | Cen | Mgmt | Plan | Draw | Whse | Prod op | Prod sup | QC | PM | Basis |
|---|---|---|---|---|---|---|---|---|---|---|
| Create request, submit to Management | Y | | | | | | | | | EX |
| Approve / reject request (Manager role) | | Y | | | | | | | | EX |
| Reopen a rejected request | | Y | Y | | | | | | | EX |
| Forward request to Drawing / send back | | | Y | | | | | | | EX |
| Record technical result; request clarification | | | | Y | | | | | | EX / REQ |
| Create / send raw-material inquiry | | | R | Y | R | | | | | REQ (R for Plan, Whse: PROP) |
| Answer inquiry lines (Yes/No + notes); ask clarification | | | | R | Y | | | | | REQ |
| Decide contract required; create / submit contract | | | Y | | R | | | | | EX; Whse read-only REQ |
| Approve / reject contract | | Y | | | R | | | | | EX |
| Define / edit items (quantity type, dimensions, raw weight, stages) | | | Y | (Y for MAKE items: U-03) | | | | | | REQ / EX (#34) |
| Excel import of items | | | Y | Y | | | | | | EX (#34); step 7 says Planning (U-03) |
| Register operators; machine assignments; qualifications | | | Y | | | | | | | REQ; qualification PROP (U-13) |
| Define machine codes; machine hourly rates | | Y (rates) | Y | | | | | | | REQ; rates EX (15.2) |
| Assign a stage to unit and machine | | | Y | | | | (substitution: U-12) | | | REQ; U-12 |
| Maintain material price per kg | | Y | | | Y | | | | | EX |
| Maintain units of measure and quantity types | | | | | | | | Y | | REQ / EX |
| Define shift templates, unit shifts, OFF days, capacity, schedule workers | | | Y | | | | | | | REQ |
| Assign operator and machine day-to-day | | | | | | | Y | | | EX (Master 9) |
| Overtime assignment (create) | | | | | | | Y | | | REQ |
| Overtime allowance / approval | | | Y | | | | | | | PROP (U-37) |
| Release first stage; start; submit for QC | | | Y (release) | | | Y (start, submit) | Y | | | DEMO #80 |
| QC disposition (accepted / rework / scrap); QC correction | | | | | | | | Y | | EX / REQ |
| Create and submit own daily job report | | | | | | Y | Y | | | REQ |
| Review / approve / return job reports of the unit | | | R | | | | Y | | | PROP (U-31) |
| View KPIs and job reports for any period | | R (PROP) | Y | | | | R (own unit: PROP) | | | REQ (Planning); others PROP |
| Report machine downtime | | | R | | | | | | Y | EX |
| Calculate item cost (propose) | | | R | | Y | | | | | REQ |
| Approve / change price | | Y | | | | | | | | REQ + EX |
| Mark item finished / sent | | | | | Y | | | | | REQ (U-38) |
| Administer users and roles | Admin only | | | | | | | | | EX |

Planning and Management stay unable to edit raw-material costs; Warehouse stays unable to edit machine rates (REQ). Nothing here widens a permission that the approved matrix denies; every PROP row is denied until approved.

## 8. Data flow (summary)

```text
Request --(Drawing)--> RawMaterialInquiry(lines) <--(Warehouse answers)
Request --(Planning)--> RequestItem(spec: QuantityType, mm, kg, Material) --> ItemStage(unit, machine)
QC: UnitOfMeasure, QuantityType (masters)             Planning: Operator, Machine(code), RateHistory, Shift*, Qualification, Assignment
Production: DailyJobReport(lines) --(supervisor approval)--> approved lines
QC: QcInspection(lines: ACCEPTED/REWORK/SCRAP per base unit) --> StageQuantityBalance --> next stage eligible quantity
Approved lines x MachineRate(work date) + Weight x MaterialPrice(date) --> ItemCostCalculation(snapshot) --> Management price --> Warehouse FINISHED_SENT
Approved lines --> KPI figures (Planning), with drill-down to the reports
```

The formulas and rounding are in `COSTING_DESIGN.md`; the entities in the companion documents.

## 9. Decision register

All decisions below are **UNRESOLVED and not approved**. The recommendation is mine; the owner decides. "Doc" names the companion document with the options and consequences.

| Id | Decision needed | Recommendation (not approved) | Doc |
|---|---|---|---|
| U-01 | Request type: exactly one per request (step 1) or per item (approved #2)? | Keep per item; treat step 1 as an intake hint at most. | this, 2 |
| U-02 | Contract determination: every request (approved #4), MAKE-only, or requests with at least one MAKE item? | Requests with at least one MAKE item; pure-repair requests skip the check. (Changes an approved rule; needs approval.) | this, 2 |
| U-03 | Who defines items and when (Drawing MAKE definition vs Planning at step 7); who imports Excel? | Two phases: Drawing creates MAKE item identity and material needs during review; Planning completes production definition after the gates. | this, 2 |
| U-04 | Is the request PDF mandatory before submission? | Mandatory for submission (step 1 says "attaches the PDF"). | this |
| U-05 | Request statuses after `DRAWING_RETURNED_TO_PLANNING`, production-release gates, and whether the inquiry gets a request status. | Statuses of section 3.1; gates of section 5; no request status for the inquiry. | this |
| U-06 | Clarification route (who answers Drawing), and may Drawing return a result while a clarification or inquiry is open? | Clarification goes to Planning (existing "more information needed" outcome); a result may be returned with an open inquiry, flagged. | RMI |
| U-07 | Inquiry anchor (request or item) and round/revision model. | Request-level inquiry, lines optionally linked to an item, revisions as new rounds. | RMI |
| U-08 | Material attribute set; material master ownership; free text allowed? | Grade, form/section, diameter mm, length mm, quantity, unit, standard, condition/remarks; Warehouse owns the material list; free text allowed but flagged. | RMI |
| U-09 | Yes/No only, or also a quantity? | Yes/No per line plus optional notes; each line carries the required quantity. | RMI |
| U-10 | What happens when a line is "No", and what returns to Drawing? | All lines with notes return to Drawing, who decides: revise and re-inquire, record an alternative, or proceed with external supply via Planning's supply decision. | RMI |
| U-11 | Who may cancel / close an inquiry; who sees it. | Drawing cancels / closes; Planning and Warehouse read. | RMI |
| U-12 | Machine assignment authority (Planning vs supervisor) and substitution. | Planning assigns unit and planned machine; the supervisor may substitute within the unit with a reason; costing uses the actual machine. | PLAN |
| U-13 | Operator-machine qualification and reassignment rules. | Separate qualification record; assignment requires qualification unless Planning overrides with a reason. | PLAN |
| U-14 | Machine code standard and immutability. | Unique, immutable once used, retired not deleted. | PLAN |
| U-15 | Item specification fields: weight per unit, dimensions, mandatory by work type, scale. | Weight kg per costing unit; length and diameter mm; mandatory for MAKE, optional for REPAIR. | PLAN |
| U-16 | Do all operators have logins? How is a unit supervisor identified? | Operator = user in the Production department; supervisor = Production user designated per unit. | PLAN |
| U-17 | UOM vs quantity type: relationship, base unit count, what a "set" is. | Quantity type with an integer base-unit count (piece 1, pair 2, set n); QC owns both. | QCQ |
| U-18 | Representation of individually tracked pieces. | Quantity-level counts per disposition; optional unit slot only if QC must say which member; no serials or lots. | QCQ |
| U-19 | Reconciliation, shortfall / replacement of scrap, over-production. | Balance invariants of QCQ section 6; shortfall handling decided separately. | QCQ |
| U-20 | QC correction at quantity granularity (the whole item to Stage 1, or only the affected quantity). | Keep the approved rule; scope to the affected quantity needs approval. | QCQ |
| U-21 | Costing unit (pair or piece) and partial pairs. | Cost per base piece, shown per ordered unit. | QCQ, COST |
| U-22 | Chargeable time: approved actual reports or planned estimates. | Approved actual reports; the plan only for labelled estimates. | COST |
| U-23 | Time recording, rounding, conversion. | Integer minutes, hours = minutes / 60, rounding only on money. | COST |
| U-24 | Setup, overtime, downtime, rework and scrap in cost; double-counting. | See COST section 6. | COST |
| U-25 | Which rate and price apply if they change. | Rate on the work date of each report line; material price on the item's release date. | COST |
| U-26 | Weight vs consumption reconciliation; additional material costs. | Weight x price only; extra costs only as explicit approved adjustment lines. | COST |
| U-27 | Quantity basis (accepted or produced) and authoritative total vs per-unit rounding. | Total cost is authoritative; per-unit is derived. | COST |
| U-28 | Rounding precision and who approves adjustments. | Whole Rial; adjustments by Management only, with a reason. | COST |
| U-29 | Do Contract-Make and Repair follow the universal formula? | Repair and non-contract Make: yes; Contract Make: contract amount stays the price, with an informational internal cost. | COST |
| U-30 | Minimal job-report format and mandatory fields. | JOB section 3. | JOB |
| U-31 | Report authority, amendments, late window, duplicates, overlaps. | JOB section 5. | JOB |
| U-32 | Definition of "valid" reported hours. | JOB section 4. | JOB |
| U-33 | KPI default set, authoritative rankings, tie-break, period semantics. | JOB section 7. | JOB |
| U-34 | Shift storage, midnight attribution, break definitions for A-C. | Templates as data; attribute a shift to its start date. | PLAN |
| U-35 | Overlap of shift D with A and B. | PLAN section 5. | PLAN |
| U-36 | Off-day conflict handling (block or flag) and the capacity semantics. | Block new, flag existing; capacity = maximum headcount. | PLAN |
| U-37 | Overtime authority, limits, approval; relation to the Friday rule (#13). | JOB section 8. | JOB |
| U-38 | Meaning of "finished/sent" vs final warehouse receipt vs customer dispatch. | Needs the owner's definition before design. | this |
| U-39 | Who triggers cost calculation and when; invalidation on QC correction. | Warehouse triggers after the last stage's QC; correction invalidates as in Domain 12.5. | COST |
| U-40 | Approval of the PROP rows of the authorization matrix (section 7). | Approve row by row. | this |

Doc codes: RMI = RAW_MATERIAL_INQUIRY_DESIGN, PLAN = PLANNING_RESOURCES_AND_SCHEDULING_DESIGN, QCQ = QC_QUANTITY_MODEL_DESIGN, COST = COSTING_DESIGN, JOB = JOB_REPORTS_KPI_AND_OVERTIME_DESIGN.

## 10. The working demonstration: a known simplified implementation

The demonstration (PRs #18-#27) is kept as it is. It is **not** the target process. The gaps, so nobody mistakes it for production behaviour:

| Area | Demonstration | Intended process |
|---|---|---|
| Request lifecycle | Central creates, submits; no Management decision, forwarding, Drawing review, contract or gates | Section 3 |
| Items and stages | Fixed seeded routes; items entered by hand have no stages; no unit or machine on stages | Planning defines stages with unit and machine; Excel import |
| Stage actions | Release (Planning), start / submit (Production), approve (QC) with **no** request-status, contract or Management gate [DEMO #80, #81] | Gates of section 5 |
| QC | Whole quantity only; no rejection, rework, scrap | Per-unit accepted / rework / scrap (R3, #83) |
| Units / machines / operators | None (no machine or operator in the model) | R2 masters with history |
| Queue | Item-level read-only overview, provisional order [DEMO #78] | Per-unit queue, Planning position, overrides |
| Costing / price | None | R4 |
| Job reports / KPI / shifts / overtime | None | R5-R8 |
| Material inquiry | None | R1 |
| Quantities | A single decimal on the item; no quantity type | R3 |

## 11. Staged implementation plan (small reviewable PRs)

Each stage is its own PR, starts only after the owner approves the decisions it needs, keeps the demo working until it is replaced, and carries its own tests, Decision Log entry and checklist section. Nothing below starts without authorization. **SQL Server test pass before the stage/QC milestone is already required (Decision Log #25).**

| # | PR | Needs (decisions) | Depends on |
|---|---|---|---|
| 0 | This design PR | - | - |
| 1 | QC masters: units of measure, conversions, quantity types | U-17 | 0 |
| 2 | Planning masters: machines with stable codes, machine rate history; Warehouse material master and price history (effective-dated) | U-14, U-08 (material list owner) | 0 |
| 3 | Planning: operators, operator accounts, qualifications, effective-dated assignments | U-13, U-16 | 2 |
| 4 | Request lifecycle completion: Management decision, forward to Drawing, Drawing outcome, request workflow history, status table | U-01, U-04, U-05, U-06 | 0 |
| 5 | Raw-material inquiry (header, lines, responses, clarification history, authorization) | U-07..U-11, U-09 | 2 (material master), 4 |
| 6 | Contract gate (contract entity, Planning submit, Management decide, Warehouse read-only) | U-02 | 4 |
| 7 | Item definition and stage planning: item specification fields, stage to unit/machine assignment with history, release gates | U-03, U-12, U-15, U-21 | 1, 2, 4, 6 |
| 8 | Excel import (template, preview, validation, transactional commit, conflict choices) | #16 + conflict options | 7 |
| 9 | Planning calendar: shift templates A-D, unit shifts, OFF days, capacity, worker scheduling | U-34..U-36 | 3 |
| 10 | Per-unit production queue (released stages, Planning position, categories, overrides with review flag) | #78 details | 7, 9 |
| 11 | Daily job reports and supervisor review | U-30..U-32 | 3, 7, 9 |
| 12 | Quantity-level QC (inspection lines, balances, rework loop, next-stage eligibility, correction scope) replacing the demo whole-quantity approval | U-18..U-20 | 1, 7, 10 |
| 13 | Overtime assignments | U-37 | 9, 11 |
| 14 | Costing: calculation, snapshot, Management approval, Warehouse finished/sent | U-22..U-29, U-38, U-39 | 2, 11, 12 |
| 15 | KPI reports (date range, unit comparisons, operator ranking, drill-down) | U-33 | 11, 12 |
| 16 | Retire the demonstration stage flow and its seeded shortcuts | - | 12 |

Sizing rule: no PR crosses two of the groups masters / lifecycle / production / costing / reporting.

## 12. Change to existing documents

This PR annotates (does not rewrite) the three authoritative specifications and the Decision Log; the passages that the new requirements supersede or extend keep their text and gain a pointer (Decision Log #28).
