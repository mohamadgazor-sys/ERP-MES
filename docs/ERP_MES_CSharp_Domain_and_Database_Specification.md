# ERP / MES — C# Domain and Database Specification

**Document status:** Draft for business/domain review

**Authority:**
- `ERP_MES_CSharp_Master_Specification_Current_State.md` — business authority
- `ERP_MES_CSharp_Architecture_Specification.md` — technical architecture authority
- This document translates those two documents into a canonical domain model and relational database design.

**Critical rule:** This document does not silently invent business rules. Items marked **[DECISION REQUIRED]** must be confirmed before their corresponding implementation is frozen.

---

# 1. Purpose

This document defines the canonical C# domain model and relational database model for the ERP/MES application.

The target business model is request-based:

```text
Customer Request
    ↓
Request Items
    ↓
Item-specific WorkType (MAKE / REPAIR)
    ↓
Item-specific Production Stages
    ↓
Production / External Execution
    ↓
QC after each applicable stage
    ↓
Next stage
    ↓
Costing / Management approval where applicable
    ↓
Warehouse receipt
```

Projects are not mandatory and are not part of the canonical production flow.

Internal company requests are outside C# v1.

---

# 2. Domain Modeling Principles

## 2.1 Request is the customer tracking container

A Request represents the customer's incoming request and remains the primary unit for:

- customer follow-up;
- request-level workflow;
- contract determination;
- contract approval;
- high-level progress monitoring;
- request history.

A request may contain many items.

## 2.2 Request Item is the production-tracking identity

A Request Item represents one distinguishable item within the request.

Two items that are physically or descriptively similar must still be distinguishable by their Request Item identity and item number.

Example:

```text
Request #11 — Customer X

Item 1.10 — Ring A — MAKE
Item 1.20 — Ring B — MAKE
```

The Request No. and Item No. must remain visible in production queues and history.

## 2.3 Work type exists at item level

> **Superseded/clarified (2026-10-22, Decision Log #84):** WorkType is a property of the request; items inherit it. The text below is kept as history.



`WorkType` / `WorkTypeID` is authoritative and supports:

- `MAKE`
- `REPAIR`

One customer request may contain both.

`WorkTypeID` is not interchangeable with `RequestTypeID`.

## 2.4 Stage is the canonical production route

Every executable item has an ordered set of stages.

Stage definitions are item-specific and may differ between items in the same request.

## 2.5 History is append-oriented

Operational events that matter for audit and reporting should be stored as transactions/history records rather than repeatedly overwriting one row.

Examples:

- request workflow actions;
- stage work sessions;
- QC inspections;
- QC corrections;
- external dispatch/return;
- cost approval;
- warehouse receipt;
- queue overrides.

Current-state fields may exist for fast access, but the historical transaction remains the audit source for events.

---

# 3. Canonical Entity Inventory

The core domain entities are:

| Entity | Purpose |
|---|---|
| User | Application identity and authentication subject |
| Role | Authorization role |
| Department | Department ownership/access context |
| Customer | Customer/buyer master |
| Request | Customer request header and request-level state |
| RequestWorkflowEntry | Request-level movement/decision history |
| RequestItem | Item within a request |
| RepairItemDetails | Repair-specific item information |
| ItemStage | Planned route/stage for a request item |
| ItemStageWorkSession | Actual work entry/production session |
| QCInspection | Stage-level QC transaction/history |
| QCRejectionReason | Controlled QC/rework reason |
| ExternalWorkDispatch | External stage send/return tracking |
| Contract | Request-level contract and approval |
| ContractNotification | Dashboard-visible contract notifications/acknowledgement state if persisted |
| CostCalculation | Item-level costing result and management approval |
| CostCalculationDetail | Future detailed cost lines by work session/operation |
| WarehouseInquiry | Request/item inquiry to warehouse about availability |
| WarehouseTransaction | Final receipt and later warehouse movement history |
| ProductionUnit | Internal/external execution route |
| ProductionUnitCategory | Classification of production units |
| Machine | Machine master |
| Operator | Operator master |
| UnitOfMeasure | QC-owned unit master |
| UnitOfMeasureConversion | Configurable conversion between units |
| Material | Material/raw-material master |
| MachineHourlyRate | Planning-owned historical machine rate |
| MaterialPriceRate | Warehouse-owned historical raw-material price/kg |
| StopReason | Downtime/stop reason |
| ShiftType | Shift definition |
| ExcelImportBatch | Audit record for high-volume import operation |
| ExcelImportRowError | Per-row import validation error |
| Document | PDF/document content stored in database |
| SchedulingSnapshot / QueueOverride | Queue and planning override history as required by implementation |

Some entities above are direct requirements and some are structured technical representations of already-confirmed behavior. Any entity that would introduce new business behavior must remain subject to confirmation.

---

# 4. Request Domain

## 4.1 Request fields

The canonical Request should include at minimum:

| Field | Meaning |
|---|---|
| RequestID | Primary key |
| RequestNo | Customer-supplied request number (free-form text; not globally unique — see §4.2) |
| RequestDate | Request date (the customer's request date; source of `JalaliRequestYear`) |
| JalaliRequestYear | Jalali year derived from `RequestDate`, persisted, part of the business key (§4.2; Decision Log #31) |
| CustomerID | Customer reference |
| RequestTitle | Short title |
| RequestDescription | Main description |
| CurrentDepartmentID | Current responsible department |
| CurrentStatusID | Current request status |
| ContractRequired | Request-level contract gate |
| CreatedByUserID | Creator |
| CreatedAt | Creation timestamp |
| UpdatedAt | Last update timestamp |
| Notes | General notes |

> **[Decision Log #31]** `JalaliRequestYear` was added to the table above. `ContractRequired` is not created in Milestone 2; its representation (including an "undetermined" state) is decided with the contract milestone (audit U10, Decision Log Unresolved).

Request-level emergency fields are excluded from the canonical C# model.

ProjectID is not required by the C# production model.

## 4.2 Request number

`RequestNo` is the **customer/order number supplied by the customer**, not a number generated by the ERP/MES.

The field must therefore be stored as **Short Text / string** and must accept free-form values. Typical values are numeric customer order numbers from one to four digits, but alphanumeric values are also valid, for example:

```text
1
27
846
1234
P-565
wsj-898
```

The same `RequestNo` may legitimately occur for different customers. Therefore `RequestNo` must **not** be globally unique by itself. The internal database identity remains `RequestID`.

The customer controls the numbering sequence. Their sequence normally increases during the Jalali year and resets for the next Jalali year. The application must **store and display the customer's supplied number exactly as received**; it must not generate or renumber customer RequestNo values.

For request lookup and business identification, the normal business combination is **Customer + RequestNo + Jalali Request Year**.

The same customer will not issue two distinct requests with the exact same `RequestNo` within the same Jalali year. Therefore the database/domain model should enforce the business-key uniqueness of **Customer + RequestNo + Jalali Request Year**, while retaining `RequestID` as the immutable internal primary key. A new customer order/request receives a new customer-supplied RequestNo even when it concerns the same physical item or repair case. The RequestNo comparison for this uniqueness rule is **case-sensitive**: `wsj-898` and `WSJ-898` are treated as different RequestNo values for the same customer and Jalali year.

**Decided rules (Decision Log #31):**

- `JalaliRequestYear` is derived from the customer's `RequestDate`, fixed at creation, and changeable only through an audited correction that re-checks uniqueness.
- Leading or trailing whitespace in `RequestNo` is rejected with a validation error; it is never auto-trimmed.
- No letter or digit normalisation is applied to `RequestNo`; it is stored and compared as exact text.
- `RequestNo` uses a case-sensitive binary collation on both SQLite and SQL Server, verified by a test on both providers.
- A non-blocking warning is shown when a case-insensitive near-duplicate exists for the same customer and Jalali year.

## 4.3 Request status

`CurrentStatusID` points to a controlled RequestStatus record.

The Request row holds the current state for fast retrieval. Every state-changing action also creates a `RequestWorkflowEntry`.

**Initial status set (Decision Log #29):** `CREATED`, `SENT_TO_MANAGEMENT`, `MGMT_APPROVED_AT_PLANNING`, `MGMT_REJECTED_AT_PLANNING`, `UNDER_DRAWING_REVIEW`, `DRAWING_RETURNED_TO_PLANNING`, `CANCELLED`, `CLOSED`. Management rejection requires a reason. A Management-rejected request can be reopened for a new Management decision by the Planning or Management department, with a reason and an audit entry. Later statuses (contract, production, completion) are added with their milestones. The transition table is a draft that is **not approved**; its review is deferred until before the request-workflow milestone: `docs/REQUEST_STATUS_TRANSITIONS_DRAFT.md`.

## 4.4 Request workflow history

`RequestWorkflowEntry` should contain at minimum:

| Field | Meaning |
|---|---|
| RequestWorkflowEntryID | Primary key |
| RequestID | Request reference |
| FromDepartmentID | Previous responsible department |
| ToDepartmentID | New responsible department |
| ActionTypeCode | Structured action |
| ActionResultCode | Structured result |
| NewStatusID | State after action |
| ActionNote | Optional note |
| ActionByUserID | Actor |
| ActionAt | Timestamp |
| IsCurrent | Fast current-row marker if retained |
| NeedManagerApproval | Whether action required manager approval |
| ManagerApproved | Approval result if applicable |
| ManagerApprovedByUserID | Approver |
| ManagerApprovalAt | Approval timestamp |

`RequestWorkflowEntry` is request-level only. It must not replace item-stage execution history.

---

## 4.5 Customer (Decision Log #32)

| Field | Meaning |
|---|---|
| CustomerID | Primary key |
| Name | Customer name (Persian) |
| IsActive | Active flag |
| CreatedByUserID, CreatedAt, UpdatedAt | Audit fields |

No customer code and no contact fields for now; they are added when a requirement exists. "Customer" is the canonical term ("Buyer" is legacy). There is no item-level customer; an item's customer is its request's customer.

---

# 5. Request Item Domain

## 5.1 RequestItem fields

> **Design annotation (2026-10-22, Decision Log #82/#83; PROPOSAL, not approved unless stated):** Proposed additions (not approved): QuantityTypeId, ordered quantity in base units, raw-material weight kg, length mm, diameter mm, material reference. See `PLANNING_RESOURCES_AND_SCHEDULING_DESIGN.md` section 4 and `QC_QUANTITY_MODEL_DESIGN.md`.


The canonical RequestItem should include at minimum:

| Field | Meaning |
|---|---|
| RequestItemID | Primary key |
| RequestID | Parent request |
| ItemNo | Customer/request item identifier; Short Text |
| PartName | Part/item name |
| WorkTypeID | MAKE or REPAIR |
| RequestedQty | Requested quantity |
| OrderUOMID | Unit in which the request/order is expressed |
| TrackingUOMID | Unit in which production tracking is performed |
| QtyConversionFactor | Derived/snapshot conversion where needed |
| (drawing document) | Linked through a document link table (§20); no single document foreign key (Decision Log #35) |
| MaterialID | Material reference for Make items where applicable |
| Dimensions | Dimension text/structured data as currently required |
| Description | General item description |
| SupplyTypeID | Supply route classification |
| FinalMaterialName | Current Make-field requirement where applicable |
| RawMaterialWeightKg | Weight used for applicable costing |
| MaterialPriceRateID | Material pricing source where applicable |
| SupplyRequestNo | External supply identifier where applicable |
| SupplyRequestDate | External supply request date where applicable |
| NeedsIncomingQC | Whether incoming external material must pass QC |
| Priority | Planning-controlled priority |
| PlannedStartDateTime | Calculated/planned start |
| PlannedEndDateTime | Calculated/planned end |
| CreatedByUserID | Creator |
| CreatedAt | Creation timestamp |
| UpdatedAt | Last update timestamp |

> **[Decision Log #58]** The Planning `Priority` belongs to the `RequestItem` (this row). Every stage and queue entry of the item inherits it; no separate, independent stage-priority field exists. New items default to `NORMAL`.

**Quantities (Decision Log #33):** `RequestedQty`, UOM conversion factors/results and planned minutes per piece are decimals with a fixed scale, set in the Milestone 2 design (proposal: 4 decimal places).

> **[Decision Log #35]** Document references on this entity are link-table associations, not single foreign keys; see §20.

## 5.2 Item number uniqueness

> **[Note added by #121]** The displayed item number is now system-derived (`RequestNo-NNN`, child numbers `...-NNN.NNN`) from a stored integer sequence, unique within the request; the customer's own number is the optional customer item reference. Parent/assembly structure is added. The earlier text is kept as history.

The existing design treats `ItemNo` as Short Text and evaluates duplicates in the scope of the current RequestID during Excel import.

The canonical C# database should therefore enforce, or at minimum validate, uniqueness of:

```text
(RequestID, ItemNo)
```

This supports values such as:

```text
1.10
1.20
A-01
RING-001
```

without numeric truncation or loss of leading characters.

## 5.3 RepairItemDetails

Repair-specific information must remain separate from the general RequestItem entity.

Recommended canonical fields:

| Field | Meaning |
|---|---|
| RepairItemDetailID | Primary key |
| RequestItemID | One-to-one RequestItem reference |
| RepairItemCode | Customer/repair code |
| SerialNo | Customer serial number |
| ExitNo | Exit/reference number |
| ReceivedDate | Physical receipt date |
| RepairWorkDescription | Repair description |
| RepairRequiredDate | Required date |
| RepairRequiredNote | Required-date note |
| IsEmergency | Excluded from canonical model because Emergency was removed |
| HasReferenceSample | Reference-sample flag |
| DrawingRequired | Drawing requirement |
| DrawingProvided | Drawing availability |
| (drawing document) | Linked through a document link table (§20); no single document foreign key (Decision Log #35) |
| CurrentRepairItemStatusID | Current repair-item status |
| CreatedByUserID | Creator |
| CreatedAt | Creation timestamp |
| UpdatedAt | Last update |

**Correction:** `IsEmergency` must be omitted from the actual C# entity because Emergency was explicitly removed from scope. It is retained in this table only to identify the legacy source field that must not be carried forward.

> **[Decision Log #35]** Document references on this entity are link-table associations, not single foreign keys; see §20.

---

# 6. Item Stage Domain

## 6.1 ItemStage identity and fields

Each stage belongs to exactly one RequestItem.

Minimum fields:

| Field | Meaning |
|---|---|
| ItemStageID | Primary key |
| RequestItemID | Parent item |
| StageNo | Ordered stage number |
| StageTitle | Operation/work title |
| ProductionUnitID | Execution route/unit |
| Primary/normal routing information | Where the stage is normally performed |
| IsExternal | External execution/supply route indicator or derived classification |
| StageStatusID | Current stage state |
| PlannedMinutesPerPiece | Planning input |
| PlannedQuantity | Quantity used for planning |
| PlannedTotalMinutes | Calculated snapshot: PlannedMinutesPerPiece × PlannedQuantity |
| ExpectedExternalLeadTimeMinutes | Expected lead time for external stage where applicable |
| PlannedStartDateTime | Calculated schedule start |
| PlannedEndDateTime | Calculated schedule end |
| StageNotes | Planning/technical notes |
| CreatedByUserID | Creator |
| CreatedAt | Creation timestamp |
| UpdatedAt | Last update |

Actual execution fields such as ActualStart and ActualEnd should not overwrite Planning estimates.

## 6.2 Stage ordering and controlled route revision

Stage sequence is item-specific. Planning may insert a new stage before or after an existing stage when the production route must change, including after production has started, but it must use a controlled revision operation rather than silently rewriting history.

Required behavior:

- Keep the original stage records and execution/QC history auditable.
- Record the route revision, the stage inserted, its position relative to the existing stages, reason, actor, timestamp, and relevant before/after sequence.
- Do not silently delete or rewrite work sessions, QC inspections, external dispatches, costing records, or warehouse transactions.
- A stage revision must be performed by an authorized Planning user and must be visible in item history.
- Existing started/completed stages retain their historical execution facts.
- The precise handling of already-created downstream stages after inserting a new stage (for example whether any are marked for Planning review, paused, or kept as-is) remains a blocking subdecision. Claude Code must not invent an automatic cascade/reset rule.

Example route:

```text
Before: 1 Turning → 2 Grinding → 3 Gear Cutting
After:  1 Turning → 2 Heat Treatment → 3 Grinding → 4 Gear Cutting
```

The route-revision process must preserve both the prior sequence and the new sequence for audit purposes.
---

# 7. Stage Status Model

The canonical stage-state concept must support at least the following operational states already established by the specification:

```text
PLANNED
WAITING
WAITING_PREVIOUS_QC
IN_PROGRESS
STOPPED
WAITING_EXTERNAL_SEND
EXTERNAL_SENT
WAITING_QC
QC_APPROVED
QC_REJECTED / REWORK_REQUIRED
IN_REWORK
REWORK_COMPLETED
READY_FOR_WAREHOUSE
WAREHOUSE_RECEIVED
CANCELLED
CLOSED
```

The final database should use controlled status records/codes rather than free-text status strings.

The exact canonical list and whether some of these are event-derived rather than stored states should be finalized before schema freeze.

---

# 8. Production Work Sessions

## 8.1 ItemStageWorkSession

A work session is an actual production transaction.

Minimum fields:

| Field | Meaning |
|---|---|
| WorkSessionID | Primary key |
| ItemStageID | Stage worked on |
| RequestItemID | Direct item reference for query performance if desired |
| ProductionUnitID | Unit |
| MachineID | Machine, where applicable |
| OperatorID | Operator |
| WorkDate | Working date |
| ManualStartTime | Optional manual start time |
| ManualEndTime | Optional manual end time |
| WorkedMinutes | Actual reported minutes |
| ProducedQty | Produced quantity |
| RejectedQty | Rejected/scrap quantity |
| WorkEntryStatusCode | DONE / CONTINUED / STOPPED / CANCELLED / REWORK |
| EntryModeCode | MANUAL / AUTO |
| StopReasonID | Stop reason where applicable |
| SessionNote | Note |
| CreatedByUserID | Creator |
| CreatedAt | Timestamp |

Multiple work sessions per operator/day/stage are valid.

Actual work is separate from planned time.

---

# 9. QC Domain

## 9.1 QC inspection

QC is stage-level and occurs after each applicable stage.

There is no separate Final QC subsystem in the canonical model.

Minimum fields:

| Field | Meaning |
|---|---|
| QCInspectionID | Primary key |
| RequestID | Request reference |
| RequestItemID | Item reference |
| ItemStageID | Stage being inspected |
| DeliveredToQCAt | Production handoff timestamp |
| DeliveredToQCByUserID | Handoff actor |
| QCReceivedAt | QC receipt timestamp |
| QCReceivedByUserID | Receiving inspector/user |
| QCStatusID | Controlled result |
| AcceptedQty | Accepted quantity |
| RejectedQty | Rejected quantity |
| ReworkQty | Quantity returned for rework |
| QCNote | Inspection note |
| InspectorUserID | Inspector |
| InspectedAt | Decision timestamp |
| IsCurrent | Whether this is the active/current QC result |
| SupersededByQCInspectionID | Link to corrected replacement decision if applicable |
| CorrectionReason | Required when correcting an earlier decision |
| CorrectedAt | Correction timestamp |
| CorrectedByUserID | Correction actor |

## 9.2 QC gate invariant

For stage N > 1:

> The next stage must not become normally executable until QC has approved the preceding stage for the applicable quantity/route.

For the first stage:

- internal first stage → Planning release may make it executable;
- external first stage → the external supply/operation must arrive/return and pass QC before the following stage becomes executable.

## 9.3 Partial quantities

> **Design annotation (2026-10-22, Decision Log #82/#83; PROPOSAL, not approved unless stated):** Decision Log #83: individual-unit QC dispositions are required for the future production workflow. Proposed entities: `QcInspectionLine`, `StageQuantityBalance`; see `QC_QUANTITY_MODEL_DESIGN.md`.


Example:

```text
Stage quantity = 100
QC result:
  Accepted = 90
  Rejected = 5
  Rework = 5
```

The 90 accepted pieces continue.

The 5 rework pieces return to production.

A quantity permanently rejected by QC is classified as **SCRAP**. It is removed from the quantity expected to continue through the normal route, while the original inspection and quantities remain in the audit history. Scrap is not treated as rework; only the recorded rework quantity returns to production.

## 9.4 QC correction

QC may correct a historical QC decision.

The confirmed simplified rule is:

- do not reconstruct all downstream stages automatically;
- send the affected item back to **Stage 1**;
- downstream stages are handled manually afterward;
- retain the old QC history;
- mark the item as having undergone a QC correction.

This correction must therefore be auditable and must not physically delete the old decision.

The item must visibly indicate that at least one QC decision was corrected. The data model must preserve the original inspection, link the correction to the superseded inspection, and retain correction actor, timestamp, and reason. A derived/persisted item-level indicator such as `HasQCCorrection` may power the badge; it must not replace the inspection audit history.

---

# 10. External Stage Domain

External supply is represented as a production stage.

Examples:

- Ceramic supply;
- Casting;
- Tehran purchase;
- Contractor processing;
- External plating/hardening/etc.

The stage remains part of the item's ordered route.

## 10.1 ExternalWorkDispatch

Minimum fields:

| Field | Meaning |
|---|---|
| ExternalWorkDispatchID | Primary key |
| ItemStageID | External stage |
| SentAt | Send timestamp |
| SentByUserID | Sender |
| SentQty | Quantity sent |
| PieceDescription | Description |
| DispatchNote | Send note |
| ExpectedReturnDateTime | Expected external return/arrival |
| ReturnedAt | Actual return/arrival |
| ReturnedByUserID | User registering return |
| ReturnNote | Return note |
| DispatchStatusCode | External dispatch state |
| AutoCreated | Whether created automatically for a first external stage |
| CreatedByUserID | Creator |
| CreatedAt | Creation timestamp |
| UpdatedAt | Last update |

External return/arrival is **not** final warehouse receipt.

External returned goods go through QC before the next stage becomes executable.

---

# 11. Contract Domain

## 11.1 Contract ownership

Contract determination is request-level and applies to the entire customer request, including all MAKE and REPAIR items.

After Drawing confirms technical doability, Planning decides whether the request requires a contract.

If required:

```text
Planning creates contract
    ↓
Imports contract PDF
    ↓
Sends to Management
    ↓
Management Approves / Rejects
```

Contract rejection is corrected and resubmitted.

After approval:

- Planning must be informed in its dashboard;
- Warehouse must be informed in its dashboard;
- production release may proceed subject to all other requirements.

## 11.2 Contract fields

| Field | Meaning |
|---|---|
| ContractID | Primary key |
| RequestID | Request covered by contract |
| ContractNo | Contract number |
| ContractDate | Contract date |
| ContractTitle | Title |
| ContractStatusID/Code | Current contract status |
| (contract document) | Linked through a document link table (§20); no single document foreign key (Decision Log #35) |
| ManagementDecision | APPROVED / REJECTED |
| ManagementDecisionNote | Optional explanation |
| ManagementDecisionByUserID | Approver/rejector |
| ManagementDecisionAt | Decision timestamp |
| IsCurrent | Current contract marker |
| IsActive | Active marker |
| CreatedByUserID | Creator |
| CreatedAt | Creation timestamp |
| UpdatedAt | Last update |

The contract customer is always derived by joining `Contract.RequestID` to `Request.CustomerID`; do not store a redundant `Contract.CustomerID`. Contract calculation/value logic is separate from this document unless later explicitly reinstated.

> **[Decision Log #35]** Document references on this entity are link-table associations, not single foreign keys; see §20.

> **[Decision Log #93]** Implemented as `Contracts` (one per request, own status), `ContractVersions` (number, date, title, note, state, Management decision, decider, time, reason), `ContractDocumentLinks` (link rows to the stored PDF, Decision Log #35) and `ContractHistory` (append-only). `ContractRequired` is represented by the `Requirement` of the contract record (no record = undecided). The request status set is unchanged. `IsCurrent`/`IsActive` are replaced by version states. A rejection reason is mandatory.

## 11.3 Contract navigation indexes

Contract search must support:

- Contract No.
- Contract Date
- Customer

for authorized Planning, Management, and Warehouse users.

Recommended database indexes should support these searches.

## 11.4 Dashboard notification

The system needs a persistent way to make an approved contract visible in Planning and Warehouse dashboards.

**[DECISION REQUIRED]** Whether this is implemented as persistent unread notifications, dashboard alerts derived directly from contract state, or both is a technical/UI decision that can be finalized later; the business rule is only that approved contracts become visible/actionable to those departments.

---

# 12. Costing Domain

## 12.1 Ownership

- Planning owns machine hourly cost/rate.
- Warehouse owns raw-material cost per kg.
- QC owns units of measure.

All monetary values are Rial.

## 12.2 MachineHourlyRate

Minimum fields:

| Field | Meaning |
|---|---|
| MachineHourlyRateID | Primary key |
| MachineID | Machine reference |
| HourlyRateRial | Rate in Rial/hour |
| EffectiveFromDate | Start date |
| EffectiveToDate | End date, nullable |
| IsActive | Active indicator |
| CreatedByUserID | Creator |
| CreatedAt | Timestamp |
| Notes | Explanation |

Historical rates must remain available so old cost calculations do not change after a rate update.

## 12.3 MaterialPriceRate

Minimum fields:

| Field | Meaning |
|---|---|
| MaterialPriceRateID | Primary key |
| MaterialID | Material reference |
| PricePerKgRial | Price in Rial/kg |
| EffectiveFromDate | Start date |
| EffectiveToDate | End date, nullable |
| IsActive | Active indicator |
| CreatedByUserID | Creator |
| CreatedAt | Timestamp |
| Notes | Explanation |

## 12.4 CostCalculation

> **Design annotation (2026-10-22, Decision Log #82/#83; PROPOSAL, not approved unless stated):** Proposed formula, policies and snapshot contents: `COSTING_DESIGN.md` (U-22..U-29). Not approved.


Applies primarily to customer Repair and non-contract Make routes according to the confirmed architecture.

Minimum fields:

| Field | Meaning |
|---|---|
| ItemCostCalculationID | Primary key |
| RequestID | Request |
| RequestItemID | Item |
| CalculationDate | Calculation date/time |
| CalculationTypeCode | REPAIR / MAKE_NON_CONTRACT |
| RawMaterialWeightKg | Snapshotted weight |
| MaterialPriceRateID | Rate source |
| MaterialPricePerKgRial | Snapshotted price |
| RawMaterialCostRial | Calculated material cost |
| TotalMachineMinutes | Actual work minutes |
| TotalMachineCostRial | Sum of session costs |
| CalculatedTotalCostRial | System-calculated proposed total |
| ManagementFinalCostRial | Management-approved/adjusted amount |
| ManagementApproved | Approval flag |
| ManagementApprovedByUserID | Approver |
| ManagementApprovalAt | Approval timestamp |
| ManagementNote | Note |
| SentToWarehouse | Release flag |
| SentToWarehouseAt | Release timestamp |
| CreatedByUserID | Creator |
| CreatedAt | Timestamp |

Machine cost:

```text
Worked Minutes / 60 × Applicable Machine Hourly Rate
```

Raw material cost:

```text
Weight (kg) × Applicable Material Price per kg
```

Each work session may use its own machine rate.

## 12.5 Cost correction after QC correction

Because QC correction can send an item back to Stage 1, prior downstream costing/warehouse-release state must not be treated as the new truth.

Historical cost and warehouse records remain historical records.

A new production/QC/cost route is generated as required by the current state after the correction.

---

# 13. Warehouse Domain

Warehouse v1 is **history/inquiry/receipt oriented**, not a full inventory-management system.

> **[Decision Log #88, #94, #95]** Implemented as `RawMaterialInquiries` (per request and round), `RawMaterialInquiryLines`, append-only `RawMaterialInquiryResponses` (Yes/No), `RawMaterialInquiryDispositions` (Drawing, per No answer), `RawMaterialInquirySupplyRoutes` (Planning), `RawMaterialInquiryHistory`, and request-level `DrawingClarifications`. The AVAILABLE/PARTIAL/UNAVAILABLE vocabulary below is superseded by Yes/No.

## 13.1 WarehouseInquiry

> **Superseded/clarified (2026-10-22, Decision Log #88):** Yes/No per line, request-level inquiry (#86), no reservation or deduction. The text below is kept as history.



Purpose:

- ask whether required material/stock is available;
- record warehouse response;
- provide traceability.

Inquiry must not deduct inventory.

Minimum conceptual fields:

| Field | Meaning |
|---|---|
| WarehouseInquiryID | Primary key |
| RequestID | Request |
| RequestItemID | Item |
| InquiryDate | Date/time |
| RequestedQty | Quantity requested |
| UOMID | Quantity unit |
| AvailableQty | Warehouse response where known |
| ResponseStatusCode | AVAILABLE / PARTIAL / UNAVAILABLE |
| RespondedByUserID | Warehouse user |
| RespondedAt | Response time |
| Note | Explanation |

## 13.2 WarehouseTransaction

Minimum conceptual fields:

| Field | Meaning |
|---|---|
| WarehouseTransactionID | Primary key |
| RequestID | Request |
| RequestItemID | Item |
| ItemStageID | Stage when applicable |
| TransactionTypeCode | RECEIVE and future movement types |
| Quantity | Quantity moved/received |
| UOMID | Quantity unit |
| TransactionAt | Timestamp |
| PerformedByUserID | Warehouse user |
| Note | Explanation |

Warehouse receipt must prevent duplicate final receipt according to the item/stage route.

External return-to-QC is not `WarehouseTransaction.RECEIVE`.

---

# 14. Production Unit, Machine, and Operator Domain

## 14.1 ProductionUnit

> **[Note added by #122/#123]** Units, machines, operators and operator-machine eligibility are maintained by Program Admin; working-calendar rules apply at unit level (recurring schedules and dated exceptions with hours).

Minimum fields:

| Field | Meaning |
|---|---|
| ProductionUnitID | Primary key |
| UnitCode | Stable code |
| UnitNameFA | Persian name |
| UnitNameEN | English/internal name |
| ProductionUnitCategoryID | Optional classification |
| IsExternal | External route flag |
| RequiresDeliveryTracking | External tracking behavior |
| RequiresReturnTracking | External return tracking |
| RequiresPurchaseFollowUp | External supply follow-up |
| IsActive | Active |
| SortOrder | Display order |

ProductionUnit is the execution route, not the operation name and not the supply type.

## 14.2 ProductionUnitCategory

Used to distinguish internal and external operating destinations.

Examples from the source:

- INTERNAL_PRODUCTION
- EXTERNAL_PLATING
- EXTERNAL_HEAT_TREATMENT
- EXTERNAL_CASTING
- EXTERNAL_CERAMIC
- EXTERNAL_GRINDING
- EXTERNAL_GEAR_CUTTING
- EXTERNAL_TEHRAN
- EXTERNAL_OTHER

The actual master list is configurable.

## 14.3 Machine

> **Design annotation (2026-10-22, Decision Log #82/#83; PROPOSAL, not approved unless stated):** Proposed: stable immutable machine codes (FCC-06, TCC-12, HUB-01), rate and assignment history, qualification separate from assignment. See `PLANNING_RESOURCES_AND_SCHEDULING_DESIGN.md` (U-13, U-14).


Minimum fields:

| Field | Meaning |
|---|---|
| MachineID | Primary key |
| MachineCode | Stable identifier |
| MachineName | Name |
| ProductionUnitID | Primary unit |
| MachineHourlyRateLegacy | Legacy/fallback rate if retained |
| IsActive | Active |
| Notes | Notes |

The actual current rate should come from `MachineHourlyRate` history where applicable.

## 14.4 Operator

> **Superseded/clarified (2026-10-22, Decision Log #89):** Planning owns operator registration and operator-machine master assignments. The text below is kept as history.


> **Design annotation (2026-10-22, Decision Log #82/#83; PROPOSAL, not approved unless stated):** Proposed: operators need user accounts to submit job reports (U-16); job reports, KPI and overtime entities in `JOB_REPORTS_KPI_AND_OVERTIME_DESIGN.md`.


Minimum fields:

| Field | Meaning |
|---|---|
| OperatorID | Primary key |
| OperatorCode | Stable code |
| OperatorName | Name |
| PrimaryProductionUnitID | Primary unit |
| UserID | Optional/when operator also has application login |
| SkillLevel | Optional |
| CanWorkMultipleUnits | Optional |
| IsActive | Active |
| Notes | Notes |

Dynamic assignment can still permit an operator to work elsewhere when authorized by production rules.

---

# 15. User, Role, and Department Domain

## 15.1 User

Each user has exactly:

- one Department;
- one Role.

Minimum fields:

| Field | Meaning |
|---|---|
| UserID | Primary key |
| Username | Login name |
| PasswordHash | Secure password hash |
| DepartmentID | Exactly one department |
| RoleID | Exactly one role |
| DisplayName | Name |
| IsActive | Active |
| CreatedAt | Creation timestamp |
| LastLoginAt | Optional |

Passwords must never be stored plaintext.

> **[Approved — Decision Log #61, #62]** The Domain `User` holds the fields above except the password: `PasswordHash` (with its salt, algorithm, version, work factor, the lockout counters and the one-time flag) is credential data kept in a separate `UserCredentials` record owned by Application and stored by Infrastructure, never a Domain field. `Username` is stored as typed and made unique, case-insensitively, through one canonical key (`UsernameKey`). `CreatedByUserId` / `ApprovedByUserId` columns of customers, requests, items and QC approvals are foreign keys to `Users` (#70).

## 15.2 Canonical authorization matrix

Every user has exactly one Department and one Role. Authorization for sensitive actions must enforce both the specified department and, where stated, the role. Do not rely only on hiding or disabling UI buttons; validate authorization in application services as well.

| Action | Authorized department/role | Rule |
|---|---|---|
| Initial Management request approval/rejection | Department = Management AND Role = Manager | Only a Manager in Management may perform this action. |
| Drawing technical-doability decision | Department = Drawing | Only Drawing users may record the technical decision. |
| Create/submit a contract | Department = Planning | Planning owns contract preparation and submission. |
| Approve/reject a contract | Department = Management | Only Management users may decide; optional explanation is supported. |
| QC inspect or correct a QC decision | Department = QC | Only QC users may inspect or correct QC outcomes. |
| Change Planning priority | Department = Planning OR Management | Planning controls ordinary priority/order; Management priority edits must not be confused with the explicit queue-order override action. |
| Override queue order | Department = Management | Managerial override may reorder multiple stages/items in one queue operation; every affected row is audited. |
| Change machine hourly rates | Department = Planning OR Management | Rate changes retain effective-date and audit history. |
| Change raw-material price per kg | Department = Warehouse OR Management | Price changes retain effective-date and audit history. |
| Record final warehouse receipt | Department = Warehouse | External return-to-QC is not a final warehouse receipt. |
| Administer users and roles | Dedicated Admin user/ADMIN authorization | Only the Admin authorization may administer accounts/roles. |
| Define/edit Units of Measure and conversion rules | Department = QC | QC owns the UOM master. |
| Maintain holiday/off-day calendar | Department = Planning | Planning configures calendar dates and working-day overrides. |
| Register maintenance/power-outage downtime reported to Planning | Maintenance/PM reports; Planning reviews/uses it in scheduling | The exact account/approval workflow for downtime remains a technical configuration item, not permission to invent that downtime is ignored. |

Other screens and actions require least-privilege access and must use this table as the minimum explicit authorization contract. If an unlisted sensitive action lacks a defined permission, block it pending an explicit rule rather than assuming all users or all Managers may do it.

## 15.3 Role and Department reference values

> **[Note added by #122]** `PRODUCTION_MANAGER` ("مدیر تولید") is added; the display name of `CENTRAL` is «منشی» (the code is unchanged). Program Admin owns production master data; Planning no longer approves overtime; see Decision Log #122.

Canonical departments include Central, Management, Planning/Project Control, Drawing, Warehouse, Production Units, QC, and the maintenance/PM function for downtime reporting. The Internal Request workflow and Emergency subsystem are not part of C# v1.

**Development seed (Decision Log #30):** Departments: Central, Management, Planning, Drawing, Warehouse, Production, QC, Maintenance/PM, and System (home of the Admin account). Roles: Admin, Manager, Staff. Final company names may replace these without a schema change. Authorization rows that name a role use these names; all other rows are department-based.

## 15.4 Provisional development permission map (Decision Log #34)

Used for development only; revisit before production. Every action not listed here or in §15.2 remains denied.

| Action | Allowed department |
|---|---|
| Create a request and submit it to Management | Central |
| Receive a request, record the hand-off to Drawing, edit request details | Planning |
| Reopen a Management-rejected request for a new Management decision (reason and audit required) | Planning or Management |
| Define MAKE items | Drawing |
| Define REPAIR items; create/edit any item | Planning |
| Import items from Excel | Planning or Drawing |

Items are editable only until production release; afterwards only Planning may change them, through the controlled route-revision mechanism that is still to be specified (§6.2).

> **[Approved - Decision Log #75]** Added by the owner while building the first screens: **maintain customers** (create, rename, deactivate, reactivate; never delete) - Central or Planning; **view customers and requests** (read-only) - every active signed-in user. A mixed MAKE/REPAIR request is built in turn: Central creates and submits it, Drawing adds MAKE items, Planning adds REPAIR items and may add any item; a priority other than Normal at item entry also needs the permission to change the Planning priority. The only request transition implemented is CREATED to SENT_TO_MANAGEMENT (Central).

> **[Approved - Decision Log #78]** **View the read-only production overview** (`ViewProductionQueue`) - active users in Management or Planning; every other department is denied by default. Views of one production unit's queue may get separate permissions later.

> **[Approved, DEMO ONLY - Decision Log #80]** Demonstration stage flow: **release the first stage** (`ReleaseStage`) - Planning; **start a stage and submit it for QC** (`RunStage`) - Production; **whole-quantity QC approval** - QC (the existing 15.2 row). This does not finalize the production authorization matrix, and it implements no request-status, contract or Management-approval gate: the request-status transition table and the relationship between contract / Management approval and production release remain unresolved.

---

# 16. Unit of Measure Domain

QC owns the UOM master.

## 16.1 UnitOfMeasure

> **Superseded/clarified (2026-10-22, Decision Log #90):** QC owns UOM and quantity types; Planning selects them for items. The text below is kept as history.


> **Design annotation (2026-10-22, Decision Log #82/#83; PROPOSAL, not approved unless stated):** Proposed: a separate QC-owned QuantityType (piece, pair, set) with base units per type, distinct from UOM. See `QC_QUANTITY_MODEL_DESIGN.md` (U-17).


Minimum fields:

| Field | Meaning |
|---|---|
| UnitOfMeasureID | Primary key |
| UnitCode | Stable code |
| UnitNameFA | Persian name |
| UnitNameEN | English/internal name |
| IsActive | Active |
| SortOrder | Display order |
| CreatedByUserID | Creator |
| CreatedAt | Timestamp |

## 16.2 UnitOfMeasureConversion

Because UOM conversions are configurable, conversion rules should be represented as data rather than hard-coded logic.

Minimum conceptual fields:

| Field | Meaning |
|---|---|
| UOMConversionID | Primary key |
| FromUOMID | Source unit |
| ToUOMID | Target unit |
| ConversionFactor | Numeric factor |
| IsActive | Active |
| CreatedByUserID | Creator |
| CreatedAt | Timestamp |
| Notes | Explanation |

Example:

```text
PAIR → PCS = 2
PCS → PAIR = 0.5  (automatically derived reciprocal)
```

QC defines the primary conversion factor. The application must automatically permit the reverse conversion using the reciprocal (`1 / ConversionFactor`), so QC does not need to enter both directions. Conversion factors must be positive and non-zero. The stored quantity must always retain its associated UOM; conversion must not silently relabel a value without changing the numeric quantity.

---

# 17. Material Domain

The source specification has material master data and material price data.

A canonical Material entity should separate:

```text
Material identity
      ↓
Material price history
```

Minimum fields:

| Field | Meaning |
|---|---|
| MaterialID | Primary key |
| MaterialCode | Material/alloy code |
| MaterialName | Name |
| MaterialCategory | Category |
| IsActive | Active |
| Notes | Notes |

Material price is stored in `MaterialPriceRate`, not directly as an unversioned mutable value on the material record.

---

# 18. Planning Priority and Queue Ordering

Planning owns normal production priority and queue order. The allowed priority values are the three named values below, displayed in Persian:

| Code | Persian display | Sort order |
|---|---|---:|
| `VERY_URGENT` | خیلی فوری | 1 (highest) |
| `URGENT` | فوری | 2 |
| `NORMAL` | عادی | 3 (lowest) |

Multiple stages/items may share the same priority. Planning can change priority and normal queue order after items have entered the queue; the live queue must reflect those changes. **Granularity (resolved, Decision Log #58):** Planning priority is stored on the `RequestItem`; every stage and queue entry of that item inherits it, and the schema must not add an independent stage-priority field. New items default to `NORMAL`. The model must keep the Planning priority/order separate from any Management override, which never overwrites the underlying Planning priority.

## 18.1 Management queue-order override

Management may override the order of **multiple stages/items in one queue operation**. Use an override batch record plus one detail record for each affected stage. The batch records the Production Unit queue, manager/user, timestamp, and reason. Each detail records the stage, previous effective queue position, new effective position, and snapshots of the underlying Planning priority/order needed to explain the result later.

A Management override changes the effective queue order; it must not erase or overwrite the underlying Planning priority. Every override is auditable and may be reviewed historically.

Required audit data:

- user;
- timestamp;
- previous order/priority context;
- new order/priority context;
- reason;
- all affected stages/items in that one operation.

> **[Approved - Decision Log #78; not implemented]** A Management override stays active until Management explicitly clears it. If Planning subsequently changes the priority of an item that is covered by an active override, the system preserves both the underlying Planning priority and the active override and flags the override as requiring Management review; it never silently cancels or replaces it. Every override records the actor, timestamp, reason and previous/new order. Open details: absolute position versus "move ahead of row X", item versus stage scope, and how the review flag is cleared.

> **[Approved target - Decision Log #78; not implemented]** Order of a production unit's queue: Planning gives each **released stage** an explicit **Planning position**; equal positions are decided first come, first served. Full order: operational category (IN_PROGRESS, then STOPPED / problem states, then WAITING); then Planning priority; then Planning position; then available planned-end information; then stable identifiers. The current read-only overview (`docs/PRODUCTION_QUEUE.md`) is item-level with a provisional display order and does not apply this.

## 18.2 Queue display behavior

At minimum the queue must make current status, Planning priority, planned order, any active Management override, RequestNo/customer, ItemNo, stage, Production Unit, planned end, and blocking reason visible. Priority changes must trigger a current queue refresh/requery and recalculate derived order where required.
---

# 19. Scheduling Data Model

Planning supplies:

- planned machining/work minutes **per piece**;
- planned quantity;
- production unit;
- priority and normal queue order;
- applicable machine requirements where known;
- expected external lead time for external stages.

Baseline load calculation:

```text
Planned Total Processing Minutes = Planned Minutes Per Piece × Planned Quantity
```

The planned estimate is separate from actual work-session minutes. External stages additionally use their expected lead time; that lead time is not treated as machining minutes.

## 19.1 Configurable production calendar

> **Design annotation (2026-10-22, Decision Log #82/#83; PROPOSAL, not approved unless stated):** Proposed: shift templates A-D (C crosses midnight), unit shift working/OFF, staffing capacity, worker schedule and off-days as data. See `PLANNING_RESOURCES_AND_SCHEDULING_DESIGN.md` section 5 (U-34..U-36).


The initial factory calendar defaults are:

- normal working days: Saturday through Thursday;
- Friday: non-working by default;
- one current shift, 08:00–17:00, with a 12:00–13:00 break (8 working hours);
- the calendar must support a future two-shift configuration, currently envisioned as 06:00–14:00 and 14:00–22:00;
- the calendar must support three shifts of eight hours, with exact times configured rather than hard-coded;
- shift count, shift boundaries, and breaks are configuration, not constants embedded in code;
- Planning can define holidays and exceptional off-days in the application;
- work on Fridays or additional hours outside the normal schedule is allowed only with supervisor approval, and the approved exception should be recorded;
- calendar configuration changes affect future calculations and should not rewrite the historical actual timestamps.

## 19.2 Downtime and capacity

Maintenance/PM reports machine downtime to Planning. At minimum, downtime records must support machine/unit, start/end, reason (including maintenance and power outage), reporter, notes, and audit timestamps. Planning must be able to factor accepted downtime into available production capacity and planned-end recalculation. Do not assume downtime is automatically irrelevant or merely a free-text note.

The scheduling design must allow machine-specific unavailable intervals and configured calendars/holidays. It must also record when planned dates were recalculated and keep actual timestamps separate.

## 19.3 Scheduling algorithm: still not fully frozen

The exact algorithm is still open where it depends on actual factory capacity. A stage is assigned to a Production Unit by Planning; a supervisor/shift leader may assign a specific machine later. Before promising exact end dates, we must decide how the scheduler allocates work when a unit has multiple eligible machines, parallel work, or no machine assigned yet.

Until that capacity model is finalized, the application may show a clearly labelled **estimated** end date based on known inputs, but must not present it as a guaranteed completion date. The configurable calendar above is confirmed; parallel machine allocation and rescheduling precedence remain to be specified.
---

# 20. Document / PDF Domain

The C# system stores PDFs as database document content instead of path-only references.

The canonical Document should support at least:

| Field | Meaning |
|---|---|
| DocumentID | Primary key |
| DocumentTypeCode | REQUEST_PDF / DRAWING_PDF / CONTRACT_PDF / QC_REPORT_PDF / BUY_REQUEST_PDF / other approved type |
| FileName | Original file name |
| Content | Binary PDF data |
| ContentHash | Optional integrity/deduplication hash |
| ContentLength | Size |
| UploadedByUserID | Uploader |
| UploadedAt | Upload timestamp |
| IsActive | Active |
| Notes | Note |

**Decided rules (Decision Log #35):** documents are linked through link tables only; a stored binary is shared by all its links. Request PDFs accept PDF only, with a maximum size held as a configuration value; the limit is 20 MiB (20,971,520 bytes), displayed to users as "20 MB" (Decision Log #54, which supersedes the 10 MB in #35). Content-hash de-duplication on upload and retention/deletion rules are not decided.

A single uploaded document may be logically attached to multiple records. Store the binary content once in `Document`, then represent each logical association through document-link rows. Prefer entity-specific link tables (for example RequestDocumentLink, RequestItemDocumentLink, ContractDocumentLink, QCInspectionDocumentLink, ItemStageDocumentLink) where required to retain database foreign-key integrity. Adding another supported owner type must not require duplicating the stored PDF bytes.

### 20.1 Database storage technology

This document defines the domain behavior (document content is stored in the database) but does not yet freeze SQL Server-specific storage such as `varbinary(max)` versus FILESTREAM.

That is a technical implementation decision to be finalized during SQL Server architecture work.

---

# 21. Excel Import Domain

Excel import is a core v1 capability because requests may contain hundreds or around 1,000 items.

The importer must support one mixed worksheet containing MAKE and REPAIR items.

The importer must not infer WorkType from another field.

Each row must contain an explicit WorkType value.

## 21.1 Import pipeline

```text
Excel workbook
    ↓
Read worksheet
    ↓
Validate headers / rows
    ↓
Map WorkType = MAKE / REPAIR
    ↓
Validate UOM/material/supply references where applicable
    ↓
Detect duplicates within Request
    ↓
Show preview + validation errors
    ↓
User confirms
    ↓
Transactional batch insert
    ↓
Excel import audit record
```

## 21.2 Existing prototype mapping retained as historical reference

The Access prototype used this positional mapping:

| Legacy Excel columns | Legacy field |
|---|---|
| A | ItemNo |
| B:E merged | PartName (top-left cell) |
| F:I merged | Dimensions (top-left cell) |
| J:K merged | MaterialName (top-left cell) |
| L | RequestedQty |
| M | SupplyTypeID |
| N | SupplyRequestNo |

This table documents the old Access template; it does not constrain the new C# template to those column positions. The C# template uses **Persian headers**, and the importer maps fields by header name so column rearrangement does not mis-map values. Merged-cell handling may be supported for legacy files, but the official C# template should prefer a standard unmerged tabular sheet for predictable 1,000-row imports.

The official mixed worksheet must include an explicit Work Type column (for example, header `نوع کار`) whose values map to canonical MAKE/REPAIR codes. It must never infer the work type from the request or another field.

## 21.3 Import identity and re-import conflict resolution

Duplicate detection uses:

```text
RequestID + ItemNo
```

within the same request. ItemNo is text, not numeric, so leading zeros, prefixes, and hierarchical numbering such as `1.10` are preserved.

On re-import:

- new ItemNo rows are offered for insertion;
- identical existing rows are reported as already present and are not duplicated;
- an existing ItemNo with changed imported values (such as quantity, material, or dimensions) is shown as a conflict for manual choice;
- the preview displays existing values beside incoming values for each conflict;
- the user must resolve all conflicts before committing the import transaction;
- the importer must not silently overwrite an existing item, reset active stages, or alter work/QC history.

The exact set of conflict choices and the rule for updating an item whose stages have already been released/started remain pending. The C# importer must not invent those rules.
---

# 22. Request Progress Data Model

Request progress is a read model derived from current item/stage state.

It is not manually entered.

For v1, the request screen should expose:

- total items;
- completed items;
- in-progress items;
- waiting items;
- waiting QC;
- waiting external;
- rework;
- blocked/problem counts;
- per-item stage progress.

A visual percentage may be displayed as a monitoring aid.

For v1, the percentage is **not** a workflow gate or financial KPI.

**[DECISION REQUIRED]** The exact formula for the headline visual percentage should be frozen after the first UI prototype; the current safe behavior is to derive it from completed items ÷ total items while separately showing quantity and status information.

---

# 23. Warehouse and Costing Relationships

The domain relationships should support:

```text
RequestItem
   ├── WorkType
   ├── UOM
   ├── Material
   ├── RepairDetails (if REPAIR)
   ├── ItemStages
   │     ├── WorkSessions
   │     ├── QCInspections
   │     └── ExternalDispatch
   ├── CostCalculation
   └── WarehouseTransactions
```

This is the canonical production chain.

---

# 24. Database Relationship Summary

Conceptual relational structure:

```text
Department ─────┐
Role ────────────┤
                 ▼
               User
                 │
                 ├─────────────── audit references
                 │
Customer ────────┐
                 ▼
               Request
                 │
        ┌────────┴─────────┐
        ▼                  ▼
RequestWorkflow      Contract
        │                  │
        │              Document
        ▼
   RequestItem
        │
   ┌────┼─────────┬──────────────┐
   │    │         │              │
   ▼    ▼         ▼              ▼
Repair  Make     UOM           Material
Details Details   /Conversions   │
   │                        PriceRates
   │
   └─────────────┐
                 ▼
             ItemStage
                 │
       ┌─────────┼──────────────┐
       ▼         ▼              ▼
 WorkSession    QC        ExternalDispatch
       │         │              │
       │         │              │
 Machine────HourlyRate           │
       │                        │
       └──────────────┬─────────┘
                      ▼
                 CostCalculation
                      │
                      ▼
              WarehouseTransaction
```

---

# 25. Keys, Foreign Keys, and Constraints

## 25.1 Primary keys

Use surrogate numeric primary keys for transactional/master entities unless a business identifier is explicitly a natural key.

Examples:

```text
RequestID
RequestItemID
ItemStageID
QCInspectionID
ContractID
UserID
MachineID
```

Business identifiers such as RequestNo, ItemNo, ContractNo, MachineCode, and MaterialCode remain separate indexed fields.

## 25.2 Foreign keys

Foreign-key integrity should be enforced for core relationships.

Examples:

```text
RequestItem.RequestID → Request.RequestID
ItemStage.RequestItemID → RequestItem.RequestItemID
WorkSession.ItemStageID → ItemStage.ItemStageID
QCInspection.ItemStageID → ItemStage.ItemStageID
Contract.RequestID → Request.RequestID
```

## 25.3 Delete policy

Transactional records should not be physically deleted when deletion would destroy audit history.

Use inactive/cancelled/superseded states or archival strategies instead.

Reference/master rows may use inactive flags rather than destructive deletion where historical transactions reference them.

**[DECISION REQUIRED]** Exact hard-delete permissions and retention periods have not been defined.

---

# 26. Indexing Strategy

Initial index candidates:

- Request.CustomerID + Request.RequestNo + Request.JalaliRequestYear (unique, case-sensitive comparison of RequestNo)
- Request.RequestNo
- Request.CustomerID
- Request.RequestDate
- Request.CurrentStatusID
- Request.CurrentDepartmentID
- Request.ContractRequired
- RequestItem `(RequestID, ItemNo)` unique/validated
- RequestItem.WorkTypeID
- Planning priority on `RequestItem` (stages inherit it; Decision Log #58) and the planning queue order
- ItemStage `(RequestItemID, StageNo)`
- ItemStage.StageStatusID
- ItemStage.ProductionUnitID
- ItemStage.PlannedEndDateTime
- WorkSession.ItemStageID
- WorkSession.OperatorID
- WorkSession.MachineID
- WorkSession.WorkDate
- QCInspection.ItemStageID
- QCInspection.RequestItemID
- Contract.ContractNo
- Contract.ContractDate
- CostCalculation.RequestItemID
- WarehouseTransaction.RequestItemID
- WarehouseTransaction.ItemStageID

Exact indexes must be validated against actual SQL query plans and data volume.

---

# 27. Transactions and Atomic Operations

The following operations should be atomic wherever a partial write would corrupt workflow state.

## Request action

```text
validate
→ update Request current state
→ append RequestWorkflowEntry
→ commit
```

## Stage release

```text
validate prior state
→ update stage state
→ create external dispatch if required
→ create queue/scheduling projection if required
→ commit
```

## QC decision

```text
validate QC authority
→ record QCInspection
→ update stage state
→ release/hold next stage according to result
→ update item/request current state where required
→ commit
```

## Contract approval

```text
validate contract
→ record Management decision
→ update Contract status
→ update/request production gate state
→ create dashboard-visible notification/state
→ commit
```

## Excel import

```text
validate workbook
→ create import batch
→ insert valid rows
→ record row errors where supported
→ commit only when user accepts and validation policy permits
```

---

# 28. Database Provider Requirements

The schema must be implementable first in SQLite and later in SQL Server.

Avoid:

- provider-specific business logic in Domain;
- SQLite-only SQL embedded in Application services;
- Access-specific field types/lookup behavior;
- business rules represented only by database quirks.

SQLite is the development provider, not the final production backend.

---

# 29. C# Entity Naming Guidance

Recommended C# names:

```text
Request
RequestItem
ItemStage
ItemStageWorkSession
QcInspection
Contract
CostCalculation
WarehouseTransaction
ExternalWorkDispatch
ProductionUnit
Machine
Operator
User
Role
Department
UnitOfMeasure
UnitOfMeasureConversion
Material
MachineHourlyRate
MaterialPriceRate
```

Avoid carrying Access prefixes such as `tbl`, `qry`, `frm`, and `mod` into the C# domain model.

Access names may remain in migration/reference documentation only.

---

# 30. Legacy Access Mapping

The following are legacy/reference concepts, not automatic one-to-one C# entities:

| Access concept | C# treatment |
|---|---|
| tblRequests | Request |
| tblRequestItems | RequestItem |
| tblRepairItemDetails | RepairItemDetails |
| tblRepairItemWorkflow | Unified repair workflow history or dedicated repair history entity, depending on final domain decision |
| tblItemStages | ItemStage |
| tblItemStageWorkSessions | ItemStageWorkSession |
| tblItemQCInspections | QCInspection |
| tblExternalWorkDispatches | ExternalWorkDispatch |
| tblContracts | Contract |
| tblItemCostCalculations | CostCalculation |
| tblItemWarehouseTransactions | WarehouseTransaction |
| tblMachines | Machine |
| tblProductionUnits | ProductionUnit |
| tblProductionUnitCategories | ProductionUnitCategory |
| tblOperators | Operator |
| tblUsers | User |
| tblRoles | Role |
| tblDepartments | Department |
| tblStopReasons | StopReason |
| tblUnitOfMeasures | UnitOfMeasure |
| tblMaterialPriceRates | MaterialPriceRate |
| tblMachineHourlyRates | MachineHourlyRate |
| tblProjects / tblItems | Legacy; not required for canonical C# production flow |
| frm* / qry* / mod* | UI/query/module implementation references; redesign in C# |

The mapping is conceptual. It does not mean the SQL Server schema must preserve legacy table names.

---

# 31. Required Business Invariants

The following should be enforced by the application/domain layer and, where practical, supported by database constraints:

1. RequestItem.WorkType must be MAKE or REPAIR.
2. A Request may contain both work types.
3. RequestItem.ItemNo must be unique within its Request.
4. An ItemStage belongs to exactly one RequestItem.
5. Stage ordering is item-specific.
6. A later stage cannot normally execute before preceding-stage QC approval.
7. An external first stage must arrive/return and pass QC before the next stage proceeds.
8. Accepted quantity continues downstream.
9. Rework quantity returns to production.
10. QC cannot silently delete historical decisions.
11. QC correction sends the item back to Stage 1 and preserves history.
12. Planning controls normal priority.
13. Management may override the normal queue order.
14. Queue overrides require user, timestamp, old order/priority, new order/priority, and reason.
15. A required contract blocks production until approved.
16. Contract requirement applies to the entire request.

> **Superseded/clarified (2026-10-27, Decision Log #111; recorded 2026-10-29):** the contract DOCUMENT and its approval stay request-level, but the contract DECISION and the production gate are evaluated per item (append-only `ItemContractDecisions`; an item with no decision follows the request).
17. Contract rejection follows correct-and-resubmit.
18. Warehouse inquiry does not deduct inventory.
19. Final warehouse receipt is distinct from external return-to-QC.
20. All monetary values are Rial.
21. All user-facing dates are Jalali.
22. Users have exactly one department and one role.
23. Passwords are stored securely as hashes, not plaintext.
24. SQLite is development-only; SQL Server is the production target.
25. Projects are not required for production execution.
26. Internal Requests are outside v1.

---

# 32. Decision Register — Current Status

This register distinguishes confirmed business rules from decisions still needing explicit approval. Confirmed rules elsewhere in this document are authoritative and must not be treated as unresolved merely because older text once listed them as questions.

## 32.1 Request number — RESOLVED

`RequestNo` is customer-supplied free-form text. It is not generated by the ERP/MES. The business key is **Customer + RequestNo + Jalali Request Year**, unique within that combination. RequestNo comparison is case-sensitive (`wsj-898` differs from `WSJ-898`). `RequestID` remains the internal primary key. Do not silently change case or generate a replacement number.

## 32.2 Priority and queue override — RESOLVED

Priority values are exactly `عادی`, `فوری`, and `خیلی فوری`, mapped to stable codes and ordered from lowest to highest *(see §18 for the authoritative table: `SortOrder` 1 = highest urgency, `VERY_URGENT`; audit X20)*. Planning owns normal priority/order and may update it; the live queue must respond. Multiple stages/items may share a priority. Management may override multiple stages/items in one queue operation. Every override batch and affected stage position must be audited with user, timestamp, old/new order and reason; preserve the underlying Planning priority.

Resolved (Decision Log #58): (a) the normal Planning priority belongs to the `RequestItem`; stages and queue entries inherit it. (b) How a later Planning priority edit interacts with an active Management override is resolved in Decision Log #78 (the override stays active, both facts are preserved, the override is flagged for Management review); not implemented yet.

## 32.3 Scheduling calendar — BASE RULES RESOLVED; capacity algorithm pending

Confirmed defaults/configuration:

- Saturday through Thursday are normal working days.
- Friday is normally off, but Friday work/additional hours are allowed with supervisor approval and must be recorded.
- Current shift: 08:00–17:00, with a 12:00–13:00 break (8 net work hours).
- Support configurable one-, two-, or three-shift operation. The currently envisioned two shifts are 06:00–14:00 and 14:00–22:00; three-shift times must be configurable.
- Planning configures holidays and exceptional off-days.
- Maintenance/PM reports machine downtime (including maintenance and power outage) to Planning; downtime must influence available capacity and schedule recalculation.
- Planning enters minutes per piece. Estimated stage load = minutes per piece × planned quantity. External stages additionally have expected lead time.

Pending: capacity allocation across multiple eligible machines within a Production Unit, scheduling of a stage before the supervisor assigns a machine, and planned-end information for the queue order. (The queue tie-break after equal Planning position and the override precedence are decided in Decision Log #78.)

## 32.4 Stage route revisions — PARTIALLY RESOLVED

Planning may insert a stage before/after an existing stage through a controlled route revision, including after production starts. Preserve the previous route and all stage/work/QC history; log actor, time, reason, and before/after order. Pending: exact downstream-state handling when the inserted stage changes the required sequence of already-created stages.

## 32.5 Excel template and re-import — PARTIALLY RESOLVED

The official template uses Persian headers and one mixed worksheet containing MAKE and REPAIR items, with an explicit Work Type column. New rows may be appended on re-import. Existing same-ItemNo rows with changed import values must appear as conflicts for manual choice; identical rows must not duplicate.

Pending: final exact Persian header strings/accepted values and the available conflict choices, especially when the existing item already has released or started stages. Import must never silently reset stage or audit history.

## 32.6 UOM conversion — RESOLVED

QC owns UOM definitions. Reverse conversions are automatic reciprocals (for example, `PAIR → PCS = 2` implies `PCS → PAIR = 0.5`), provided the conversion factor is positive and non-zero. Quantities retain their UOM identity.

## 32.7 Permanent QC rejection — RESOLVED

Permanently rejected quantity is **SCRAP**. Accepted quantity proceeds; only the recorded rework quantity returns to production. Original QC decisions and all correction history remain auditable.

## 32.8 Authorization matrix — INITIAL SENSITIVE-ACTION MATRIX RESOLVED

| Action | Allowed department/role |
|---|---|
| Initial Management request approval/rejection | Management department + Manager role |
| Drawing doability decision | Drawing department only |
| Create/submit contract | Planning department only |
| Approve/reject contract | Management department only |
| QC inspect/correct | QC department only |
| Change normal priority | Planning or Management |
| Override queue order | Management only |
| Change machine hourly rate | Planning or Management |
| Change material cost/kg | Warehouse or Management |
| Record final warehouse receipt | Warehouse only |
| Edit UOM/conversions | QC only |
| Configure holidays/off-days | Planning |
| Reopen a Management-rejected request | Planning or Management (reason and audit required; Decision Log #29) |
| Administer users/roles | Admin user/ADMIN authorization only |

Maintenance/PM reports downtime to Planning, which considers it in scheduling. Detailed permissions for less-sensitive/unlisted screens and the precise downtime-entry approval flow remain implementation details to review before rollout. Authorization must be enforced in application services, not just the UI.

## 32.9 Contract customer — RESOLVED

Do not store redundant `Contract.CustomerID`. Derive customer through `Contract.RequestID → Request.CustomerID`.

## 32.10 Document ownership — RESOLVED

One stored Document/PDF may be linked to multiple records. Store binary content once and represent each relationship with document-link rows; prefer foreign-key-safe link tables for supported parent types.

## 32.11 Request progress percentage — PENDING USER CONFIRMATION

The UI must show a request-level progress indicator and counts by item status. A proposed v1 headline is completed items ÷ total items, with separate quantity progress and counts for waiting, in progress, waiting QC, waiting external, rework, and blocked items. This is not yet a confirmed formula and must remain clearly labelled as a monitoring metric until approved.

## 32.12 Other deferred decisions

- Exact rules for stage revision impact on existing downstream stages.
- Exact machine capability matrix and scheduling allocation over machine pools.
- Re-import conflict actions for items already in production.
- Detailed operator/unit reward scoring (Phase 2).
- Archive retention and hard-delete policy.
- SQL Server document-content optimization (for example, varbinary storage versus FILESTREAM).
- Dashboard refresh/polling versus real-time push notification.
- Installer/update rollout policy and operational backup/restore acceptance process.

No coding agent may silently invent a business outcome for any item still marked pending.

# 33. Deferred / Non-Blocking Decisions

These can be refined after the initial application is working:

- detailed KPI formulas;
- 24-day reward scoring formula;
- attendance/overtime/leave integration;
- advanced external movement reporting;
- warehouse historical movement expansion;
- detailed cost calculation lines if the aggregated costing route is accepted first;
- dashboard push notifications versus refresh/polling;
- SQL Server document storage optimization such as FILESTREAM;
- deployment/update system;
- archive/retention policy for old documents.

---

# 34. Initial Database Implementation Order

The schema should be implemented in dependency order:

```text
1. Departments / Roles / Users
2. Customers
3. WorkTypes / statuses / controlled lookup data
4. Units of Measure + conversions
5. Materials + price rates
6. Production Units + categories
7. Machines + hourly rates
8. Operators
9. Requests + request workflow
10. Request Items
11. Repair Item Details
12. Contracts + Documents
13. Item Stages + Stage Statuses
14. Work Sessions
15. QC Inspections + reasons
16. External Dispatch
17. Warehouse Inquiry / Transactions
18. Cost Calculations
19. Excel Import audit tables
20. Scheduling/queue history structures once scheduling rules are finalized
```

This order is intended to reduce circular dependencies and make incremental migrations/tests practical.

---

# 35. Acceptance Criteria for the Domain/Database Layer

The database/domain design is ready for implementation when:

- every entity has an explicit purpose;
- all core relationships and foreign keys are known;
- all state-owning tables are identified;
- no legacy Access-only workaround is required by the C# domain;
- the Request → Item → Stage → QC chain is complete;
- partial quantities can be represented without corrupting stage history;
- external stages and first-stage external supply are representable;
- contract gate is representable at Request level;
- Planning priority and Management override history are representable;
- Excel import can handle approximately 1,000 items without requiring row-by-row UI entry;
- request progress can be calculated from current state;
- all monetary values are Rial;
- all user-facing dates are Jalali;
- SQLite development and SQL Server production can use the same business model;
- unresolved business decisions are explicitly recorded rather than guessed.

---

# 36. Next Review Step

The older checklist below is superseded by Section 32, Decision Register — Current Status. Confirmed decisions must not be asked again. The remaining design decisions are listed there, particularly:

1. Request-level headline progress formula.
2. ~~How queue priority is attached and how an override interacts with later Planning changes~~ - decided (Decision Log #58 and #78); implementation of Planning positions and overrides remains.
3. Machine-capacity allocation and recalculation rules for the scheduling engine.
4. Exact downstream-state handling after a route revision.
5. Final Persian Excel header/value strings and conflict options for active items.

After those items are resolved, the database schema can be frozen into the first entity/EF Core model.
# 37. Confirmed Business Decisions — 2026-10-09

This section summarizes newly confirmed decisions and supersedes older conflicting notes in this document.

- RequestNo uniqueness: Customer + RequestNo + Jalali year, case-sensitive RequestNo comparison.
- Priority: `عادی` / `فوری` / `خیلی فوری`; Planning owns normal order and may change it; Manager queue override is batch-based and auditable.
- Calendar defaults: Saturday–Thursday workdays, Friday off unless supervisor-approved, current 08:00–17:00 shift with 12:00–13:00 break, configurable 1/2/3 shifts and holidays/off-days. Current envisioned 2 shifts are 06:00–14:00 and 14:00–22:00.
- Maintenance/PM reports machine downtime to Planning; downtime impacts schedule capacity.
- Stage revision: Planning may insert stages before/after existing stages through controlled, audited route revisions; do not destroy execution history.
- Excel: one mixed worksheet, Persian headers, explicit Work Type, manual conflict resolution when an existing ItemNo row has changed values.
- UOM: QC-owned and configurable; reverse conversions derive automatically as reciprocals.
- Permanent QC rejected quantity is SCRAP. A QC correction remains historical and sends the affected item back to Stage 1; downstream stages are handled manually.
- Sensitive-action authorization follows the matrix in Section 15.2.
- Contract customer is derived through Request; no duplicate Contract.CustomerID.
- Documents may link to multiple records while binary content is stored once.
- Request progress formula remains pending confirmation; show statuses/counts regardless.

---

# 38. Confirmed Business Decisions — 2026-10-10

Synchronised with `docs/DECISION_LOG.md` #29–#35. Where an older passage disagrees, this section and the Decision Log win.

- Request statuses: initial set of eight; rejected requests can be reopened by Planning or Management with reason and audit (§4.3; #29). Transition table: draft in `docs/REQUEST_STATUS_TRANSITIONS_DRAFT.md`, not yet approved.
- Departments and roles seed: §15.3 (#30). Provisional permission map: §15.4 (#34).
- RequestNo business-key rules: §4.2 (#31). `JalaliRequestYear` is a persisted Request field (§4.1).
- Customer master: name only plus active flag and audit fields (§4.5; #32).
- Quantities and minutes per piece: decimals with a fixed scale (§5.1; #33).
- Documents: link tables only; PDF only; 20 MiB (20,971,520 bytes, shown as "20 MB") maximum, configurable (§20; #35, #54).
- Technical conventions (#22–#27): .NET 10 target, integer keys with an application-managed version column, UTC timestamps and date-only types, provider-neutral subset with one migration set per provider, generic audit table, PBKDF2.
