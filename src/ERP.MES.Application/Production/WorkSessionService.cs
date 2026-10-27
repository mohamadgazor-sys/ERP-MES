using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Requests;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Application.Production;

/// <summary>What the signed-in user may do with work sessions of one unit, for enabling buttons (every command checks again).</summary>
public sealed record WorkSessionCapabilities(bool CanView, bool IsSupervisorOfUnit, bool IsOperator, bool CanCorrect, bool CanApprove, bool CanManageReasons)
{
    public static WorkSessionCapabilities None { get; } = new(false, false, false, false, false, false);
}

/// <summary>A unit's work sessions with the pause reasons and open outages.</summary>
public sealed record UnitWork(IReadOnlyList<SessionRow> Sessions, IReadOnlyList<Stored<PauseReason>> Reasons, IReadOnlyList<OutageEvent> OpenOutages, WorkSessionCapabilities Capabilities);

/// <summary>Dispatch of a stage's work to a machine and an operator. <see cref="ExpectedStageVersion"/> is the stage row version the supervisor saw in the queue.</summary>
public sealed record DispatchCommand(long ItemId, int StageNo, long MachineId, long OperatorId, long? ExpectedStageVersion, string? OutsideShiftReason, bool StartNow);

/// <summary>A recorded outage and the sessions it paused.</summary>
public sealed record OutageRecorded(OutageEvent Outage, IReadOnlyList<Stored<WorkSession>> Paused);

/// <summary>The result of resuming the sessions of an ended outage: each session that was resumed and each that stays paused with the reason.</summary>
public sealed record OutageResumeResult(IReadOnlyList<Stored<WorkSession>> Resumed, IReadOnlyList<(long SessionId, ValidationIssue Issue)> StillPaused);

/// <summary>Raised inside a session command when a business rule refuses it; nothing is saved.</summary>
public sealed class SessionRefusedException(ValidationIssue issue) : InvalidOperationException("The work session command was refused: " + issue)
{
    public ValidationIssue Issue { get; } = issue;
}

/// <summary>
/// The work session use cases (owner direction of 2026-10-27, Decision Log #112): dispatch a stage's work to a machine of the stage's unit and an available operator, start, pause (reason), resume,
/// finish, hand over, cancel, common outage, corrections and time approval. Every command: (1) reloads the user and checks the permission against the database; (2) checks the unit scope in the
/// Application (a supervisor of the stage's unit, or - for pause, resume and finish - the operator's own session); (3) runs the business rules and the Domain transitions inside ONE transaction that
/// re-reads the stage, the machine and the operator, fences the stage row, checks the session's expected version and relies on the database's unique open-interval indexes; (4) writes the audit
/// entry in the same transaction. Finishing a session never completes the stage; the stage's submission to QC is a separate use case that refuses while sessions are unfinished.
/// </summary>
public sealed class WorkSessionService(
    IWorkSessionRepository sessions,
    IPlanningMasterRepository planning,
    IUnitQueueRepository unitStore,
    IOperatorRepository operators,
    ISchedulingRepository scheduling,
    IOvertimeRepository overtime,
    RequestGateService gates,
    CurrentAuthorization authorization,
    IClock clock,
    IJalaliDateService dates,
    IAuditContext auditContext)
{
    private static readonly Permission[] All =
        [Permission.ViewWorkSessions, Permission.OperateWorkSessions, Permission.CorrectWorkSessions, Permission.ApproveSessionTime, Permission.ManagePauseReasons];

    // ---- reading ----

    public async Task<WorkSessionCapabilities> GetCapabilitiesAsync(Principal? principal, long? unitId = null, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, All, cancellationToken);
        if (set.Current is null)
        {
            return WorkSessionCapabilities.None;
        }

        var designation = unitId is { } unit ? await DesignationAsync(set.Current, unit, cancellationToken) : null;
        var operatorRecord = set.Has(Permission.OperateWorkSessions) ? await OperatorOfAsync(set.Current, cancellationToken) : null;
        return new WorkSessionCapabilities(
            set.Has(Permission.ViewWorkSessions), set.Has(Permission.OperateWorkSessions) && designation is { IsSupervisor: true }, operatorRecord is not null,
            set.Has(Permission.CorrectWorkSessions), set.Has(Permission.ApproveSessionTime), set.Has(Permission.ManagePauseReasons));
    }

    /// <summary>The unit's sessions (unfinished ones and those finished in the last <paramref name="finishedWithinDays"/> days). A Production user sees only units they are designated to.</summary>
    public async Task<Outcome<UnitWork>> LoadUnitAsync(Principal? principal, long unitId, int finishedWithinDays = 7, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ViewWorkSessions, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<UnitWork>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        if (user.DepartmentCode == DepartmentCodes.Production && await DesignationAsync(user, unitId, cancellationToken) is null)
        {
            return Outcome<UnitWork>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var since = clock.UtcNow.AddDays(-Math.Max(1, finishedWithinDays));
            return Outcome<UnitWork>.Ok(new UnitWork(
                await sessions.ListForUnitAsync(unitId, since, cancellationToken), await sessions.ListReasonsAsync(cancellationToken), await sessions.ListOpenOutagesAsync(unitId, cancellationToken),
                await GetCapabilitiesAsync(principal, unitId, cancellationToken)));
        });
    }

    /// <summary>The signed-in operator's own sessions (the operator record linked to the user).</summary>
    public async Task<Outcome<IReadOnlyList<SessionRow>>> LoadMineAsync(Principal? principal, int finishedWithinDays = 7, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ViewWorkSessions, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<IReadOnlyList<SessionRow>>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var own = await OperatorOfAsync(decision.Current!, cancellationToken);
            return Outcome<IReadOnlyList<SessionRow>>.Ok(own is null ? [] : await sessions.ListForOperatorAsync(own.Id, clock.UtcNow.AddDays(-Math.Max(1, finishedWithinDays)), cancellationToken));
        });
    }

    /// <summary>All sessions of one stage (history included), for the stage detail and before QC submission.</summary>
    public async Task<Outcome<IReadOnlyList<SessionRow>>> LoadStageAsync(Principal? principal, long itemId, int stageNo, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ViewWorkSessions, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<IReadOnlyList<SessionRow>>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var rows = await sessions.ListForStageAsync(itemId, stageNo, cancellationToken);
            var user = decision.Current!;
            if (user.DepartmentCode == DepartmentCodes.Production)
            {
                var designated = (await unitStore.ListDesignationsAsync(cancellationToken)).Where(d => d.Value.UserId == user.UserId && d.Value.IsActive).Select(d => d.Value.ProductionUnitId).ToHashSet();
                rows = rows.Where(r => designated.Contains(r.Session.Value.ProductionUnitId)).ToList();
            }

            return Outcome<IReadOnlyList<SessionRow>>.Ok(rows);
        });
    }

    /// <summary>The five distinct actual-time metrics of one stage (machine-hours, labour-hours, elapsed, paused by reason, planned vs actual) from the recorded instants of its sessions.</summary>
    public async Task<Outcome<StageMetrics>> LoadStageMetricsAsync(Principal? principal, long itemId, int stageNo, bool approvedOnly = false, CancellationToken cancellationToken = default)
    {
        var rows = await LoadStageAsync(principal, itemId, stageNo, cancellationToken);
        if (!rows.Succeeded)
        {
            return Outcome<StageMetrics>.Fail(rows.Status);
        }

        var planned = rows.Value!.Count == 0 ? 0m : rows.Value[0].PlannedMinutes;
        return Outcome<StageMetrics>.Ok(SessionMetricsCalculator.Compute(
            rows.Value.Select(r => r.Session.Value), [new StagePlan(itemId, stageNo, planned)], clock.UtcNow, approvedOnly ? s => s.IsTimeApproved : null));
    }

    // ---- dispatch and start ----

    /// <summary>
    /// A supervisor of the stage's unit assigns a machine of that unit and an available operator to the stage and, with <see cref="DispatchCommand.StartNow"/>, starts the work. A missing
    /// qualification is a warning kept on the session; an operator outside the shift needs a recorded reason; an absent or inactive operator, a machine of another unit and a busy machine or
    /// operator are refused.
    /// </summary>
    public async Task<Outcome<Stored<WorkSession>>> DispatchAsync(Principal? principal, DispatchCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await RunAsync(principal, async (user, now) =>
        {
            var supervised = await RequireSupervisorOfStageUnitAsync(user, command.ItemId, command.StageNo, cancellationToken);
            var plan = await PlanAsync(now, command.OperatorId, command.MachineId, supervised.UnitId, cancellationToken);
            return await sessions.CommitAsync(async ws =>
            {
                var (_, item, stage) = await ws.FindStageAsync(command.ItemId, command.StageNo, cancellationToken) ?? throw new RecordNotFoundException("stage", command.ItemId);
                ws.ExpectAndFenceStage(stage, command.ExpectedStageVersion);
                await CheckStageOperationalAsync(ws, item, stage, cancellationToken);
                var flags = await CheckResourcesAsync(ws, item.Id, stage, plan, command.OutsideShiftReason, exceptSessionId: 0, cancellationToken);
                var session = WorkSession.Dispatch(command.ItemId, command.StageNo, supervised.UnitId, command.MachineId, command.OperatorId, null, flags.QualificationMissing, flags.OutsideShiftReason, user.UserId, now);
                ws.Add(session);
                await ws.CheckpointAsync(cancellationToken);
                ws.AddAudit(Audit(user, now, AuditActions.SessionDispatched, command.ItemId + "/" + command.StageNo,
                    $"Stage {command.ItemId}/{command.StageNo} dispatched to machine {plan.Machine.Code} and operator {plan.Operator.Code}" + Warnings(flags, stage, command.MachineId)));
                if (command.StartNow)
                {
                    await StartInsideAsync(ws, item, stage, session, user, now, cancellationToken);
                }

                return new SessionCommitOutput([session]);
            }, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>Starts a session that was dispatched earlier. The gates, the stage state, the machine, the operator and their availability are checked again now.</summary>
    public Task<Outcome<Stored<WorkSession>>> StartAsync(Principal? principal, long sessionId, long expectedVersion, string? outsideShiftReason = null, CancellationToken cancellationToken = default) =>
        RunAsync(principal, async (user, now) =>
        {
            var row = await sessions.FindAsync(sessionId, cancellationToken) ?? throw new RecordNotFoundException("work session", sessionId);
            await RequireSupervisorAsync(user, row.Session.Value.ProductionUnitId, cancellationToken);
            var plan = await PlanAsync(now, row.Session.Value.OperatorId, row.Session.Value.MachineId, row.Session.Value.ProductionUnitId, cancellationToken);
            return await sessions.CommitAsync(async ws =>
            {
                var session = await ws.FindSessionAsync(sessionId, cancellationToken) ?? throw new RecordNotFoundException("work session", sessionId);
                ws.ExpectVersion(session, expectedVersion);
                var (_, item, stage) = await ws.FindStageAsync(session.RequestItemId, session.StageNo, cancellationToken) ?? throw new RecordNotFoundException("stage", session.RequestItemId);
                ws.ExpectAndFenceStage(stage, null);
                await CheckStageOperationalAsync(ws, item, stage, cancellationToken);
                await CheckResourcesAsync(ws, item.Id, stage, plan, outsideShiftReason ?? session.OutsideShiftReason, session.Id, cancellationToken);
                await StartInsideAsync(ws, item, stage, session, user, now, cancellationToken);
                return new SessionCommitOutput([session]);
            }, cancellationToken);
        }, cancellationToken);

    // ---- pause, resume, finish ----

    /// <summary>Pauses a session with a reason (a note when the reason needs one). The stage is not paused; the work already recorded stays.</summary>
    public Task<Outcome<Stored<WorkSession>>> PauseAsync(Principal? principal, long sessionId, long expectedVersion, long reasonId, string? note, CancellationToken cancellationToken = default) =>
        RunSessionAsync(principal, sessionId, expectedVersion, supervisorOnly: false, async (ws, session, user, now) =>
        {
            var reason = await ws.FindReasonAsync(reasonId, cancellationToken);
            if (reason is null || !reason.IsActive)
            {
                throw new SessionRefusedException(ValidationIssue.SessionPauseReasonInvalid);
            }

            if (reason.RequiresNote && string.IsNullOrWhiteSpace(note))
            {
                throw new SessionRefusedException(ValidationIssue.SessionPauseNoteRequired);
            }

            var scope = reason.DefaultScope == PauseScope.Unit ? PauseScope.Session : reason.DefaultScope;
            session.Pause(reason.Id, reason.RequiresNote, note, scope, null, user.UserId, now);
            ws.AddAudit(Audit(user, now, AuditActions.SessionPaused, SessionKey(session), $"Work session {session.Id} paused: {reason.Code}" + (string.IsNullOrWhiteSpace(note) ? string.Empty : "; " + note.Trim())));
        }, cancellationToken);

    /// <summary>Resumes a paused session: authorization, gates, machine, operator and availability are checked again.</summary>
    public Task<Outcome<Stored<WorkSession>>> ResumeAsync(Principal? principal, long sessionId, long expectedVersion, string? outsideShiftReason = null, CancellationToken cancellationToken = default) =>
        RunSessionAsync(principal, sessionId, expectedVersion, supervisorOnly: false, async (ws, session, user, now) =>
        {
            var plan = await PlanAsync(now, session.OperatorId, session.MachineId, session.ProductionUnitId, cancellationToken);
            var (_, item, stage) = await ws.FindStageAsync(session.RequestItemId, session.StageNo, cancellationToken) ?? throw new RecordNotFoundException("stage", session.RequestItemId);
            await CheckStageOperationalAsync(ws, item, stage, cancellationToken);
            await CheckResourcesAsync(ws, item.Id, stage, plan, outsideShiftReason ?? session.OutsideShiftReason, session.Id, cancellationToken);
            session.Resume(user.UserId, now);
            ws.AddAudit(Audit(user, now, AuditActions.SessionResumed, SessionKey(session), $"Work session {session.Id} resumed"));
        }, cancellationToken);

    /// <summary>Finishes a session. This does not complete the stage.</summary>
    public Task<Outcome<Stored<WorkSession>>> FinishAsync(Principal? principal, long sessionId, long expectedVersion, string? note, CancellationToken cancellationToken = default) =>
        RunSessionAsync(principal, sessionId, expectedVersion, supervisorOnly: false, (ws, session, user, now) =>
        {
            session.Finish(SessionFinishKind.Completed, note, user.UserId, now);
            ws.AddAudit(Audit(user, now, AuditActions.SessionFinished, SessionKey(session), $"Work session {session.Id} finished; active time {SessionTime.Active(session, now).TotalMinutes:0.##} min"));
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>Withdraws a session that never started.</summary>
    public Task<Outcome<Stored<WorkSession>>> CancelAsync(Principal? principal, long sessionId, long expectedVersion, string? reason, CancellationToken cancellationToken = default) =>
        RunSessionAsync(principal, sessionId, expectedVersion, supervisorOnly: true, (ws, session, user, now) =>
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new SessionRefusedException(ValidationIssue.ReasonRequired);
            }

            session.Cancel(reason, user.UserId, now);
            ws.AddAudit(Audit(user, now, AuditActions.SessionCancelled, SessionKey(session), $"Work session {session.Id} cancelled before start; {reason.Trim()}"));
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>
    /// Another operator takes the machine over: the session is finished as handed over and a successor session for the new operator starts at the SAME instant on the same machine, so machine runtime
    /// is counted once and each operator's labour time is counted for the operator. Nothing of the predecessor is overwritten.
    /// </summary>
    public async Task<Outcome<IReadOnlyList<Stored<WorkSession>>>> HandOverAsync(
        Principal? principal, long sessionId, long expectedVersion, long newOperatorId, string? outsideShiftReason, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.OperateWorkSessions, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<IReadOnlyList<Stored<WorkSession>>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var row = await sessions.FindAsync(sessionId, cancellationToken) ?? throw new RecordNotFoundException("work session", sessionId);
                await RequireSupervisorAsync(user, row.Session.Value.ProductionUnitId, cancellationToken);
                if (row.Session.Value.OperatorId == newOperatorId)
                {
                    throw new SessionRefusedException(ValidationIssue.SessionHandoverSameOperator);
                }

                var plan = await PlanAsync(now, newOperatorId, row.Session.Value.MachineId, row.Session.Value.ProductionUnitId, cancellationToken);
                var result = await sessions.CommitAsync(async ws =>
                {
                    var old = await ws.FindSessionAsync(sessionId, cancellationToken) ?? throw new RecordNotFoundException("work session", sessionId);
                    ws.ExpectVersion(old, expectedVersion);
                    var (_, item, stage) = await ws.FindStageAsync(old.RequestItemId, old.StageNo, cancellationToken) ?? throw new RecordNotFoundException("stage", old.RequestItemId);
                    ws.ExpectAndFenceStage(stage, null);
                    await CheckStageOperationalAsync(ws, item, stage, cancellationToken);
                    old.Finish(SessionFinishKind.HandedOver, null, user.UserId, now);
                    ws.AddAudit(Audit(user, now, AuditActions.SessionHandedOver, SessionKey(old), $"Work session {old.Id} handed over from operator {row.OperatorCode} to {plan.Operator.Code}"));

                    // The old interval is closed and saved first, so the database's one-open-interval-per-machine rule accepts the successor in the same transaction.
                    await ws.CheckpointAsync(cancellationToken);
                    var flags = await CheckResourcesAsync(ws, item.Id, stage, plan, outsideShiftReason, exceptSessionId: 0, cancellationToken);
                    var next = WorkSession.Dispatch(old.RequestItemId, old.StageNo, old.ProductionUnitId, old.MachineId, newOperatorId, old.Id, flags.QualificationMissing, flags.OutsideShiftReason, user.UserId, now);
                    ws.Add(next);
                    await ws.CheckpointAsync(cancellationToken);
                    ws.AddAudit(Audit(user, now, AuditActions.SessionDispatched, SessionKey(old), $"Successor of work session {old.Id} dispatched to operator {plan.Operator.Code}" + Warnings(flags, stage, old.MachineId)));
                    await StartInsideAsync(ws, item, stage, next, user, now, cancellationToken);
                    return new SessionCommitOutput([old, next]);
                }, cancellationToken);
                return Outcome<IReadOnlyList<Stored<WorkSession>>>.Ok(result.Sessions);
            });
        }
        catch (SessionRefusedException refused)
        {
            return Outcome<IReadOnlyList<Stored<WorkSession>>>.Invalid(refused.Issue);
        }
        catch (InvalidOperationException)
        {
            return Outcome<IReadOnlyList<Stored<WorkSession>>>.Fail(OperationStatus.InvalidState);
        }
    }

    // ---- common outage ----

    /// <summary>
    /// Records ONE common event (power outage, unit-wide stop) and pauses the unit's active sessions - all of them, or only those on the named machines - each with its own pause instant. The stage
    /// itself is not paused. Only a supervisor of the unit.
    /// </summary>
    public async Task<Outcome<OutageRecorded>> RecordOutageAsync(
        Principal? principal, long unitId, IReadOnlyCollection<long> machineIds, long reasonId, string? note, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machineIds);
                var decision = await authorization.CheckAsync(principal, Permission.OperateWorkSessions, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<OutageRecorded>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                await RequireSupervisorAsync(user, unitId, cancellationToken);
                var result = await sessions.CommitAsync(async ws =>
                {
                    var reason = await ws.FindReasonAsync(reasonId, cancellationToken);
                    if (reason is null || !reason.IsActive)
                    {
                        throw new SessionRefusedException(ValidationIssue.SessionPauseReasonInvalid);
                    }

                    if (reason.RequiresNote && string.IsNullOrWhiteSpace(note))
                    {
                        throw new SessionRefusedException(ValidationIssue.SessionPauseNoteRequired);
                    }

                    foreach (var machineId in machineIds)
                    {
                        if ((await ws.FindMachineAsync(machineId, cancellationToken))?.ProductionUnitId != unitId)
                        {
                            throw new SessionRefusedException(ValidationIssue.SessionMachineNotInUnit);
                        }
                    }

                    var active = await ws.ActiveSessionsAsync(unitId, machineIds, cancellationToken);
                    if (active.Count == 0)
                    {
                        throw new SessionRefusedException(ValidationIssue.SessionNothingToPause);
                    }

                    var outage = new OutageEvent(reason.Id, unitId, note, user.UserId, now);
                    ws.Add(outage);
                    await ws.CheckpointAsync(cancellationToken);
                    foreach (var session in active)
                    {
                        session.Pause(reason.Id, reason.RequiresNote, note, PauseScope.Unit, outage.Id, user.UserId, now);
                    }

                    ws.AddAudit(Audit(user, now, AuditActions.OutageRecorded, "OutageEvent", $"Outage {outage.Id} ({reason.Code}) in unit {unitId}: {active.Count} session(s) paused: {string.Join(", ", active.Select(a => a.Id))}"));
                    return new SessionCommitOutput(active, outage);
                }, cancellationToken);
                return Outcome<OutageRecorded>.Ok(new OutageRecorded(result.Outage!, result.Sessions));
            });
        }
        catch (SessionRefusedException refused)
        {
            return Outcome<OutageRecorded>.Invalid(refused.Issue);
        }
        catch (InvalidOperationException)
        {
            return Outcome<OutageRecorded>.Fail(OperationStatus.InvalidState);
        }
    }

    /// <summary>
    /// Ends the outage and resumes each session it paused, one by one, each with its own resume instant and the same checks as an ordinary resume (a session whose machine or operator is busy now, or whose
    /// gates no longer hold, stays paused and is reported). The outage is closed either way.
    /// </summary>
    public async Task<Outcome<OutageResumeResult>> EndOutageAsync(Principal? principal, long outageId, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.OperateWorkSessions, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<OutageResumeResult>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var outages = await sessions.ListOpenOutagesAsync(null, cancellationToken);
                var open = outages.FirstOrDefault(o => o.Id == outageId) ?? throw new RecordNotFoundException("outage", outageId);
                await RequireSupervisorAsync(user, open.ProductionUnitId ?? throw new SessionRefusedException(ValidationIssue.SessionNotSupervisor), cancellationToken);
                var resumed = new List<WorkSession>();
                var kept = new List<(long, ValidationIssue)>();
                var result = await sessions.CommitAsync(async ws =>
                {
                    var outage = await ws.FindOutageAsync(outageId, cancellationToken) ?? throw new RecordNotFoundException("outage", outageId);
                    var paused = await ws.SessionsPausedByOutageAsync(outageId, cancellationToken);
                    foreach (var session in paused)
                    {
                        try
                        {
                            var plan = await PlanAsync(now, session.OperatorId, session.MachineId, session.ProductionUnitId, cancellationToken);
                            var (_, item, stage) = await ws.FindStageAsync(session.RequestItemId, session.StageNo, cancellationToken) ?? throw new RecordNotFoundException("stage", session.RequestItemId);
                            await CheckStageOperationalAsync(ws, item, stage, cancellationToken);
                            await CheckResourcesAsync(ws, item.Id, stage, plan, session.OutsideShiftReason, session.Id, cancellationToken);
                            session.Resume(user.UserId, now);
                            await ws.CheckpointAsync(cancellationToken);
                            resumed.Add(session);
                        }
                        catch (SessionRefusedException refused)
                        {
                            kept.Add((session.Id, refused.Issue));
                        }
                    }

                    outage.Close(user.UserId, now);
                    ws.AddAudit(Audit(user, now, AuditActions.OutageClosed, "OutageEvent", $"Outage {outage.Id} ended: {resumed.Count} session(s) resumed, {kept.Count} still paused"));
                    return new SessionCommitOutput(resumed);
                }, cancellationToken);
                return Outcome<OutageResumeResult>.Ok(new OutageResumeResult(result.Sessions, kept));
            });
        }
        catch (SessionRefusedException refused)
        {
            return Outcome<OutageResumeResult>.Invalid(refused.Issue);
        }
        catch (InvalidOperationException)
        {
            return Outcome<OutageResumeResult>.Fail(OperationStatus.InvalidState);
        }
    }

    // ---- Planning: corrections, approval, reasons ----

    /// <summary>Moves one recorded instant with a mandatory reason (Planning). Refused after the stage was submitted to QC, in the future, out of order or when it would overlap another session of the machine or operator.</summary>
    public Task<Outcome<Stored<WorkSession>>> CorrectAsync(
        Principal? principal, long sessionId, long expectedVersion, SessionBoundary boundary, int pauseIndex, DateTime newInstantUtc, string? reason, CancellationToken cancellationToken = default) =>
        RunPlanningAsync(principal, Permission.CorrectWorkSessions, sessionId, expectedVersion, async (ws, session, user, now) =>
        {
            var (_, _, stage) = await ws.FindStageAsync(session.RequestItemId, session.StageNo, cancellationToken) ?? throw new RecordNotFoundException("stage", session.RequestItemId);
            if (stage.Status is ItemStageStatus.WaitingQc or ItemStageStatus.QcApproved or ItemStageStatus.WaitingShortfallDecision)
            {
                throw new SessionRefusedException(ValidationIssue.SessionCorrectionAfterSubmission);
            }

            ws.ExpectAndFenceStage(stage, null);
            try
            {
                session.Correct(boundary, pauseIndex, newInstantUtc, reason, user.UserId, now, now);
            }
            catch (ArgumentException)
            {
                throw new SessionRefusedException(ValidationIssue.SessionCorrectionInvalid);
            }

            if (await ws.OverlapsOtherSessionsAsync(session, now, cancellationToken))
            {
                throw new SessionRefusedException(ValidationIssue.SessionCorrectionInvalid);
            }

            var last = session.Corrections[^1];
            ws.AddAudit(Audit(user, now, AuditActions.SessionCorrected, SessionKey(session), $"Work session {session.Id} {last.Boundary} corrected {last.OldValueUtc:O} -> {last.NewValueUtc:O}; reason: {last.Reason}"));
        }, cancellationToken);

    /// <summary>Planning approves the actual time of a finished session (never the person who dispatched or started it). Only approved time reaches costing.</summary>
    public Task<Outcome<Stored<WorkSession>>> ApproveTimeAsync(Principal? principal, long sessionId, long expectedVersion, CancellationToken cancellationToken = default) =>
        RunPlanningAsync(principal, Permission.ApproveSessionTime, sessionId, expectedVersion, (ws, session, user, now) =>
        {
            if (session.DispatchedByUserId == user.UserId || session.StartedByUserId == user.UserId)
            {
                throw new SessionRefusedException(ValidationIssue.SessionApproverIsActor);
            }

            session.ApproveTime(user.UserId, now);
            ws.AddAudit(Audit(user, now, AuditActions.SessionTimeApproved, SessionKey(session), $"Work session {session.Id} actual time approved: active {SessionTime.Active(session, now).TotalMinutes:0.##} min"));
            return Task.CompletedTask;
        }, cancellationToken);

    public async Task<Outcome<Stored<PauseReason>>> AddReasonAsync(
        Principal? principal, string? code, string? name, PauseScope scope, bool requiresNote, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ManagePauseReasons, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<PauseReason>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        PauseReason reason;
        try
        {
            reason = new PauseReason(0, code ?? string.Empty, name ?? string.Empty, scope, requiresNote, isSystem: false, user.UserId, now);
        }
        catch (ArgumentException)
        {
            return Outcome<Stored<PauseReason>>.Invalid(ValidationIssue.PauseReasonCodeInvalid);
        }

        try
        {
            return await CustomerService.GuardAsync(async () => Outcome<Stored<PauseReason>>.Ok(
                await sessions.AddReasonAsync(reason, Audit(user, now, AuditActions.PauseReasonAdded, reason.Code, $"Pause reason {reason.Code} added"), cancellationToken)));
        }
        catch (ArgumentOutOfRangeException)
        {
            return Outcome<Stored<PauseReason>>.Invalid(ValidationIssue.PauseReasonCodeInvalid);
        }
    }

    public async Task<Outcome<Stored<PauseReason>>> SetReasonActiveAsync(Principal? principal, long reasonId, long expectedVersion, bool isActive, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ManagePauseReasons, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<PauseReason>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () => Outcome<Stored<PauseReason>>.Ok(
            await sessions.UpdateReasonAsync(reasonId, expectedVersion, r => r.SetActive(isActive), Audit(user, now, AuditActions.PauseReasonChanged, reasonId.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Pause reason {reasonId} set {(isActive ? "active" : "inactive")}"), cancellationToken)));
    }

    // ---- plumbing ----

    private sealed record Plan(Machine Machine, Operator Operator, bool OperatorAuthorizedForUnit, bool Qualified, OperatorAvailability Availability);

    private sealed record Flags(bool QualificationMissing, string? OutsideShiftReason);

    private async Task<Outcome<Stored<WorkSession>>> RunAsync(Principal? principal, Func<Principal, DateTime, Task<SessionCommitResult>> work, CancellationToken cancellationToken)
    {
        var decision = await authorization.CheckAsync(principal, Permission.OperateWorkSessions, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<WorkSession>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () => Outcome<Stored<WorkSession>>.Ok((await work(user, now)).Sessions[0]));
        }
        catch (SessionRefusedException refused)
        {
            return Outcome<Stored<WorkSession>>.Invalid(refused.Issue);
        }
        catch (InvalidOperationException)
        {
            return Outcome<Stored<WorkSession>>.Fail(OperationStatus.InvalidState);
        }
    }

    private Task<Outcome<Stored<WorkSession>>> RunSessionAsync(
        Principal? principal, long sessionId, long expectedVersion, bool supervisorOnly, Func<ISessionWorkspace, WorkSession, Principal, DateTime, Task> change, CancellationToken cancellationToken) =>
        RunAsync(principal, async (user, now) =>
        {
            var row = await sessions.FindAsync(sessionId, cancellationToken) ?? throw new RecordNotFoundException("work session", sessionId);
            await RequireSupervisorOrOwnerAsync(user, row.Session.Value, supervisorOnly, cancellationToken);
            return await sessions.CommitAsync(async ws =>
            {
                var session = await ws.FindSessionAsync(sessionId, cancellationToken) ?? throw new RecordNotFoundException("work session", sessionId);
                ws.ExpectVersion(session, expectedVersion);
                await change(ws, session, user, now);
                return new SessionCommitOutput([session]);
            }, cancellationToken);
        }, cancellationToken);

    private async Task<Outcome<Stored<WorkSession>>> RunPlanningAsync(
        Principal? principal, Permission permission, long sessionId, long expectedVersion, Func<ISessionWorkspace, WorkSession, Principal, DateTime, Task> change, CancellationToken cancellationToken)
    {
        var decision = await authorization.CheckAsync(principal, permission, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<WorkSession>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var result = await sessions.CommitAsync(async ws =>
                {
                    var session = await ws.FindSessionAsync(sessionId, cancellationToken) ?? throw new RecordNotFoundException("work session", sessionId);
                    ws.ExpectVersion(session, expectedVersion);
                    await change(ws, session, user, now);
                    return new SessionCommitOutput([session]);
                }, cancellationToken);
                return Outcome<Stored<WorkSession>>.Ok(result.Sessions[0]);
            });
        }
        catch (SessionRefusedException refused)
        {
            return Outcome<Stored<WorkSession>>.Invalid(refused.Issue);
        }
        catch (InvalidOperationException)
        {
            return Outcome<Stored<WorkSession>>.Fail(OperationStatus.InvalidState);
        }
    }

    private async Task<UserUnitDesignation?> DesignationAsync(Principal user, long unitId, CancellationToken cancellationToken) =>
        (await unitStore.ListDesignationsAsync(cancellationToken)).Select(d => d.Value).FirstOrDefault(d => d.UserId == user.UserId && d.ProductionUnitId == unitId && d.IsActive);

    private async Task<Operator?> OperatorOfAsync(Principal user, CancellationToken cancellationToken) =>
        (await operators.ListOperatorsAsync(cancellationToken)).Select(o => o.Value).FirstOrDefault(o => o.UserId == user.UserId && o.IsActive);

    private async Task RequireSupervisorAsync(Principal user, long unitId, CancellationToken cancellationToken)
    {
        var designation = await DesignationAsync(user, unitId, cancellationToken);
        if (designation is null)
        {
            throw new SessionRefusedException(ValidationIssue.StageUnitNotDesignated);
        }

        if (!designation.IsSupervisor)
        {
            throw new SessionRefusedException(ValidationIssue.SessionNotSupervisor);
        }
    }

    private async Task RequireSupervisorOrOwnerAsync(Principal user, WorkSession session, bool supervisorOnly, CancellationToken cancellationToken)
    {
        var designation = await DesignationAsync(user, session.ProductionUnitId, cancellationToken);
        if (designation is null)
        {
            throw new SessionRefusedException(ValidationIssue.StageUnitNotDesignated);
        }

        if (designation.IsSupervisor)
        {
            return;
        }

        if (supervisorOnly)
        {
            throw new SessionRefusedException(ValidationIssue.SessionNotSupervisor);
        }

        var own = await OperatorOfAsync(user, cancellationToken);
        if (own is null || own.Id != session.OperatorId)
        {
            throw new SessionRefusedException(ValidationIssue.SessionNotOwn);
        }
    }

    private async Task<(long UnitId, ItemStage Stage)> RequireSupervisorOfStageUnitAsync(Principal user, long itemId, int stageNo, CancellationToken cancellationToken)
    {
        var unitId = await sessions.FindStageUnitAsync(itemId, stageNo, cancellationToken) ?? throw new SessionRefusedException(ValidationIssue.StageUnitRequired);
        await RequireSupervisorAsync(user, unitId, cancellationToken);
        return (unitId, null!);
    }

    // Reads that decide a dispatch, outside the transaction: the operator, whether the operator is authorized for the unit, qualified for the machine, and available now. The machine and the stage are re-read inside.
    private async Task<Plan> PlanAsync(DateTime now, long operatorId, long machineId, long unitId, CancellationToken cancellationToken)
    {
        var worker = (await operators.ListOperatorsAsync(cancellationToken)).Select(o => o.Value).FirstOrDefault(o => o.Id == operatorId) ?? throw new SessionRefusedException(ValidationIssue.SessionOperatorUnavailable);
        var machines = (await planning.ListMachinesAsync(cancellationToken)).Select(m => m.Value).ToList();
        var machine = machines.FirstOrDefault(m => m.Id == machineId) ?? throw new SessionRefusedException(ValidationIssue.SessionMachineUnusable);
        if (machine.ProductionUnitId != unitId)
        {
            throw new SessionRefusedException(ValidationIssue.SessionMachineNotInUnit);
        }

        var (localDate, localTime) = dates.UtcToLocal(now);
        var localNow = localDate.ToDateTime(localTime);
        var unitMachines = machines.Where(m => m.ProductionUnitId == unitId).Select(m => m.Id).ToHashSet();
        var assignments = (await operators.ListAssignmentsAsync(cancellationToken)).Select(a => a.Value).ToList();
        var linkedDesignation = worker.UserId is { } linked
            && (await unitStore.ListDesignationsAsync(cancellationToken)).Any(d => d.Value.UserId == linked && d.Value.ProductionUnitId == unitId && d.Value.IsActive);
        var authorized = linkedDesignation || assignments.Any(a => unitMachines.Contains(a.MachineId) && a.OperatorId == operatorId && a.CoversDate(localDate));
        var qualified = OperatorMachineRules.IsQualified((await operators.ListQualificationsAsync(cancellationToken)).Select(q => q.Value), operatorId, machineId, localDate);
        var config = await scheduling.LoadConfigurationAsync(cancellationToken);
        var rows = (await scheduling.ListSchedulesAsync(localDate.AddDays(-1), localDate, cancellationToken)).Select(r => r.Value).ToList();
        var offDays = (await scheduling.ListOffDaysAsync(cancellationToken)).Select(o => o.Value).ToList();
        var extra = (await overtime.ListAsync(localDate.AddDays(-1), localDate, cancellationToken)).Select(o => o.Value).ToList();
        var availability = OperatorAvailabilityRules.Evaluate(localNow, worker, unitId, offDays, rows, config.Templates.Select(t => t.Value).ToDictionary(t => t.Id), config.ToCalendar(), extra);
        return new Plan(machine, worker, authorized, qualified, availability);
    }

    private async Task CheckStageOperationalAsync(ISessionWorkspace ws, RequestItem item, ItemStage stage, CancellationToken cancellationToken)
    {
        var requestId = (await ws.FindStageAsync(item.Id, stage.StageNo, cancellationToken))!.Value.Request;
        if (requestId.Status is RequestStatus.Cancelled or RequestStatus.Closed)
        {
            throw new SessionRefusedException(ValidationIssue.StageGateNotSatisfied);
        }

        if (stage.Status is not (ItemStageStatus.Waiting or ItemStageStatus.InProgress))
        {
            throw new SessionRefusedException(ValidationIssue.SessionStageNotOperational);
        }

        if (stage.IsStopped)
        {
            throw new SessionRefusedException(ValidationIssue.StageStopped);
        }

        var gate = await gates.EvaluateAsync(requestId.Id, item.Id, cancellationToken);
        if (gate is not { Satisfied: true })
        {
            throw new SessionRefusedException(ValidationIssue.StageGateNotSatisfied);
        }
    }

    // The machine and operator rules, checked against the rows read INSIDE the transaction. Returns the warnings that stay on the session. The database's unique open-interval indexes back the busy checks.
    private async Task<Flags> CheckResourcesAsync(ISessionWorkspace ws, long itemId, ItemStage stage, Plan plan, string? outsideShiftReason, long exceptSessionId, CancellationToken cancellationToken)
    {
        var machine = await ws.FindMachineAsync(plan.Machine.Id, cancellationToken);
        if (machine is null || !machine.IsActive)
        {
            throw new SessionRefusedException(ValidationIssue.SessionMachineUnusable);
        }

        if (machine.ProductionUnitId != stage.ProductionUnitId)
        {
            throw new SessionRefusedException(ValidationIssue.SessionMachineNotInUnit);
        }

        if (!plan.OperatorAuthorizedForUnit || plan.Availability is OperatorAvailability.Inactive or OperatorAvailability.Absent)
        {
            throw new SessionRefusedException(ValidationIssue.SessionOperatorUnavailable);
        }

        string? reason = null;
        if (plan.Availability == OperatorAvailability.OutsideShift)
        {
            reason = string.IsNullOrWhiteSpace(outsideShiftReason) ? throw new SessionRefusedException(ValidationIssue.SessionOutsideShiftReasonRequired) : outsideShiftReason.Trim();
        }

        if (await ws.IsMachineBusyAsync(machine.Id, exceptSessionId, cancellationToken))
        {
            throw new SessionRefusedException(ValidationIssue.SessionMachineBusy);
        }

        if (await ws.IsOperatorBusyAsync(plan.Operator.Id, exceptSessionId, cancellationToken))
        {
            throw new SessionRefusedException(ValidationIssue.SessionOperatorBusy);
        }

        if (await ws.StageHasReportLinesAsync(itemId, stage.StageNo, cancellationToken))
        {
            throw new SessionRefusedException(ValidationIssue.SessionTimeBasisConflict);
        }

        return new Flags(!plan.Qualified, reason);
    }

    // Starts the session and moves a waiting stage to in progress through the existing Domain transition, in the same transaction.
    private async Task StartInsideAsync(ISessionWorkspace ws, RequestItem item, ItemStage stage, WorkSession session, Principal user, DateTime now, CancellationToken cancellationToken)
    {
        if (stage.Status == ItemStageStatus.Waiting)
        {
            item.StartStage(stage.StageNo);
        }

        session.Start(user.UserId, now);
        ws.AddAudit(Audit(user, now, AuditActions.SessionStarted, SessionKey(session), $"Work session {session.Id} started on stage {item.Id}/{stage.StageNo}, machine {session.MachineId}, operator {session.OperatorId}"));
        await Task.CompletedTask;
    }

    private static string Warnings(Flags flags, ItemStage stage, long machineId) =>
        (flags.QualificationMissing ? "; WARNING: operator has no current qualification for the machine" : string.Empty)
        + (flags.OutsideShiftReason is null ? string.Empty : "; outside the shift: " + flags.OutsideShiftReason)
        + (stage.MachineId is { } planned && planned != machineId ? $"; differs from the planned machine {planned}" : string.Empty);

    private static string SessionKey(WorkSession session) => session.RequestItemId + "/" + session.StageNo + "/s" + session.Id;

    private AuditLogEntry Audit(Principal user, DateTime now, string code, string targetId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, "WorkSession", targetId, summary);
}
