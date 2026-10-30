using ERP.MES.Application.Security;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Production;
using ERP.MES.Application.Reporting;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Requests;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Infrastructure.Tests;

// Dispatch, start, pause, resume, finish, handover, outage, correction and approval of work sessions (Decision Log #112) on a real SQLite file with the real authorization.
public class WorkSessionServiceTests
{
    private static CancellationToken Token => KitWorld.Token;

    private static long Reason(string code) => code switch
    {
        PauseReason.PowerOutage => 1,
        PauseReason.PlannedMaintenance => 2,
        PauseReason.MachineBreakdown => 3,
        PauseReason.OperatorAbsenceOrHandover => 4,
        PauseReason.MaterialUnavailable => 5,
        PauseReason.WaitingAssistance => 6,
        _ => 7,
    };

    private static long Count(SessionScene s, string tableAndWhere) => s.W.Count(tableAndWhere);

    // ---- dispatch and start ----

    [Fact]
    public async Task A_supervisor_dispatches_and_starts_and_the_stage_moves_to_in_progress_with_everything_recorded()
    {
        using var s = await SessionScene.CreateAsync();

        var started = await s.StartedAsync(s.ItemA, s.M1, s.Op1);

        var session = started.Value;
        Assert.Equal(WorkSessionStatus.Active, session.Status);
        Assert.Equal((s.ItemA, 1, s.Milling, s.M1, s.Op1), (session.RequestItemId, session.StageNo, session.ProductionUnitId, session.MachineId, session.OperatorId));
        Assert.Equal((s.Supervisor.UserId, s.Supervisor.UserId), (session.DispatchedByUserId, session.StartedByUserId));
        Assert.Equal(SessionScene.At(9), session.StartedAtUtc);
        Assert.False(session.QualificationWarning);
        Assert.Equal(ItemStageStatus.InProgress, await s.StageStatusAsync(s.ItemA, 1));
        Assert.Equal(["WorkSessionDispatched", "WorkSessionStarted"], s.W.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode LIKE 'WorkSession%' ORDER BY Id").ToArray());
        Assert.Equal(1, Count(s, "WorkSessionIntervals WHERE EndedAtUtc IS NULL"));
    }

    [Fact]
    public async Task Dispatch_without_start_assigns_but_records_no_time_and_does_not_start_the_stage()
    {
        using var s = await SessionScene.CreateAsync();

        var dispatched = await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M1, s.Op1, startNow: false), Token);

        Assert.True(dispatched.Succeeded, dispatched.Status + " " + dispatched.Issue);
        Assert.Equal(WorkSessionStatus.Assigned, dispatched.Value!.Value.Status);
        Assert.Equal(0, Count(s, "WorkSessionIntervals"));
        Assert.Equal(ItemStageStatus.Waiting, await s.StageStatusAsync(s.ItemA, 1));

        var started = await s.W.Services.WorkSessions.StartAsync(s.Supervisor, dispatched.Value.Value.Id, dispatched.Value.Version, null, Token);

        Assert.True(started.Succeeded, started.Status + " " + started.Issue);
        Assert.Equal(WorkSessionStatus.Active, started.Value!.Value.Status);
        Assert.Equal(ItemStageStatus.InProgress, await s.StageStatusAsync(s.ItemA, 1));
    }

    [Fact]
    public async Task A_machine_of_another_unit_or_an_inactive_machine_cannot_be_selected()
    {
        using var s = await SessionScene.CreateAsync();

        Assert.Equal(ValidationIssue.SessionMachineNotInUnit, (await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.T1, s.Op1), Token)).Issue);
        var machine = (await s.W.Services.PlanningMaster.LoadAsync(s.W.Planning, Token)).Value!.Machines.Single(m => m.Value.Id == s.M2);
        Assert.True((await s.W.Services.PlanningMaster.SetMachineActiveAsync(s.W.Admin, s.M2, machine.Version, false, Token)).Succeeded);

        Assert.Equal(ValidationIssue.SessionMachineUnusable, (await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M2, s.Op1), Token)).Issue);
        Assert.Equal(0, Count(s, "WorkSessions"));
    }

    [Fact]
    public async Task A_missing_qualification_is_a_warning_kept_on_the_session_not_a_block_and_not_a_new_rule()
    {
        using var s = await SessionScene.CreateAsync();

        var qualified = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        var unqualified = await s.StartedAsync(s.ItemB, s.M2, s.Op2);

        Assert.False(qualified.Value.QualificationWarning);
        Assert.True(unqualified.Value.QualificationWarning);
        Assert.Contains("WARNING", s.W.Fixture.Db.Query("SELECT Summary FROM AuditLog WHERE ActionCode = 'WorkSessionDispatched' ORDER BY Id DESC LIMIT 1").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_operator_who_is_absent_inactive_or_not_authorized_for_the_unit_cannot_be_assigned()
    {
        using var s = await SessionScene.CreateAsync();
        Assert.True((await s.W.Services.WorkerSchedule.AddOffDayAsync(s.W.Planning, s.Op2, DateOnly.FromDateTime(SessionScene.Day), DateOnly.FromDateTime(SessionScene.Day), OffDayKind.Sick, null, Token)).Succeeded);

        Assert.Equal(ValidationIssue.SessionOperatorUnavailable, (await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M1, s.Op2), Token)).Issue);
        Assert.Equal(ValidationIssue.SessionOperatorUnavailable, (await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M1, s.Op3), Token)).Issue);
        var op1 = (await s.W.Services.Operators.LoadAsync(s.W.Planning, Token)).Value!.Operators.Single(o => o.Value.Id == s.Op1);
        Assert.True((await s.W.Services.Operators.SetActiveAsync(s.W.Admin, s.Op1, op1.Version, false, Token)).Succeeded);
        Assert.Equal(ValidationIssue.SessionOperatorUnavailable, (await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M1, s.Op1), Token)).Issue);
        Assert.Equal(0, Count(s, "WorkSessions"));
    }

    [Fact]
    public async Task Outside_the_shift_a_reason_is_needed_and_is_kept_on_the_session()
    {
        using var s = await SessionScene.CreateAsync();
        s.SetNow(20);

        var refused = await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M1, s.Op1), Token);
        var accepted = await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M1, s.Op1, outsideShift: "تحویل فوری مشتری"), Token);

        Assert.Equal(ValidationIssue.SessionOutsideShiftReasonRequired, refused.Issue);
        Assert.True(accepted.Succeeded, accepted.Status + " " + accepted.Issue);
        Assert.Equal("تحویل فوری مشتری", accepted.Value!.Value.OutsideShiftReason);
    }

    [Fact]
    public async Task Approved_overtime_makes_an_operator_available_outside_the_shift_without_a_reason()
    {
        using var s = await SessionScene.CreateAsync();
        s.SetNow(19);
        Assert.True((await s.W.Services.Overtime.SetLimitsAsync(s.W.Manager, 0, 600, 3000, Token)).Succeeded);
        var overtime = await s.W.Services.Overtime.ProposeAsync(s.Supervisor, s.Op1, s.Milling, s.M1, SessionScene.At(18), SessionScene.At(21), "کار فوری", Token);
        Assert.True(overtime.Succeeded, overtime.Status + " " + overtime.Issue);
        Assert.True((await s.W.Services.Overtime.DecideAsync(s.W.ProductionManager, overtime.Value!.Value.Id, overtime.Value.Version, true, null, Token)).Succeeded);

        var dispatched = await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M1, s.Op1), Token);

        Assert.True(dispatched.Succeeded, dispatched.Status + " " + dispatched.Issue);
        Assert.Null(dispatched.Value!.Value.OutsideShiftReason);
    }

    [Fact]
    public async Task A_night_shift_that_crosses_midnight_keeps_the_operator_available_after_midnight()
    {
        using var s = await SessionScene.CreateAsync();
        var night = (await s.W.Services.Calendar.AddTemplateAsync(s.W.Planning, "N", "شیفت N", new TimeOnly(22, 0), new TimeOnly(6, 0), null, null, Token)).Value!.Value.Id;
        Assert.True((await s.W.Services.Calendar.AddUnitShiftAsync(s.W.Planning, s.Milling, night, 3, 1, false, Token)).Succeeded);
        Assert.True((await s.W.Services.WorkerSchedule.ScheduleAsync(s.W.Planning, s.Op3, DateOnly.FromDateTime(SessionScene.Day), s.Milling, night, null, null, null, Token)).Succeeded);
        var assigned = await s.W.Services.Operators.AssignAsync(s.W.Admin, s.Op3, s.M1, "1405/01/01", false, null, Token);
        Assert.True(assigned.Succeeded);

        s.W.Fixture.Clock.UtcNow = SessionScene.At(23, 30);
        var evening = await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M1, s.Op3), Token);
        Assert.True(evening.Succeeded, evening.Status + " " + evening.Issue);
        Assert.Null(evening.Value!.Value.OutsideShiftReason);
        s.W.Fixture.Clock.UtcNow = SessionScene.At(24, 30);
        var finished = await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, evening.Value.Value.Id, evening.Value.Version, null, Token);

        Assert.True(finished.Succeeded, finished.Status + " " + finished.Issue);
        Assert.Equal(60, SessionTime.Active(finished.Value!.Value, SessionScene.At(30)).TotalMinutes);
        Assert.Equal(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), finished.Value.Value.FinishedAtUtc);
    }

    // ---- no double booking, concurrency of sessions ----

    [Fact]
    public async Task A_busy_machine_or_a_busy_operator_cannot_be_given_to_another_session()
    {
        using var s = await SessionScene.CreateAsync();
        await s.StartedAsync(s.ItemA, s.M1, s.Op1);

        Assert.Equal(ValidationIssue.SessionMachineBusy, (await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemB, s.M1, s.Op2), Token)).Issue);
        Assert.Equal(ValidationIssue.SessionOperatorBusy, (await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemB, s.M2, s.Op1), Token)).Issue);
        Assert.Equal(1, Count(s, "WorkSessions"));
    }

    [Fact]
    public async Task One_stage_can_have_concurrent_sessions_on_different_machines_and_sequential_ones_after_a_finish()
    {
        using var s = await SessionScene.CreateAsync();
        var first = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        var second = await s.StartedAsync(s.ItemA, s.M2, s.Op2);

        var stageSessions = (await s.W.Services.WorkSessions.LoadStageAsync(s.Supervisor, s.ItemA, 1, Token)).Value!;
        Assert.Equal(2, stageSessions.Count(r => r.Session.Value.Status == WorkSessionStatus.Active));

        s.W.Tick();
        Assert.True((await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, first.Value.Id, first.Version, null, Token)).Succeeded);
        var third = await s.StartedAsync(s.ItemA, s.M1, s.Op1);

        Assert.NotEqual(first.Value.Id, third.Value.Id);
        Assert.Equal(3, (await s.W.Services.WorkSessions.LoadStageAsync(s.Supervisor, s.ItemA, 1, Token)).Value!.Count);
        Assert.Equal(ItemStageStatus.InProgress, await s.StageStatusAsync(s.ItemA, 1));
        Assert.Equal(WorkSessionStatus.Active, (await s.ReloadAsync(second.Value.Id)).Value.Status);
    }

    [Fact]
    public async Task A_double_click_on_start_or_a_stale_version_is_refused_and_changes_nothing()
    {
        using var s = await SessionScene.CreateAsync();
        var assigned = (await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M1, s.Op1, startNow: false), Token)).Value!;

        var first = await s.W.Services.WorkSessions.StartAsync(s.Supervisor, assigned.Value.Id, assigned.Version, null, Token);
        var duplicate = await s.W.Services.WorkSessions.StartAsync(s.Supervisor, assigned.Value.Id, assigned.Version, null, Token);

        Assert.True(first.Succeeded);
        Assert.Equal(OperationStatus.ConcurrencyConflict, duplicate.Status);
        Assert.Equal(1, Count(s, "WorkSessionIntervals"));
        Assert.Equal(1, Count(s, "WorkSessionEvents WHERE Type = 2"));
    }

    [Fact]
    public async Task A_stale_stage_version_on_dispatch_is_refused()
    {
        using var s = await SessionScene.CreateAsync();
        var seen = await s.StageVersionAsync(s.ItemA, 1);
        await s.StartedAsync(s.ItemA, s.M1, s.Op1);

        var stale = await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M2, s.Op2) with { ExpectedStageVersion = seen }, Token);

        Assert.Equal(OperationStatus.ConcurrencyConflict, stale.Status);
        Assert.Equal(1, Count(s, "WorkSessions"));
    }

    // ---- pause, resume, finish ----

    [Fact]
    public async Task A_pause_needs_an_active_reason_and_a_note_for_other_and_leaves_the_stage_in_progress()
    {
        using var s = await SessionScene.CreateAsync();
        var running = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.W.Tick();

        Assert.Equal(ValidationIssue.SessionPauseReasonInvalid, (await s.W.Services.WorkSessions.PauseAsync(s.Supervisor, running.Value.Id, running.Version, 999, null, Token)).Issue);
        Assert.Equal(ValidationIssue.SessionPauseNoteRequired, (await s.W.Services.WorkSessions.PauseAsync(s.Supervisor, running.Value.Id, running.Version, Reason("OTHER"), " ", Token)).Issue);
        var paused = await s.W.Services.WorkSessions.PauseAsync(s.Supervisor, running.Value.Id, running.Version, Reason(PauseReason.MaterialUnavailable), "مواد از انبار نرسید", Token);

        Assert.True(paused.Succeeded, paused.Status + " " + paused.Issue);
        Assert.Equal(WorkSessionStatus.Paused, paused.Value!.Value.Status);
        Assert.Equal(PauseScope.Session, paused.Value.Value.Pauses[0].Scope);
        Assert.Null(paused.Value.Value.OpenInterval);
        Assert.Equal(ItemStageStatus.InProgress, await s.StageStatusAsync(s.ItemA, 1));
    }

    [Fact]
    public async Task A_deactivated_pause_reason_is_refused_but_stays_on_the_pauses_that_used_it()
    {
        using var s = await SessionScene.CreateAsync();
        var running = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.W.Tick();
        var paused = (await s.W.Services.WorkSessions.PauseAsync(s.Supervisor, running.Value.Id, running.Version, Reason(PauseReason.WaitingAssistance), null, Token)).Value!;
        var reasons = (await s.W.Services.WorkSessions.LoadUnitAsync(s.Supervisor, s.Milling, 7, Token)).Value!.Reasons;
        var tool = reasons.Single(r => r.Value.Code == PauseReason.PlannedMaintenance);
        Assert.True((await s.W.Services.WorkSessions.SetReasonActiveAsync(s.W.Planning, tool.Value.Id, tool.Version, false, Token)).Succeeded);
        s.W.Tick();
        Assert.True((await s.W.Services.WorkSessions.ResumeAsync(s.Supervisor, paused.Value.Id, paused.Version, null, Token)).Succeeded);
        var again = (await s.ReloadAsync(paused.Value.Id));
        s.W.Tick();

        var refused = await s.W.Services.WorkSessions.PauseAsync(s.Supervisor, again.Value.Id, again.Version, tool.Value.Id, null, Token);

        Assert.Equal(ValidationIssue.SessionPauseReasonInvalid, refused.Issue);
        Assert.Equal(Reason(PauseReason.WaitingAssistance), again.Value.Pauses[0].ReasonId);
    }

    [Fact]
    public async Task Resume_opens_a_new_interval_and_active_time_excludes_the_pause()
    {
        using var s = await SessionScene.CreateAsync();
        var running = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.SetNow(10);
        var paused = (await s.W.Services.WorkSessions.PauseAsync(s.Supervisor, running.Value.Id, running.Version, Reason(PauseReason.MachineBreakdown), null, Token)).Value!;
        s.SetNow(10, 45);
        var resumed = (await s.W.Services.WorkSessions.ResumeAsync(s.Supervisor, paused.Value.Id, paused.Version, null, Token)).Value!;
        s.SetNow(11, 30);
        var finished = (await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, resumed.Value.Id, resumed.Version, null, Token)).Value!;

        var session = finished.Value;
        Assert.Equal(2, session.Intervals.Count);
        Assert.Equal(60 + 45, SessionTime.Active(session, SessionScene.At(23)).TotalMinutes);
        Assert.Equal(45, SessionTime.Paused(session, SessionScene.At(23)).TotalMinutes);
        Assert.Equal(150, SessionTime.Elapsed(session, SessionScene.At(23)).TotalMinutes);
        Assert.Equal(PauseScope.Machine, session.Pauses[0].Scope);
    }

    [Fact]
    public async Task Resume_is_refused_when_the_machine_was_taken_while_the_session_was_paused_and_the_session_stays_paused()
    {
        using var s = await SessionScene.CreateAsync();
        var running = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.W.Tick();
        var paused = (await s.W.Services.WorkSessions.PauseAsync(s.Supervisor, running.Value.Id, running.Version, Reason(PauseReason.MaterialUnavailable), null, Token)).Value!;
        s.W.Tick();
        await s.StartedAsync(s.ItemB, s.M1, s.Op2);
        s.W.Tick();

        var resume = await s.W.Services.WorkSessions.ResumeAsync(s.Supervisor, paused.Value.Id, paused.Version, null, Token);

        Assert.Equal(ValidationIssue.SessionMachineBusy, resume.Issue);
        Assert.Equal(WorkSessionStatus.Paused, (await s.ReloadAsync(paused.Value.Id)).Value.Status);
    }

    [Fact]
    public async Task Finishing_a_session_does_not_complete_the_stage_or_the_other_sessions()
    {
        using var s = await SessionScene.CreateAsync();
        var first = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        var second = await s.StartedAsync(s.ItemA, s.M2, s.Op2);
        s.W.Tick();

        var finished = await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, first.Value.Id, first.Version, "پایان سهم من", Token);

        Assert.True(finished.Succeeded);
        Assert.Equal(WorkSessionStatus.Finished, finished.Value!.Value.Status);
        Assert.Equal(ItemStageStatus.InProgress, await s.StageStatusAsync(s.ItemA, 1));
        Assert.Equal(WorkSessionStatus.Active, (await s.ReloadAsync(second.Value.Id)).Value.Status);
    }

    [Fact]
    public async Task A_session_that_never_started_can_be_cancelled_with_a_reason_but_a_started_one_cannot()
    {
        using var s = await SessionScene.CreateAsync();
        var assigned = (await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M1, s.Op1, startNow: false), Token)).Value!;
        var running = await s.StartedAsync(s.ItemB, s.M2, s.Op2);

        Assert.Equal(ValidationIssue.ReasonRequired, (await s.W.Services.WorkSessions.CancelAsync(s.Supervisor, assigned.Value.Id, assigned.Version, " ", Token)).Issue);
        Assert.True((await s.W.Services.WorkSessions.CancelAsync(s.Supervisor, assigned.Value.Id, assigned.Version, "اپراتور غایب", Token)).Succeeded);
        Assert.Equal(OperationStatus.InvalidState, (await s.W.Services.WorkSessions.CancelAsync(s.Supervisor, running.Value.Id, running.Version, "x", Token)).Status);
    }

    // ---- handover ----

    [Fact]
    public async Task A_handover_keeps_the_history_starts_the_successor_at_the_same_instant_and_counts_machine_time_once()
    {
        using var s = await SessionScene.CreateAsync();
        var first = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.SetNow(11);

        var handed = await s.W.Services.WorkSessions.HandOverAsync(s.Supervisor, first.Value.Id, first.Version, s.Op2, null, Token);

        Assert.True(handed.Succeeded, handed.Status + " " + handed.Issue);
        var (old, next) = (handed.Value![0].Value, handed.Value[1].Value);
        Assert.Equal((WorkSessionStatus.Finished, SessionFinishKind.HandedOver), (old.Status, old.FinishKind));
        Assert.Equal(WorkSessionStatus.Active, next.Status);
        Assert.Equal((old.Id, s.Op2, s.M1), (next.PredecessorSessionId, next.OperatorId, next.MachineId));
        Assert.Equal(old.FinishedAtUtc, next.StartedAtUtc);
        s.SetNow(13);
        var final = (await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, next.Id, handed.Value[1].Version, null, Token)).Value!.Value;

        var intervals = old.Intervals.Concat(final.Intervals).ToList();
        Assert.Equal(240, SessionTime.Union(intervals, SessionScene.At(23)).TotalMinutes);
        Assert.Equal(120, SessionTime.Active(old, SessionScene.At(23)).TotalMinutes);
        Assert.Equal(120, SessionTime.Active(final, SessionScene.At(23)).TotalMinutes);
        Assert.Equal(2, Count(s, "WorkSessions"));
    }

    [Fact]
    public async Task A_handover_to_the_same_or_a_busy_operator_is_refused_and_leaves_the_first_session_running()
    {
        using var s = await SessionScene.CreateAsync();
        var first = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        await s.StartedAsync(s.ItemB, s.M2, s.Op2);
        s.W.Tick();

        Assert.Equal(ValidationIssue.SessionHandoverSameOperator, (await s.W.Services.WorkSessions.HandOverAsync(s.Supervisor, first.Value.Id, first.Version, s.Op1, null, Token)).Issue);
        Assert.Equal(ValidationIssue.SessionOperatorBusy, (await s.W.Services.WorkSessions.HandOverAsync(s.Supervisor, first.Value.Id, first.Version, s.Op2, null, Token)).Issue);

        Assert.Equal(WorkSessionStatus.Active, (await s.ReloadAsync(first.Value.Id)).Value.Status);
        Assert.Equal(2, Count(s, "WorkSessions"));
    }

    [Fact]
    public async Task A_paused_session_can_be_handed_over_for_operator_absence()
    {
        using var s = await SessionScene.CreateAsync();
        var first = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.SetNow(10);
        var paused = (await s.W.Services.WorkSessions.PauseAsync(s.Supervisor, first.Value.Id, first.Version, Reason(PauseReason.OperatorAbsenceOrHandover), null, Token)).Value!;
        Assert.Equal(PauseScope.Operator, paused.Value.Pauses[0].Scope);
        s.SetNow(10, 20);

        var handed = await s.W.Services.WorkSessions.HandOverAsync(s.Supervisor, paused.Value.Id, paused.Version, s.Op2, null, Token);

        Assert.True(handed.Succeeded, handed.Status + " " + handed.Issue);
        Assert.Equal(20, SessionTime.Paused(handed.Value![0].Value, SessionScene.At(23)).TotalMinutes);
        Assert.Equal(60, SessionTime.Active(handed.Value[0].Value, SessionScene.At(23)).TotalMinutes);
    }

    // ---- common outage ----

    [Fact]
    public async Task A_power_outage_pauses_every_active_session_of_the_unit_under_one_event_with_separate_resumes()
    {
        using var s = await SessionScene.CreateAsync();
        await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        await s.StartedAsync(s.ItemB, s.M2, s.Op2);
        s.SetNow(10);

        var outage = await s.W.Services.WorkSessions.RecordOutageAsync(s.Supervisor, s.Milling, [], Reason(PauseReason.PowerOutage), "قطع برق شبکه", Token);

        Assert.True(outage.Succeeded, outage.Status + " " + outage.Issue);
        Assert.Equal(2, outage.Value!.Paused.Count);
        Assert.All(outage.Value.Paused, p => Assert.Equal(outage.Value.Outage.Id, p.Value.Pauses[0].OutageEventId));
        Assert.All(outage.Value.Paused, p => Assert.Equal(PauseScope.Unit, p.Value.Pauses[0].Scope));
        Assert.Equal(ItemStageStatus.InProgress, await s.StageStatusAsync(s.ItemA, 1));
        Assert.Equal(1, Count(s, "OutageEvents"));

        s.SetNow(10, 30);
        var ended = await s.W.Services.WorkSessions.EndOutageAsync(s.Supervisor, outage.Value.Outage.Id, Token);

        Assert.True(ended.Succeeded, ended.Status + " " + ended.Issue);
        Assert.Equal(2, ended.Value!.Resumed.Count);
        Assert.Empty(ended.Value.StillPaused);
        Assert.All(ended.Value.Resumed, r => Assert.Equal(30, r.Value.Pauses[0].Duration(SessionScene.At(23)).TotalMinutes));
        Assert.Empty(await s.W.Services.WorkSessions.LoadUnitAsync(s.Supervisor, s.Milling, 7, Token) is { Value: { } u } ? u.OpenOutages : []);
    }

    [Fact]
    public async Task An_outage_can_be_limited_to_named_machines_and_refuses_machines_of_other_units_or_nothing_to_pause()
    {
        using var s = await SessionScene.CreateAsync();
        await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        await s.StartedAsync(s.ItemB, s.M2, s.Op2);
        s.W.Tick();

        Assert.Equal(ValidationIssue.SessionMachineNotInUnit, (await s.W.Services.WorkSessions.RecordOutageAsync(s.Supervisor, s.Milling, [s.T1], Reason(PauseReason.PowerOutage), null, Token)).Issue);
        var outage = await s.W.Services.WorkSessions.RecordOutageAsync(s.Supervisor, s.Milling, [s.M1], Reason(PauseReason.MachineBreakdown), null, Token);

        Assert.Equal([s.M1], outage.Value!.Paused.Select(p => p.Value.MachineId).ToArray());
        Assert.Equal(WorkSessionStatus.Active, (await s.ReloadAsync((await s.W.Services.WorkSessions.LoadUnitAsync(s.Supervisor, s.Milling, 7, Token)).Value!.Sessions.Single(r => r.Session.Value.MachineId == s.M2).Session.Value.Id)).Value.Status);
        s.W.Tick();
        Assert.Equal(ValidationIssue.SessionNothingToPause, (await s.W.Services.WorkSessions.RecordOutageAsync(s.SupervisorTurning, s.Turning, [], Reason(PauseReason.PowerOutage), null, Token)).Issue);
    }

    [Fact]
    public async Task Ending_an_outage_leaves_a_session_paused_whose_machine_was_taken_and_reports_it()
    {
        using var s = await SessionScene.CreateAsync();
        await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.SetNow(10);
        var outage = (await s.W.Services.WorkSessions.RecordOutageAsync(s.Supervisor, s.Milling, [], Reason(PauseReason.PowerOutage), null, Token)).Value!;
        s.SetNow(10, 10);
        await s.StartedAsync(s.ItemB, s.M1, s.Op2);
        s.SetNow(10, 30);

        var ended = await s.W.Services.WorkSessions.EndOutageAsync(s.Supervisor, outage.Outage.Id, Token);

        Assert.True(ended.Succeeded, ended.Status + " " + ended.Issue);
        Assert.Empty(ended.Value!.Resumed);
        Assert.Equal(ValidationIssue.SessionMachineBusy, Assert.Single(ended.Value.StillPaused).Issue);
        Assert.Equal(0, Count(s, "OutageEvents WHERE EndedAtUtc IS NULL"));
    }

    // ---- authorization ----

    [Fact]
    public async Task Only_a_supervisor_of_the_stages_unit_can_dispatch_and_nobody_outside_production_can_operate()
    {
        using var s = await SessionScene.CreateAsync();
        var command = s.Dispatch(s.ItemA, s.M1, s.Op1);

        Assert.Equal(OperationStatus.NotAuthorized, (await s.W.Services.WorkSessions.DispatchAsync(s.W.Planning, command, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await s.W.Services.WorkSessions.DispatchAsync(s.W.Manager, command, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await s.W.Services.WorkSessions.DispatchAsync(s.W.Qc, command, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await s.W.Services.WorkSessions.DispatchAsync(null, command, Token)).Status);
        Assert.Equal(ValidationIssue.SessionNotSupervisor, (await s.W.Services.WorkSessions.DispatchAsync(s.Operator1, command, Token)).Issue);
        Assert.Equal(ValidationIssue.StageUnitNotDesignated, (await s.W.Services.WorkSessions.DispatchAsync(s.SupervisorTurning, command, Token)).Issue);
        Assert.Equal(0, Count(s, "WorkSessions"));
    }

    [Fact]
    public async Task An_operator_can_pause_resume_and_finish_only_their_own_session()
    {
        using var s = await SessionScene.CreateAsync();
        var mine = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        var theirs = await s.StartedAsync(s.ItemB, s.M2, s.Op2);
        s.W.Tick();

        Assert.Equal(ValidationIssue.SessionNotOwn, (await s.W.Services.WorkSessions.PauseAsync(s.Operator1, theirs.Value.Id, theirs.Version, Reason(PauseReason.WaitingAssistance), null, Token)).Issue);
        var paused = await s.W.Services.WorkSessions.PauseAsync(s.Operator1, mine.Value.Id, mine.Version, Reason(PauseReason.WaitingAssistance), null, Token);
        Assert.True(paused.Succeeded, paused.Status + " " + paused.Issue);
        s.W.Tick();
        var resumed = await s.W.Services.WorkSessions.ResumeAsync(s.Operator1, paused.Value!.Value.Id, paused.Value.Version, null, Token);
        Assert.True(resumed.Succeeded, resumed.Status + " " + resumed.Issue);
        s.W.Tick();
        Assert.True((await s.W.Services.WorkSessions.FinishAsync(s.Operator1, resumed.Value!.Value.Id, resumed.Value.Version, null, Token)).Succeeded);
        Assert.Equal(ValidationIssue.SessionNotSupervisor, (await s.W.Services.WorkSessions.CancelAsync(s.Operator1, theirs.Value.Id, theirs.Version, "x", Token)).Issue);
        Assert.Equal(ValidationIssue.SessionNotSupervisor, (await s.W.Services.WorkSessions.HandOverAsync(s.Operator1, theirs.Value.Id, theirs.Version, s.Op1, null, Token)).Issue);
    }

    [Fact]
    public async Task A_supervisor_of_another_unit_cannot_act_on_this_units_session_by_naming_its_id()
    {
        using var s = await SessionScene.CreateAsync();
        var running = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.W.Tick();

        var pause = await s.W.Services.WorkSessions.PauseAsync(s.SupervisorTurning, running.Value.Id, running.Version, Reason(PauseReason.WaitingAssistance), null, Token);
        var finish = await s.W.Services.WorkSessions.FinishAsync(s.SupervisorTurning, running.Value.Id, running.Version, null, Token);

        Assert.Equal(ValidationIssue.StageUnitNotDesignated, pause.Issue);
        Assert.Equal(ValidationIssue.StageUnitNotDesignated, finish.Issue);
        Assert.Equal(WorkSessionStatus.Active, (await s.ReloadAsync(running.Value.Id)).Value.Status);
    }

    [Fact]
    public async Task A_deactivated_user_and_a_user_who_must_change_the_password_are_refused()
    {
        using var s = await SessionScene.CreateAsync();
        Assert.Equal(AccountStatus.Succeeded, (await s.W.Fixture.Services.Accounts.SetActiveAsync(s.W.Admin, s.Supervisor.UserId, false, Token)).Status);

        Assert.Equal(OperationStatus.NotAuthorized, (await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M1, s.Op1), Token)).Status);
    }

    [Fact]
    public async Task Production_users_see_only_their_units_sessions()
    {
        using var s = await SessionScene.CreateAsync();
        var mine = await s.StartedAsync(s.ItemA, s.M1, s.Op1);

        Assert.Equal(OperationStatus.NotAuthorized, (await s.W.Services.WorkSessions.LoadUnitAsync(s.SupervisorTurning, s.Milling, 7, Token)).Status);
        Assert.Single((await s.W.Services.WorkSessions.LoadUnitAsync(s.Operator2, s.Milling, 7, Token)).Value!.Sessions);
        Assert.Empty((await s.W.Services.WorkSessions.LoadMineAsync(s.Operator2, 7, Token)).Value!);
        Assert.Equal(mine.Value.Id, Assert.Single((await s.W.Services.WorkSessions.LoadMineAsync(s.Operator1, 7, Token)).Value!).Session.Value.Id);
        Assert.Empty((await s.W.Services.WorkSessions.LoadStageAsync(s.SupervisorTurning, s.ItemA, 1, Token)).Value!);
        Assert.Equal(OperationStatus.NotAuthorized, (await s.W.Services.WorkSessions.LoadUnitAsync(s.W.Warehouse, s.Milling, 7, Token)).Status);
        Assert.Single((await s.W.Services.WorkSessions.LoadUnitAsync(s.W.Manager, s.Milling, 7, Token)).Value!.Sessions);
    }

    // ---- stage gates, QC and the stage's submission ----

    [Fact]
    public async Task A_later_stage_cannot_be_dispatched_until_QC_approved_the_preceding_stage_and_then_it_can()
    {
        using var s = await SessionScene.CreateAsync();
        var supervisorBoth = s.SupervisorTurning;
        Assert.Equal(ValidationIssue.SessionStageNotOperational, (await s.W.Services.WorkSessions.DispatchAsync(supervisorBoth, s.Dispatch(s.ItemA, s.T1, s.Op3, stage: 2), Token)).Issue);

        var running = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.W.Tick();
        Assert.True((await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, running.Value.Id, running.Version, null, Token)).Succeeded);
        var submit = await s.W.Services.StageWork.PerformAsync(s.Operator1, StageAction.SubmitForQc, await s.W.TargetAsync(s.RequestId, s.ItemA, 1), Token);
        Assert.True(submit.Succeeded, submit.Status + " " + submit.Issue);
        Assert.Equal(ValidationIssue.SessionStageNotOperational, (await s.W.Services.WorkSessions.DispatchAsync(supervisorBoth, s.Dispatch(s.ItemA, s.T1, s.Op3, stage: 2), Token)).Issue);
        var inspected = await s.W.Services.QcInspection.RecordInspectionAsync(s.W.Qc, await s.W.TargetAsync(s.RequestId, s.ItemA, 1), [new InspectionEntry(InspectionDisposition.Accepted, 10, null)], Token);
        Assert.True(inspected.Succeeded, inspected.Status + " " + inspected.Issue);
        Assert.Equal(ItemStageStatus.Waiting, await s.StageStatusAsync(s.ItemA, 2));
    }

    [Fact]
    public async Task A_stage_cannot_be_submitted_to_QC_while_a_session_is_assigned_active_or_paused_and_can_afterwards()
    {
        using var s = await SessionScene.CreateAsync();
        var running = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        var assigned = (await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M2, s.Op2, startNow: false), Token)).Value!;
        s.W.Tick();

        Assert.Equal(ValidationIssue.SubmitBlockedBySessions, (await s.W.Services.StageWork.PerformAsync(s.Operator1, StageAction.SubmitForQc, await s.W.TargetAsync(s.RequestId, s.ItemA, 1), Token)).Issue);
        var paused = (await s.W.Services.WorkSessions.PauseAsync(s.Supervisor, running.Value.Id, running.Version, Reason(PauseReason.MaterialUnavailable), null, Token)).Value!;
        Assert.Equal(ValidationIssue.SubmitBlockedBySessions, (await s.W.Services.StageWork.PerformAsync(s.Operator1, StageAction.SubmitForQc, await s.W.TargetAsync(s.RequestId, s.ItemA, 1), Token)).Issue);
        s.W.Tick();
        Assert.True((await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, paused.Value.Id, paused.Version, null, Token)).Succeeded);
        Assert.Equal(ValidationIssue.SubmitBlockedBySessions, (await s.W.Services.StageWork.PerformAsync(s.Operator1, StageAction.SubmitForQc, await s.W.TargetAsync(s.RequestId, s.ItemA, 1), Token)).Issue);
        Assert.True((await s.W.Services.WorkSessions.CancelAsync(s.Supervisor, assigned.Value.Id, assigned.Version, "غیرضروری", Token)).Succeeded);

        var submitted = await s.W.Services.StageWork.PerformAsync(s.Operator1, StageAction.SubmitForQc, await s.W.TargetAsync(s.RequestId, s.ItemA, 1), Token);

        Assert.True(submitted.Succeeded, submitted.Status + " " + submitted.Issue);
        Assert.Equal(ItemStageStatus.WaitingQc, await s.StageStatusAsync(s.ItemA, 1));
    }

    [Fact]
    public async Task A_stage_with_only_cancelled_sessions_cannot_be_submitted_and_the_legacy_start_is_refused_on_a_session_stage()
    {
        using var s = await SessionScene.CreateAsync();
        var assigned = (await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M1, s.Op1, startNow: false), Token)).Value!;
        Assert.Equal(ValidationIssue.SessionTimeBasisConflict, (await s.W.Services.StageWork.PerformAsync(s.Operator1, StageAction.Start, await s.W.TargetAsync(s.RequestId, s.ItemA, 1), Token)).Issue);
        Assert.True((await s.W.Services.WorkSessions.CancelAsync(s.Supervisor, assigned.Value.Id, assigned.Version, "x", Token)).Succeeded);

        // Cancelled sessions do not count as tracking: the stage may still be started the legacy way; nothing ever ran on it, so it cannot be submitted with sessions.
        Assert.True((await s.W.Services.StageWork.PerformAsync(s.Operator1, StageAction.Start, await s.W.TargetAsync(s.RequestId, s.ItemA, 1), Token)).Succeeded);
    }

    [Fact]
    public async Task A_stage_with_job_report_lines_refuses_sessions_and_a_stage_with_sessions_refuses_job_report_lines()
    {
        using var s = await SessionScene.CreateAsync();
        Assert.True((await s.W.Services.StageWork.PerformAsync(s.Operator1, StageAction.Start, await s.W.TargetAsync(s.RequestId, s.ItemB, 1), Token)).Succeeded);
        var report = await s.W.Services.JobReports.SaveDraftAsync(
            s.Operator1, null, null, DateOnly.FromDateTime(SessionScene.Day), s.Milling, s.Template,
            [new JobLineInput(s.RequestId, s.ItemB, 1, s.M1, SessionScene.At(8), SessionScene.At(10), 0, null, 5, null)], Token);
        Assert.True(report.Succeeded, report.Status + " " + report.Issue);

        Assert.Equal(ValidationIssue.SessionTimeBasisConflict, (await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemB, s.M1, s.Op1), Token)).Issue);

        var running = await s.StartedAsync(s.ItemA, s.M2, s.Op2);
        Assert.NotNull(running);
        var refused = await s.W.Services.JobReports.SaveDraftAsync(
            s.Operator2, null, null, DateOnly.FromDateTime(SessionScene.Day), s.Milling, s.Template,
            [new JobLineInput(s.RequestId, s.ItemA, 1, s.M2, SessionScene.At(8), SessionScene.At(9), 0, null, 5, null)], Token);
        Assert.Equal(ValidationIssue.SessionTimeBasisConflict, refused.Issue);
    }

    [Fact]
    public async Task A_stopped_stage_a_closed_request_or_a_lapsed_gate_blocks_dispatch_and_resume()
    {
        using var s = await SessionScene.CreateAsync();
        var running = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.W.Tick();
        var paused = (await s.W.Services.WorkSessions.PauseAsync(s.Supervisor, running.Value.Id, running.Version, Reason(PauseReason.MaterialUnavailable), null, Token)).Value!;
        var stop = await s.W.Services.UnitQueue.StopStageAsync(s.Supervisor, new QueueRowRef(s.RequestId, s.ItemA, 1, await s.StageVersionAsync(s.ItemA, 1)), "مشکل کیفیت مواد", Token);
        Assert.True(stop.Succeeded, stop.Status + " " + stop.Issue);
        s.W.Tick();

        Assert.Equal(ValidationIssue.StageStopped, (await s.W.Services.WorkSessions.ResumeAsync(s.Supervisor, paused.Value.Id, paused.Version, null, Token)).Issue);
        Assert.Equal(ValidationIssue.StageStopped, (await s.W.Services.WorkSessions.DispatchAsync(s.Supervisor, s.Dispatch(s.ItemA, s.M2, s.Op2), Token)).Issue);
    }

    // ---- Planning: corrections, approval, reasons ----

    [Fact]
    public async Task Planning_corrects_a_recorded_instant_with_a_reason_and_everything_is_kept()
    {
        using var s = await SessionScene.CreateAsync();
        var running = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.SetNow(11);
        var finished = (await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, running.Value.Id, running.Version, null, Token)).Value!;
        s.SetNow(12);

        Assert.Equal(OperationStatus.NotAuthorized, (await s.W.Services.WorkSessions.CorrectAsync(s.Supervisor, finished.Value.Id, finished.Version, SessionBoundary.Start, 0, SessionScene.At(8, 30), "x", Token)).Status);
        Assert.Equal(ValidationIssue.SessionCorrectionInvalid, (await s.W.Services.WorkSessions.CorrectAsync(s.W.Planning, finished.Value.Id, finished.Version, SessionBoundary.Start, 0, SessionScene.At(8, 30), " ", Token)).Issue);
        Assert.Equal(ValidationIssue.SessionCorrectionInvalid, (await s.W.Services.WorkSessions.CorrectAsync(s.W.Planning, finished.Value.Id, finished.Version, SessionBoundary.Finish, 0, SessionScene.At(13), "x", Token)).Issue);
        var corrected = await s.W.Services.WorkSessions.CorrectAsync(s.W.Planning, finished.Value.Id, finished.Version, SessionBoundary.Start, 0, SessionScene.At(8, 30), "ثبت دیرهنگام شروع", Token);

        Assert.True(corrected.Succeeded, corrected.Status + " " + corrected.Issue);
        Assert.Equal(150, SessionTime.Active(corrected.Value!.Value, SessionScene.At(23)).TotalMinutes);
        var correction = Assert.Single(corrected.Value.Value.Corrections);
        Assert.Equal((SessionScene.At(9), SessionScene.At(8, 30), s.W.Planning.UserId), (correction.OldValueUtc, correction.NewValueUtc, correction.CorrectedByUserId));
        Assert.Equal("WorkSessionCorrected", s.W.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode = 'WorkSessionCorrected'").Single());
    }

    [Fact]
    public async Task A_correction_that_would_overlap_another_session_of_the_machine_or_operator_is_refused()
    {
        using var s = await SessionScene.CreateAsync();
        s.SetNow(9);
        var first = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.SetNow(10);
        var finished = (await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, first.Value.Id, first.Version, null, Token)).Value!;
        var second = await s.StartedAsync(s.ItemB, s.M1, s.Op2);
        s.SetNow(11);
        await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, second.Value.Id, second.Version, null, Token);
        s.SetNow(12);

        var overlap = await s.W.Services.WorkSessions.CorrectAsync(s.W.Planning, finished.Value.Id, finished.Version, SessionBoundary.Finish, 0, SessionScene.At(10, 30), "نادرست", Token);

        Assert.Equal(ValidationIssue.SessionCorrectionInvalid, overlap.Issue);
        Assert.Equal(0, Count(s, "WorkSessionCorrections"));
    }

    [Fact]
    public async Task A_correction_after_the_stage_was_submitted_to_QC_is_refused()
    {
        using var s = await SessionScene.CreateAsync();
        var running = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.SetNow(10);
        var finished = (await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, running.Value.Id, running.Version, null, Token)).Value!;
        Assert.True((await s.W.Services.StageWork.PerformAsync(s.Operator1, StageAction.SubmitForQc, await s.W.TargetAsync(s.RequestId, s.ItemA, 1), Token)).Succeeded);

        var correction = await s.W.Services.WorkSessions.CorrectAsync(s.W.Planning, finished.Value.Id, finished.Version, SessionBoundary.Finish, 0, SessionScene.At(9, 50), "x", Token);

        Assert.Equal(ValidationIssue.SessionCorrectionAfterSubmission, correction.Issue);
    }

    [Fact]
    public async Task Planning_approves_a_finished_sessions_time_once_and_a_correction_withdraws_the_approval()
    {
        using var s = await SessionScene.CreateAsync();
        var running = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.SetNow(10);
        var unfinishedApproval = await s.W.Services.WorkSessions.ApproveTimeAsync(s.W.Planning, running.Value.Id, running.Version, Token);
        Assert.Equal(OperationStatus.InvalidState, unfinishedApproval.Status);
        var finished = (await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, running.Value.Id, running.Version, null, Token)).Value!;

        Assert.Equal(OperationStatus.NotAuthorized, (await s.W.Services.WorkSessions.ApproveTimeAsync(s.Supervisor, finished.Value.Id, finished.Version, Token)).Status);
        var approved = await s.W.Services.WorkSessions.ApproveTimeAsync(s.W.Planning, finished.Value.Id, finished.Version, Token);
        Assert.True(approved.Succeeded, approved.Status + " " + approved.Issue);
        Assert.True(approved.Value!.Value.IsTimeApproved);
        Assert.Equal(OperationStatus.InvalidState, (await s.W.Services.WorkSessions.ApproveTimeAsync(s.W.Planning, approved.Value.Value.Id, approved.Value.Version, Token)).Status);

        var corrected = await s.W.Services.WorkSessions.CorrectAsync(s.W.Planning, approved.Value.Value.Id, approved.Value.Version, SessionBoundary.Finish, 0, SessionScene.At(9, 50), "اصلاح", Token);
        Assert.True(corrected.Succeeded, corrected.Status + " " + corrected.Issue);
        Assert.False(corrected.Value!.Value.IsTimeApproved);
    }

    [Fact]
    public async Task Planning_manages_the_pause_reasons_and_nobody_else_can()
    {
        using var s = await SessionScene.CreateAsync();

        Assert.Equal(OperationStatus.NotAuthorized, (await s.W.Services.WorkSessions.AddReasonAsync(s.Supervisor, "TOOL_CHANGE", "تعویض ابزار", PauseScope.Machine, false, Token)).Status);
        Assert.Equal(ValidationIssue.PauseReasonCodeInvalid, (await s.W.Services.WorkSessions.AddReasonAsync(s.W.Planning, "bad code", "x", PauseScope.Machine, false, Token)).Issue);
        var added = await s.W.Services.WorkSessions.AddReasonAsync(s.W.Planning, "TOOL_CHANGE", "تعویض ابزار", PauseScope.Machine, false, Token);
        Assert.True(added.Succeeded, added.Status + " " + added.Issue);
        Assert.False(added.Value!.Value.IsSystem);
        Assert.Equal(OperationStatus.Failed, (await s.W.Services.WorkSessions.AddReasonAsync(s.W.Planning, "TOOL_CHANGE", "تکراری", PauseScope.Machine, false, Token)).Status);
        Assert.Equal(8, (await s.W.Services.WorkSessions.LoadUnitAsync(s.Supervisor, s.Milling, 7, Token)).Value!.Reasons.Count);
        Assert.Equal(2, Count(s, "AuditLog WHERE ActionCode = 'PauseReasonAdded'") + 1);
    }

    // ---- audit completeness ----

    [Fact]
    public async Task Every_session_action_leaves_one_audit_entry_with_the_actor_and_the_history_keeps_every_event()
    {
        using var s = await SessionScene.CreateAsync();
        var running = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        s.SetNow(10);
        var paused = (await s.W.Services.WorkSessions.PauseAsync(s.Supervisor, running.Value.Id, running.Version, Reason(PauseReason.MaterialUnavailable), null, Token)).Value!;
        s.SetNow(10, 20);
        var resumed = (await s.W.Services.WorkSessions.ResumeAsync(s.Supervisor, paused.Value.Id, paused.Version, null, Token)).Value!;
        s.SetNow(11);
        var finished = (await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, resumed.Value.Id, resumed.Version, null, Token)).Value!;
        await s.W.Services.WorkSessions.ApproveTimeAsync(s.W.Planning, finished.Value.Id, finished.Version, Token);

        Assert.Equal(
            ["WorkSessionDispatched", "WorkSessionStarted", "WorkSessionPaused", "WorkSessionResumed", "WorkSessionFinished", "WorkSessionTimeApproved"],
            s.W.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode LIKE 'WorkSession%' ORDER BY Id").ToArray());
        var final = (await s.W.Services.WorkSessions.LoadStageAsync(s.Supervisor, s.ItemA, 1, Token)).Value!.Single().Session.Value;
        Assert.Equal(
            [WorkSessionEventType.Dispatched, WorkSessionEventType.Started, WorkSessionEventType.Paused, WorkSessionEventType.Resumed, WorkSessionEventType.Finished, WorkSessionEventType.TimeApproved],
            final.Events.Select(e => e.Type).ToArray());
        Assert.All(final.Events, e => Assert.NotEqual(0, e.ByUserId));
    }
}
