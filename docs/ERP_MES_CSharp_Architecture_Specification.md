# ERP / MES — C# Architecture Specification

**Document status:** Draft technical architecture derived from the canonical C# Master Specification + Current State

**Authority:** The file `ERP_MES_CSharp_Master_Specification_Current_State.md` remains the authority for business behavior. This document defines the proposed technical structure for implementing that behavior in C#.

**Important rule:** This document must never silently create or change a business rule. Where business behavior is unresolved, it is marked as pending confirmation rather than guessed.

---

# 1. Architecture Goals

The new application is a Windows-only Persian RTL ERP/MES desktop application.

Target client operating systems are Windows 10 and Windows 11. Windows 7 and Windows 8 are not supported. The purpose of the Windows 10/11 requirement is to allow the factory's legacy Windows 7 machines to be migrated as part of deployment.

The exact .NET runtime version remains a technical selection to be validated against representative Windows 10 and Windows 11 client machines before the solution is locked. The application should use a modern supported C#/.NET/WPF stack rather than preserving Windows 7 constraints.

The architecture must:

1. Preserve the approved business behavior of the Request → Item → Stage → QC model.
2. Keep UI concerns separate from business rules and persistence.
3. Support temporary single-user SQLite development.
4. Allow migration to SQL Server without rewriting business logic.
5. Keep workflow/state transitions centralized and difficult to bypass.
6. Preserve detailed historical/audit information.
7. Handle requests containing hundreds or around 1,000 items.
8. Support both manual and automatic production work entries.
9. Support mixed MAKE/REPAIR items inside one customer request.
10. Support live production monitoring without making dashboards the source of truth.
11. Keep documents and heavy data operations in the database/backend rather than embedding data in the desktop application's binaries.
12. Permit future modules to be added without redesigning the core domain.

---

# 2. Canonical Business Model

The canonical operational hierarchy is:

```text
Customer Request
    |
    +-- Request Item 1 (MAKE / REPAIR)
    |       |
    |       +-- Stage 1
    |       +-- Stage 2
    |       +-- Stage 3
    |       +-- Work Sessions
    |       +-- QC History
    |
    +-- Request Item 2 (MAKE / REPAIR)
            |
            +-- Stage 1
            +-- Stage 2
            +-- ...
```

A request is the primary tracking unit for customer follow-up, but production execution is item/stage based.

There is no mandatory Project entity in the C# production flow.

`WorkTypeID` is the authoritative item-level classification:

- MAKE
- REPAIR

A single customer request may contain both.

Internal requests are outside v1 scope.

---

# 3. Proposed Solution Structure

The recommended C# solution structure is:

```text
ERP-MES.sln
│
├── src/
│   ├── ERP.MES.Desktop/
│   ├── ERP.MES.Application/
│   ├── ERP.MES.Domain/
│   ├── ERP.MES.Infrastructure/
│   ├── ERP.MES.Infrastructure.Sqlite/
│   └── ERP.MES.Provisioning/
│
├── tests/
│   ├── ERP.MES.Domain.Tests/
│   ├── ERP.MES.Application.Tests/
│   └── ERP.MES.Infrastructure.Tests/
│
└── docs/
    ├── ERP_MES_CSharp_Master_Specification_Current_State.md
    ├── ERP_MES_CSharp_Architecture_Specification.md
    └── ERP_MES_CSharp_Domain_and_Database_Specification.md
```

> **[Approved — Decision Log #37; details in `docs/MILESTONE_1_PLAN.md`]** `ERP.MES.Infrastructure.Sqlite` is added so that provider-specific configuration and migrations are structurally separate (Decision Log #25). A future `ERP.MES.Infrastructure.SqlServer` project would sit beside it with its own configuration and separate migrations, reusing the shared Infrastructure and the same business logic; it is not created now and the production SQL Server version/provider remains unresolved. A separate `ERP.MES.Provisioning` console project (not part of the factory-PC distribution) supports initial administrator setup and recovery (Decision Log #38, #39, #47). `docs/MILESTONE_1_PLAN.md` §2–3 defines the project responsibilities and dependency direction and prevails for details.

## 3.1 ERP.MES.Desktop

Responsibilities:

- WPF windows/views.
- ViewModels.
- Navigation.
- Persian RTL presentation.
- User interaction.
- Validation feedback presentation.
- Dashboard rendering.
- File/document viewing initiation.

It must not contain authoritative workflow rules.

Examples of logic that must NOT live only in button-click handlers:

- whether an item may enter production;
- whether the next stage may be released;
- whether QC may approve/reject;
- whether a contract gate is open;
- whether a warehouse receipt is allowed.

Those rules belong in Application/Domain services.

## 3.2 ERP.MES.Application

Responsibilities:

- Use cases.
- Application services.
- Authorization checks at use-case boundaries.
- Workflow orchestration.
- Transaction orchestration.
- Import orchestration.
- Dashboard/query services.
- Report generation.

Representative use cases:

```text
CreateCustomerRequest
SubmitRequestToManagement
ManagementApproveRequest
ManagementRejectRequest
PlanningReceiveRequest
RecordDrawingDecision
CreateRequestItem
ImportItemsFromExcel
DefineItemStages
DetermineContractRequirement
CreateContract
SubmitContractForApproval
ApproveContract
RejectContract
ReleaseFirstStage
StartWorkSession
CompleteWorkSession
DeliverStageToQC
ReceiveStageInQC
ApproveQC
RejectQCForRework
CorrectHistoricalQCDecision
ReturnExternalStageToQC
ReceiveFinalWarehouseDelivery
CalculateItemCost
ApproveItemCost
GetRequestProgress
GetLiveProductionQueue
GetProductionSchedule
```

These names are architectural examples, not additional business requirements. The final set should be aligned to the master specification.

## 3.3 ERP.MES.Domain

Responsibilities:

- Entities.
- Value objects.
- Domain rules.
- State definitions.
- Invariants.
- Domain events where useful.

Domain objects should not depend on WPF, Access, SQLite, SQL Server, or UI controls.

Important domain concepts:

```text
Request
RequestWorkflowEntry
RequestItem
RepairItemDetails
ItemStage
ItemStageWorkSession
QCInspection
ExternalWorkDispatch
Contract
CostCalculation
WarehouseTransaction
ProductionUnit
ProductionUnitCategory
Machine
Operator
User
Role
Department
UnitOfMeasure
MachineHourlyRate
MaterialPriceRate
```

## 3.4 ERP.MES.Infrastructure

Responsibilities:

- Database access.
- Entity mappings.
- EF Core/provider-specific implementation if EF Core is selected.
- SQLite configuration.
- SQL Server configuration.
- File/blob persistence.
- Database migrations.
- Transaction implementation.
- Secure password storage implementation.

The Infrastructure layer must hide database-provider-specific details from the Domain layer and, as much as practical, from Application use cases.

> **[Approved — Decision Log #37; details in `docs/MILESTONE_1_PLAN.md`]** The list above is split: provider-neutral code (model configuration, repositories, unit of work, password hashing, audit writing) stays in `ERP.MES.Infrastructure`; SQLite configuration and SQLite migrations go in `ERP.MES.Infrastructure.Sqlite`; SQL Server configuration and its separate migrations will go in a future `ERP.MES.Infrastructure.SqlServer`.

---

# 4. Recommended Technical Baseline

The following is the recommended implementation baseline. These are technical recommendations, not business rules.

## 4.1 UI

- C#
- modern supported .NET runtime, subject to Windows 10/11 compatibility validation
- WPF
- MVVM
- RTL/Persian-first presentation

Development should be performed on a supported modern Windows environment. Factory client machines do not need Visual Studio or other development tools to run the deployed application.

## 4.2 Persistence

A relational ORM/data-access layer should be used so the same application/domain model can target SQLite during development and SQL Server in production.

**Recommended candidate:** Entity Framework Core.

This recommendation should be validated when the actual solution is scaffolded; it does not change business rules.

## 4.3 Database providers

Development:

```text
Microsoft.Data.Sqlite / SQLite
```

Production:

```text
SQL Server
```

The application should select the provider through configuration rather than hard-coded business logic.

## 4.4 Application configuration

Environment-dependent settings should be externalized from business code.

Example conceptual configuration:

```text
Development
  DatabaseProvider = SQLite
  DatabasePath = ./data/erp-mes.db

Production
  DatabaseProvider = SQLServer
  ConnectionString = <configured securely>
```

Actual production connection-string/security policy is not defined here.

---

# 5. Layering Rules

The following dependency direction should be maintained:

```text
Desktop
   ↓
Application
   ↓
Domain

Infrastructure → Application/Domain contracts
Infrastructure does not become the business layer.
```

Practical rule:

> The WPF screen asks an Application use case to perform an action. The use case validates authorization and business state, invokes the domain logic, and persists the transaction. The screen then refreshes from the resulting state.

This prevents a user interface from becoming the source of truth.

---

# 6. Domain Entity Model

## 6.1 Request

Purpose:

- Customer request header.
- Customer reference.
- Request number/date.
- Current request status.
- Current department.
- General description/notes.
- Contract requirement.
- Workflow history relationship.

Important relationships:

```text
Request 1 → many RequestItems
Request 1 → many RequestWorkflowEntries
Request 1 → zero/one active Contract
```

## 6.2 RequestItem

Purpose:

- Individual customer item within a request.
- Preserves distinct identity even when two items look identical.
- Stores `WorkTypeID` = MAKE or REPAIR.
- Quantity/UOM information.
- General item-definition information.

A request may contain many items, including mixed work types.

## 6.3 RepairItemDetails

Repair-specific information remains separated from the general RequestItem entity.

This avoids contaminating every item with repair-only fields.

## 6.4 ItemStage

Purpose:

- Ordered production route for one RequestItem.
- Stage number.
- Stage operation title.
- Production unit.
- Planned machining/work time per piece.
- Priority/scheduling information where applicable.
- Expected external lead time where the stage is external.
- Current stage status.
- External/internal classification through production-unit routing.

Each item owns its own stage sequence.

## 6.5 ItemStageWorkSession

Purpose:

- Actual production work transaction.
- Operator.
- Unit.
- Machine.
- Work date/time.
- Worked minutes.
- Produced quantity.
- Rejected quantity.
- Entry mode.
- Work-entry status.
- Notes.

Multiple sessions per item/stage/day are valid.

## 6.6 QCInspection

Purpose:

- Stage-level QC inspection history.
- Accepted/rejected/rework quantities.
- QC decision.
- Inspector.
- Date/time.
- Correction history.

QC is a production-stage gate. There is no separate Final QC subsystem in the canonical v1 model.

## 6.7 ExternalWorkDispatch

Purpose:

- Dispatch of external production/supply stages.
- Sent time/user.
- Return time/user.
- Quantity/description/notes.
- Current external status.

External supply itself is represented as a production stage and is subject to QC when it returns/arrives.

## 6.8 Contract

Purpose:

- Contract number/date/customer/request.
- Contract PDF binary.
- Contract status.
- Management approval/rejection.
- Optional rejection explanation.
- Audit information.

Contracts must be searchable/navigable by contract number, contract date, and customer for Planning, Management, and Warehouse.

## 6.9 CostCalculation

Purpose:

- Item-level costing where applicable.
- Machine-work costs.
- Raw-material costs for applicable Make items.
- Historical rate snapshots.
- Management-approved final cost.
- Warehouse-release state.

All monetary values are Rial.

## 6.10 WarehouseTransaction

Purpose:

- Final item receipt.
- Historical warehouse movement records where implemented.

Warehouse v1 does not maintain a full live inventory balance.

---

# 7. Workflow Engine Architecture

Workflow/state transitions must be centralized.

Recommended pattern:

```text
WPF ViewModel
   ↓
Application Command / Use Case
   ↓
Authorization + State Validation
   ↓
Domain Workflow Service
   ↓
Repository / Unit of Work
   ↓
Transaction Commit
```

No UI screen should be allowed to directly perform a state-changing database update that bypasses this path.

## 7.1 Request workflow

The request state machine handles request-level movement:

```text
Central creates request
       ↓
Management review
       ↓
Planning
       ↓
Drawing
       ↓
Planning
       ↓
Contract determination/gate when required
       ↓
Production release
```

The exact permitted transitions must come from the canonical Master Specification.

## 7.2 Item-stage workflow

The stage state machine is separate from request workflow.

Conceptual flow:

```text
Planned
   ↓
Waiting / Released
   ↓
In Progress
   ↓
Waiting QC
   ├── QC Approved → Next Stage
   └── QC Rework   → Rework / production
```

For an external first stage:

```text
Planned
   ↓
Waiting External Send
   ↓
External Sent
   ↓
Waiting QC
   ├── Approved → Next Stage
   └── Rejected → External correction/rework route
```

> **[Phase 0.1 annotation — superseded]** The "Rejected" branch above predates the SCRAP rule: permanently rejected quantity is SCRAP and only rework quantity returns (Decision Log #7; §39.1). Return-to-supplier handling for external stages is an open audit item (U17).

## 7.3 QC gate invariant

For stage N where N > 1:

> Stage N must not become normally executable until stage N-1 has an approved QC result for the applicable quantity/route.

For the first stage:

- Normal internal first stage: Planning releases it into production.
- First external stage: the external supply/operation must return/arrive and pass QC before production can proceed to the following stage.

Partial quantities may proceed independently where supported by the quantity model.

---

# 8. Partial Quantity Model

The application must not assume that one item always moves as one indivisible unit.

Example:

```text
Request Item Qty = 100

Stage 1 → 100 completed
QC → 90 accepted, 5 rejected, 5 rework

90 → normal downstream route
5 → rework route
5 → rejected/scrap according to recorded QC result
```

The canonical business rule confirmed by the system owner is that accepted quantity continues while rework quantity returns to production.

A quantity permanently rejected by QC is SCRAP. Only the recorded rework quantity returns to production; accepted quantity continues through the normal route.

---

# 9. Production Planning and Scheduling Architecture

Planning owns:

- production priority;
- planned machining/work time per piece;
- stage-level planning inputs.

The system calculates planned dates from Planning inputs and the configured production calendar.

## 9.1 Separation of concerns

Planning input:

```text
Priority
Planned Minutes Per Piece
Planned Quantity
Production Unit
Machine requirements where applicable
Expected External Lead Time where applicable
```

For a stage planned for Q pieces at M minutes per piece, the baseline planned processing time is:

```text
Planned Total Minutes = Q × M
```

The planned minutes-per-piece value is separate from actual work-session minutes.

Scheduling engine:

```text
Planning inputs
+ current queue
+ configured production calendar
+ configured working availability
→ Planned Start
→ Planned End
```

Execution data must not silently overwrite the original planning estimate. Actual work sessions remain separate from planned time.

## 9.2 Priority and schedule-order authority

Planning controls the normal production priority and queue order.

Planning may change the priority after a stage has entered the production queue. The active work queue must reflect the new Planning priority.

Management is authorized to override the queue order. Every Management override must record:

- the user who made the override;
- timestamp;
- previous order/priority;
- new order/priority;
- reason.

The override must be auditable and must not erase the underlying Planning priority/history.

## 9.3 Scheduling engine boundary

The exact machine-capacity allocation algorithm is intentionally not invented yet. The following factory calendar rules are confirmed and must be configurable:

- normal working days are Saturday through Thursday; Friday is normally off but work/additional hours may be enabled with supervisor approval;
- current shift is 08:00–17:00 with a 12:00–13:00 break (8 net work hours);
- support configurable one-, two-, or three-shift calendars; the currently envisioned two shifts are 06:00–14:00 and 14:00–22:00; three-shift times are configurable;
- Planning manages holidays/off-days;
- maintenance/PM reports machine downtime, including maintenance and power outage, to Planning, and downtime impacts available capacity and planned-end calculation.

Confirmed scheduling inputs include:

- configured shift calendar;
- working days;
- holidays/non-working dates;
- planned minutes per piece;
- planned quantity;
- production-unit availability;
- machine capacity/availability assumptions;
- Planning priority;
- Management override order;
- handling of interrupted/continued work;
- rescheduling when actual progress differs from plan;
- expected lead time for external stages.

The application must distinguish estimated/calculated Planned End from actual completion.

# 10. Production Work Queue Architecture

## 10.1 Unit queue

Each ProductionUnit has a queue of released stages that belong to it.

The queue should show at minimum:

- Request No.
- Customer
- Item No.
- Part Name
- Stage No.
- Stage Title
- Priority
- Planned End
- Current Status
- Quantity/progress
- Machine/operator where already assigned
- External/internal route indicator where applicable

## 10.2 Queue ordering

The live queue must respond to Planning priority changes and Management-authorized overrides.

Operational state remains visible for attention ordering:

```text
IN_PROGRESS
then STOPPED / problem states
then WAITING
```

Within those operational categories, the normal ordering is controlled by Planning priority and planned-end information. A Management override may supersede the normal Planning order and must be logged according to Section 9.2.

Queue recalculation must not modify the actual production history. It changes only the current operational order.

## 10.3 Management live dashboard

`ProductionLiveOverview` is a management/supervision view of open work across all units.

It is a read/monitoring projection, not an independent source of production state.

The dashboard should support:

- all open jobs;
- in-progress;
- waiting;
- stopped;
- rejected/rework;
- production-unit filtering;
- Request No.;
- customer;
- item/stage;
- priority;
- planned end;
- current unit;
- elapsed/planned information where data is available.

Selecting a row should navigate to the corresponding request/item/stage/work queue context.

---

# 11. Request Progress Architecture

The request page must display request progress without replacing item-level truth.

## 11.1 Required presentation

Example:

```text
Request #1405-0031
Customer: Example Customer

Overall item completion
██████████████░░░░ 72%

18 / 25 items completed

MAKE   10 / 14
REPAIR  8 / 11
```

The screen should also show counts such as:

- completed;
- in progress;
- waiting for QC;
- waiting external;
- waiting production;
- rework;
- blocked.

## 11.2 Progress calculation

The canonical source should be the current state of the request's items and their stages, not manually entered percentage text.

For v1, the request page must show completed-item count, total-item count, status counts, and per-item stage progress. A visual percentage may be displayed as a monitoring aid, but it is not a workflow gate or financial KPI until a formal weighting rule is approved.

Safe v1 display options can include:

- completed item count / total item count;
- current stage progress per item;
- stage-completion ratio;
- status counts.

A weighted percentage based on machining minutes should remain a separate future/confirmed rule unless explicitly approved.

---

# 12. Excel Import Architecture

Excel import is a core feature, not an optional convenience.

Target scale:

- hundreds of items;
- approximately 1,000 items in a request;
- repeat imports of revised lists.

## 12.1 Import pipeline

```text
Excel workbook
     ↓
Read/parse
     ↓
Validate columns/types
     ↓
Normalize values
     ↓
Preview validation result
     ↓
Duplicate detection
     ↓
User confirmation
     ↓
Database transaction
     ↓
Request Items
```

## 12.2 Existing prototype mapping

> **[Phase 0.1 annotation — superseded]** The positional table below is legacy reference only. The C# template maps by Persian header name in one mixed worksheet with an explicit Work Type column (Decision Log #16; Domain and Database Specification §21.2). See audit X28.

The current prototype mapping is preserved as a reference:

| Excel column | RequestItem field |
|---|---|
| A | ItemNo |
| B | PartName |
| F | Dimensions |
| J | MaterialName |
| L | RequestedQty |
| M | SupplyTypeID |
| N | SupplyRequestNo |

Merged cells are read from their top-left cell.

`ItemNo` must remain text because values may be hierarchical/alphanumeric such as `1.10`.

Duplicate behavior evaluates `ItemNo` within the current RequestID. Identical rows are not duplicated; a changed existing row must be shown as a conflict for explicit manual resolution rather than silently skipped or overwritten. The official template uses Persian headers and one mixed sheet with an explicit Work Type column. Exact conflict options for items already in production remain to be finalized.

## 12.3 C# import requirements

The import service must:

- never require the WPF form to create 1,000 individual controls/rows before saving;
- validate rows in memory/streaming fashion as appropriate;
- report row-level errors;
- preserve valid rows when the user explicitly chooses a partial-import policy, if such a policy is later approved;
- use a transaction for the committed batch;
- provide an import summary;
- support safe re-import of expanded/revised BOMs;
- avoid duplicate RequestItems under the defined uniqueness rules.

## 12.4 WorkType mapping

The C# importer uses one mixed worksheet for MAKE and REPAIR items. A dedicated WorkType column is mandatory and maps to `WorkTypeID` values MAKE or REPAIR.

The final header name and column position can be fixed in the concrete Excel template, but the importer must never infer MAKE/REPAIR from another field. Invalid or missing WorkType values must be reported as import errors.

---

# 13. Contract Architecture

Contract workflow is request-level. A contract requirement applies to the entire customer request, including all MAKE and REPAIR items contained in that request.

Canonical process:

```text
Drawing confirms doability
        ↓
Planning decides ContractRequired
        ↓
Planning creates contract + imports PDF
        ↓
Management approval/rejection
        ↓
Approved → notify Planning + Warehouse
        ↓
Production gate opens
```

A rejected contract remains recorded and may include an optional explanation. Planning corrects the contract and resubmits it to Management. A rejected or missing contract does not open the production gate.

## 13.1 Contract document storage

The production SQL Server system stores contract PDFs as binary document data in the database.

The desktop client should retrieve documents when needed rather than storing large copies of them in the application's installation directory.

Document metadata should include at minimum:

- ContractID;
- RequestID;
- file name;
- MIME/content type;
- document size;
- created date/time;
- created user;
- document content;
- active/current indicator where applicable.

The final SQL Server blob/file architecture remains an engineering decision and should be validated before production deployment.

## 13.2 Contract navigation

Authorized Planning, Management, and Warehouse users must be able to search contracts by:

- contract number;
- contract date;
- customer.

Management approval/rejection should generate dashboard-visible information for Planning and Warehouse.

---

# 14. QC Architecture

QC owns the Unit of Measure master data.

QC is stage-based and occurs after each applicable production/external stage.

## 14.1 Handoff

```text
Production / External route
       ↓
Deliver to QC
       ↓
QC receives
       ↓
Inspection
```

Delivery time and QC receipt/decision time should be retained separately where operationally required.

## 14.2 Partial result

QC must support at least:

- accepted quantity;
- rejected quantity;
- rework quantity;
- notes;
- inspector;
- timestamp;
- decision/status.

The accepted quantity may continue while the rework quantity returns to production.

## 14.3 QC correction

The confirmed rare downstream-correction scenario is deliberately limited for v1:

- QC may correct a historical QC decision.
- The corrected item is sent back to the affected Stage 1 path in the rare scenario discussed with the owner.
- Downstream stages are then handled manually.
- Existing historical records are retained.
- The corrected item must be visibly marked as having undergone a QC correction.

The exact representation of the correction marker is an implementation choice as long as audit history remains intact.

---

# 15. Warehouse Architecture

Warehouse has two distinct responsibilities:

1. warehouse inquiry/receipt/movement history;
2. raw-material price-per-kg master data.

Warehouse v1 does **not** maintain a complete on-hand inventory system.

## 15.1 Warehouse inquiry

Inquiry records:

- request/item;
- requested material/quantity;
- response;
- response quantity where applicable;
- date/user;
- notes.

Inquiry must not automatically deduct inventory.

## 15.2 Final receipt

Final receipt creates a warehouse transaction and advances the item to its terminal warehouse state.

External return-to-QC is not the same thing as final warehouse receipt.

---

# 16. Costing Architecture

## 16.1 Ownership

Planning owns:

- machine machining/working cost per hour.

Warehouse owns:

- raw-material/alloy cost per kg.

QC owns:

- UOM definitions.

## 16.2 Historical rates

Machine rates and material prices need effective dates and audit information.

Calculations should snapshot the actual rate used so later master-data changes do not rewrite historical approved costs.

## 16.3 Machine cost

Conceptual formula:

```text
Machine Cost = Worked Minutes / 60 × Applicable Machine Hourly Rate
```

Multiple work sessions may use different machines/rates.

Total machine cost is the sum of the applicable session-level costs.

## 16.4 Raw material cost

For applicable non-contract Make items:

```text
Raw Material Cost = Weight (kg) × Applicable Price per kg
```

## 16.5 Cost approval

Where applicable to customer repair/non-contract Make:

```text
QC Approved
   ↓
Cost Calculation
   ↓
Management Price Approval/Adjustment
   ↓
Warehouse Release
   ↓
Warehouse Receipt
```

Contract-specific pricing logic remains separate from ordinary non-contract item costing unless later redesigned.

---

# 17. Authentication and Authorization Architecture

Users authenticate with username/password stored in the application's database.

Each user has exactly:

- one Department;
- one Role.

Authorization should be evaluated using:

```text
Authenticated User
+ Department
+ Role
+ Current workflow state
+ Requested action
```

## 17.1 Password storage

Passwords must never be stored as plaintext.

The concrete password-hashing algorithm/library is an implementation/security decision to be validated during implementation.

## 17.2 Authorization enforcement

Permission checks must exist at the Application use-case boundary.

UI visibility is supplemental UX behavior, not the security boundary.

Example:

```text
CanApproveContract(user, contract)
CanCreateRepairDetails(user, requestItem)
CanReleaseStage(user, itemStage)
CanApproveQC(user, itemStage)
CanReceiveWarehouse(user, item)
CanMaintainMachineRates(user)
CanMaintainMaterialRates(user)
CanMaintainUnitsOfMeasure(user)
```

The exact role matrix remains pending explicit confirmation.

---

# 18. Localization Architecture

All user-facing localization is mandatory.

## 18.1 Persian RTL

- Right-to-left layout.
- Persian labels.
- Persian status titles.
- Persian-friendly fonts and rendering.
- Consistent RTL navigation.

## 18.2 Jalali

All user-facing dates are Shamsi/Jalali.

Internal storage may use native `DateTime`/database date types.

Recommended application responsibility:

```text
JalaliDateService

FormatGregorianToJalali
ParseJalaliInput
GetJalaliYear
GetJalaliMonth
BuildGregorianRangeFromJalaliRange
```

Formatted Jalali strings must not be used as the database's date-comparison mechanism.

## 18.3 Rial

All monetary values are Iranian Rial.

Formatting belongs in the presentation layer; numeric storage remains numeric.

No silent Toman conversion is permitted.

---

# 19. Document Architecture

General PDF/document storage should use SQL Server binary storage in production.

The canonical document abstraction should hide the physical storage mechanism.

Example:

```text
IDocumentStore
    SaveDocument
    GetDocument
    Delete/DeactivateDocument
    GetMetadata
```

The application should not scatter direct SQL/blob handling through WPF screens.

Document types currently include concepts such as:

- request PDF;
- drawing PDF;
- contract PDF;
- QC document where applicable;
- supply/buy document where later implemented.

---

# 20. Data Access and Query Architecture

The application should distinguish between:

- transactional commands;
- read models/projections;
- reporting queries.

Large management dashboards should not repeatedly load the entire transactional graph into WPF memory.

For high-volume screens, use purpose-built query/read models such as:

```text
RequestLookupReadModel
ProductionLiveOverviewReadModel
UnitWorkQueueReadModel
QCCurrentInboxReadModel
ContractSearchReadModel
WarehouseInboxReadModel
RequestProgressReadModel
```

This keeps the desktop client light and lets the database perform heavy filtering/aggregation.

---

# 21. Large-Request Performance

The application must be designed for a request with roughly 1,000 items.

Avoid:

- one database query per item;
- one UI control per database field per item;
- loading all item history when only summary is needed;
- repeated recalculation of the same request aggregates in the UI.

Prefer:

- paged/virtualized item lists;
- set-based database queries;
- batch Excel insertion;
- summary/read-model queries;
- lazy loading of histories/details;
- indexes on RequestID, RequestItemID, ItemStageID, status fields, dates, and other major search paths.

Exact indexes will be finalized against the database schema and real query plans.

---

# 22. Concurrency and Transactions

SQLite development is single-user, but the final SQL Server application is expected to be multi-user.

The architecture must therefore be concurrency-safe from the beginning.

Critical transitions should be transactional:

### Request transition

```text
Validate
→ Update current state
→ Write workflow record
→ Update related state
→ Commit
```

### QC decision

```text
Validate current stage
→ Write QC inspection
→ Update stage status
→ Unlock/route next stage
→ Commit
```

### Contract approval

```text
Validate contract
→ Record decision
→ Update contract state
→ Update required dashboard state/gate state
→ Commit
```

### Warehouse receive

```text
Validate eligibility
→ Detect duplicate receipt
→ Write transaction
→ Update terminal item/stage state
→ Commit
```

### Excel import

```text
Validate batch
→ Insert/update permitted rows
→ Record import summary
→ Commit / rollback
```

The exact transaction implementation is technical, but the business outcome must be atomic.

---

# 23. Request Lookup Architecture

The request lookup is a central read-oriented feature.

Users in authorized Planning/Project Control and Management contexts should be able to search using:

- Request No.
- customer;
- item number;
- part name;
- date range;
- work type;
- current status;
- open/closed state.

The result should summarize item-level information without requiring users to navigate through the old project-centered model.

Each item result should be able to navigate to:

- item/stage progress;
- production work history;
- QC history;
- contract information where relevant;
- costing where relevant;
- warehouse history;
- related PDFs/documents.

---

# 24. Management Dashboard Architecture

Management dashboards are read-oriented projections over current transactional data.

At minimum, architecture must support:

1. Live production overview.
2. Request monitoring/progress.
3. Contract notifications and search.
4. QC/status visibility.
5. Cost approval visibility.
6. Warehouse/external bottleneck visibility.

Dashboards should refresh through controlled query operations. They should not mutate business state simply by loading.

Real-time behavior is not yet defined as a strict sub-second streaming requirement. Initial implementation may use refresh/requery intervals or explicit refresh, with future live-notification improvements possible.

---

# 25. Planning Dashboard Architecture

Planning is responsible for:

- request/item processing within its workflow permissions;
- production priority;
- stage planning;
- planned machining/work time;
- machine-rate master data;
- contract determination and contract creation when required.

The Planning dashboard should make visible:

- work waiting for Planning;
- contract-required requests;
- contracts awaiting management approval;
- released/waiting production work;
- overdue/at-risk planned work;
- stage planning gaps;
- external/supply blockers.

---

# 26. Production Unit Dashboard Architecture

A Production Unit dashboard is a filtered operational view for one production unit.

It should expose:

- active jobs;
- waiting jobs;
- stopped/problem jobs;
- item/stage identity;
- customer/request number;
- priority;
- planned end;
- assigned machine/operator when applicable;
- work-session entry functions permitted to that unit.

Operator assignment and machine assignment remain subordinate to the planning/production responsibility model established in the Master Specification.

---

# 27. Master Data Screens

The application should have dedicated maintenance screens rather than embedding master-data editing into transactional forms.

At minimum:

### QC

- Unit of Measure management.

### Planning

- Machine hourly rate management.
- Production priority/planning fields.

### Warehouse

- Raw-material price per kg management.

### System administration

- User management.
- Role management.
- Department management.
- Production units.
- Machines.
- Operators.
- Status/lookup data as authorized.

The exact role matrix is pending confirmation.

---

# 28. Reporting Architecture

Reports should primarily query transactional/read-model data rather than duplicate operational information manually.

## v1 reporting targets

- daily production work report;
- request/item progress;
- production live overview;
- QC history;
- contract search/report;
- warehouse history;
- external-work tracking;
- item cost/management approval status.

## Phase 2

The performance/ranking infrastructure should be built now, but the actual reward calculation remains deferred until its scoring policy is approved.

The 24-day Jalali performance period remains the intended reporting period.

---

# 29. Audit Architecture

Important business actions must create structured historical records.

Audit-worthy events include:

- request creation;
- request workflow movement;
- management decisions;
- drawing decisions;
- item creation/update;
- stage creation/update;
- production assignment/reassignment;
- work sessions;
- QC decisions;
- QC corrections;
- external dispatch/return;
- contract creation/submission/approval/rejection;
- cost calculation/approval;
- warehouse receipt;
- master-data changes.

Audit records should identify at minimum:

- user;
- action;
- target entity;
- timestamp;
- relevant before/after state or decision information where applicable.

The exact generalized audit-log strategy versus domain-specific history tables is an implementation decision; business-specific histories already required by the Master Specification must remain queryable.

---

# 30. Error Handling

Business errors should be returned as structured application results where practical.

Examples:

```text
ContractRequiredButNotApproved
StageCannotStartBeforePreviousQC
DuplicateWarehouseReceipt
InvalidExcelRow
MissingRepairDetails
MissingStageDefinition
UnauthorizedAction
InvalidWorkflowTransition
MissingMachineRate
MissingMaterialPrice
```

The UI should display understandable Persian explanations while preserving technical details in logs for troubleshooting.

---

# 31. Testing Strategy

Testing must occur at multiple levels.

## 31.1 Domain tests

Test pure business rules:

- valid/invalid stage transitions;
- QC gate;
- partial accepted/rework quantity;
- contract gate;
- request/item classification;
- scheduling calculations once defined.

## 31.2 Application tests

Test use-case behavior:

- request submission;
- contract approval;
- Excel import;
- stage release;
- QC decision;
- external return;
- warehouse receipt;
- costing;
- permissions.

## 31.3 Database/integration tests

Test:

- SQLite schema;
- transactions;
- relationships;
- duplicate prevention;
- query performance for large request datasets.

## 31.4 UI tests

Test:

- RTL layout;
- Jalali date input/display;
- Rial formatting;
- dashboard filtering;
- Excel import preview;
- request progress display;
- navigation by contract/request/item.

---

# 32. Migration Strategy

The C# application is not a literal rewrite of Access forms and VBA.

Migration principle:

```text
Business behavior
        ↓
Canonical C# domain model
        ↓
New relational schema
        ↓
C# application
```

Do not create one C# class merely because an old Access form existed.

Do not preserve an Access implementation workaround if the underlying business need can be implemented more safely in C#.

Legacy Access tables/form/query/module names remain useful as migration references.

---

# 33. SQLite → SQL Server Migration Principles

The database abstraction must avoid SQLite-specific assumptions.

Before production migration:

1. Freeze/approve the schema.
2. Test all core workflows against SQLite.
3. Review all SQLite-specific SQL.
4. Create SQL Server schema/migrations.
5. Migrate reference/master data.
6. Migrate approved transactional data if required.
7. Validate document storage.
8. Validate indexes/query performance.
9. Run end-to-end acceptance scenarios against SQL Server.
10. Switch the production configuration to SQL Server.

The exact migration tooling and data-migration scope will be defined later.

---

# 34. Deployment Architecture

Initial development deployment:

```text
Developer PC
   └── WPF app
       └── Local SQLite
```

Final deployment target:

```text
Factory PCs
   └── WPF app
          ↓
       SQL Server
```

The application should be deployable to multiple Windows clients without requiring users to install development tools.

The final deployment/update mechanism is not yet specified.

---

# 35. Security Baseline

Minimum technical security requirements:

- hashed/password-protected credentials;
- authorization at application-service boundaries;
- no SQL passwords hard-coded in source;
- no plaintext database credentials in committed configuration;
- validation of file/document inputs;
- controlled document access;
- audit of sensitive actions;
- parameterized database access through the chosen data-access layer.

This section is a technical security baseline and does not add business permissions that were not specified.

---

# 36. Performance Principles

The desktop client should remain lightweight.

Heavy filtering, aggregation, and reporting calculations should preferentially be executed by SQL Server in production.

The application should avoid pulling entire tables across the network merely to filter them in C#.

For SQLite development, the same query shapes should be used as much as practical to expose provider differences early.

High-volume request operations should be batch oriented.

---

# 37. Extensibility

The specification is intentionally a living document.

Future modules may be added through the Application/Domain architecture without changing the core request/item/stage model.

Examples of future modules already anticipated by the master specification include:

- detailed cost lines;
- richer external movement reporting;
- attendance/overtime/leave;
- advanced KPI dashboards;
- configurable performance scoring;
- expanded warehouse movement history.

Such modules must be added through explicit requirement updates rather than silently changing existing business rules.

---

# 38. Recommended First Implementation Milestones

Do **not** begin by implementing dashboards or every workflow.

> **[Revised and approved — Decision Log #36; details in `docs/MILESTONE_1_PLAN.md`]** The earlier first-milestone list bundled the technical foundation with Request/RequestItem, the request workflow engine, Excel import and MAKE/REPAIR functionality. It is replaced by three narrower milestones. Milestone 1 is the technical foundation only. The earlier 12-item list is superseded.

## 38.1 Milestone 1 — Technical foundation

```text
1. Create solution and projects (build and CI configuration)
2. Configure WPF RTL/Persian shell (login and empty main window)
3. Domain primitives (Jalali date service, Rial money formatting, result/error type)
4. Application layer scaffolding (abstractions, authorization rule set)
5. SQLite persistence base and initial schema for Department / Role / User / AuditLog / setup state
6. Secure one-time Administrator initialization and the restricted Administrator recovery mechanism (Provisioning project)
7. Authentication (password hashing, lockout) and default-deny authorization
```

Milestone 1 must **not** implement: Customer, Request, RequestItem or any request workflow; Excel import; MAKE/REPAIR functionality; documents; stages, production, QC, contracts, costing or warehouse.

## 38.2 Milestone 2 — Customers, requests and items

Customer, UOM and conversions, Request with its business key, request workflow engine (after the status-transition table is approved), documents, MAKE/REPAIR items and basic request UI.

## 38.3 Milestone 3 — Excel import

Header-mapped template, preview/validation, conflict detection and resolution, transactional commit, import audit.

## 38.4 After Milestone 3

Only after this foundation is stable should the project move into:

```text
Stages
→ Production queues
→ QC
→ External routes
→ Contracts
→ Costing
→ Warehouse
→ Management live dashboard
→ Scheduling refinements
```

---

# 39. Business Decisions Still Explicitly Pending

The following are still unresolved and must not be silently invented during implementation:

1. Exact queue tie-break among stages with the same Planning priority, and how a later Planning priority change interacts with an active Management override.
2. Scheduling capacity allocation across multiple eligible machines in a Production Unit, especially before a supervisor assigns a specific machine.
3. Exact handling of already-created downstream stages when Planning inserts a new route stage after work has started.
4. Exact Persian Excel header/value strings and available manual conflict choices when a conflicting item already has released/started stages.
5. Request-level headline progress formula. Proposed v1 is completed item count divided by total item count, with separate quantity progress and status counts, but it remains to be confirmed.
6. Machine capability/eligibility matrix for each Production Unit and stage type.
7. 24-day performance/reward scoring formula (Phase 2).
8. Retention/hard-delete policy for historical records and documents.
9. Dashboard refresh/polling versus real-time push notifications.
10. SQL Server document-content optimization (`varbinary(max)` versus FILESTREAM), installer/update strategy, and operational backup/restore policy.

The initial sensitive-action permission matrix is defined in the Domain/Database Specification and must be implemented. Any sensitive action not covered by that matrix must be blocked until its permission is defined.
# 39.1 Newly Confirmed Scheduling and Queue Rules

The following decisions are now authoritative:

- Planning enters machining/work time as minutes per piece.
- Planned total processing time is based on minutes per piece multiplied by the applicable planned quantity.
- Planning manually sets and may change production priority.
- The live production work queue must reflect Planning priority changes.
- Management is the only role authorized to override the normal Planning queue order.
- Queue overrides must record user, timestamp, old order/priority, new order/priority, and reason.
- External stages may have an expected lead time, which participates in planned-date calculation once the scheduling capacity algorithm is finalized.
- Allowed Planning priorities are `عادی` (Normal), `فوری` (Urgent), and `خیلی فوری` (Very Urgent); multiple items/stages may share a priority.
- A Management override can cover multiple stages/items in one queue operation, with batch/detail audit records.
- Permanent QC rejection is SCRAP; QC correction preserves history and sends the item back to Stage 1, while downstream stages are manually handled.
- The official Excel import template uses Persian headers in one mixed worksheet; changed existing rows are conflicts requiring manual resolution.
- QC owns configurable UOM definitions; reverse conversion factors are automatically reciprocated.
- One PDF may be linked to multiple records while its binary data is stored once.
- Contract customer is derived from its parent Request and is not redundantly stored on Contract.
- One customer request may contain both MAKE and REPAIR items.
- A request-level contract requirement applies to the entire customer request.
- Contract rejection is correct-and-resubmit, with the rejection retained as history and an optional explanation.
- The C# application targets Windows 10 and Windows 11; Windows 7/8 are not deployment targets.

# 40. Claude Code Operating Contract

When the repository is eventually handed to Claude Code, it should operate under these rules:

1. Read the Master Specification and Architecture Specification before implementing a feature.
2. Treat the Master Specification as the business authority.
3. Treat the Architecture Specification as the technical structure.
4. Never infer a missing business rule when an explicit rule is absent.
5. Ask for a decision or mark a feature as blocked when the missing rule would change business behavior.
6. Do not rewrite large portions of the repository merely to improve style unless explicitly requested.
7. Implement one coherent milestone at a time.
8. Build and test after each milestone.
9. Add automated tests for important domain/workflow rules.
10. Preserve historical/audit data during corrections and state changes.
11. Keep business logic out of WPF click handlers.
12. Keep SQLite and SQL Server provider differences isolated in Infrastructure.
13. Never treat Access-specific implementation quirks as business rules unless the Master Specification explicitly says they are behavior that must be preserved.
14. Before changing a database entity or workflow, identify all dependent use cases, queries, screens, and tests.
15. Provide a concise implementation summary and list of unresolved questions after each major milestone.

---

# 41. End State

The intended final system is:

```text
                 ┌────────────────────────────┐
                 │        WPF Desktop         │
                 │    Persian / RTL / Jalali  │
                 └─────────────┬──────────────┘
                               │
                 ┌─────────────▼──────────────┐
                 │   Application / Use Cases  │
                 └─────────────┬──────────────┘
                               │
                 ┌─────────────▼──────────────┐
                 │ Domain / Workflow / Rules  │
                 └─────────────┬──────────────┘
                               │
                 ┌─────────────▼──────────────┐
                 │      Infrastructure        │
                 │ Data / Documents / Auth    │
                 └─────────────┬──────────────┘
                               │
                 ┌─────────────▼──────────────┐
                 │ SQLite (development)       │
                 │ SQL Server (production)    │
                 └────────────────────────────┘
```

The business mental model remains:

```text
Customer Request
      ↓
Mixed MAKE / REPAIR Items
      ↓
Item-Specific Production Stages
      ↓
Planning Priority + Planned Work Time
      ↓
Internal / External Execution
      ↓
QC After Each Stage
      ↓
Next Stage
      ↓
Costing / Management Approval where applicable
      ↓
Warehouse Receipt
      ↓
Request / Item / Stage history and management visibility
```

This is the technical foundation on which the C# application should be built.


# 42. Confirmed Domain Decisions — 2026-10-09

These decisions supersede older conflicting statements in the architecture document.

## Priority and queue
- Planning priority values: `عادی`, `فوری`, `خیلی فوری`; multiple stages/items may share a value.
- Planning owns normal queue priority/order and may change either. The live queue must respond. Management may override multiple stages/items in one queue operation. Preserve the underlying Planning priority and log each affected stage's old/new effective order, actor, timestamp, and reason.
- Pending edge case: precedence between a later Planning change and an active Management override.

## Calendar and scheduling inputs
- Normal working days: Saturday–Thursday. Friday is off by default, but Friday work/extra hours are allowed with supervisor approval and must be recorded.
- Current shift: 08:00–17:00, 12:00–13:00 break (8 net hours). Support configurable one-, two-, or three-shift calendars. Current two-shift concept: 06:00–14:00 and 14:00–22:00. Three-shift boundaries are configurable.
- Planning manages holidays/off-days. Maintenance/PM reports machine downtime (including maintenance/power outage) to Planning; downtime impacts available capacity.
- Planning enters minutes per piece; total stage load = minutes per piece × planned quantity. External stages also require expected lead time. Exact multi-machine capacity allocation remains open.

## Other resolved rules
- Planning may insert stages before/after an existing stage through controlled, audited route revisions; preserve route versions and execution/QC history. Downstream stage-state handling after insertion remains open.
- Persian headers, one mixed Excel sheet, explicit Work Type; changed existing rows are conflicts for manual resolution.
- QC owns configurable UOM and reverse conversions are automatic reciprocals.
- Permanently rejected QC quantity is SCRAP.
- Contract.Customer is derived from Request.Customer; do not duplicate it.
- Documents may be linked to multiple logical records with one stored binary payload.
- Authorization matrix lives in the Domain/Database Specification.
- Progress headline formula still needs user approval.


## Remaining clarifications after the 2026-10-09 review
- Request-progress headline percentage is not yet approved. Show status counts and quantity information regardless.
- Queue priority storage granularity is resolved (Decision Log #58): the Planning priority belongs to the `RequestItem` and its stages inherit it. The precedence of an active Management override after a subsequent Planning change remains open.
- Exact scheduling capacity allocation over multiple machines remains open; calendar/week/shift/holiday/downtime requirements are confirmed.
- Downstream stage-state treatment after inserting a new route stage remains open.
- Excel conflict options are pending for already released/started items.
