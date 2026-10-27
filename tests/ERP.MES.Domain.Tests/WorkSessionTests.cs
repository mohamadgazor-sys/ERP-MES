using ERP.MES.Domain.Production;

namespace ERP.MES.Domain.Tests;

// The work session (Decision Log #112): intervals, pauses and history; active time is the sum of intervals, nothing is overwritten.
public class WorkSessionTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);

    private static DateTime At(int minutes) => T0.AddMinutes(minutes);

    private static WorkSession Dispatched() => WorkSession.Dispatch(10, 1, 3, 5, 7, null, false, null, 100, At(-5));

    private static WorkSession Active()
    {
        var s = Dispatched();
        s.Start(101, At(0));
        return s;
    }

    [Fact]
    public void Dispatch_records_who_what_and_when_without_any_time()
    {
        var s = Dispatched();

        Assert.Equal(WorkSessionStatus.Assigned, s.Status);
        Assert.Equal((10L, 1, 3L, 5L, 7L), (s.RequestItemId, s.StageNo, s.ProductionUnitId, s.MachineId, s.OperatorId));
        Assert.Equal(100, s.DispatchedByUserId);
        Assert.Empty(s.Intervals);
        Assert.True(s.IsUnfinished);
        Assert.Equal(WorkSessionEventType.Dispatched, Assert.Single(s.Events).Type);
    }

    [Fact]
    public void Start_opens_the_first_interval_with_the_machine_and_operator_and_the_actor()
    {
        var s = Active();

        var interval = Assert.Single(s.Intervals);
        Assert.Equal((5L, 7L, At(0), 101L), (interval.MachineId, interval.OperatorId, interval.StartedAtUtc, interval.StartedByUserId));
        Assert.True(interval.IsOpen);
        Assert.Equal(WorkSessionStatus.Active, s.Status);
        Assert.Throws<InvalidOperationException>(() => s.Start(101, At(1)));
    }

    [Fact]
    public void A_pause_closes_the_interval_and_a_resume_opens_a_new_one_so_active_time_excludes_the_pause()
    {
        var s = Active();
        s.Pause(1, false, null, PauseScope.Session, null, 101, At(60));
        s.Resume(101, At(90));
        s.Pause(2, false, "قطعی برق", PauseScope.Machine, null, 101, At(150));
        s.Resume(102, At(160));
        s.Finish(SessionFinishKind.Completed, null, 101, At(200));

        Assert.Equal(3, s.Intervals.Count);
        Assert.Equal(2, s.Pauses.Count);
        Assert.Equal(60 + 60 + 40, s.Intervals.Sum(i => i.Duration(At(500)).TotalMinutes));
        Assert.Equal(30 + 10, s.Pauses.Sum(p => p.Duration(At(500)).TotalMinutes));
        Assert.Equal(WorkSessionStatus.Finished, s.Status);
        Assert.False(s.IsUnfinished);
        Assert.All(s.Intervals, i => Assert.Equal((5L, 7L), (i.MachineId, i.OperatorId)));
        Assert.Equal(102, s.Pauses[1].ResumedByUserId);
        Assert.Equal(At(200), s.FinishedAtUtc);
    }

    [Fact]
    public void An_open_interval_counts_up_to_the_given_instant_and_never_negative()
    {
        var s = Active();

        Assert.Equal(45, s.Intervals[0].Duration(At(45)).TotalMinutes);
        Assert.Equal(0, s.Intervals[0].Duration(At(-10)).TotalMinutes);
    }

    [Fact]
    public void A_pause_that_needs_a_note_is_refused_without_one()
    {
        var s = Active();

        Assert.Throws<ArgumentException>(() => s.Pause(7, true, "  ", PauseScope.Session, null, 101, At(10)));
        Assert.Equal(WorkSessionStatus.Active, s.Status);
        Assert.Empty(s.Pauses);
        s.Pause(7, true, "علت دیگر", PauseScope.Session, null, 101, At(10));
        Assert.Equal("علت دیگر", s.Pauses[0].Note);
    }

    [Fact]
    public void Illegal_transitions_are_refused()
    {
        var s = Dispatched();
        Assert.Throws<InvalidOperationException>(() => s.Pause(1, false, null, PauseScope.Session, null, 1, At(1)));
        Assert.Throws<InvalidOperationException>(() => s.Resume(1, At(1)));
        Assert.Throws<InvalidOperationException>(() => s.Finish(SessionFinishKind.Completed, null, 1, At(1)));
        s.Start(1, At(0));
        Assert.Throws<InvalidOperationException>(() => s.Resume(1, At(1)));
        Assert.Throws<InvalidOperationException>(() => s.Cancel("x", 1, At(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Pause(1, false, null, PauseScope.Session, null, 1, At(0)));
        s.Finish(SessionFinishKind.Completed, null, 1, At(10));
        Assert.Throws<InvalidOperationException>(() => s.Pause(1, false, null, PauseScope.Session, null, 1, At(11)));
        Assert.Throws<InvalidOperationException>(() => s.Finish(SessionFinishKind.Completed, null, 1, At(12)));
    }

    [Fact]
    public void Times_must_be_utc_and_in_order()
    {
        var s = Dispatched();
        Assert.Throws<ArgumentException>(() => s.Start(1, new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Local)));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Start(1, At(-10)));
    }

    [Fact]
    public void Finishing_while_paused_ends_the_pause_and_keeps_the_active_time()
    {
        var s = Active();
        s.Pause(3, false, null, PauseScope.Session, null, 101, At(30));

        s.Finish(SessionFinishKind.Completed, "مواد نرسید", 101, At(100));

        Assert.Equal(30, s.Intervals.Sum(i => i.Duration(At(500)).TotalMinutes));
        Assert.Equal(70, s.Pauses[0].Duration(At(500)).TotalMinutes);
        Assert.Null(s.OpenPause);
        Assert.Null(s.OpenInterval);
    }

    [Fact]
    public void A_handover_finishes_the_session_as_handed_over_and_keeps_its_time()
    {
        var s = Active();

        s.Finish(SessionFinishKind.HandedOver, null, 101, At(120));
        var successor = WorkSession.Dispatch(10, 1, 3, 5, 8, s.Id, false, null, 101, At(120));
        successor.Start(101, At(120));

        Assert.Equal(SessionFinishKind.HandedOver, s.FinishKind);
        Assert.Equal(IntervalEnd.HandedOver, s.Intervals[0].EndedBy);
        Assert.Equal(WorkSessionEventType.HandedOver, s.Events[^1].Type);
        Assert.Equal(120, s.Intervals[0].Duration(At(999)).TotalMinutes);
        Assert.Equal(At(120), successor.Intervals[0].StartedAtUtc);
        Assert.Equal(8, successor.OperatorId);
    }

    [Fact]
    public void Only_a_session_that_never_started_can_be_cancelled_and_it_needs_a_reason()
    {
        var s = Dispatched();
        Assert.Throws<ArgumentException>(() => s.Cancel(" ", 1, At(0)));
        s.Cancel("اپراتور غایب", 1, At(0));
        Assert.Equal(WorkSessionStatus.Cancelled, s.Status);
        Assert.False(s.IsUnfinished);
        Assert.Empty(s.Intervals);
    }

    [Fact]
    public void Time_approval_needs_a_finished_session_and_a_correction_withdraws_it_and_keeps_the_original()
    {
        var s = Active();
        Assert.Throws<InvalidOperationException>(() => s.ApproveTime(5, At(10)));
        s.Pause(1, false, null, PauseScope.Session, null, 101, At(60));
        s.Resume(101, At(90));
        s.Finish(SessionFinishKind.Completed, null, 101, At(150));
        s.ApproveTime(5, At(200));
        Assert.True(s.IsTimeApproved);
        Assert.Throws<InvalidOperationException>(() => s.ApproveTime(5, At(201)));

        var correction = s.Correct(SessionBoundary.PauseEnd, 1, At(80), "ثبت دیرهنگام", 6, At(210), At(300));

        Assert.Equal((At(90), At(80)), (correction.OldValueUtc, correction.NewValueUtc));
        Assert.Equal(At(80), s.Pauses[0].ResumedAtUtc);
        Assert.Equal(At(80), s.Intervals[1].StartedAtUtc);
        Assert.False(s.IsTimeApproved);
        Assert.Single(s.Corrections);
        Assert.Equal(60 + 70, s.Intervals.Sum(i => i.Duration(At(500)).TotalMinutes));
        Assert.Equal(WorkSessionEventType.Corrected, s.Events[^1].Type);
        Assert.Contains("approval withdrawn", s.Events[^1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Corrections_keep_the_order_need_a_reason_and_cannot_reach_into_the_future()
    {
        var s = Active();
        s.Pause(1, false, null, PauseScope.Session, null, 101, At(60));
        s.Resume(101, At(90));
        s.Finish(SessionFinishKind.Completed, null, 101, At(150));

        Assert.Throws<ArgumentException>(() => s.Correct(SessionBoundary.Start, 0, At(-5), " ", 6, At(200), At(300)));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Correct(SessionBoundary.Start, 0, At(60), "x", 6, At(200), At(300)));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Correct(SessionBoundary.PauseStart, 1, At(90), "x", 6, At(200), At(300)));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Correct(SessionBoundary.PauseEnd, 1, At(60), "x", 6, At(200), At(300)));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Correct(SessionBoundary.Finish, 0, At(400), "x", 6, At(200), At(300)));
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Correct(SessionBoundary.Finish, 0, At(90), "x", 6, At(200), At(300)));
        Assert.Empty(s.Corrections);

        s.Correct(SessionBoundary.Start, 0, At(-5), "شروع زودتر", 6, At(200), At(300));
        s.Correct(SessionBoundary.PauseStart, 1, At(55), "x", 6, At(201), At(300));
        s.Correct(SessionBoundary.Finish, 0, At(160), "x", 6, At(202), At(300));

        Assert.Equal(3, s.Corrections.Count);
        Assert.Equal(At(-5), s.StartedAtUtc);
        Assert.Equal(At(160), s.FinishedAtUtc);
        Assert.Equal(At(55), s.Intervals[0].EndedAtUtc);
        Assert.Equal(At(55), s.Pauses[0].PausedAtUtc);
    }

    [Fact]
    public void A_session_that_never_started_has_no_time_to_correct_and_the_finish_while_paused_is_corrected_through_the_finish()
    {
        Assert.Throws<InvalidOperationException>(() => Dispatched().Correct(SessionBoundary.Start, 0, At(0), "x", 1, At(1), At(2)));
        var s = Active();
        s.Pause(1, false, null, PauseScope.Session, null, 101, At(30));
        s.Finish(SessionFinishKind.Completed, null, 101, At(100));

        Assert.Throws<InvalidOperationException>(() => s.Correct(SessionBoundary.PauseEnd, 1, At(90), "x", 1, At(200), At(300)));
        s.Correct(SessionBoundary.Finish, 0, At(90), "x", 1, At(200), At(300));

        Assert.Equal(At(90), s.Pauses[0].ResumedAtUtc);
        Assert.Equal(30, s.Intervals.Sum(i => i.Duration(At(500)).TotalMinutes));
    }

    [Fact]
    public void An_outage_event_is_closed_once_and_not_before_it_began()
    {
        var e = new OutageEvent(1, null, "قطعی برق شبکه", 5, At(0));

        Assert.True(e.IsOpen);
        Assert.Throws<ArgumentOutOfRangeException>(() => e.Close(5, At(-1)));
        e.Close(5, At(45));
        Assert.False(e.IsOpen);
        Assert.Throws<InvalidOperationException>(() => e.Close(5, At(50)));
    }

    [Fact]
    public void Pause_reason_codes_and_names_are_validated()
    {
        Assert.Throws<ArgumentException>(() => new PauseReason(0, "bad code", "x", PauseScope.Session, false, false, 1, At(0)));
        Assert.Throws<ArgumentException>(() => new PauseReason(0, "OK", " ", PauseScope.Session, false, false, 1, At(0)));
        var reason = new PauseReason(0, "TOOL_CHANGE", "تعویض ابزار", PauseScope.Machine, false, false, 1, At(0));
        reason.SetActive(false);
        Assert.False(reason.IsActive);
    }
}
