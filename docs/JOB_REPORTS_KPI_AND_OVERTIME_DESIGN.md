# Daily Job Reports, KPI Reporting and Overtime Assignments - Design (R5, R6, R8)

**Status: DESIGN ONLY. Not implemented. PROPOSED items are not approved** (register U-30..U-33, U-37). Rewards and bonuses are decided outside the application ([REQ]; Decision Log #21 keeps the reward formula in Phase 2).

## 1. Purpose

Operators record what they worked on each day inside the application. Planning uses approved reports for workforce and production KPIs; Warehouse uses approved report time for costing (COSTING_DESIGN). The report is a record of reported work, not a QC result and not proof of attendance.

## 2. Entities [PROPOSED]

| Entity | Fields |
|---|---|
| `DailyJobReport` | Id, OperatorId, WorkDate (Jalali shown; stored with UTC instants), ProductionUnitId, ShiftTemplateId, Status, CreatedAt, SubmittedAt, ReviewedBy, ReviewedAt, ReturnReason, RevisionNo, RowVersion |
| `DailyJobReportLine` | ReportId, LineNo, MachineId (code), RequestItemId, ItemStageId, StartTime, EndTime (instants), WorkedMinutes (derived from times), MachiningMinutes (portion on the machine), DowntimeMinutes, DowntimeReason, ProcessedBaseUnits, SelfReportedDefectBaseUnits (optional), Exceptions/Note, OvertimeAssignmentId (optional) |
| `DailyJobReportEvent` | Report id, Event, Actor, At, Reason (submission, return, amendment, approval) |

Operators do not enter accepted / rework / scrap; **QC owns those** (QC_QUANTITY_MODEL_DESIGN). A defect count the operator reports is informational and never changes a QC decision.

## 3. Smallest complete field set

Mandatory per report: operator, work date, unit, shift. Mandatory per line: machine code, request/item/stage, start and end time (or minutes), processed quantity. Optional: downtime minutes with reason, exception note, self-reported defects. Everything else (accepted, rework, scrap) is read from QC and displayed, not typed. Reasons for the small set: costing needs hours and machine; KPIs need hours, unit and quantities; the rest is derivable.

## 4. "Valid" reported hours [PROPOSED, U-32]

A line's hours count toward costing and KPI rankings only when all of these hold:
1. The report is APPROVED (see section 5).
2. The operator, machine and item-stage exist and the stage was released at that time.
3. Line times lie within the shift (or an approved overtime assignment) for that unit and date, or the supervisor approved the exception with a reason.
4. No overlap with another approved line of the same operator, and no overlap with another operator on the same machine (except an explicit handover overlap).
5. Downtime minutes are subtracted from productive hours but shown.

Hours = approved WorkedMinutes (integer) divided by 60. Never rounded for ranking.

## 5. Workflow, authority and exceptions [PROPOSED, U-31]

```text
DRAFT --submit(Operator)--> SUBMITTED
SUBMITTED --approve(Supervisor)--> APPROVED
SUBMITTED --return(Supervisor, reason)--> RETURNED --edit+resubmit(Operator)--> SUBMITTED
APPROVED --amend(Supervisor or Operator request; new revision; reason)--> SUBMITTED (the approved revision stays in force until the new one is approved)
```

| Question | Proposal |
|---|---|
| Who creates / submits | The operator (or the supervisor on the operator's behalf, with reason, flagged) |
| Who reviews / approves | The unit supervisor; Planning may approve when no supervisor is assigned |
| Duplicates | One report per operator per work date per shift; a second is rejected; lines may not duplicate (operator, machine, time range) |
| Late reports | Allowed within N working days (N configured, owner decides); later ones need Planning approval and are flagged |
| Corrections | New revision with reason; old revision kept |
| Overlapping hours | Blocked at submission (see section 4) |
| Relation to QC | Reports never alter QC results or stage statuses; if a report is amended after costing, the cost snapshot becomes "not current" (COSTING_DESIGN) |

## 6. Attendance, schedule and overtime relationship

A report is *reported work*. The schedule (PLANNING doc) says who was planned; an overtime assignment (section 8) says who was authorized for extra hours; neither proves work. Where a report conflicts with the schedule (operator on an OFF day or not scheduled) the line is flagged for supervisor decision, not rejected silently. A separate attendance record is not part of this design (U-32 may require it).

## 7. KPI reporting (R6) [PROPOSED, U-33]

All KPIs read approved report lines and QC records only; each figure states its source and whether it is actual, planned, accepted, reworked, scrapped or estimated.

Default KPI set (practical, not a composite score):

| KPI | Definition | Type |
|---|---|---|
| Productive hours | sum of approved WorkedMinutes minus downtime / 60 | actual |
| Completed quantity | accepted base units whose stage was completed in the period (QC) | accepted |
| QC acceptance rate | accepted / (accepted + rework + scrap) decided in the period | accepted |
| Rework rate, scrap rate | rework / decided, scrap / decided | reworked, scrapped |
| Plan vs actual time | planned minutes (minutes per piece x quantity) against approved actual minutes for the same stage | planned vs actual |
| Estimated finish | forecast from the queue | estimated, labelled |
| Utilization | productive hours / scheduled hours | actual vs planned |

There is **no single "best unit" score** and no formula for rewards. Unit comparisons show each metric side by side. Metrics requiring owner approval before rankings are authoritative: definitions of "valid", quantity attribution when one stage spans units, treatment of overtime hours (included but labelled), handling of periods with incomplete approvals, and per-unit normalization (headcount).

**Operator ranking:** ranked by total valid hours in the selected period, descending; deterministic tie-breaks: more distinct worked days, then OperatorCode ascending. Ranks are labelled as hour rankings only.

**Period:** any Jalali date range X-Y (date-range filter); the 24-day payroll-style period ([EXISTING] Master 22.2: Jalali day 1-24) is a preset, not the only option. Reports are as-of: re-running with later approvals may change figures; an exported report carries its run time and filter.

**Drill-down:** every figure links to the report lines, then to the report, then to the audit trail. Planning and Management read; supervisors read their own unit [PROP]; operators read their own reports.

## 8. Overtime assignments (R8) [PROPOSED, U-37]

Overtime is arranged by unit supervisors, so there is an explicit record:

| Entity `OvertimeAssignment` | Fields |
|---|---|
| Id, SupervisorUserId, OperatorId, ProductionUnitId, MachineId, WorkDate, PlannedStart, PlannedEnd, Reason, Status (PROPOSED / APPROVED / CANCELLED / COMPLETED), ApprovedBy, ApprovedAt, ActualMinutesReported (derived from linked report lines), AuditTrail |

Rules:
1. The supervisor selects the operator and machine code; the operator must be assigned to or qualified on that machine (U-13), else blocked unless Planning overrides with a reason.
2. No overlap with the operator's other assignments or scheduled shift (overtime is outside the normal shift) and no overlap on the machine.
3. Total planned hours per operator per day / period capped by a configurable limit; staffing capacity may be exceeded only by an approved overtime assignment (PLANNING section 5.5).
4. Who approves: Planning recommended (existing Friday/extra hours need supervisor approval, #13; whether Planning must also approve is U-37). Unit-supervisor identification: U-16.
5. Actual time is taken from job report lines linked to the assignment (`OvertimeAssignmentId`); the assignment itself never adds hours.
6. **Assignment does not prove work performed.** Hours enter KPIs and costs only through approved report lines, once, so overtime cannot be double-counted: a line is either linked to an overtime assignment or to the normal shift, not both.
7. Overtime cost: same machine rate unless a premium is approved (COSTING_DESIGN section 6). Machine-hour costing remains based on reported machine time, independent of the number of attendance hours.
