using ERP.MES.Application.Costing;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Materials;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Tests;

// The five distinct actual-time metrics and machine-rate costing from sessions (Decision Log #114), over fictional instants. Local time is UTC+3:30 where a test needs a zone.
public class SessionMetricsTests
{
    private static readonly DateTime T0 = new(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Offset = TimeSpan.FromMinutes(210);

    private static DateTime At(double minutes) => T0.AddMinutes(minutes);

    private static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(utc + Offset);

    private static DateTime LocalMidnightUtc(DateOnly date) => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue) - Offset, DateTimeKind.Utc);

    private static WorkSession Run(long machine, long op, double start, double end, long item = 1, int stage = 1, long id = 0)
    {
        var s = WorkSession.Dispatch(item, stage, 3, machine, op, null, false, null, 100, At(start - 1));
        s.Start(100, At(start));
        s.Finish(SessionFinishKind.Completed, null, 100, At(end));
        typeof(WorkSession).GetProperty(nameof(WorkSession.Id))!.SetValue(s, id);
        return s;
    }

    [Fact]
    public void Two_machines_for_two_hours_are_four_machine_hours_but_two_hours_elapsed()
    {
        var sessions = new[] { Run(5, 7, 0, 120, id: 1), Run(6, 8, 0, 120, id: 2) };

        var m = SessionMetricsCalculator.Compute(sessions, [new StagePlan(1, 1, 90m)], At(500));

        Assert.Equal(240, m.MachineHours.Sum(r => r.Runtime.TotalMinutes));
        Assert.Equal(120, Assert.Single(m.Elapsed).Elapsed.TotalMinutes);
        Assert.Equal(240, m.LaborHours.Sum(r => r.Time.TotalMinutes));
        var planned = Assert.Single(m.PlannedVsActual);
        Assert.Equal((90m, 240d, 120d, 240d), (planned.PlannedMinutes, planned.MachineRuntime.TotalMinutes, planned.Elapsed.TotalMinutes, planned.Labor.TotalMinutes));
    }

    [Fact]
    public void A_handover_on_one_machine_counts_machine_time_once_and_each_operators_labour_separately()
    {
        var sessions = new[] { Run(5, 7, 0, 120, id: 1), Run(5, 8, 120, 240, id: 2) };

        var m = SessionMetricsCalculator.Compute(sessions, [], At(500));

        var machine = Assert.Single(m.MachineHours);
        Assert.Equal(240, machine.Runtime.TotalMinutes);
        Assert.Equal([120d, 120d], m.LaborHours.OrderBy(r => r.OperatorId).Select(r => r.Time.TotalMinutes).ToArray());
        Assert.Equal(240, Assert.Single(m.Elapsed).Elapsed.TotalMinutes);
    }

    [Fact]
    public void Overlapping_intervals_on_one_machine_are_merged_so_runtime_never_exceeds_the_calendar_span()
    {
        var sessions = new[] { Run(5, 7, 0, 100, id: 1), Run(5, 8, 60, 180, id: 2) };

        Assert.Equal(180, Assert.Single(SessionMetricsCalculator.Compute(sessions, [], At(500)).MachineHours).Runtime.TotalMinutes);
    }

    [Fact]
    public void Pauses_are_totalled_by_reason_and_excluded_from_active_time()
    {
        var s = WorkSession.Dispatch(1, 1, 3, 5, 7, null, false, null, 100, At(-1));
        s.Start(100, At(0));
        s.Pause(2, false, null, PauseScope.Machine, null, 100, At(60));
        s.Resume(100, At(90));
        s.Pause(2, false, null, PauseScope.Machine, null, 100, At(120));
        s.Resume(100, At(135));
        s.Pause(5, false, null, PauseScope.Session, null, 100, At(150));
        s.Resume(100, At(160));
        s.Finish(SessionFinishKind.Completed, null, 100, At(200));

        var m = SessionMetricsCalculator.Compute([s], [], At(500));

        Assert.Equal((45d, 2), (m.PausedByReason.Single(r => r.ReasonId == 2).Time.TotalMinutes, m.PausedByReason.Single(r => r.ReasonId == 2).Pauses));
        Assert.Equal(10, m.PausedByReason.Single(r => r.ReasonId == 5).Time.TotalMinutes);
        Assert.Equal(200 - 55, Assert.Single(m.MachineHours).Runtime.TotalMinutes);
    }

    [Fact]
    public void Cancelled_sessions_and_the_include_filter_keep_unwanted_time_out()
    {
        var cancelled = WorkSession.Dispatch(1, 1, 3, 5, 7, null, false, null, 100, At(0));
        cancelled.Cancel("x", 100, At(1));
        var approved = Run(5, 7, 0, 60, id: 1);
        approved.ApproveTime(9, At(70));
        var unapproved = Run(6, 8, 0, 60, id: 2);

        Assert.Equal(120, SessionMetricsCalculator.Compute([cancelled, approved, unapproved], [], At(500)).MachineHours.Sum(r => r.Runtime.TotalMinutes));
        Assert.Equal(60, SessionMetricsCalculator.Compute([cancelled, approved, unapproved], [], At(500), s => s.IsTimeApproved).MachineHours.Sum(r => r.Runtime.TotalMinutes));
    }

    [Fact]
    public void An_unfinished_session_counts_up_to_the_given_instant()
    {
        var s = WorkSession.Dispatch(1, 1, 3, 5, 7, null, false, null, 100, At(-1));
        s.Start(100, At(0));

        var m = SessionMetricsCalculator.Compute([s], [], At(45));

        Assert.Equal(45, Assert.Single(m.MachineHours).Runtime.TotalMinutes);
        Assert.True(Assert.Single(m.Elapsed).IsOpen);
    }

    // ---- midnight ----

    [Fact]
    public void An_interval_across_local_midnight_is_split_between_the_two_dates()
    {
        // Local 23:30 on the 24th is 20:00 UTC; local midnight is 20:30 UTC.
        var start = new DateTime(2026, 10, 24, 20, 0, 0, DateTimeKind.Utc);

        var slices = IntervalDays.Split(start, start.AddMinutes(90), LocalDate, LocalMidnightUtc);

        Assert.Equal([(new DateOnly(2026, 10, 24), 30d), (new DateOnly(2026, 10, 25), 60d)], slices.Select(s => (s.Date, s.Time.TotalMinutes)).ToArray());
    }

    [Fact]
    public void An_interval_spanning_two_midnights_gets_three_slices_that_add_up()
    {
        var start = new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);

        var slices = IntervalDays.Split(start, start.AddHours(40), LocalDate, LocalMidnightUtc);

        Assert.Equal(3, slices.Count);
        Assert.Equal(40 * 60, slices.Sum(s => s.Time.TotalMinutes));
    }

    [Fact]
    public void Machine_days_merge_overlaps_split_at_midnight_and_round_per_machine_day()
    {
        var start = new DateTime(2026, 10, 24, 20, 0, 0, DateTimeKind.Utc);
        var a = Run(5, 7, 0, 0.5, id: 1);
        var first = WorkSession.Dispatch(1, 1, 3, 5, 7, null, false, null, 100, start.AddMinutes(-1));
        first.Start(100, start);
        first.Finish(SessionFinishKind.HandedOver, null, 100, start.AddMinutes(40));
        var second = WorkSession.Dispatch(1, 1, 3, 5, 8, null, false, null, 100, start.AddMinutes(39));
        second.Start(100, start.AddMinutes(40));
        second.Finish(SessionFinishKind.Completed, null, 100, start.AddMinutes(90).AddSeconds(20));
        typeof(WorkSession).GetProperty(nameof(WorkSession.Id))!.SetValue(first, 3L);
        typeof(WorkSession).GetProperty(nameof(WorkSession.Id))!.SetValue(second, 4L);

        var days = SessionMetricsCalculator.MachineDays([first, second], start.AddDays(2), LocalDate, LocalMidnightUtc);

        Assert.Equal([(new DateOnly(2026, 10, 24), 30), (new DateOnly(2026, 10, 25), 60)], days.Select(d => (d.Date, d.Minutes)).ToArray());
        Assert.Equal([3L, 4L], days[1].SessionIds.ToArray());
        Assert.NotNull(a);
    }

    // ---- costing ----

    private static MachineHourlyRate Rate(long id, long machine, decimal rate, DateOnly from) => new(id, machine, rate, from, null, 1, T0);

    private static CostInputs Inputs(IReadOnlyList<WorkSession> sessions, IReadOnlyList<MachineHourlyRate> rates, int unapproved = 0, IReadOnlyList<ApprovedTimeLine>? lines = null) =>
        new(
            1, 1, "R-1", "A", WorkType.Make, false, true, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), 100, 100, null, null, null,
            lines ?? [], rates, new Dictionary<long, string> { [5] = "M1", [6] = "M2" }, [], 1, sessions, unapproved);

    [Fact]
    public void Machine_cost_is_the_machine_runtime_of_approved_sessions_times_the_rate_in_force_counted_once_across_a_handover()
    {
        var first = Run(5, 7, 0, 120, id: 1);
        var second = Run(5, 8, 120, 240, id: 2);

        var build = CostCalculator.Build(Inputs([first, second], [Rate(1, 5, 1_200_000m, new DateOnly(2026, 1, 1))]), 9, At(1000), LocalDate, LocalMidnightUtc);

        Assert.Equal(ValidationIssue.None, build.Refusal);
        var line = Assert.Single(build.Calculation!.Lines);
        Assert.Equal((CostLineKind.Machining, 5L, 240, 4_800_000m, "1,2"), (line.Kind, line.MachineId, line.Minutes, line.AmountRial, line.SessionIds));
        Assert.Null(line.JobReportLineId);
        Assert.Equal(4_800_000m, build.Calculation.TotalRial);
    }

    [Fact]
    public void Two_machines_running_together_cost_both_machines_and_operators_add_no_cost_of_their_own()
    {
        var a = Run(5, 7, 0, 120, id: 1);
        var b = Run(6, 8, 0, 120, id: 2);
        var rates = new[] { Rate(1, 5, 1_200_000m, new DateOnly(2026, 1, 1)), Rate(2, 6, 600_000m, new DateOnly(2026, 1, 1)) };

        var build = CostCalculator.Build(Inputs([a, b], rates), 9, At(1000), LocalDate, LocalMidnightUtc);

        Assert.Equal([2_400_000m, 1_200_000m], build.Calculation!.Lines.OrderBy(l => l.MachineId).Select(l => l.AmountRial).ToArray());
        Assert.Equal(3_600_000m, build.Calculation.TotalRial);
    }

    [Fact]
    public void A_rate_change_at_local_midnight_prices_each_days_share_with_the_rate_of_that_day()
    {
        var start = new DateTime(2026, 10, 24, 20, 0, 0, DateTimeKind.Utc);
        var s = WorkSession.Dispatch(1, 1, 3, 5, 7, null, false, null, 100, start.AddMinutes(-1));
        s.Start(100, start);
        s.Finish(SessionFinishKind.Completed, null, 100, start.AddMinutes(90));
        var rates = new[] { Rate(1, 5, 1_200_000m, new DateOnly(2026, 1, 1)), Rate(2, 5, 2_400_000m, new DateOnly(2026, 10, 25)) };

        var build = CostCalculator.Build(Inputs([s], rates), 9, At(5000), LocalDate, LocalMidnightUtc);

        Assert.Equal([(new DateOnly(2026, 10, 24), 30, 600_000m), (new DateOnly(2026, 10, 25), 60, 2_400_000m)], build.Calculation!.Lines.Select(l => (l.WorkDate!.Value, l.Minutes!.Value, l.AmountRial)).ToArray());
    }

    [Fact]
    public void Session_time_and_report_lines_of_different_stages_are_both_costed_and_missing_rates_are_named()
    {
        var s = Run(5, 7, 0, 60, id: 1);
        var withBoth = CostCalculator.Build(Inputs([s], [Rate(1, 5, 1_200_000m, new DateOnly(2026, 1, 1)), Rate(2, 6, 600_000m, new DateOnly(2026, 1, 1))], lines: [new ApprovedTimeLine(11, 6, new DateOnly(2026, 10, 24), 60)]), 9, At(1000), LocalDate, LocalMidnightUtc);
        var noRate = CostCalculator.Build(Inputs([s], []), 9, At(1000), LocalDate, LocalMidnightUtc);

        Assert.Equal(1_800_000m, withBoth.Calculation!.TotalRial);
        Assert.Equal(ValidationIssue.CostingRateMissing, noRate.Refusal);
        Assert.Equal("RATE|M1|2026-10-24", Assert.Single(noRate.Gaps));
    }

    [Fact]
    public void Finished_sessions_whose_time_is_not_approved_stop_the_calculation_instead_of_being_left_out()
    {
        var s = Run(5, 7, 0, 60, id: 1);

        var build = CostCalculator.Build(Inputs([s], [Rate(1, 5, 1_200_000m, new DateOnly(2026, 1, 1))], unapproved: 1), 9, At(1000), LocalDate, LocalMidnightUtc);

        Assert.Equal(ValidationIssue.CostingSessionsNotApproved, build.Refusal);
        Assert.Equal(ValidationIssue.CostingNoApprovedTime, CostCalculator.Build(Inputs([], [Rate(1, 5, 1_200_000m, new DateOnly(2026, 1, 1))]), 9, At(1000), LocalDate, LocalMidnightUtc).Refusal);
    }
}
