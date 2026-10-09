# ERP / MES — C# Master Specification + Current State

**Document status:** C#-ready consolidation of the existing ERP/MES Master Specification

**Source document:** `ERP_MES_Master_Spec_Detailed_Update_2026_07_21.md`

**Purpose of this document:** Preserve the business behavior and accumulated design of the source specification while removing obsolete Access-specific assumptions and resolving only the design decisions explicitly confirmed by the system owner.

**Important:** The source specification remains the historical/reference record. This document is the canonical C#-oriented working specification for the new application. It must not silently invent business rules that are not supported by the source or by explicit owner decisions.

---

# 0. Authority, Scope, and Current Reality

## 0.1 System purpose

The application is an ERP/MES workflow system intended to make the company's request-to-production process visible, traceable, auditable, and searchable.

The system covers:

- customer request registration
- high-volume Excel-based item import (including requests with hundreds or around 1,000 items)
- management review
- Planning / Project Control decisions
- Drawing technical review and item definition
- warehouse inquiry
- external supply tracking
- external processing tracking
- production routing
- production execution
- production-unit work queues
- QC delivery, receipt, and inspection after production stages
- rework
- operator work reporting
- machine downtime tracking
- costing and management price approval where applicable
- warehouse receipt
- request-centric customer follow-up
- management monitoring and reporting
- historical/audit tracking

The central design principle is that important movements and decisions must be stored as structured data, not merely as notes.

## 0.2 Current factory situation

The new system described here is **not yet the factory's production system**.

The factory continues to use its existing/old Access program. The new Access application documented in the source file was a development/prototype environment and has not been deployed as the live factory system.

The intended next implementation is a new Windows desktop C# application.

## 0.3 Development database and final database

The C# application shall use:

- **SQLite** as a temporary, single-user development/test database.
- **SQL Server** as the final production backend after the application has been tested and approved by the company.

SQLite is not intended to be the final multi-user production database.

The business/domain/application design must therefore avoid coupling business logic to SQLite-specific behavior and must leave a clean path to SQL Server.

## 0.4 Frontend technology

Confirmed technical direction:

- Windows-only desktop application.
- C#.
- .NET with WPF for the UI.
- Persian-language user interface.
- Right-to-left layout.
- English backend/entity/table/property naming may be retained.
- Business behavior must be preserved while Access-specific implementation details may be redesigned.

The exact .NET runtime version, ORM, dependency-injection library, and packaging/deployment technology are implementation decisions and are not invented by this document.

## 0.5 Authentication

Users authenticate with:

- username
- password

The credentials are stored in the application's own database rather than using Windows/Active Directory authentication.

Each user is associated with exactly:

- one department
- one role

The system must enforce permissions based on the authenticated user's department, role, current workflow state, and permitted actions.

## 0.6 Global localization, calendar, and currency rules

These are **mandatory global application rules**, not optional display preferences.

### Calendar / date rule

- All user-facing dates must be displayed in the **Shamsi (Jalali) calendar**.
- All user-facing date-time values must be displayed in Jalali date + 24-hour time.
- Users must enter/edit dates through Jalali-aware controls or parsing.
- Gregorian dates must not be exposed as the normal user-facing representation anywhere in the application.
- Database date/time values may use the native database `DateTime` representation (Gregorian-based internally) for reliable storage, sorting, filtering, indexing, and calculations.
- Conversion between stored date/time values and Jalali display/input is an application-layer concern.
- Jalali month/year/day filtering must be converted to database date ranges before querying; the application must not compare formatted Jalali strings as dates.
- Jalali year extraction is required for Jalali-based numbering and reporting periods.

### Currency rule

- **All monetary values, prices, rates, costs, contract amounts, machine hourly rates, material prices, management-approved costs, and related financial figures are denominated in Iranian Rial (IRR).**
- The application must display monetary values as **Rial**, not Toman.
- No financial field may silently assume Toman.
- Numeric monetary values should be stored as numeric database values rather than formatted strings.
- Formatting such as thousand separators and the Persian/Rial label belongs to the presentation layer.
- Excel import/export and reports must preserve monetary values as Rial and must not convert them to Toman unless a future, explicitly approved requirement introduces a conversion.

---

# Master Data Ownership Rules

The following master-data ownership rules are mandatory in the C# system. They are business responsibilities, not merely suggestions for who happens to use a form.

| Master data | Owning department | Purpose |
|---|---|---|
| Unit of Measure | **QC** | Define and maintain the units used for quantities throughout the system. |
| Machine hourly cost/rate | **Planning / Project Control** | Define and maintain the machine machining/working cost per hour used by planning and costing. |
| Raw-material cost per kg | **Warehouse** | Define and maintain the current raw-material/alloy cost per kilogram used by Make-item costing. |

The system must keep these values as controlled master data with audit information and effective dates where historical calculations depend on them. User-facing quantities and monetary values must use the corresponding master definitions.

The exact approval hierarchy for changing these master data sets is not expanded beyond the ownership rules above unless later specified.

# 1. Authoritative Design Decisions

The following decisions were explicitly confirmed after reviewing the source specification. They override conflicting older notes in the source document.

## 1.1 Request and item work classification

The new C# system is **customer-request based**. Internal company requests are **not processed as a request type in v1**.

At item level, `WorkTypeID` is the authoritative classifier:

- `MAKE`
- `REPAIR`

A single CUSTOMER request **may contain both MAKE and REPAIR items**.

Therefore:

```text
Customer Request #11
  ├── Item A → WorkType = MAKE
  ├── Item B → WorkType = MAKE
  └── Item C → WorkType = REPAIR
```

`WorkTypeID` must be used for MAKE/REPAIR workflow branching.

`RequestTypeID` is not a MAKE/REPAIR indicator. If retained for migration compatibility, its supported v1 value is CUSTOMER and it must not be used to introduce an internal-request workflow.

No internal-request screens, numbering scheme, requester fields, or internal-request workflow are part of the C# v1 scope.

## 1.2 No mandatory Projects

The new system is **request-based, not project-based**.

Projects are not mandatory for the new C# system.

The old factory application is project-based, but that model is not the target architecture for this application.

The canonical operational trace is:

```text
Request
  ↓
Request Items
  ↓
Item Production Stages
  ↓
Production / External Route
  ↓
QC after each stage
  ↓
Further stages / completion
  ↓
Costing where applicable
  ↓
Warehouse receipt
```

The old Access objects `tblProjects`, `tblItems`, `tblProjectStatuses`, `tblProjectStatusLogs`, and project-oriented forms/queries are therefore **legacy/reference objects**, not the canonical C# production model.

The C# system must not require a request to be converted into a project before production can begin.

## 1.3 Contract determination and approval

Every customer request must pass a contract-determination check by Planning after Drawing has confirmed the technical doability/capability of the request.

The canonical sequence is:

```text
Drawing confirms doability
        ↓
Planning checks whether a contract is required
        ↓
If no contract is required → continue normal workflow

If contract is required
        ↓
Planning creates/fills the Contract form
        ↓
Contract PDF is imported into the application
        ↓
Contract is sent to Management
        ↓
Management APPROVES or REJECTS
```

When a contract is required:

- `ContractRequired = true` at request level.
- Planning is responsible for creating the contract record and attaching/importing the contract document.
- The contract must retain its own number, date, customer/request relationship, status, audit information, and document content.
- Management may approve or reject the contract. A rejection explanation is optional.
- Production release must remain blocked while the required contract is absent or not approved.
- After Management approval, both **Planning** and **Warehouse** must be informed through their dashboards that the contract is approved/available.
- Contracts must be searchable/navigable by **Contract Number, Contract Date, and Customer** for Planning, Management, and Warehouse.
- The application must preserve historical contract records and approval decisions.

The exact downstream action after a contract rejection is not invented here; the rejection must block the production gate and preserve the rejection reason when supplied until a later business decision defines a return/correction route.

> **[Phase 0.1 annotation — superseded]** The return/correction route is now decided: a rejected contract returns to Planning for correction and resubmission, with the rejection retained as history (Decision Log #4; Domain and Database Specification §11.1). See `PHASE0_SPECIFICATION_AUDIT.md` X9.

### Contract document storage

Unlike the Access prototype's path-only PDF storage, the C# application's canonical document model stores the contract PDF binary content in SQL Server in the production deployment. SQLite may store the same document content during development/testing. The UI should open/view the document without requiring users to manage filesystem paths.

## 1.4 Every item has its own production stages

Each request item has its own ordered production route.

Stages are dynamic per item. Different items in the same request may have different stages.

Example:

```text
Request #11

Item 1 — Ring A
  Stage 1: Turning
  Stage 2: Grinding
  Stage 3: Gear Cutting

Item 2 — Ring B
  Stage 1: Casting
  Stage 2: Turning
  Stage 3: Grinding
```

The request number and item identity must remain visible in production queues so that two physically similar or identical parts can still be distinguished.

## 1.4A QC gate between stages

The first stage may be released by Planning when it is an internal production stage. If the first stage is external supply, it waits for external arrival and QC before the next internal production stage can be released.

Every subsequent stage requires confirmation/approval by QC of the preceding stage before it may be released.

Therefore:

```text
First stage
   ↓
Production / external completion
   ↓
QC approval
   ↓
Next stage unlocked
   ↓
Production / external completion
   ↓
QC approval
   ↓
Next stage unlocked
```

A stage must not become normally executable merely because the previous stage was marked complete by production.

## 1.5 First stage may itself be external supply

The first stage is not necessarily an internal operation.

If an item's first stage is an external supply route such as Ceramic, Casting, Tehran supply, or another external source, the system waits for the externally supplied item/material to arrive and then routes it through QC.

Example:

```text
Stage 1 = Ceramic supply
      ↓
External supply arrives
      ↓
QC inspection
      ↓
QC approved
      ↓
Stage 2 = internal finishing
```

The external supply is represented as a **production stage**, not as a separate disconnected pre-production object.

## 1.6 No Emergency subsystem

The previous Emergency/Override concept is **removed from the new canonical specification**.

Do not implement:

- Emergency request types
- emergency levels
- emergency approval fields
- emergency bypass logic
- emergency dashboards
- emergency-specific workflow states

Any old Access Emergency fields or logic are legacy/deprecated and must not be carried into the C# business model unless explicitly reintroduced later.

## 1.7 No separate Final QC concept

The previous `NeedsFinalQC` (legacy only) / final-QC idea is **not part of the canonical C# workflow**.

QC occurs after individual production stages.

The system should not introduce a separate final-QC gate merely because an older section of the Access specification mentioned one.

## 1.8 Warehouse does not maintain full inventory in v1

The warehouse subsystem is intentionally lightweight.

The C# system records:

- warehouse inquiries
- warehouse responses
- warehouse receipts
- warehouse movements/history

It does **not** attempt to maintain a complete continuously accurate on-hand raw-material inventory in v1.

The rationale is operational: obtaining and maintaining an accurate list of current available raw materials was excessively time-consuming, so the system should record the inquiry and response rather than attempting to become a full inventory-management system.

Warehouse inquiry is informational and must not deduct inventory.

## 1.9 PDF/document storage

The old Access design stored PDF files by filesystem path to reduce Access file size.

The C# system will instead store PDFs **inside the SQL backend** in the final architecture.

The application frontend must remain lightweight and delegate data/file retrieval to the database layer as much as practical.

Old `PdfPath`/`DrawingPath` fields and the Access path-only strategy are therefore legacy implementation details.

The final C# document model should preserve metadata such as file type, title, ownership/reference, creation information, and the actual PDF content. The exact SQL binary-storage schema is an implementation detail to be finalized before implementation of the document subsystem.

## 1.10 QC correction

QC may correct a previous QC decision.

The rare case in which a downstream stage has already been processed is **not** handled by automatically rebuilding all downstream stages.

The canonical correction rule is:

- return the item to the affected stage (for the confirmed example, Stage 1);
- retain historical QC records;
- mark the QC decision as corrected/superseded;
- downstream stages are then handled manually according to the actual production situation;
- previous cost approval and warehouse transactions are not treated as authoritative after the correction and must not silently remain the current outcome.

The exact mechanics for invalidating/reopening a previously closed cost or warehouse record must be explicit in the transaction implementation and must never delete historical evidence.

---

# 2. Domain Vocabulary

| Term | Canonical meaning |
|---|---|
| Request | A customer work request and top-level workflow container. |
| Request Type | CUSTOMER in v1; retained only if needed for migration/extensibility. It does not determine MAKE vs. REPAIR. |
| Work Type | MAKE or REPAIR. Applies at item level. |
| Request Item | One distinguishable physical/logical item within a request. |
| Production Stage | One ordered operation/route step for one request item. |
| Production Unit | The unit/location/route responsible for executing a stage. May be internal or external. |
| Stage Title | Describes the operation being performed, e.g. turning, grinding, plating, casting. |
| Supply Type | Describes where material/item supply comes from. It must not be confused with the stage operation or production unit. |
| External Stage | A production stage whose route is external. |
| Work Session | A production transaction describing work performed by an operator on a stage. |
| QC Inspection | A structured inspection transaction for a completed/delivered stage or externally supplied stage. |
| Rework | Work required after a QC rejection. |
| Warehouse Inquiry | A request to warehouse for availability information; does not deduct stock. |
| Warehouse Receipt | A physical receipt/hand-off recorded by warehouse. |
| Contract | A separate management-controlled contract record used to gate production when ContractRequired is true. |
| Request Number | User-visible customer request identifier. |
| Registration Code | User-visible short-text item tracking identifier where required. |

The following three concepts must never be merged:

```text
StageTitle       = what operation is being performed
ProductionUnit  = where/by which route it is being performed
SupplyType      = where the material/item supply comes from
```

---

# 3. Request-Level Workflow

## 3.1 All requests begin at Central

The starting path is:

```text
Central
  ↓
Management
  ↓
Planning / Project Control
```

The common request header contains, as applicable:

- RequestID
- RequestNo
- RequestDate
- RequestTypeID
- BuyerID for CUSTOMER requests *(legacy name; the Domain and Database Specification uses `Customer`/`CustomerID` — audit X2)*
- RequestTitle
- RequestDescription
- CurrentDepartmentID
- CurrentStatusID
- CreatedByUserID
- CreatedAt
- Notes
- ContractRequired where applicable
- other workflow-control fields confirmed by the current contract/drawing model

## 3.2 Central creation

Central:

1. receives the incoming request PDF;
2. creates the request record;
3. stores/associates the document;
4. identifies the customer request and enters the required customer/request information;
5. enters basic request information;
6. sends the request to Management;
7. creates the corresponding request workflow/audit record.

## 3.3 Initial Management review

Management has two fundamental actions:

- Approve
- Reject

### Approve

The request is routed to Planning / Project Control and the action is logged.

### Reject

The rejection reason must be captured.

The request is routed to Planning / Project Control and the rejection is logged.

Planning handles the cancellation/communication process for rejected requests, according to the established operational workflow.

## 3.4 Planning receives the request

For an approved request, Planning determines the required item-level workflow.

A CUSTOMER request may contain a mixture of MAKE and REPAIR items.

---

# 4. Request-Workflow Audit Trail

Every important request-level transition must produce a structured audit event.

The legacy Access table was `tblRequestWorkflow`. The C# equivalent must retain the same business responsibility but does not need to reproduce Access/VBA mechanics.

A workflow event contains, at minimum:

- WorkflowEventID
- RequestID
- FromDepartmentID
- ToDepartmentID
- ActionTypeCode
- ActionResultCode
- NewStatusCode/ID
- ActionNote where relevant
- ActionByUserID
- ActionDateTime
- current-event indicator where required by the UI/query model
- manager approval information when the action requires it

Important rule:

> Request-level workflow events are not production-stage execution logs.

Production stages and work sessions must have separate transaction/history structures.

The historical record must never be rewritten merely to make current state easier to display.

---

# 5. Customer Repair Workflow

## 5.1 High-level path

```text
Central
  ↓
Management
  ↓
Planning
  ↓
Drawing capability review
  ↓
Planning defines repair items
  ↓
Repair items receive production stages where required
  ↓
Production / external route
  ↓
QC after each stage
  ↓
Rework loop when rejected
  ↓
Cost approval where applicable
  ↓
Warehouse receipt
```

## 5.2 Drawing capability review

Drawing does not define the repair item rows.

Drawing reviews the incoming request and returns a controlled decision such as:

- fully accepted
- partially accepted
- rejected
- more information needed
- ordinary return/correction where applicable

The decision and technical notes are recorded as request-level workflow data.

## 5.3 Repair item definition

Planning defines all relevant received repair items, not only accepted items, to maintain complete traceability.

General fields may include:

- RequestItemID
- ItemNo
- ItemName / PartName
- BuyerID where item-level buyer differs *(historical; not carried into Domain and Database Specification §5.1. Whether an item-level customer is needed is an open audit item, X1)*
- Quantity
- ReceiveDate
- ItemCode
- SerialNumber
- Notes
- Drawing/document association where required
- WorkTypeID = REPAIR

Repair-specific data belongs in a dedicated repair-detail entity analogous to `tblRepairItemDetails`, rather than polluting the general request-item entity.

Repair-specific information documented in the source includes:

- RepairItemCode
- SerialNo
- ExitNo
- ReceivedDate
- RepairWorkDescription
- IsEmergency — **deprecated and removed**
- HasReferenceSample
- RepairRequiredDate
- RepairRequiredNote
- drawing-required flags
- current repair-item status

ItemCode and SerialNumber are short text values and must preserve:

- leading zeros
- letters
- slashes
- dashes

## 5.4 Repair-specific workflow history

A repair item has its own item-level history analogous to `tblRepairItemWorkflow`.

It is separate from request-level workflow history.

Typical actions include:

- finalized
- started repair
- completed repair
- sent to QC
- QC approved
- QC rejected
- sent to rework
- rework completed
- warehouse received

## 5.5 Repair statuses

The current repair status progression is:

```text
REGISTERED
  ↓
READY_FOR_REPAIR
  ↓
IN_REPAIR
  ↓
WAITING_QC
  ↓
READY_FOR_WAREHOUSE
  ↓
DELIVERED_TO_WAREHOUSE
```

QC rejection loop:

```text
WAITING_QC
  ↓
REWORK_REQUIRED
  ↓
IN_REPAIR
  ↓
WAITING_QC
```

Historical Access names such as `REPAIR_DELIVERED_TO_WAREHOUSE` may remain as compatibility/status-history references where required, but the C# canonical current-state model should use one coherent status vocabulary.

## 5.6 Repair finalization validation

Before a repair item is released to production, required repair information must be validated.

The source specification calls out at least:

- RepairItemCode
- RepairWorkDescription
- RepairRequiredDate or RepairRequiredNote

If drawing is required, its required/provided condition must also be validated.

No incomplete repair item may silently enter production.

---

# 6. Customer Make Workflow

## 6.1 High-level path

```text
Central
  ↓
Management initial review
  ↓
Planning
  ↓
Drawing item definition
  ↓
Supply decisions / warehouse inquiry / external supply setup
  ↓
Drawing finalization
  ↓
Planning stage definition
  ↓
Contract gate where applicable
  ↓
First-stage production/external release
```

There is **no mandatory Project conversion step**.

## 6.2 Make item definition

MAKE items are identified by:

`WorkTypeID = MAKE`

The shared request-item entity contains the general manufacturing-item information.

Documented Make fields include:

- ItemNo
- PartName / ItemName
- RequestedQty
- Order Unit
- Tracking Unit
- QtyPerOrderUnit
- MaterialName
- FinalMaterialName
- Dimensions
- WorkTypeID
- DrawingPath/document association
- Description
- SupplyTypeID
- NeedsIncomingQC where applicable
- SupplyRequestDate
- SupplyRequestNo
- RawMaterialWeightKg
- MaterialPriceRateID for non-contract costing where applicable

## 6.3 Supply decision

Drawing determines the supply route for each relevant item/material.

Known supply concepts include:

- internal warehouse stock
- external purchase/supply
- casting
- ceramic
- contractor processing
- direct internal production
- customer-supplied repair
- partner manufacturing
- mixed supply where genuinely required

Supply decisions must remain data-driven.

The system must not hard-code Casting, Ceramic, Tehran, Contractor, or any other route as always being Stage 1.

## 6.4 Warehouse inquiry

When internal warehouse availability needs to be checked:

1. Drawing/Planning creates an inquiry.
2. Warehouse responds with availability information.
3. The response is recorded.
4. No inventory deduction takes place.

Possible responses include:

- available
- partially available
- unavailable
- approved quantity
- notes

The inquiry is informational, not an inventory transaction.

## 6.5 External supply

External supply itself is modeled as a production stage.

Examples:

- Casting
- Ceramic
- Tehran purchase/supply

The stage waits for the external material/item to arrive. Upon arrival it is received into the company's workflow and sent through QC.

If QC accepts it, the next stage can be released.

If QC rejects it, the appropriate external return/supplier-correction process begins.

## 6.6 Contractor/external processing

Contractor processing differs from external purchasing/supply.

Contractor processing means the company already has the item and sends it outside for an operation such as:

- hardening
- coating
- plating
- special machining
- heat treatment
- external grinding
- gear work
- other work not available internally

It is represented as a production stage and may have external dispatch/return tracking.

---

# 7. Customer Request Item Classification

All operational requests processed by the C# v1 system are customer requests.

The request itself identifies the customer business case through fields such as RequestNo, RequestDate, BuyerID, title, description, documents, workflow state, and related audit records.

### Canonical RequestNo rule

`RequestNo` is the customer's own order/request number and is stored as free-form text. Typical values are one-to-four digit numbers, but alphanumeric values such as `P-565` and `wsj-898` are valid. The same RequestNo may be used by different customers, so RequestNo is not globally unique. The database uses `RequestID` as the internal primary key. The normal business identification/lookup combination is **Customer + RequestNo + Jalali Request Year**. The customer's sequence normally increases during its Jalali year and resets for the next Jalali year; the ERP/MES stores the supplied number exactly and does not generate or renumber it.

The same customer will not issue two distinct requests with the exact same RequestNo within the same Jalali year. The database should therefore enforce uniqueness on **Customer + RequestNo + Jalali Request Year**. A new customer order/request receives a new customer-supplied RequestNo even when it concerns the same physical item or repair case. The RequestNo comparison for this uniqueness rule is **case-sensitive**: `wsj-898` and `WSJ-898` are treated as different RequestNo values for the same customer and Jalali year.

The actual manufacturing/repair classification is made independently for each request item through `WorkTypeID`: `MAKE` or `REPAIR`.

A request may therefore contain a mixture of item work types. The application must not force one request-wide Make/Repair classification.


# 8. Production Item and Stage Model

## 8.1 Canonical production hierarchy

The C# application shall use:

```text
Request
  └── RequestItem
        └── ItemStage (ordered)
              ├── WorkSession(s)
              ├── QC Inspection(s)
              ├── External Dispatch (when external)
              └── Rework/history events
```

There is no required `Project → Item` intermediary.

## 8.2 Request item identity

Every request item must be distinguishable even when multiple items have the same part name.

At minimum, production displays should provide enough information to distinguish:

- RequestNo
- ItemNo
- PartName
- quantity
- WorkType
- current stage/status
- production unit

Example:

```text
Request #11 | Item 1 | Ring | Qty 2 | Turning | Unit: CNC Lathe
Request #11 | Item 2 | Ring | Qty 2 | Turning | Unit: Universal Lathe
```

## 8.3 Dynamic stage routing

Every item has its own ordered stage list.

A stage contains conceptually:

- ItemStageID
- RequestItemID
- StageNo
- StageTitle
- ProductionUnitID
- StandardMachineTimeMin
- StageStatusID/code
- StageNotes
- external-route indicator/classification
- planned/summary values where applicable

Execution values must not be confused with planning values.

## 8.4 Editing a route after execution begins

Started/completed stages must not be silently rewritten.

If a routing change is required after execution has started, the system must preserve the historical record and handle the change explicitly.

The exact UX/mechanism for creating a revised route is an implementation item, but historical execution data must remain intact.

---

# 9. Production Unit Assignment

Planning assigns stages to Production Units, not normally to individual machines/operators.

Normal responsibility split:

```text
Planning
  → assigns ItemStage to ProductionUnit

Unit Supervisor / Shift Leader
  → assigns Operator and Machine

Operator / Supervisor
  → records production work

QC
  → receives and inspects completed stage
```

## 9.1 Alternative units

The production floor may use an alternative production unit when the normal unit is unavailable.

For example:

```text
Universal Lathe
     ↓ unavailable
Allowed alternative:
CNC Lathe
```

The system should support:

- primary unit
- allowed alternative units
- reassignment reason
- approval where required
- reassignment history

A capability structure analogous to `tblStageUnitCapabilities` is part of the intended design.

A reassignment history structure analogous to `tblStageReassignments` is also intended.

## 9.2 Machines and operators

A machine belongs to a production unit.

An operator has a primary production unit but may be assigned dynamically where permitted by operational rules.

The older `ProducionUnitID` spelling in the Access database is a legacy implementation detail and must not dictate C# property naming.

---

# 10. Stage Status and Release Logic

The exact status vocabulary should be centralized in the C# application rather than scattered as UI strings.

Relevant documented stage states include:

- PLANNED
- WAITING_PREVIOUS_QC
- WAITING
- IN_PROGRESS
- PAUSED
- STOPPED
- COMPLETED_BY_PRODUCTION / equivalent completion state
- DELIVERED_TO_QC
- WAITING_QC
- QC_RECEIVED
- UNDER_QC_INSPECTION
- QC_APPROVED
- QC_REJECTED
- REWORK_REQUIRED
- IN_REWORK
- REWORK_COMPLETED
- WAITING_EXTERNAL_SEND
- EXTERNAL_SENT
- RETURNED_FROM_CONTRACTOR / external return state
- WAITING_EXTERNAL_SUPPLY
- WAITING_MATERIAL
- WAITING_DRAWING_REVISION
- READY_FOR_WAREHOUSE
- WAREHOUSE_RECEIVED
- CLOSED

Some exact legacy status names can be normalized in C# provided their business meanings are preserved.

## 10.1 First internal production stage

When Planning releases an item:

```text
Stage 1 = internal
    → Stage 1 becomes WAITING
    → appears in its Production Unit work queue
```

All later stages remain locked behind the previous QC gate.

## 10.2 First external stage

When Planning releases an item whose first stage is external:

```text
Stage 1 = external
    → external dispatch is created/registered
    → stage becomes EXTERNAL_SENT
    → it does not appear in the normal internal production queue
    → external item returns
    → stage becomes WAITING_QC
    → QC receives and inspects it
```

## 10.3 Subsequent internal stage

After QC approval of the preceding stage:

```text
Current stage = QC_APPROVED
    ↓
Next stage = internal
    ↓
Next stage becomes WAITING
```

## 10.4 Subsequent external stage

After QC approval of the preceding stage:

```text
Current stage = QC_APPROVED
    ↓
Next stage = external
    ↓
Next stage becomes WAITING_EXTERNAL_SEND
```

Warehouse/external workflow then registers the send.

---

# 11. Production Work Sessions

The shop floor is irregular. Operators may:

- work on several jobs in one day
- switch tasks
- leave a task unfinished
- continue a task later
- stop a task
- have a task canceled
- rework a task
- work with several operators on one stage where permitted

Therefore the system must not rely only on Start/Stop buttons.

## 11.1 Canonical work transaction

`ItemStageWorkSession` is the production-performance transaction.

Each row represents one work segment and can contain:

- WorkSessionID
- WorkDate
- ItemStageID
- ProductionUnitID
- MachineID where relevant
- OperatorID
- WorkedMinutes
- ProducedQty
- RejectedQty
- StopReasonID where relevant
- SessionNote
- EntryModeCode
- WorkEntryStatusCode
- ManualStartTime
- ManualEndTime
- CreatedByUserID
- CreatedAt

## 11.2 Entry modes

Supported entry modes:

```text
MANUAL
AUTO
```

Manual entry is the preferred practical method for the current environment.

Automatic Start/Stop can exist as an additional UI mode, but it must write to the same work-session transaction model.

## 11.3 Work-entry status

Documented values include:

- DONE
- CONTINUED
- STOPPED
- CANCELLED
- REWORK

These statuses describe the work-entry transaction and are distinct from the stage's lifecycle state.

## 11.4 Multiple rows per day are valid

The system must not require exactly one work session per operator/day/stage.

Example:

```text
Operator A
1405/06/10

180 minutes → CONTINUED
120 minutes → DONE
 90 minutes → REWORK
```

All rows remain valid source transactions for reporting.

---

# 12. Production Unit Queues and Dashboards

## 12.1 Unit work queue

Each production unit has a work queue containing stages currently released to that unit.

The queue should show enough information to identify the job clearly:

- RequestNo
- ItemNo
- PartName
- quantity
- WorkType
- StageNo
- StageTitle
- machine/operator information where assigned
- current stage status
- relevant notes
- external/internal indicator where relevant

## 12.2 Queue ordering

The current Access prototype uses the preferred ordering:

1. IN_PROGRESS
2. STOPPED
3. REJECTED / REWORK
4. WAITING

The C# implementation should preserve the operational intent rather than relying on numeric IDs whose meaning may change.

## 12.3 Production dashboard

The system has a unit launcher/dashboard concept:

```text
Production
  ↓
Select Production Unit
  ↓
Unit Dashboard
  ├── Work Queue
  ├── Daily Work Entry
  └── Daily Production Report
```

## 12.4 Management live-production overview

Management and Production supervision need a **live production work-queue dashboard** covering all production units, not merely a static daily report.

The dashboard must show the current open production workload and distinguish at minimum:

- IN_PROGRESS
- STOPPED
- REJECTED / REWORK
- WAITING

Each row should retain enough identity to distinguish similar physical items, including RequestNo, ItemNo, PartName, WorkType, StageNo, StageTitle, ProductionUnit, priority, and current status.

Management must be able to open the relevant production-unit queue and item progress from the live overview. The dashboard is a core C# management feature, not an optional prototype screen.

The source prototype used status ordering with active/problem jobs before ordinary waiting work. The C# application should preserve that operational visibility while making the ordering and priority rules explicit.

Automatic refresh behavior/interval is not yet fixed by the business specification; the implementation must nevertheless always display the current persisted production state when refreshed.

The Management dashboard is an operational monitoring screen, not merely a report. It must make the following immediately visible for open work:

- RequestNo
- Customer
- ItemNo
- PartName
- WorkType (MAKE/REPAIR)
- StageNo / StageTitle
- Production Unit
- Priority
- Planned End
- Current Stage Status
- QC-blocked / waiting state where applicable

Management must be able to navigate from a live queue row to the item's progress/history and to the relevant production-unit queue.

## 12.5 Request and item progress visibility

The request page must provide an immediate visual indication of how much of the request has progressed. This is especially important for large requests containing hundreds or around 1,000 items.

The item list on the request page is therefore not just a data-entry grid. It is also a production-progress view.

At minimum, the request view should provide:

- a request-level progress indicator/progress bar;
- the total number of request items;
- the number of completed items;
- the number of active/in-progress items;
- the number of waiting/blocked items;
- the number of items in rework where applicable;
- a visible item list showing each item's current stage/status.

Each item should also expose stage progress so that users can distinguish, for example, an item with 1 of 5 stages completed from an item with 4 of 5 stages completed.

The request-level progress metric must be derived from item/stage state rather than manually entered as a note. The exact aggregate weighting formula for cases where stage durations differ is intentionally not invented here; the first implementation must at least support a transparent item-completion measure and per-item stage progress, with weighted scheduling/progress metrics available for later refinement.

### 12.5.1 Progress calculation requirements

The request page must expose both request-level and item-level progress without requiring manual percentage entry.

At minimum the UI must show:

- a visual progress bar/percentage for the request;
- completed-item count / total-item count;
- active, waiting/blocked, and rework item counts;
- per-item stage progress such as `2 / 5 stages`;
- current stage/status for each item.

The first C# implementation shall use a **transparent item-completion metric** (completed items divided by total request items) for the headline request percentage unless a later approved rule replaces it. Stage-level progress must remain visible separately so that the request percentage is not mistaken for a weighted production-time percentage.

> **[Phase 0.1 annotation — superseded]** The headline percentage is a *proposed default only*; the closing "Canonical Business Decision Update — 2026-10-09" section and the Decision Log record the formula as not yet approved. Status counts and quantity information are required regardless.

For very large requests, progress must be calculated using database-side aggregation or otherwise efficient queries; loading all historical work-session records into the WPF client merely to calculate a progress bar is not acceptable.


---

# 13. Production Planning Priority and End-Date Calculation

Production Planning controls the production priority and provides the planned machining/work time per piece for each production stage. The application must use these Planning inputs to calculate planned production dates/end dates and to order work appropriately in production queues.

These planning inputs are part of the executable production plan and must be visible to production queues and Management monitoring.

## 13.1 Planning priority

Planning must be able to assign a priority to production work. Priority must be visible in:

- request/item views;
- production-unit work queues;
- the management live-production dashboard;
- planning and scheduling views.

The system must support manual priority changes because production plans can change during execution. The live queue must reflect the current Planning priority.

Management is authorized to override the normal Planning queue order. Every such override must record the user, timestamp, previous order/priority, new order/priority, and reason. The override must be auditable and must not erase the underlying Planning priority/history.

The exact priority scale (for example numeric levels or named levels) is not specified in the source and is therefore not invented here. The canonical requirement is that Planning owns the normal priority and Management owns explicit override authority.

> **[Phase 0.1 annotation — superseded]** The priority scale is now decided: `عادی`, `فوری`, `خیلی فوری` (Decision Log #11; Domain and Database Specification §18). See audit X19.

## 13.2 Planned machining/work time

Planning enters the planned machining/work time for each item stage as **minutes per piece**.

For a stage with planned quantity Q and Planning estimate M minutes per piece:

```text
Planned Total Minutes = Q × M
```

This planning estimate is used for schedule/end-date calculation. Actual work-session minutes are measured separately and must not overwrite the original planning estimate.

## 13.3 Planned start and end dates

The system must calculate a planned completion/end date-time for production work from the Planning-provided machining/work time and the production schedule.

At minimum, the scheduling model must account for:

- the planned work duration derived from Planning's minutes-per-piece estimate and applicable quantity;
- production-unit/machine availability as represented by the configured plan;
- the item's current queue/priority position;
- previous-stage QC gating, which can prevent downstream work from starting;
- manual production-plan changes and reassignments;
- expected lead time for external stages where applicable.

When Planning changes priority, planned time, stage order, or the production assignment, the affected planned dates must be recalculable.

Planning is the owner of the planned machining/work duration and production priority. These values are part of the production plan and must be visible wherever the work is queued or monitored.

The system must preserve both **planned** and **actual** values:

- planned machining/work minutes entered by Planning;
- planned start date/time;
- planned end date/time;
- actual work-session minutes;
- actual start/end events;
- priority and any manual priority override history.

The planned end date must never overwrite or be confused with the actual completion date.

The exact factory working-calendar algorithm (shift pattern, holidays, break handling, machine-capacity model, and whether parallel machines share a common capacity pool) is **not specified in the source document and must remain a deliberate later design decision rather than being guessed**.

> **[Phase 0.1 annotation — superseded]** The calendar rules (Saturday–Thursday, Friday by supervisor approval, current and future shift patterns, holidays, downtime) are now decided (Decision Log #13, #14; section "Calendar and planning time" at the end of this document). Only the multi-machine capacity algorithm remains open. See audit X19.

The C# data model should nevertheless reserve explicit fields for planned scheduling information separately from actual execution timestamps, such as:

- PlannedStartDateTime
- PlannedEndDateTime
- PlannedDurationMinutes
- Priority / PriorityCode

These fields represent the plan and may change without destroying the historical actual-work data.

---

# 14. QC System

## 14.1 QC is stage-based

QC is performed after individual production stages, including external-supply/external-processing stages when they return to the company.

There is no separate final-QC architecture in the canonical system.

## 14.2 Production-to-QC handoff

Two timestamps are required conceptually:

1. `DeliveredToQCAt`
2. `QCReceivedAt`

Production:

1. identifies the RequestItem/ItemStage;
2. enters the delivered quantity;
3. clicks/records Deliver to QC;
4. system records `DeliveredToQCAt`.

QC:

1. identifies the same RequestItem/ItemStage;
2. receives it;
3. system records `QCReceivedAt`.

The inspection result follows separately.

## 14.3 QC inspection transaction

A QC inspection contains conceptually:

- QCInspectionID
- RequestID
- RequestItemID
- ItemStageID
- inspection date/time
- result/status
- QtyAccepted
- QtyRejected
- QtyRework
- QC note
- InspectorUserID
- current/corrected indicators
- correction reason
- created-at metadata

The generic unified QC entity is the canonical design for stage-based and repair-related QC.

The old Access repair-only QC table/form remains legacy/reference.

## 14.4 Partial QC result

The system must support partial results such as:

```text
Accepted = 90
Rejected = 5
Rework   = 5
```

The 90 accepted pieces continue forward.

Only the 5 rejected/rework pieces return to the rework route.

The quantity model must therefore allow quantities to move independently without requiring the entire item quantity to restart.

## 14.5 QC approval unlocks next stage

When an item stage is approved by QC:

- the current stage reaches the QC-approved state;
- if another stage exists, only the next stage is unlocked;
- if the next stage is internal, it becomes available in the appropriate unit queue;
- if the next stage is external, it becomes ready for external dispatch;
- downstream stages remain locked.

## 14.6 QC rejection

When QC rejects/requires rework:

- the rejected quantity enters the rework path;
- the relevant item/stage history is preserved;
- the stage can be worked again;
- completed rework returns to QC.

> **[Phase 0.1 annotation — superseded]** The first bullet above predates the SCRAP rule. Settled rule: permanently rejected quantity is SCRAP; only the recorded rework quantity returns to production (Decision Log #7; Domain and Database Specification §9.3). See audit X13.

---

# 15. QC Correction and Historical Integrity

QC must be able to correct a previous decision.

The old QC row must not be deleted.

The correction should create/record:

- correction user
- correction time
- correction reason
- relationship to the original decision
- resulting current decision/status

## 15.1 Rare downstream-correction scenario

If a QC correction affects an earlier stage after later stages have already progressed:

```text
Corrected Stage
   ↓
Return item to that stage
   ↓
Re-run/complete it as operationally required
   ↓
Downstream stages are manually handled
```

The system must not automatically assume that every downstream stage must be deleted/recreated.

Previously created cost approval or warehouse receipt outcomes must no longer be treated as current truth when the correction invalidates them.

Historical records remain visible for audit.

The item should carry a visible indicator/history that a QC correction occurred.

---

# 16. External Supply and External Processing

## 16.1 Conceptual separation

The system distinguishes:

### Supply type

Where the material/item comes from.

Examples:

- warehouse stock
- customer-supplied
- Tehran supply
- Ceramic supply
- Casting supply

### Stage operation

What operation is being performed.

Examples:

- turning
- milling
- grinding
- plating
- hardening
- gear cutting
- casting
- welding
- purchasing/supply operation where represented as the stage title

### Production route/unit

Where/by whom the stage is executed.

Examples:

- internal turning unit
- internal milling unit
- contractor
- Tehran
- Ceramic route
- Casting route

## 16.2 External classification

A production unit can be classified as external.

The current Access design uses a production-unit category structure analogous to:

- INTERNAL_PRODUCTION
- EXTERNAL_PLATING
- EXTERNAL_HEAT_TREATMENT
- EXTERNAL_CASTING
- EXTERNAL_CERAMIC
- EXTERNAL_GRINDING
- EXTERNAL_GEAR_CUTTING
- EXTERNAL_TEHRAN
- EXTERNAL_OTHER

The C# implementation may normalize these names but must preserve the underlying distinction.

A stage is operationally external when the assigned production route is external.

## 16.3 External dispatch

The current source design uses a dispatch transaction analogous to `tblExternalWorkDispatches`.

It contains conceptually:

- ExternalWorkDispatchID
- ItemStageID
- SentAt
- SentByUserID
- SentQty
- PieceDescription
- DispatchNote
- ReturnedAt
- ReturnedByUserID
- ReturnNote
- DispatchStatusCode
- AutoCreated
- AutoCreateReason
- CreatedByUserID
- CreatedAt
- UpdatedAt

A simplified current status vocabulary includes:

- SENT
- RETURNED_TO_QC

## 16.4 First external stage

If Stage 1 is external, production release automatically prepares/registers the external dispatch according to the current workflow design.

Stage status:

```text
EXTERNAL_SENT
```

The stage does not enter a normal internal unit queue.

## 16.5 Later external stage

If a later stage is external and the preceding stage is QC-approved:

```text
WAITING_EXTERNAL_SEND
```

Then warehouse/external workflow registers dispatch:

```text
EXTERNAL_SENT
```

## 16.6 Return from external route

The warehouse records return-to-QC.

The user does not manually type the return timestamp.

The system uses the current time of the transaction.

The stage becomes:

```text
WAITING_QC
```

and appears in the stage QC workflow.

## 16.7 Combined external/supply inbox

The Access prototype uses a combined cartable built from modular sources analogous to:

- external supply request inbox
- external stage inbox
- combined external work inbox

The C# application should provide one external-work operational view while maintaining separate underlying domain concepts.

The view should support at least:

- request number
- part name
- source type
- supply type
- supply request number/date
- stage
- external production unit
- current external status
- send information
- return information
- notes

---

# 17. Warehouse

## 17.1 Warehouse inquiry

Warehouse inquiry is informational.

It must not deduct stock.

The inquiry records:

- request/item reference
- requested material/item
- quantity where applicable
- availability response
- approved/available quantity
- notes
- user/date audit data

## 17.2 Final warehouse receipt

Warehouse final receipt is a physical completion transaction.

The current prototype has two source-specific routes:

- repair receipt
- stage-based receipt

The C# system can unify the UI/domain operation while preserving the underlying reference to the correct item/route.

A duplicate receipt must be prevented.

## 17.3 External return is not warehouse final receipt

An external stage returning to the company is **not** the same thing as final warehouse receipt.

```text
External return
  → WAITING_QC
  → QC inspection
```

whereas:

```text
Final warehouse receipt
  → completion/received state
```

These are different business events and must remain separate.

## 17.4 Warehouse history

A warehouse-history view is required to answer:

- when an item was received
- by whom
- which request/item/stage it belonged to
- previous warehouse movements where supported

The detailed warehouse movement model can evolve later, but the history must never overwrite prior receipt records.

---

# 18. Contract Control

The contract subsystem remains part of the current design where a request requires a contract before production.

## 18.1 Contract determination by Planning

After Drawing confirms that the requested work is technically doable, Planning must check whether the request requires a contract. This check occurs before the request is allowed to proceed through a contract-required production release.

If a contract is required, Planning must:

1. mark the request as `ContractRequired = true`;
2. create/open the contract form;
3. enter the contract information;
4. import the contract PDF/document into the application;
5. submit the contract to Management for approval.

The contract record remains associated with the request and customer and is independently searchable.

## 18.2 Contract gate

`ContractRequired` belongs to the request, not individual request items.

When `ContractRequired = true`, production release requires:

- an active/current approved contract
- required contract PDF/document present

The gate must execute before production movement is committed.

## 18.3 Contract entity

The current Access design contains a contract header analogous to `tblContracts` with:

- ContractID
- RequestID
- ContractYear
- ContractSequence
- ContractNo
- ContractTitle
- ContractDate
- Contract document
- ContractStatusCode
- IsCurrent
- IsActive
- management approval/rejection fields
- notes
- audit fields

## 18.4 Contract status

Documented contract states:

- DRAFT
- WAITING_MANAGEMENT_APPROVAL
- APPROVED
- REJECTED
- CANCELLED
- EXPIRED (reserved for later validity control)

## 18.5 Contract navigation and dashboard notifications

The application must provide contract navigation/search to:

- Planning
- Management
- Warehouse

Search/navigation criteria must include, at minimum:

- Contract Number
- Contract Date
- Customer

Additional request-based lookup may be available, but the three fields above are mandatory.

When Management approves a submitted contract:

- the approval is written to the contract history/audit fields;
- Planning is informed in its dashboard;
- Warehouse is informed in its dashboard;
- the request's production gate is evaluated from the approved/current contract state.

The dashboard notification represents an operational alert/readiness indicator. It must not duplicate the contract as a separate business object or require users to manually notify each other outside the system.

## 18.6 Contract value calculation

The source specification states that contract value calculation was intentionally kept outside Access for the current simplified contract route.

That principle is preserved.

The current C# specification therefore does **not** invent a new contract-pricing algorithm.

Where contract-required logic is used, the contract approval/gate behavior remains authoritative.

Older fields such as:

- CalculatedContractValue
- ManagementFinalContractValue
- FinalManagementApproved
- FinalManagementApprovalDate
- FinalManagementApprovedByUserID

are deprecated in the current simplified contract route unless later required, but must not be deleted from any migration source until dependency/data audit is complete.

---

# 19. Costing

## 19.0 Currency standard

All currency and monetary amounts in the system use **Iranian Rial (IRR)** as the sole canonical currency in v1.

This includes, at minimum:

- machine hourly rates
- material price per kilogram
- raw material cost
- machine/work cost
- calculated item cost
- management final cost
- contract amounts
- any other prices, rates, or monetary values introduced later

The UI should use appropriate numeric formatting with thousand separators and an explicit Rial indication. The system must not display Toman as a substitute unit.

Historical calculations must retain the monetary values actually used at the time of calculation, while later changes to rate tables must not silently alter previously approved calculations.

## 19.1 Applicability

Current costing rules distinguish:

### Customer Repair

Initial item cost is based on actual production work sessions and valid machine hourly rates.

Machine hourly cost/rate master data is maintained by Planning / Project Control. Raw-material cost-per-kg master data is maintained by Warehouse. Unit-of-measure master data is maintained by QC.

```text
MachineCost
= Sum(WorkedMinutes / 60 × MachineHourlyRate)
```

### Customer Make — non-contract

```text
TotalCost
= RawMaterialCost + MachineCost
```

where:

```text
RawMaterialCost
= RawMaterialWeightKg × PricePerKg
```

### Contract Make

The contract/proforma/final-management route remains authoritative. The system must not invent a duplicate contract-value calculation inside ordinary item costing.

## 19.2 Machine hourly rates

Historical machine-rate support is required.

Conceptual entity analogous to `tblMachineHourlyRates`:

- MachineHourlyRateID
- MachineID
- HourlyRate
- EffectiveFromDate
- EffectiveToDate
- IsActive
- Notes
- CreatedByUserID
- CreatedAt

The machine rate used for a cost calculation must correspond to the relevant calculation/work date.

## 19.3 Material prices

Conceptual entity analogous to `tblMaterialPriceRates`:

- MaterialPriceRateID
- MaterialCode
- MaterialName
- MaterialCategory
- PricePerKg
- EffectiveFromDate
- EffectiveToDate
- IsActive
- Notes
- CreatedByUserID
- CreatedAt

## 19.4 Item cost calculation

The current design uses an aggregate cost entity analogous to `tblItemCostCalculations`:

- ItemCostCalculationID
- RequestItemID
- RequestID
- CalculationDate
- CalculationTypeCode
- RawMaterialWeightKg
- MaterialPriceRateID
- MaterialPricePerKg snapshot
- RawMaterialCost
- TotalMachineMinutes
- TotalMachineCost
- CalculatedTotalCost
- ManagementFinalCost
- ManagementApproved
- ManagementApprovedByUserID
- ManagementApprovalDate
- ManagementNote
- SentToWarehouse
- SentToWarehouseAt
- CreatedByUserID
- CreatedAt

## 19.5 Machine-rate snapshot rule

The rate used for an old approved calculation must not silently change because a current rate table was later edited.

The calculated transaction therefore snapshots the rate/value used for the calculation.

## 19.6 Cost approval route

For customer repair and non-contract Make:

```text
Stage/QC completion
  ↓
Cost calculation
  ↓
Management price review
  ↓
Approve or adjust
  ↓
Release to warehouse
  ↓
Warehouse receipt
```


Contract Make follows the contract route.

## 19.7 Detailed cost lines

A future detailed table analogous to `tblItemCostCalculationDetails` is required so management can see exactly how machine cost was built.

Proposed concept from the source:

- calculation reference
- work-session reference
- machine
- production unit
- worked minutes
- hourly rate snapshot
- row cost
- operation/stage context

This is a backlog item, not a reason to invent a different costing model now.

---

# 20. Quantity and Unit-of-Measure Model

The system needs to distinguish:

- order quantity
- order unit
- tracking quantity
- tracking unit
- quantity produced
- quantity rejected
- quantity accepted
- quantity sent externally
- quantity returned
- quantity reworked

The source specification also documents unit conversion support through a `tblUnitOfMeasures` concept.

Example:

```text
PAIR → PCS
QtyPerOrderUnit = 2
```

The exact supported unit list can remain configurable.

## 20.1 Partial progress

A stage may operate on a subset of the total item quantity.

For example:

```text
Requested = 100

Stage work:
70 completed
30 remaining
```

The model must support multiple work sessions and partial stage progress.

## 20.2 Partial QC

A QC result may split a stage output:

```text
Accepted = 90
Rejected = 5
Rework   = 5
```

Only the rejected/rework portion re-enters rework.

The 90 accepted pieces continue forward.

The precise balance/reconciliation algorithm should be implemented centrally so UI forms cannot accidentally create impossible quantities.

---

# 21. Machine Downtime

Downtime is separate from work performed on an item/stage.

Conceptual entity:

`MachineDowntime`

Fields:

- DowntimeID
- MachineID
- ProductionUnitID
- StopReasonID
- StartDateTime
- EndDateTime
- DurationMinutes
- ReportedByUserID
- ApprovedByUserID
- Note
- CreatedAt

Documented stop reasons include:

- power outage
- preventive maintenance / PM
- breakdown
- no operator
- no material
- no drawing
- no QC result
- setup/changeover
- other

Available machine time is conceptually:

```text
Shift time - approved downtime
```

Downtime must not be incorrectly attributed to operator performance when the cause is external to the operator.

---

# 22. Operator and Unit Performance Reporting

## 22.1 Daily production report

The daily production report is generated from `ItemStageWorkSession` transactions.

It must include both:

- MANUAL work entries
- AUTO work entries

The report should show:

- date
- operator
- unit
- machine
- request
- item
- stage
- worked minutes
- produced quantity
- rejected quantity
- work status
- notes
- entry mode

Multiple partial work rows on the same day are valid.

## 22.2 24-day performance period

The documented performance period is Jalali day 1 through day 24 of each month.

The report must be labeled as a **24-Day Performance Ranking**, not a full-month final production report.

The purpose is to make performance information available several days before salary/reward processing.

Work after day 24 may be carried to the next cycle or handled as a supplemental adjustment according to later management policy.

## 22.3 Current scope boundary

The C# project shall implement the reporting infrastructure first.

The actual operator reward/ranking formula is **Phase 2 / later work** because the source specification does not yet contain a finalized scoring formula.

Do not invent the final reward formula.

## 22.4 Future attendance data

A future attendance entity is documented:

`OperatorDailyAttendance`

Fields:

- AttendanceID
- OperatorID
- WorkDate
- NormalWorkMinutes
- OvertimeMinutes
- LeaveMinutes
- AbsenceMinutes
- AttendanceNote
- CreatedByUserID
- CreatedAt

This is required before a fully fair reward calculation can be considered complete.

---

# 23. Request-Centric Search and Customer Follow-Up

The system must be request-centric rather than project-centric.

Planning / Project Control and Management need a search screen that can answer customer follow-up calls quickly.

## 23.1 Request lookup

The planned screen should support search by:

- RequestNo
- BuyerName
- ItemNo
- PartName
- RequestDate range
- RequestType
- current request status
- open-only filter

## 23.2 Request result summary

The header should show:

- RequestNo
- request date
- customer
- title
- current request status
- current department
- latest workflow action
- contract-required/contract-approved state where relevant

Emergency information must not appear because Emergency has been removed from the canonical system.

## 23.3 Item results

One row per RequestItem should display, where applicable:

- ItemNo
- PartName
- order quantity
- tracking quantity
- WorkType
- current operational state
- current production unit
- current stage
- production priority
- planned start date/time
- planned end date/time
- planned machining/work minutes
- latest QC result
- cost approval state
- warehouse receipt state
- last update
- external/supply indicators

## 23.4 Navigation

The selected item should provide access to:

- request details
- item details
- production progress
- request workflow history
- QC history
- cost record
- warehouse history
- relevant documents

---

# 24. Documents and PDFs

## 24.1 Document types

Document concepts from the source include:

- REQUEST_PDF
- DRAWING_PDF
- BUY_REQUEST_PDF
- QC_REPORT_PDF
- CONTRACT_PDF

Additional document types may be added when a business need is established.

## 24.2 Canonical ownership

Documents can be associated with:

- Request
- RequestItem
- ItemStage
- QC Inspection
- Contract
- other supported business entity as later defined

## 24.3 Storage

Final C#/SQL Server architecture:

- PDF content stored in SQL Server.
- Metadata stored alongside the document record.
- UI opens/downloads/views the document through the application's document service.
- The application should avoid loading large unrelated document content into ordinary list queries.

The source-path-only Access strategy must not be carried forward as the canonical C# behavior.

## 24.4 Lightweight frontend requirement

Normal request/item/stage queries should retrieve metadata and structured fields only.

Binary document content should be fetched on explicit request/open operations rather than by default in large lists.

---

# 25. Security and Permissions

## 25.1 Identity

Authentication is application-local:

```text
Username + Password
```

## 25.2 User relationship

Each user has exactly:

```text
User
 ├── Department
 └── Role
```

## 25.3 Permission principle

Actions depend on:

- authenticated user
- department
- role
- request state
- item state
- stage state
- action-specific authorization

The UI may hide/disable actions for convenience, but business authorization must also be enforced in the application/domain layer.

## 25.4 Examples

Planning may:

- define items
- define stages
- release production
- perform planning workflow actions

Production supervisors/operators may:

- view their unit queue
- assign machines/operators where permitted
- record work sessions
- deliver completed work to QC

QC may:

- receive stages
- inspect
- approve/reject/rework
- correct prior QC decisions where authorized

Warehouse may:

- respond to inquiries
- register external dispatch/return
- receive final items
- view warehouse history where permitted

Management may:

- approve request actions
- approve contracts
- approve customer item costs
- view management dashboards/history

These examples preserve the business divisions documented in the source; the exact role matrix must be implemented from the final company-approved role list rather than invented.

---

# 26. Departments

Canonical departments:

- Central
- Management
- Planning / Project Control
- Drawing
- Warehouse
- Production Units
- QC
- External contractors / external suppliers

External contractors/suppliers are operational routes rather than ordinary application departments unless later business rules say otherwise.

---

# 27. Dates and Localization

The application is Persian and RTL.

Jalali/Shamsi is the **mandatory user-facing calendar throughout the application**. This applies to forms, dashboards, queues, request/item views, reports, history screens, filters, date pickers, messages, exports, and all other normal user-visible date representations.

The application must not present Gregorian dates to ordinary users as the normal date format. Gregorian/native `DateTime` values may exist internally in the database and application logic solely for reliable storage, comparison, sorting, indexing, and calculations.

Users must enter and edit dates using Jalali-aware controls. Date-range searches entered by users in Jalali form must be converted into Gregorian/native database date ranges before querying. Formatted Persian/Jalali strings must never be used as the underlying comparison value for date logic.

Jalali calendar support is also required for user-facing numbering and reporting periods.

The source prototype contains a Jalali helper concept with capabilities analogous to:

- ToJalali
- ToJalaliDateTime
- JalaliTextToDate
- Jalali date-range conversion
- Jalali-year extraction

The C# implementation may use a different library/implementation but must preserve the observable behavior.

Date-time displays should support 24-hour time.

Queries should compare actual database date/time values rather than comparing formatted Persian strings.

The exact timezone policy is an implementation/deployment decision and is not invented here.

---

# 28. Excel BOM Import

> **[Phase 0.1 annotation — superseded]** The positional column mapping tables in this section describe the legacy Access template only. The C# template is one mixed worksheet with Persian headers, mapped by header name, with an explicit Work Type column that is never inferred (Decision Log #16; Domain and Database Specification §21). Changed existing rows are conflicts requiring manual choice, not silent skips. Exact header strings and conflict options remain open. See audit X28.

Excel import is a **core C# v1 input method**, not merely a legacy prototype convenience. Some customer requests may contain hundreds of items and can reach approximately **1,000 items**, making manual entry impractical. The application must therefore allow item characteristics to be prepared/entered in Excel and imported into a selected customer request.

The goal is to let Planning/Drawing populate large item lists efficiently while preserving the same item-level validation and downstream production workflow used for manually entered items.


The C# application should preserve the business behavior:

- Excel can supply complex externally prepared item lists.
- Re-importing the same request must not duplicate existing items.
- Existing items are identified at least by RequestID + ItemNo.
- ItemNo is text, not numeric.
- Merged-cell source layouts are supported in the existing prototype.

Current documented Excel mapping:

| Excel column | Field |
|---|---|
| A | ItemNo |
| B:E | PartName — top-left cell of merged range |
| F:I | Dimensions — top-left cell of merged range |
| J:K | MaterialName — top-left cell of merged range |
| L | RequestedQty |
| M | SupplyTypeID |
| N | SupplyRequestNo |

`ItemNo` must remain text to preserve values such as:

- `1.10`
- alphanumeric prefixes
- hierarchical subassembly numbering

The C# import implementation may use a different spreadsheet library, but it must preserve these business behaviors.

---

### 28.1 Large request / high-volume import requirement

Some customer requests may contain **hundreds or around 1,000 items**. Manual row-by-row entry is not an acceptable primary method for such requests.

The C# application must therefore provide a dedicated Excel import workflow for request items.

Required behavior:

1. Planning/Drawing selects the target customer request.
2. The user selects an Excel workbook containing item characteristics.
3. The application previews/validates the rows before committing them.
4. Valid rows are imported into the selected request as item records.
5. `ItemNo` is treated as Short Text/string and must preserve values such as `1.10`, hierarchical numbering, leading zeros, and alphanumeric identifiers.
6. Existing request items are not duplicated when the same `RequestID + ItemNo` already exists.
7. Re-importing an updated workbook must be able to add newly added items without duplicating previously imported items.
8. Import errors must identify the affected Excel row and the reason for failure so the user can correct the workbook.
9. The import must not partially corrupt the request. The C# implementation should use a transaction or equivalent all-or-reject strategy for each import batch where practical.
10. Imported items must immediately participate in the same Make/Repair, stage-definition, progress, QC, and production workflow as manually entered items.
11. For a large import, the UI must show a clear import summary (rows detected, rows accepted, rows skipped as duplicates, rows rejected with errors).
12. The import process must not require the operator to create 1,000 item records manually after import; imported rows are first-class request items.
13. The request progress view must update after a successful import so the new total item count and progress denominator are immediately reflected.

The source prototype documents the following current column mapping and merged-cell behavior:

| Excel column | Field | Source behavior |
|---|---|---|
| A | ItemNo | Text |
| B:E | PartName | Top-left cell of merged range |
| F:I | Dimensions | Top-left cell of merged range |
| J:K | MaterialName | Top-left cell of merged range |
| L | RequestedQty | Quantity |
| M | SupplyTypeID | Supply route/type |
| N | SupplyRequestNo | Supply request identifier |

The C# implementation may use a different spreadsheet library from Access, but these business/data behaviors must be preserved.

# 29. Production Release and Sync

The current Access prototype has a production release operation analogous to:

`btnSendItemsToProduction`

This is now conceptually a general production-release/update operation, not a repair-only finalize action.

The current behavior includes a distinction between:

- first production release
- synchronization/update after the request is already in production

## 29.1 First release

The application validates the request's items/stages and contract gate where applicable, then releases the appropriate first stages.

## 29.2 Sync after release

If the request is already in production and item/stage data has changed, the system must not reset active production queues.

The prototype's important behavior is:

- active stages are preserved;
- new/unreleased stages can be unlocked where appropriate;
- planning/item-data changes can become visible to production;
- the operation is logged.

The exact synchronization algorithm should be implemented as a transaction/service rather than reproducing Access button/VBA code.

## 29.3 Active-stage protection

The current prototype specifically avoids blindly resetting stages already in states such as:

- WAITING
- IN_PROGRESS
- EXTERNAL_SENT

The C# system must preserve the same intent.

---

# 30. Canonical Data Model — C# View

The following entities form the core conceptual model.

## 30.1 Request

Key concepts:

- RequestID
- RequestNo
- RequestDate
- RequestType (optional/legacy-compatible; CUSTOMER in v1)
- Buyer
- Title/Description
- CurrentDepartment
- CurrentStatus
- ContractRequired
- Notes
- audit metadata

Derived request-level progress should be calculated from RequestItem and ItemStage state rather than stored as a manually editable percentage.

## 30.2 RequestItem

Key concepts:

- RequestItemID
- RequestID
- ItemNo
- PartName
- quantities and units
- WorkType
- material/dimensions
- drawing/document references
- supply type
- Make-related fields
- cost-related selection fields where appropriate
- audit metadata

## 30.3 RepairItemDetails

Repair-only attributes:

- RepairItemCode
- SerialNo
- ExitNo
- ReceivedDate
- RepairWorkDescription
- HasReferenceSample
- RepairRequiredDate
- RepairRequiredNote
- drawing flags
- current repair status
- audit metadata

No Emergency fields.

## 30.4 ItemStage

Key concepts:

- ItemStageID
- RequestItemID
- StageNo
- StageTitle
- ProductionUnitID
- StandardMachineTimeMin / PlannedDurationMinutes
- Priority / PriorityCode
- PlannedStartDateTime
- PlannedEndDateTime
- StageStatus
- StageNotes
- external classification
- planned/summary values
- actual execution timestamps where applicable

## 30.5 ItemStageWorkSession

Key concepts described in Section 11.

## 30.6 QCInspection

Generic QC transaction for stage-based/relevant repair workflows.

## 30.7 CostCalculation

Aggregate item costing transaction described in Section 19.

## 30.8 WarehouseTransaction

Stores final receipt and later warehouse movement history where supported.

## 30.9 ExternalWorkDispatch

Stores send-out/return-to-QC information for external stages.

## 30.10 Contract

Stores the request-level contract header and management approval/gate state.

## 30.11 MachineHourlyRate

Historical effective-dated machine rates.

## 30.12 MaterialPriceRate

Historical effective-dated material prices.

## 30.13 ProductionUnit

Internal/external production destination.

## 30.14 ProductionUnitCategory

Optional classification of production units including external categories.

## 30.15 Machine

Machine definition linked to ProductionUnit.

## 30.16 Operator

Operator definition linked to primary ProductionUnit.

## 30.17 User / Role / Department

Application authentication and authorization.

## 30.18 Lookup/reference entities

Includes concepts corresponding to:

- RequestStatus
- StageStatus
- SupplyType
- UnitOfMeasure
- StopReason
- WorkType
- repair item statuses
- QC statuses/reason codes
- contract statuses

---

# 31. Source-of-Truth Rules

These rules are critical and must be encoded consistently in the C# application.

1. `Request` is the authoritative request header/current request-level state.
2. Request workflow history is separate from stage execution history.
3. `RequestItem` is the canonical item within a request.
4. `WorkTypeID` determines MAKE vs REPAIR for an item.
5. A CUSTOMER request may contain mixed MAKE and REPAIR items.
6. Projects are not required for production execution.
7. `ItemStage` is the canonical ordered production route for a request item.
8. `ItemStageWorkSession` is the source for actual production work reporting.
9. QC history is transactional and historical; corrections do not erase old inspections.
10. The current QC result must be distinguishable from historical/corrected decisions.
11. External return-to-QC is distinct from final warehouse receipt.
12. Warehouse inquiry does not deduct stock.
13. Cost calculations must snapshot the rate/value used.
14. ContractRequired is request-level.
15. PDFs/documents belong to business entities through explicit document references.
16. Binary document content is stored in final SQL infrastructure, not as filesystem paths in the canonical model.
17. Persian is the user-facing language; English technical names may remain in the code/database.
18. Jalali is the user-facing calendar representation.
19. No Emergency concept is canonical.
20. No separate Final QC concept is canonical.
21. Historical records must not be deleted merely to simplify current-state display.
22. Business authorization must not rely only on disabled/hidden UI buttons.

---

# 32. Legacy Access Objects — Reference Only

The source file contains many Access tables/forms/queries/modules. They describe the history and working prototype and are not a requirement to reproduce Access structure literally.

## 32.1 Legacy tables of particular importance

- tblRequests → canonical Request concept
- tblRequestItems → canonical RequestItem concept
- tblRequestWorkflow → request workflow audit concept
- tblRepairItemDetails → RepairItemDetails
- tblRepairItemWorkflow → repair item history
- tblItemStages → ItemStage
- tblItemStageWorkSessions → ItemStageWorkSession
- tblItemQCInspections → generic QC inspection concept
- tblItemCostCalculations → CostCalculation
- tblItemWarehouseTransactions → WarehouseTransaction
- tblExternalWorkDispatches → ExternalWorkDispatch
- tblContracts → Contract
- tblProductionUnits → ProductionUnit
- tblProductionUnitCategories → ProductionUnitCategory
- tblMachines → Machine
- tblOperators → Operator
- tblMachineHourlyRates → MachineHourlyRate
- tblMaterialPriceRates → MaterialPriceRate

## 32.2 Legacy project objects

These are legacy/reference only for the new C# architecture:

- tblProjects
- tblItems
- tblProjectStatuses
- tblProjectStatusLogs
- project-oriented forms/queries

They may be needed during migration/history analysis because the existing factory application is project-based, but they must not become a mandatory C# production layer.

## 32.3 Legacy Access implementation details not to reproduce literally

- VBA modules
- DAO Recordsets
- OpenArgs
- Access Form_Load authorization helpers
- Access UNION type-inference workarounds
- bound Access-form mechanics
- Access-specific lookup fields
- Access AutoNumber behavior as a UI design
- Access button names as business rules
- Access path-only PDF storage
- Access modern/classic chart limitations

Their business intent should be preserved where relevant; their implementation mechanics may be replaced.

---

# 33. Current Prototype State — What the Existing Access Development Already Contains

This section preserves the source document's development history while making the distinction between **prototype state** and **factory deployment** explicit.

## 33.1 Repair branch

The development prototype documents that the repair branch was rebuilt around:

- general `tblRequestItems`
- repair-specific `tblRepairItemDetails`
- repair item statuses
- repair item workflow
- repair QC
- repair warehouse receipt
- management report
- item history

The old duplicated repair fields were removed from `tblRequestItems` in the prototype.

- shared production release

> **[Phase 0.1 editorial note]** The heading for §33.2 appears to be missing from the source; the content of that section was not reconstructed.

## 33.3 Make details

The prototype contains focused Make-detail handling rather than putting every Make field into every form.

## 33.4 Production stages

The prototype contains a focused stage-definition form and the shared stage-release/production queue model.

## 33.5 Manual work entry

The prototype defines/implements the production-unit-aware manual work-entry concept and unified work-session reporting.

## 33.6 Unit dashboards

The prototype contains:

- production-unit launcher
- production-unit dashboard
- unit queue
- live production overview
- daily production reporting

Testing of some dashboard branches was deferred.

## 33.7 Costing infrastructure

The prototype includes:

- machine hourly rate infrastructure
- material price rate infrastructure
- costing helper logic
- aggregate item cost calculation
- management cost approval route

Detailed per-work-session cost lines remain a backlog item.

## 33.8 Unified QC

The prototype includes a unified QC inbox and QC correction/history model.

Legacy repair-only QC forms are temporary comparison/rollback artifacts and are not the target architecture.

## 33.9 Contract subsystem

The prototype includes:

- contract header
- contract statuses
- management approval/rejection
- production gate
- contract inbox
- warehouse contract alerts

The production gate is request-level through `ContractRequired`.

## 33.10 External work

The prototype includes:

- external production-unit classification
- external dispatch entity
- external return-to-QC logic
- combined external/supply inbox query concept

At the latest source update, `frmExternalWorkInbox` was still the next active UI task.

## 33.11 Warehouse receive

The prototype includes corrected unified warehouse inbox queries.

At the latest update, the warehouse receive form existed/opened but the complete repair/stage receive test cycle remained pending.

## 33.12 Request-centric lookup

`frmRequestLookup` remained a next-step feature intended to replace the project-centric lookup concept for customer follow-up.

## 33.13 Excel BOM import

The prototype includes Excel BOM import and duplicate-safe import behavior described in Section 28.

## 33.14 Production Sync & Release

The prototype includes safe release/update logic that avoids resetting already active stages.

---

# 34. Latest Development Boundary from the Source Prototype

The last source-document roadmap identifies the following sequence:

1. Build/test external work inbox.
2. Complete unified warehouse receive tests.
3. Build warehouse item history.
4. Build request-centric lookup.
5. Add detailed cost calculation lines.
6. Add external movement reporting.
7. Perform an integrated end-to-end scenario test.

The integrated scenarios named by the source are:

- one Make/casting request
- one external mid-stage request
- one repair request
- one contract-required request
- full QC/cost/warehouse completion

For the C# rewrite, these scenarios should become automated/manual acceptance scenarios rather than merely Access button tests.

---

# 35. C# Application Structure — Business-Safe Translation

The new C# application should separate business logic from UI behavior and storage mechanics.

A suitable conceptual structure is:

```text
WPF UI
   ↓
Application / Use Cases
   ↓
Domain / Business Rules
   ↓
Persistence / Infrastructure
   ↓
SQLite (development) / SQL Server (production)
```

This section is an architectural boundary, not a new business rule.

The key requirement is that:

- workflow state transitions are implemented once;
- permission checks are not duplicated only in screens;
- database queries are not the only location of business rules;
- stage release/QC gates cannot be bypassed by direct UI actions;
- historical records are append-oriented wherever appropriate;
- the storage layer can transition from SQLite to SQL Server without rewriting business logic.

The exact framework/library choices remain implementation decisions.

---

# 36. Transaction and Concurrency Principles

The application should use database transactions for logically atomic operations.

Examples:

## 36.1 Request workflow transition

A request transition should atomically:

1. validate authorization/state;
2. update current request state;
3. write workflow history;
4. update any required related state.

## 36.2 QC approval

A QC approval should atomically:

1. validate the inspection target;
2. record the QC result;
3. update the current stage state;
4. unlock the next stage when appropriate;
5. create any required next-route transaction.

## 36.3 External return

External return-to-QC should atomically:

1. validate that a dispatch is currently external;
2. record ReturnedAt/current user;
3. update dispatch state;
4. move stage to WAITING_QC.

## 36.4 Warehouse receipt

Warehouse receipt should atomically:

1. validate eligibility;
2. prevent duplicate receipt;
3. create the warehouse transaction;
4. move the item/stage to its final receipt state.

The exact SQL transaction implementation is an engineering decision, but these business outcomes must be atomic.

---

# 37. UI Principles

## 37.1 Language and direction

- Persian labels.
- RTL layout.
- Persian-friendly date display.
- Persian status titles for user-facing screens.
- English technical identifiers remain in code/database where useful.

## 37.2 Focused screens

The source repeatedly warns against turning one form into an overloaded universal editor.

The C# application should keep focused screens such as:

- Request
- Request Items
- Repair Details
- Make Details
- Stage Definition
- Unit Work Queue
- Daily Work Entry
- QC Inbox
- QC History/Correction
- External Work Inbox
- Warehouse Inbox
- Warehouse History
- Cost Approval
- Contract Inbox/Detail
- Request Lookup
- Management dashboards

## 37.3 Current-state display vs. history

Screens should distinguish:

- current status/state
- historical actions
- pending actions
- corrected/superseded records

A history page must not simply show the latest row and hide earlier evidence.

---

# 38. Core Workflow Snapshot

The canonical end-to-end model is:

```text
REQUEST
  ↓
INITIAL MANAGEMENT REVIEW
  ↓
PLANNING
  ↓
ITEM DEFINITION
  ↓
ITEM-SPECIFIC ROUTE / STAGES
  ↓
CONTRACT GATE WHEN REQUIRED
  ↓
FIRST STAGE
     ├── INTERNAL → PRODUCTION UNIT QUEUE
     └── EXTERNAL → EXTERNAL DISPATCH / SUPPLY
  ↓
PRODUCTION OR EXTERNAL COMPLETION
  ↓
QC
  ├── APPROVED → NEXT STAGE
  └── REJECTED → REWORK / EXTERNAL CORRECTION
  ↓
NEXT STAGE ...
  ↓
WHEN ITEM ROUTE IS COMPLETE
  ↓
COSTING WHERE APPLICABLE
  ↓
MANAGEMENT PRICE APPROVAL WHERE APPLICABLE
  ↓
WAREHOUSE RECEIPT
  ↓
COMPLETE / CLOSED
```

There is no mandatory project conversion in this flow.

There is no Emergency bypass branch.

There is no separate final-QC branch.

---

# 39. Acceptance Test Scenarios

The following scenarios should be treated as foundational tests for the C# application.

## 39.1 Mixed customer request

Create one CUSTOMER request containing:

- one MAKE item
- one REPAIR item

Verify that:

- both items belong to the same request;
- WorkType distinguishes them correctly;
- each item can have its own stages;
- production views retain RequestNo and ItemNo.

## 39.2 Two identical items

Create two items with the same PartName.

Verify that production users can distinguish them using RequestNo + ItemNo and other identifiers.

## 39.3 First internal stage

Release a request item whose Stage 1 is internal.

Expected:

```text
Stage 1 = WAITING
```

It appears in the correct unit queue.

## 39.4 First external stage

Release an item whose Stage 1 is Ceramic/Casting/another external supply route.

Expected:

```text
External dispatch created/registered
Stage 1 = EXTERNAL_SENT
```

After return:

```text
Stage 1 = WAITING_QC
```

After QC approval:

```text
Stage 2 = WAITING
or
Stage 2 = WAITING_EXTERNAL_SEND
```

according to the next stage's route.

## 39.5 QC gate

Complete Stage 1 without QC approval.

Verify Stage 2 remains locked.

Approve Stage 1 through QC.

Verify Stage 2 unlocks.

## 39.6 Partial QC

Send 100 pieces to QC and record:

```text
Accepted = 90
Rejected = 5
Rework = 5
```

Verify:

- 90 advance;
- 5 go to rework;
- 5 remain rejected according to the defined QC outcome; *(superseded: the 5 rejected pieces are SCRAP — Decision Log #7; audit X13)*
- total quantities remain consistent.

## 39.7 QC rejection/rework

Reject a stage.

Verify:

```text
QC_REJECTED / REWORK_REQUIRED
 → production
 → work
 → QC again
```

## 39.8 QC correction

Correct a previous QC result.

Verify:

- original inspection remains historical;
- correction is marked;
- affected stage/item reopens appropriately;
- downstream processing is not silently erased;
- previous cost/warehouse result is no longer treated as current if invalidated by the correction.

## 39.9 Warehouse inquiry

Create a warehouse inquiry.

Verify:

- response is recorded;
- no inventory balance is deducted.

## 39.10 External stage return

Send an external stage.

Return it.

Verify:

- ReturnedAt is generated by the transaction time;
- dispatch is marked returned;
- stage becomes WAITING_QC;
- it appears in QC.

## 39.11 Costing

For a non-contract Make item:

- create work sessions on machines with different rates;
- calculate machine cost per work session;
- add raw-material cost;
- route to Management approval;
- release to Warehouse only after approval.

> **[Phase 0.1 editorial note]** There is no §39.12 in the source; the numbering is left unchanged.

## 39.13 Contract gate

For a request with `ContractRequired = true`:

- no approved active/current contract → production release blocked;
- approved active/current contract with required document → production release allowed.

## 39.14 Work-entry reporting

Enter multiple MANUAL work rows for one operator/day.

Verify the daily report shows all rows.

## 39.15 Sync release

Release a request to production.

Change/add planning data.

Run Sync.

Verify active stages are not reset or duplicated.

## 39.16 BOM import

Import 50 items.

Re-import an updated Excel containing the existing 50 plus 5 new items.

Verify only the 5 new items are added.

---

# 40. Migration Strategy

The migration path is:

```text
Existing factory Access system
        │
        │ reference / historical analysis
        ↓
New C# application
        │
        ├── SQLite during development/testing
        │
        ↓
Company approval
        │
        ↓
SQL Server production backend
```

The new C# system is not required to preserve the Access database schema exactly.

The migration should preserve business data and traceability rather than Access implementation quirks.

## 40.1 Migration principles

- Identify legacy-to-canonical mappings before importing data.
- Do not make project tables mandatory merely because the old factory system uses them.
- Preserve old request/project references when useful for historical traceability.
- Preserve document content where legally/operationally necessary.
- Preserve QC history.
- Preserve production history.
- Preserve cost snapshots.
- Preserve warehouse receipt history.
- Do not silently merge different physical items merely because their names match.

---

# 41. Deprecated / Removed Concepts

The following are explicitly not part of the new canonical business model.

## 41.1 Removed: Emergency

Do not implement Emergency as a request flag, workflow bypass, or approval subsystem.

## 41.2 Removed: Project conversion as production prerequisite

Do not implement:

```text
Request → Project → Production
```

as a mandatory route.

## 41.3 Removed: Final QC flag/routing concept

Do not implement a separate final-QC requirement based solely on the obsolete `NeedsFinalQC` (legacy only) model.

QC occurs after individual production stages.

## 41.4 Removed: Access filesystem-path document model

Do not use Access's path-only PDF strategy as the canonical C# storage architecture.

## 41.5 Removed: Access-specific implementation coupling

Do not reproduce VBA modules/forms/DAO mechanics literally.

---

# 42. Backlog

The following items remain future work rather than current core requirements.

## Near-term

- Complete external-work inbox behavior.
- Complete warehouse receive testing.
- Warehouse item history.
- Request-centric lookup.
- External movement reporting.
- Integrated scenario tests.

## Later

- Detailed cost calculation lines.
- Advanced cost breakdown/reporting.
- Attendance/overtime/leave integration.
- 24-day ranking formula.
- Full KPI dashboards.
- Additional warehouse movement reporting.
- Expanded external movement analytics.

## Explicitly not to invent now

- reward formula
- full inventory management algorithm
- contract-value calculation algorithm
- new emergency logic
- mandatory project hierarchy
- separate final-QC workflow

---

# 43. Engineering Rules for the C# Rewrite

1. Preserve business behavior first.
2. Replace Access implementation mechanics rather than translating VBA literally.
3. Keep Request, RequestItem, ItemStage, WorkSession, QC, Cost, Warehouse, and External Dispatch as distinct concepts.
4. Make stage transitions centralized and testable.
5. Make QC gating enforceable outside the UI.
6. Preserve historical events rather than overwriting them.
7. Keep document binaries out of ordinary list queries unless explicitly requested.
8. Keep the database provider replaceable between SQLite development and SQL Server production.
9. Keep Persian UI behavior separate from English technical/domain identifiers.
10. Keep Jalali formatting/display separate from database date storage/query logic.
11. Do not treat a UI button name as a business rule.
12. Do not use database numeric IDs as if they were permanent business meaning; use stable codes where workflow logic needs semantic identity.
13. Avoid a single giant query for complex operational dashboards when modular query/application aggregation is more maintainable.
14. Protect active production stages during synchronization.
15. Prevent duplicate warehouse receipts and duplicate external dispatches where the source rules require uniqueness.
16. Snapshot historical cost inputs.
17. Never delete QC history when correcting a decision.
18. Never silently convert the system back to project-centric behavior.

---

# 44. Final Canonical Mental Model

The new system is best understood as a **request-centric manufacturing workflow engine** rather than a project registry.

The core structure is:

```text
                         ┌───────────────┐
                         │    REQUEST    │
                         └───────┬───────┘
                                 │
                     ┌───────────┴───────────┐
                     │                       │
               CUSTOMER ONLY (v1)
                     │                       │
              ┌──────┴──────┐               │
              │             │               │
            MAKE          REPAIR             │
              │             │               │
              └──────┬──────┘               │
                     │                       │
                     └───────────┬───────────┘
                                 │
                         REQUEST ITEMS
                                 │
                      ┌──────────┴──────────┐
                      │                     │
                  WORK TYPE             ITEM DATA
                 MAKE/REPAIR           + DOCUMENTS
                      │
                      ▼
                 ITEM STAGES
                      │
          ┌───────────┼────────────┐
          │           │            │
       INTERNAL     EXTERNAL     EXTERNAL
      PRODUCTION     SUPPLY      PROCESSING
          │           │            │
          └───────────┴────────────┘
                      │
                  WORK SESSIONS
                      │
                     QC
                      │
             ┌────────┴────────┐
             │                 │
          APPROVED          REWORK
             │                 │
       NEXT STAGE         BACK TO WORK
             │
             ▼
      COSTING IF APPLICABLE
             │
    MANAGEMENT APPROVAL
      IF APPLICABLE
             │
             ▼
       WAREHOUSE RECEIPT
             │
             ▼
         COMPLETION
```

The defining architectural principle is:

> **A request identifies the business case; an item identifies the distinguishable work object; a stage identifies the ordered operation; production records the work; QC controls movement between stages; external routes are still stages; and history remains auditable throughout.**

---

# Extensibility and Controlled Future Updates

This specification is intended to be a **living specification**. The initial C# application is not required to contain every future ERP/MES feature on day one. New requirements may be added after factory testing and management review.

Future changes must be introduced by updating the specification and versioning the affected business rules before implementation. A later feature must not silently contradict the canonical Request → RequestItem → ItemStage → Production/External Route → QC architecture.

Examples of future additions that may be added later include:

- additional reports and dashboards;
- advanced scheduling/capacity rules;
- detailed cost breakdown;
- attendance/overtime integration;
- expanded warehouse movement history;
- additional external-work analytics;
- richer document management;
- additional permissions or roles;
- future SQL Server operational enhancements.

Adding a future feature must not require rewriting historical audit records merely to make the new feature function.

---

# Appendix A — Source Specification Crosswalk

| Source area | C# treatment |
|---|---|
| §§1–3 | Scope, departments, and request foundation preserved. |
| §4 | Superseded by customer-only request processing in v1 and WorkType=MAKE/REPAIR at item level. |
| §§5–7 | Request/document/workflow behavior preserved; document storage updated per owner decision. |
| §§8–9 | Repair/Make behavior preserved, with Project conversion removed as a prerequisite. |
| §10 | Emergency concept removed by owner decision. |
| §§11–16 | Production, stage assignment, QC handoff, work reporting, downtime preserved. |
| §17 | Status vocabulary retained selectively; obsolete emergency/project/final-QC concepts removed. |
| §§18–23 | Implementation/migration principles retained and translated out of Access terminology. |
| §§24–31 | Repair implementation/current state preserved; current prototype facts distinguished from factory deployment. |
| §§32–37 | Make/project-conversion sections reconciled using owner decision: no mandatory Projects. |
| §§38–45 | Current prototype architecture preserved selectively and translated into the request/item/stage model. Legacy internal-request branch is excluded. |
| §§46–49 | Unit dashboards, work sessions, costing, unified QC, warehouse, and request lookup preserved. |
| §§50–56 | Contract gate, external work, warehouse behavior, and workflow snapshot preserved. |
| §§57–58 | BOM import, Sync & Release, UI normalization, and latest roadmap preserved. |

---

# Appendix B — Canonical Decisions Recorded from Owner Review

1. All operational requests supported in v1 are customer requests. `RequestTypeID`, if retained for migration compatibility, identifies CUSTOMER only and is not used to create a separate internal-request workflow.
2. Work Type is MAKE or REPAIR at item level.
3. A CUSTOMER request may contain both MAKE and REPAIR items.
4. Projects are not mandatory in the new system.
5. The new system is request-based and item/stage-driven.
6. Every item has its own ordered production stages.
7. Every stage after the first is gated by previous-stage QC approval.
8. The first stage may be external supply and must then return through QC before the next stage.
9. SQLite is temporary and single-user for development.
10. SQL Server is the intended final backend.
11. Authentication uses local application username/password.
12. Each user has one department and one role.
13. Warehouse inquiry records availability information but does not maintain/deduct full inventory.
14. WorkTypeID distinguishes Make/Repair.
15. Partial quantities are supported; accepted quantities continue and only rejected/rework quantities loop back.
16. The final-QC concept is removed.
17. External supply is represented as a production stage.
18. Emergency logic is removed.
19. QC may correct prior decisions; history must remain; in the rare confirmed correction scenario, the item returns to the affected earlier stage and downstream stages are handled manually.
20. PDFs are stored as document content in the final SQL Server architecture rather than only as filesystem paths.
21. The application is Windows-only, WPF/.NET, Persian, and RTL.
22. Business behavior is preserved; Access-specific implementation may be redesigned.
23. Performance-reporting infrastructure is part of the first implementation scope; actual reward calculation is deferred to Phase 2.
24. Planning checks contract requirement after Drawing confirms technical doability.
25. When a contract is required, Planning creates/submits the contract and imports its PDF; Management approves or rejects it.
26. Approved contracts must become visible/notified in both Planning and Warehouse dashboards.
27. Contracts are searchable by Contract Number, Contract Date, and Customer for Planning, Management, and Warehouse.
28. QC owns Unit-of-Measure master-data definition.
29. Planning owns machine machining-cost/hour master data.
30. Warehouse owns raw-material cost/kg master data.
31. Customer requests may contain hundreds of items and around 1,000 items must be supported through Excel import.
32. Request pages must show request-level completion progress and per-item stage progress.
33. Management requires a live production work-queue dashboard.
34. Planning controls production priority and planned machining/work time. Machining/work time is entered as **minutes per piece** for each item stage. Planned total processing time is calculated from the applicable quantity multiplied by the minutes-per-piece estimate.
35. Planning may change production priority after work has entered a queue, and the live queue must reflect that change. Management is authorized to override the normal Planning queue order; each override must record the user, timestamp, previous order/priority, new order/priority, and reason.
36. External stages may have an expected lead time, which must be available to the scheduling model.
37. Planned end dates are calculated from Planning inputs and the configured production schedule. The exact factory calendar/capacity algorithm remains an explicit technical/business design item until finalized.
38. All user-facing dates are Jalali/Shamsi and all monetary values are Iranian Rial.
39. The specification is intentionally extensible; later approved requirements may add modules or refine rules without invalidating the core Request → Item → Stage → QC architecture.

# Appendix C — Items Requiring Explicit Confirmation Before Their Implementation

These are intentionally left as implementation decisions or future business-policy decisions rather than guessed.

1. Exact production-role/permission matrix for every role and department.
2. Exact list of accepted machine assignment rules per production unit.
3. Exact SQL Server document-storage schema for PDF binaries and metadata.
4. Exact reward/ranking formula for the 24-day performance period.
5. Exact attendance/overtime/leave policy.
6. Exact contract-value calculation process if the company later wants it inside the system.
7. Exact behavior for unusually complex QC corrections that affect multiple already-completed stages beyond the confirmed rare Stage-1 correction scenario.
8. Exact long-term warehouse movement model beyond final receipt/history.
9. Exact final .NET version and infrastructure libraries.
10. Exact Excel workbook column/location used to identify `WorkTypeID` (`MAKE` vs `REPAIR`) for imports. The existing prototype mapping documents columns A:N but does not define a WorkType column; the C# system must support mixed Make/Repair items in one request, so this mapping must be explicitly defined before implementing the import template/validator.
   > *Phase 0.1 annotation: partly superseded — an explicit Work Type column in a mixed worksheet is decided (Decision Log #16). The exact header string and accepted values remain open (audit U32).*

No C# implementation should make a business decision from this appendix silently. Each item must be explicitly settled before the corresponding feature is implemented.

---

# Appendix D — Legacy Access Source Objects Worth Preserving During Migration Audit

The source document lists many additional historical objects. They should be retained in a migration/dependency inventory until the old factory system has been mapped, but they should not automatically become C# entities or screens.

Examples include:

- tblBuyers
- tblDepartments
- tblIncomingApprovals
- tblMaterialTypes
- tblPdfFiles
- tblProductionReportOperators
- tblProductionReports
- tblQCInspections
- tblQCRejectionReasons
- tblRequestItemSupplySplits
- tblRequestMaterials
- tblRequestStatuses
- tblRoles
- tblSearch
- tblShiftTypes
- tblStopReasons
- tblSupplySplitStatuses
- tblSupplyTypes
- tblUsers
- tblWorkTypes

The correct C# model should be derived from the **business purpose** of these objects rather than blindly recreating their Access structure.

---

# End of C#-Ready Master Specification + Current State

> **[Phase 0.1 editorial note]** Appendices E–G and the closing "Canonical Business Decision Update — 2026-10-09" sections follow this marker and are part of the document.

# Appendix E — Current Access Prototype Object Registry (Migration Reference)

This appendix is retained so the C# implementation team can map the existing prototype to the new application without treating Access object names as architectural requirements.

## E.1 Prototype tables

| Access object | C# treatment |
|---|---|
| tblRequests | Request |
| tblRequestTypes | RequestType |
| tblRequestItems | RequestItem |
| tblUnitOfMeasures | UnitOfMeasure |
| tblItemStages | ItemStage |
| tblItemStageWorkSessions | ItemStageWorkSession |
| tblRepairItemDetails | RepairItemDetails |
| tblRepairItemStatuses | RepairItemStatus |
| tblRepairItemWorkflow | RepairItemWorkflow |
| tblRepairQCInspections | Legacy repair QC / source history; generic QC is target |
| tblRepairQCStatuses | Repair QC status lookup / migration reference |
| tblRepairWarehouseReceipts | Legacy repair warehouse receipt history; unified warehouse target |
| tblItemQCInspections | Generic QCInspection |
| tblItemCostCalculations | ItemCostCalculation |
| tblItemWarehouseTransactions | WarehouseTransaction |
| tblExternalWorkDispatches | ExternalWorkDispatch |
| tblContracts | Contract |
| tblProductionUnits | ProductionUnit |
| tblProductionUnitCategories | ProductionUnitCategory |
| tblMachines | Machine |
| tblOperators | Operator |
| tblStopReasons | StopReason |
| tblSupplyTypes | SupplyType |
| tblMachineHourlyRates | MachineHourlyRate |
| tblMaterialPriceRates | MaterialPriceRate |
| tblOperatorDailyAttendance | Future OperatorDailyAttendance |

## E.2 Prototype forms of business importance

- frmRequests
- frmRequestItems
- frmRepairItemDetailsEntry
- frmMakeItemDetails
- frmItemProductionStages
- frmUnitWorkQueue
- frmProductionUnitDashboard
- frmProductionUnitLauncher
- frmProductionLiveOverview
- frmDailyProductionReport
- frmOperatorDailyWorkEntry
- frmQCUnifiedInbox
- frmQCHistoryAndCorrection
- frmItemCostApprovalInbox
- frmContractInbox
- frmContracts
- frmExternalWorkInbox (planned in the latest prototype state)
- frmWarehouseUnifiedInbox
- frmWarehouseItemHistory (planned)
- frmRequestLookup (planned)
- frmRepairManagementReport
- frmRepairItemHistory

## E.3 Prototype queries of business importance

- qryRequestItems
- qryRequestWorkflow_Current
- qryRequestInbox_Management
- qryRequestInbox_Planning
- qryRequestInbox_Drawing
- qryUnitWorkQueue_All
- qryProductionUnitLauncher
- qryProductionLiveOverview
- qryDailyProductionWorkSessions
- qryRequestItemStagesProgress
- qryRequestItemWorkSessionsProgress
- qryRepairManagementBase
- qryRepairQCInbox
- qryRepairWarehouseReceive
- qryItemStages_ForManualWorkEntry
- qryQCUnifiedInbox
- qryItemCostCalculations_ApprovedForWarehouse
- qryRepairRequestItemIDs
- qryWarehouseUnifiedInbox_Repair
- qryWarehouseUnifiedInbox_Stage
- qryWarehouseUnifiedInbox
- qryRequestItemFirstStage
- qryExternalSupplyRequestInbox
- qryExternalStageInbox
- qryExternalWorkInbox
- qryContracts_Search
- qryWarehouseContractAlerts
- qryMakeRequestItems
- qryWarehouseInquiry
- qryMachineHourlyRates_Current
- qryMaterialPriceRates_Current

## E.4 Prototype modules and C# responsibilities

| Access module | C# responsibility |
|---|---|
| modJalaliDate | Localization/date service |
| modLookups | Reference-data services |
| modWorkflow | Request workflow/state-transition service |
| modItemStageWorkflow | Stage/release/work-session service |
| modRepairWorkflow | Repair workflow service |
| modCosting | Cost calculation service |
| modContracts | Contract service/gate |
| modExternalWork | External dispatch/return service |

The names above are migration aids only. The C# application should consolidate or redistribute responsibilities according to the final architecture while preserving business behavior.

---

# Appendix F — Legacy Status Catalog from the Source

The following catalog is retained for migration/reference purposes. It is **not** an instruction to implement every value literally. Values marked obsolete by the canonical specification must not be implemented in the C# business model merely because they existed in the old Access design.

## F.1 Historical request-status concepts

- Draft / Created by Central
- Sent to Management
- Management Approved - Sent to Planning
- Management Rejected - Sent to Planning
- Cancellation Pending by Planning
- Cancelled / Closed
- Approved - Under Planning Review
- Sent to Drawing
- Under Drawing Review
- Drawing Returned to Planning
- Repair Accepted Fully
- Repair Accepted Partially
- Repair Rejected by Drawing
- Need More Information
- Items Defined
- Warehouse Inquiry Pending
- Warehouse Response Received
- External Supply Pending
- External Processing Pending
- Drawing Finalized Items
- Planning Defining Stages
- Stages Defined
- Final Management Review Pending
- Final Management Approved
- Final Management Rejected
- Returned for Correction
- Ready to Convert to Project — **obsolete in canonical C# model**
- Converted to Production Project — **obsolete in canonical C# model**
- Emergency Override Active — **removed from canonical C# model**
- Closed

## F.2 Historical request workflow actions

- CREATE_REQUEST
- SEND_TO_MANAGEMENT
- MGMT_APPROVE_INITIAL
- MGMT_REJECT_INITIAL
- SEND_TO_PLANNING
- PLANNING_CANCEL_EMAIL
- SEND_TO_DRAWING
- DRAWING_REVIEW_STARTED
- DRAWING_REPAIR_DECISION
- DRAWING_DEFINE_ITEMS
- DRAWING_FINALIZE_ITEMS
- SEND_TO_WAREHOUSE
- WAREHOUSE_RESPONSE
- SEND_TO_EXTERNAL_SUPPLY
- SEND_TO_CONTRACTOR
- RECEIVE_FROM_CONTRACTOR
- SEND_TO_QC
- QC_RESULT
- SEND_TO_FINAL_MANAGEMENT
- MGMT_APPROVE_FINAL
- MGMT_REJECT_FINAL
- MGMT_MODIFY_VALUE
- RETURN_FOR_CORRECTION
- CONVERT_TO_PROJECT — **obsolete in canonical C# model**
- EMERGENCY_OVERRIDE — **removed from canonical C# model**
- CLOSE_REQUEST

## F.3 Historical action results

- APPROVED
- REJECTED
- PARTIAL_APPROVED
- NEED_INFO
- SENT
- RECEIVED
- COMPLETED
- RETURNED
- CANCELLED
- MODIFIED
- BYPASSED — **obsolete unless another explicit future rule reintroduces a bypass concept**
- FAILED

## F.4 Supply types documented in the source

- Internal Production
- Warehouse Stock
- External Purchase
- Contractor Processing
- Casting
- Ceramic
- Customer-Supplied Repair
- Partner-Manufactured
- Mixed Supply

Current prototype-specific SupplyTypeID behavior documented in the source:

| SupplyTypeID | Title | Manager approval | Incoming QC | Current external/supply behavior |
|---:|---|---|---|---|
| 1 | ارسالی مشتری | False | False | Not shown as external supply request by default |
| 2 | موجودی انبار | False | False | Not shown as external supply request by default |
| 3 | خرید تهران | False | False | Shown as SUPPLY_REQUEST |
| 4 | خرید سرامیک | True | True | Shown as SUPPLY_REQUEST |
| 5 | ریخته گری | True | True | Shown as SUPPLY_REQUEST |

These IDs are prototype data and should not be hard-coded as permanent C# business meaning unless the company approves them as stable master-data codes.

## F.5 Historical external-supply status concepts

- Not Required
- Buy Request Needed
- Buy Request Issued
- Waiting for Purchase
- Purchased / Ordered
- Partially Received
- Fully Received
- Waiting for Incoming QC
- QC Accepted
- QC Partially Accepted
- QC Rejected
- Returned to Provider
- Replacement Pending
- Ready for Next Stage
- Closed

## F.6 Historical contractor-process status concepts

- Not Required
- Contractor Process Needed
- Ready to Send Out
- Sent to Contractor
- At Contractor
- Delayed at Contractor
- Returned from Contractor
- Waiting for Incoming QC
- QC Accepted
- QC Rejected
- Sent Back to Contractor
- Correction Pending
- Ready for Next Stage
- Closed

## F.7 Stage/QC status concepts retained by the current model

- PLANNED
- WAITING_PREVIOUS_QC
- WAITING
- IN_PROGRESS
- PAUSED
- STOPPED
- DELIVERED_TO_QC
- QC_RECEIVED
- UNDER_QC_INSPECTION
- WAITING_QC
- QC_APPROVED
- QC_REJECTED
- REWORK_REQUIRED
- IN_REWORK
- REWORK_COMPLETED
- WAITING_EXTERNAL_SEND
- EXTERNAL_SENT
- RETURNED_FROM_CONTRACTOR / external return state
- WAITING_EXTERNAL_SUPPLY
- WAITING_MATERIAL
- WAITING_DRAWING_REVISION
- READY_FOR_WAREHOUSE
- WAREHOUSE_RECEIVED
- CLOSED

## F.8 QC result concepts

- Not Checked
- Waiting for QC
- Approved
- Rejected
- Partially Approved
- Rework Required
- Conditional Approval
- Returned to Supplier
- Returned to Contractor
- Final Approved — **not a separate final-QC workflow requirement; may remain as a historical label where needed for migrated data**

## F.9 Rework reasons

- Production mistake
- Operator mistake
- Drawing calculation mistake
- Wrong material
- Wrong quantity
- Wrong dimension
- Machine problem
- Supplier/provider defect
- Contractor defect
- Customer change
- Management change
- Other

---

# Appendix G — Explicit Non-Goals for the First C# Production Release

The following are outside the canonical first release unless later approved:

- full warehouse inventory balance management
- automatic reward calculation/scoring
- attendance-integrated reward calculation
- mandatory project conversion
- Emergency workflow/bypass
- separate final-QC workflow
- automatic reconstruction of all downstream stages after a QC correction
- invented contract-value calculation logic
- undocumented external-process behavior

These items may be researched/designed later, but the C# implementation must not silently introduce them.


# Canonical Business Decision Update — 2026-10-09

This section is authoritative and supersedes any conflicting older draft/backlog text above. Historical Access implementation notes remain historical reference only.

## Request identity and scope
- C# v1 processes customer requests only; the Internal Request workflow is out of scope.
- One customer request may contain both MAKE and REPAIR items. WorkType is item-level. Projects are not mandatory.
- RequestNo is customer-supplied free-form text, not generated by the application. Uniqueness is Customer + RequestNo + Jalali Request Year, with case-sensitive RequestNo comparison.

## Priority and queue
- Planning priority is one of `عادی`, `فوری`, `خیلی فوری`; same priority may be assigned to multiple stages/items. Planning may change priority/order and the live queue must reflect changes.
- Management may override multiple stages/items in one queue operation. Every affected stage's old/new effective order, actor, timestamp, and reason must be logged. Preserve underlying Planning priority. Exact precedence with later Planning changes during an active override is still pending.

## Calendar and planning time
- Planning enters minutes per piece; total processing load = minutes per piece × planned quantity. External stages additionally have expected lead time.
- Normal workdays are Saturday–Thursday; Friday is non-working by default but work/additional hours are possible with supervisor approval and must be recorded.
- Current shift is 08:00–17:00 with 12:00–13:00 break (8 net working hours). Support configurable one-, two-, or three-shift schedules. The two-shift example is 06:00–14:00 and 14:00–22:00; three-shift timings and break rules/times per shift are configurable. The current 12:00–13:00 break must not be hard-coded for all future shifts.
- Planning maintains holidays/off-days. Maintenance/PM reports machine downtime (including maintenance/power outage) to Planning, and downtime must affect capacity/end-date estimates. The exact multi-machine capacity algorithm remains pending.

## Stages, QC, quantities, and UOM
- Planning may insert a stage before/after existing stages using controlled, audited route revision. Preserve all previous stage/work/QC history. Downstream-state handling after insertion remains to be specified.
- QC approval of a stage gates the following stage. If the first stage is internal, Planning releases it; if the first stage is external supply/operation, wait for arrival/return and QC approval before the next stage.
- Permanently rejected QC quantity is SCRAP; accepted quantity continues, and only rework quantity returns to production. QC corrections retain the original inspection and send the item back to Stage 1; downstream stages are handled manually. Mark the item as having a QC correction.
- QC owns configurable UOM and conversions; reverse conversions are automatic reciprocals.

## Excel, contract, documents, and permissions
- Excel import is core v1 for requests up to approximately 1,000 items. Use one mixed worksheet with Persian headers and explicit Work Type. Changed existing rows are conflicts requiring manual choice; never silently overwrite or reset active stages/history.
- Contract requirement applies to the whole request. Planning creates/submits contract and PDF after Drawing confirms doability. Management approves/rejects with optional explanation; rejected contract is corrected/resubmitted. Approval is visible in Planning and Warehouse dashboards. Search by contract number/date/customer for Planning, Management, and Warehouse. Contract customer is derived through Request.Customer; do not duplicate CustomerID on Contract.
- One PDF/document may be linked to multiple logical records, while the binary content is stored once.
- Initial sensitive-action permissions are defined in the Domain/Database Specification.
- All user-facing dates are Shamsi/Jalali; all monetary values are Iranian Rial (IRR). Windows 10/11 only; WPF/modern .NET architecture.
- Request progress headline formula remains pending approval; regardless, the request page must show item counts by status and quantity information.


## Remaining clarifications after the 2026-10-09 review
- Request-progress headline percentage is not yet approved. Show status counts and quantity information in any case.
- Queue priority storage granularity (RequestItem vs ItemStage) and precedence of an active Management override after a subsequent Planning change remain open.
- Exact scheduling allocation over several machines and the machine-assignment timing remain open; confirmed calendar hours/holidays/downtime rules above are not open.
- Route revision must preserve history; downstream stage-state treatment after insertion remains open.
- Excel re-import changed-row conflicts require manual choice. Exact options are pending, especially for already released/started items.
