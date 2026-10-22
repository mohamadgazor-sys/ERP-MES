# Planning-Owned Resources, Item Specifications, Shifts and Capacity - Design (R2, R7)

**Status: DESIGN ONLY. Not implemented. PROPOSED items are not approved** (register U-12..U-16, U-34..U-36).

## 1. Ownership [REQ]

Planning owns: operators, machines and their codes, operator-machine assignments, machine hourly rates (together with Management, [EXISTING] 15.2), item raw-material weight / length / diameter / quantity type selection, stage-to-unit-and-machine assignment, shift templates, unit shifts, off-days, staffing capacity and worker scheduling. QC owns UOM and quantity types (Planning only selects). Warehouse owns raw-material price (Planning cannot edit it).

## 2. Operators [PROPOSED]

| Entity | Fields |
|---|---|
| `Operator` | Id, OperatorCode (stable, unique), FullName, DepartmentId (Production), UserId (optional; U-16), IsActive, HireDate, EndDate |

Domain 14.4 already has `Operator.UserID` optional. R5 requires operators to submit reports inside the application, so every operator who must report needs a user account (U-16). Operators are retired, never deleted.

## 3. Machines, codes and rates

| Entity | Fields | Rules |
|---|---|---|
| `Machine` | Id, MachineCode (e.g. FCC-06, TCC-12, HUB-01), Name, ProductionUnitId, Status, IsActive | **MachineCode is a stable business identifier**: unique, never reused, immutable once referenced by any stage, assignment, report or cost snapshot. A typo before first use is corrected by a Planning action that is audited (U-14). Retire instead of delete. |
| `MachineHourlyRate` | MachineId, RatePerHourRial, EffectiveFrom, EffectiveTo, ChangedBy, Reason | Existing design (Domain 19.1). Append-only; no overlapping intervals per machine; Warehouse has no write permission. Rate in Rial. Costs use the rate effective on the work date (COSTING_DESIGN section 5). |
| `OperatorMachineAssignment` | OperatorId, MachineId, EffectiveFrom, EffectiveTo, AssignedBy, Reason, IsPrimary | History retained; closing an assignment does not delete it. The same operator may hold several machines; a machine may have several operators over time. |
| `OperatorMachineQualification` | OperatorId, MachineId (or machine class), QualifiedFrom, QualifiedTo, CertifiedBy, Note | [PROPOSED, U-13] separate from assignment: **assignment does not imply qualification**. |

Qualification rules to approve (U-13):
- Recommended: an assignment requires a current qualification; Planning may override with a mandatory reason, flagged on reports.
- Who certifies qualification (Planning recommended, with Production supervisor input)?
- Reassignment: closing the old assignment and opening a new one the same day; a job report always records the *actual* machine, whatever the assignment says.
- Whether a qualification is per machine, per machine class, or per unit.

## 4. Item specifications and stage assignment

Item fields (Planning, or Drawing for MAKE items - U-03; U-15 for optionality):

| Field | Type | Notes |
|---|---|---|
| QuantityTypeId, OrderedQuantity | from QC list | R3 |
| RawMaterialWeightKg | decimal(12,4) | **Weight needed per costing unit** (recommended: per base unit; reported per ordered unit for display). Used in costing. |
| LengthMm, DiameterMm | decimal(10,2) | Millimetres; optional for forms where meaningless. |
| MaterialId / grade | link to Warehouse material list | Needed for pricing. |
| Required for | MAKE: weight, quantity type, material mandatory; REPAIR: optional | proposed |

`ItemStage` additions: `ProductionUnitId` (existing concept), `MachineId` (new), `PlannedMinutesPerPiece` (existing, #12), history of reassignment (who, when, why). Authority question U-12: Planning assigns the planned machine; the unit supervisor may substitute within the unit with a reason; costing and KPIs use the machine on the approved job report.

Excel import (required, [EXISTING] #16) is extended with the new columns: quantity type, weight kg, length mm, diameter mm, stage routes with unit and machine code. Preview, validation errors by row, transactional commit, manual conflict choice are unchanged. Machine codes in the file are resolved to existing machines; unknown codes are validation errors, not auto-created.

## 5. Shifts, calendars and capacity (R7)

### 5.1 Templates [REQ values; storage PROPOSED]

| Code | Start | End | Crosses midnight | Note |
|---|---|---|---|---|
| A | 06:00 | 14:00 | no | |
| B | 14:00 | 22:00 | no | |
| C | 22:00 | 06:00 | **yes** | |
| D | 08:00 | 17:00 | no | Current day schedule; break 12:00-13:00 per #13 |

Templates are **data**, not constants ([EXISTING] #13): `ShiftTemplate(Code, Name, StartTime, EndTime, CrossesMidnight (derived), BreakStart, BreakEnd (optional), IsActive, ValidFrom)`. Break definitions for A-C are not given (U-34); until supplied they are unpaid-break-free and chargeable hours come from reports, not the template.

### 5.2 Midnight handling [PROPOSED, U-34]

A shift occurrence is identified by its **start date** and template. Shift C starting on date D runs to D+1 06:00; its hours belong to date D (the business day it started). Store start and end as absolute UTC instants plus the Jalali start date; derive durations from instants. A job report line for shift C is tagged with the start date, and a line that spans midnight is not split in two (hours are reported once, on the shift's date). Reports use work date = shift start date. This must be confirmed (U-34) because KPI date ranges are affected.

### 5.3 Overlap rule [OPEN, U-35]

D (08:00-17:00) overlaps A (until 14:00) and B (from 14:00, overlap 14:00-17:00). Not assumed mutually exclusive. Recommendation:
- A **worker** may not be scheduled in two overlapping occurrences on the same date (blocked).
- A **unit** may run overlapping templates only when Planning marks it explicitly (a unit-shift row per template), each with its own capacity.
- A **machine** may not be booked by two operators at overlapping times, except an explicitly recorded handover overlap of a stated length.

### 5.4 Calendar entities [PROPOSED]

| Entity | Fields |
|---|---|
| `UnitShiftSchedule` | ProductionUnitId, Date, ShiftTemplateId, State (WORKING / OFF), Reason, ChangedBy, At |
| `UnitShiftCapacity` | ProductionUnitId, ShiftTemplateId, EffectiveFrom, EffectiveTo, MaxOperators (and optional MinOperators) |
| `WorkerSchedule` | OperatorId, Date, ShiftTemplateId, ProductionUnitId, MachineId (optional), Status (PLANNED/CONFIRMED/CANCELLED), CreatedBy |
| `OperatorOffDay` | OperatorId, Date range, Kind (leave, sick, mission, other), RecordedBy, ApprovedBy |
| Calendar exceptions | holidays / extra working days as existing (#13), at company or unit level |

There is **no hard-coded Monday-Friday week**; regular workdays (Saturday-Thursday, Friday normally off, #13) and shift patterns are configuration data. Planning-approved off-days and the working calendar drive forecast scheduling; downtime reported by PM also feeds it (#14, existing).

### 5.5 Conflict rules [PROPOSED, U-36]

- Scheduling a worker on an OFF unit-shift or on their own off-day: blocked at creation.
- Marking a day OFF or recording an off-day when workers or stages are already scheduled: allowed, but each affected schedule row is listed and flagged for Planning to resolve (nothing silently cancelled).
- Capacity: MaxOperators is a hard limit on scheduled workers per unit-shift (overtime is the explicit exception, JOB section 8). A shortage below MinOperators is a warning only.
- Capacity is headcount, not machine count; a machine limit is implicit in the one-operator-per-machine-time rule.
- Every change is audited with old and new value.

## 6. Interaction with the queue and forecast

Estimated processing time stays minutes-per-piece x planned quantity ([EXISTING] #12); the forecast walks the working calendar (shift occurrences marked WORKING), machine downtime and external lead time. The per-unit queue ordering of Decision Log #78 is unchanged. Staffing capacity limits parallel capacity per unit; how capacity multiplies forecast throughput is U-36 follow-up and not assumed.
