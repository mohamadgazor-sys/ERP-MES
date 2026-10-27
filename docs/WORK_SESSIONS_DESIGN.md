# Work sessions, dispatch and actual time — design (owner direction of 2026-10-27)

Status: implemented in phases 10a-10f (Decision Log #111-#115, #116); the WPF window is compiled and its view model is tested but has not been run by a person on Windows. Decision Log #111 (item-scoped contract) and #112 (this model). Everything marked **labelled default** is a recommendation that the owner may change; it is not an approved business rule.

## 1. Review of the merged implementation (what is reused, what is new)

| Need | Existing concept reused | What is added |
|---|---|---|
| Per-unit queue, ordering, Management override + audit | `UnitQueueService`, `QueueOverride`, Planning position/priority | Future-stage visibility with explicit blocked reason (10e); session summary per row |
| Gates (Management, Drawing, contract, inquiry, clarification) | `RequestGateService` | Evaluated **per item** (10a) |
| QC of the preceding stage, stage status machine, quantities | `ItemStage`, `RequestItem.StartStage/SubmitStageForQc`, `QcInspectionService` | Submission refused while sessions are unfinished (10c). No per-session quantities |
| Who may act on a unit | `UserUnitDesignation` (+ `IsSupervisor`) | Dispatch/start/pause/resume/finish need a supervisor designation (Planning fallback is flagged) |
| Machine of a unit, rates by date | `Machine.ProductionUnitId`, `MachineHourlyRate` history | Machine cost = machine runtime × rate in force (10d) |
| Operator, assignment, qualification | `Operator`, `OperatorMachineAssignment`, `OperatorMachineQualification` | Missing qualification stays a **warning** kept on the session |
| Shift / availability | `WorkerSchedule`, `OperatorOffDay`, `OvertimeAssignment`, `WorkerScheduleRules` | Dispatch availability check (off-day hard; outside shift needs a recorded reason) |
| Concurrency, audit, authorization | row versions, `PersistenceSave`, `CurrentAuthorization`, `AuditActions` | Same patterns |
| Manual time | `DailyJobReport` (approved lines feed costing/KPI) | Untouched. A stage is tracked by sessions **or** by job reports, never both (10c) |

There is **no** second queue: the queue rows are still stages; a session is a record *under* a stage.

## 2. Model (Domain/Production)

* `WorkSession` (aggregate, row-versioned): request item + stage number + production unit + machine + operator; status `Assigned → Active ⇄ Paused → Finished` (`Cancelled` only before start); dispatcher, starter, finisher, instants; `PredecessorSessionId` (handover); `QualificationWarning`; `OutsideShiftReason`; Planning's time approval.
* `WorkSessionInterval`: one continuous active stretch, with the **machine and operator it ran with**. Open intervals are unique per session, per machine and per operator **in the database** (filtered unique indexes) — double-booking cannot survive a race.
* `WorkSessionPause`: reason, optional note (required for "other"), scope, optional common `OutageEvent`, its own pause/resume instants and actors.
* `WorkSessionEvent` / `WorkSessionCorrection`: append-only history; a correction keeps old value, new value, reason, actor, time, and withdraws any time approval.
* `PauseReason`: configurable catalogue. Seeded: power outage, planned preventive maintenance, unplanned machine breakdown, operator absence or shift handover, material unavailable, waiting for supervisor/technical assistance, other (note required). Planning may add and deactivate reasons.
* `OutageEvent`: one common event (unit or plant) linked from the pauses of every affected session. Each session keeps its own timestamps and duration.

Active duration = Σ intervals; paused duration = Σ pauses; **neither is an editable number**. A pause never changes the stage status; the stage only *appears* stopped in the read model when every unfinished session is paused.

## 3. Rules (Application, 10c; mirrored in the UI)

1. **Dispatch** (supervisor of the stage's unit): stage must be released (Waiting/InProgress), all gates of the item hold, the machine belongs to the stage's unit and is active, the operator is active and available. Qualification missing → warning stored on the session and shown, **not** converted into a rule.
2. **Start**: records the actual start instant; the first start moves the stage Waiting → InProgress through the existing `StartStage` transition in the same transaction. The previous stage's QC approval is the existing gate.
3. **No double-booking**: a machine or operator with an open interval cannot be started or resumed elsewhere. The only documented exception is the **handover**: the predecessor is finished as handed-over and the successor starts at the same instant on the same machine — no overlap, machine runtime is counted once.
4. **Pause** needs a reason (note for "other"); **resume** re-checks authorization, machine/operator availability and gates; **finish** closes the interval (from paused, the pause ends at the finish). Finishing a session never completes the stage.
5. **Common outage**: one command pauses the selected active sessions (a machine list, or all of a unit) under one `OutageEvent`.
6. **Corrections**: Planning only, reason mandatory, not in the future, instants stay ordered, refused after the stage was submitted to QC (Planning first undoes the submission, existing correction path); approval is withdrawn.
7. **Submission to QC** is refused with a clear reason while any session is unfinished (assigned/active/paused), when the stage has no session at all while it is session-tracked, or when the stage quantities do not reconcile (existing check).
8. **Authorization is default-deny** and re-read from the database on every command (existing pattern). A user from another unit is refused in the Application, not only in the UI.

## 4. Time, timezone, midnight

All instants are stored as UTC with sub-second precision (tested) and shown in the configured application time zone with Jalali dates and 24-hour time. Durations are differences of UTC instants, so a session crossing midnight or a DST change is exact. Reports that need a work date split an interval at **local** midnight (10d).

## 5. Metrics and costing (10d)

Distinct metrics: machine-hours by machine and stage (union of intervals per machine, so a handover is counted once), labour-hours by operator and stage (Σ per operator), elapsed stage time (first start to last finish), paused time by reason, planned vs actual. Never added together.

Machine cost = machine runtime of **approved** sessions × the machine's rate in force on the work date (existing rate history, per-interval-day rounding rule of the calculator). Labour cost: **none** (no labour-cost rule exists; labour-hours are reported only).

**Time approval (labelled default):** Planning approves the actual time of a finished session; the approver may not be the person who dispatched or started it. Only approved sessions reach costing and KPIs, matching "approved actual time is authoritative" (#91).

## 6. Migration

Additive only: new tables and seeded pause reasons. Existing job reports, report lines, stage rows and costs are not read, rewritten or reinterpreted (tested). Stages that already have job-report lines keep that basis and refuse sessions; stages with sessions refuse report lines.

## 7. Open questions for the owner (defaults in force until answered)

1. Who approves session time (default: Planning, not the dispatcher).
2. Whether a plain Production operator may still use the legacy `Start` for stages tracked by job reports (default: yes, until the legacy path is retired).
3. Whether overlapping shift handover on one machine should be allowed (default: no overlap; exact handover only).
4. Labour cost rule (default: none).
5. Pause reason scopes and which reasons are billable (default: pauses are not machine runtime for costing).
