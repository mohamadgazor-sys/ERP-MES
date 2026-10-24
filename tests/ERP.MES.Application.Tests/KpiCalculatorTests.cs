using ERP.MES.Application.Persistence;
using ERP.MES.Application.Reporting;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Tests;

// The unit metrics (docs/JOB_REPORTS_KPI_AND_OVERTIME_DESIGN.md section 7; Decision Log #95 D6, #106) over fictional numbers, without a database. The display zone is UTC here.
public class KpiCalculatorTests
{
    private static readonly DateOnly From = new(2026, 10, 24);
    private static readonly DateOnly To = new(2026, 10, 30);
    private static readonly DateTime Run = new(2026, 10, 31, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Func<DateTime, DateTime> Local = utc => utc;

    private static DateTime At(int day, int hour, int minute = 0) => new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc).AddDays(day).AddHours(hour).AddMinutes(minute);

    private static KpiApprovedLine Line(long report, long person, long unit, int day, int fromHour, int toHour, int downtime = 0) =>
        new(report, person, unit, DateOnly.FromDateTime(At(day, 0)), At(day, fromHour), At(day, toHour), (toHour - fromHour) * 60 - downtime);

    private static KpiFacts Facts(
        IReadOnlyList<KpiApprovedLine>? lines = null, IReadOnlyList<KpiPending>? pending = null, IReadOnlyList<KpiInspection>? inspections = null, IReadOnlyList<KpiCompletedStage>? completed = null,
        IReadOnlyList<KpiScheduledRow>? scheduled = null, IReadOnlyList<KpiOvertimeWindow>? overtime = null) =>
        new(
            [new KpiUnit(1, "MILLING", "فرز"), new KpiUnit(2, "TURNING", "تراش")],
            [new KpiOperator(10, "OP-1", "علی"), new KpiOperator(11, "OP-2", "رضا"), new KpiOperator(12, "OP-3", "سارا")],
            lines ?? [], pending ?? [], inspections ?? [], completed ?? [], scheduled ?? [], overtime ?? []);

    [Fact]
    public void Productive_hours_rates_plan_versus_actual_and_utilization_are_separate_metrics_per_unit()
    {
        var facts = Facts(
            lines: [Line(1, 10, 1, 0, 8, 12, downtime: 30), Line(2, 11, 1, 1, 8, 12)],
            inspections:
            [
                new KpiInspection(1, InspectionDisposition.Accepted, 80, At(1, 12)),
                new KpiInspection(1, InspectionDisposition.Rework, 15, At(1, 12)),
                new KpiInspection(1, InspectionDisposition.Scrap, 5, At(1, 12)),
                new KpiInspection(2, InspectionDisposition.Accepted, 10, At(2, 12)),
            ],
            completed: [new KpiCompletedStage(1, 5, 1, 120m, 300, At(1, 12)), new KpiCompletedStage(1, 6, 1, 60m, 60, At(2, 9))],
            scheduled: [new KpiScheduledRow(1, 10, From, 480), new KpiScheduledRow(1, 11, From.AddDays(1), 480)]);

        var report = KpiCalculator.Build(facts, From, To, Run, Local, null);

        var milling = report.Units.Single(u => u.Code == "MILLING");
        Assert.Equal((210 + 240, 80L, 15L, 5L), (milling.ProductiveMinutes, milling.AcceptedBaseUnits, milling.ReworkBaseUnits, milling.ScrapBaseUnits));
        Assert.Equal((0.8m, 0.15m, 0.05m), (milling.AcceptanceRate, milling.ReworkRate, milling.ScrapRate));
        Assert.Equal((2, 180m, 360, 2m), (milling.CompletedStages, milling.PlannedMinutes, milling.ActualMinutes, milling.ActualToPlanned));
        Assert.Equal((960, 0.4688m), (milling.ScheduledMinutes, milling.Utilization));
        Assert.True(milling.IsAuthoritative);

        // The other unit has its own figures; nothing is combined into a score or an order of units.
        var turning = report.Units.Single(u => u.Code == "TURNING");
        Assert.Equal((0, 10L, 1m, (decimal?)null, (decimal?)null), (turning.ProductiveMinutes, turning.AcceptedBaseUnits, turning.AcceptanceRate, turning.Utilization, turning.ActualToPlanned));
        Assert.Equal(new[] { "MILLING", "TURNING" }, report.Units.Select(u => u.Code).ToArray());
    }

    [Fact]
    public void The_period_is_exact_in_local_days_for_lines_decisions_completions_and_schedule()
    {
        var facts = Facts(
            lines: [Line(1, 10, 1, -1, 8, 12), Line(2, 10, 1, 0, 8, 12), Line(3, 10, 1, 7, 8, 12)],
            inspections: [new KpiInspection(1, InspectionDisposition.Accepted, 1, At(-1, 23)), new KpiInspection(1, InspectionDisposition.Accepted, 2, At(0, 0)), new KpiInspection(1, InspectionDisposition.Accepted, 4, At(7, 0))],
            completed: [new KpiCompletedStage(1, 5, 1, 10m, 10, At(-1, 23)), new KpiCompletedStage(1, 6, 1, 10m, 10, At(6, 23))],
            scheduled: [new KpiScheduledRow(1, 10, From.AddDays(-1), 480), new KpiScheduledRow(1, 10, From, 480), new KpiScheduledRow(1, 10, To.AddDays(1), 480)]);

        var milling = KpiCalculator.Build(facts, From, To, Run, Local, null).Units.Single(u => u.Code == "MILLING");

        Assert.Equal((240, 2L, 1, 480), (milling.ProductiveMinutes, milling.AcceptedBaseUnits, milling.CompletedStages, milling.ScheduledMinutes));
        // A decision made just before midnight UTC belongs to the next local day when the zone is ahead.
        var ahead = KpiCalculator.Build(Facts(inspections: [new KpiInspection(1, InspectionDisposition.Accepted, 7, At(-1, 22))]), From, To, Run, utc => utc.AddHours(3), null).Units.Single(u => u.Code == "MILLING");
        Assert.Equal(7L, ahead.AcceptedBaseUnits);
    }

    [Fact]
    public void Overtime_is_estimated_from_the_time_overlap_with_the_operators_approved_windows_and_is_included_in_productive_hours()
    {
        var facts = Facts(
            lines: [Line(1, 10, 1, 0, 8, 12), Line(2, 11, 1, 0, 8, 12)],
            overtime: [new KpiOvertimeWindow(10, At(0, 11), At(0, 13)), new KpiOvertimeWindow(12, At(0, 8), At(0, 12))]);

        var milling = KpiCalculator.Build(facts, From, To, Run, Local, null).Units.Single(u => u.Code == "MILLING");

        Assert.Equal((480, 60), (milling.ProductiveMinutes, milling.EstimatedOvertimeMinutes)); // only OP-1's last hour overlaps its window
    }

    [Fact]
    public void A_unit_with_reports_waiting_for_approval_is_not_authoritative_and_the_number_is_shown()
    {
        var facts = Facts(lines: [Line(1, 10, 1, 0, 8, 12)], pending: [new KpiPending(1, 2)]);

        var report = KpiCalculator.Build(facts, From, To, Run, Local, null);

        Assert.Equal((2, false), (report.Units.Single(u => u.Code == "MILLING").PendingReports, report.Units.Single(u => u.Code == "MILLING").IsAuthoritative));
        Assert.True(report.Units.Single(u => u.Code == "TURNING").IsAuthoritative);
        Assert.Equal(2, report.TotalPendingReports);
        Assert.Equal((From, To, Run), (report.From, report.To, report.RunAtUtc));
    }

    [Fact]
    public void Operators_are_ranked_by_valid_hours_then_distinct_days_then_code_and_a_supervisor_sees_only_their_units()
    {
        var facts = Facts(
            lines:
            [
                Line(1, 11, 1, 0, 8, 12), Line(2, 11, 1, 1, 8, 12),   // OP-2: 480 minutes on 2 days
                Line(3, 10, 1, 0, 8, 16),                              // OP-1: 480 minutes on 1 day
                Line(4, 12, 1, 0, 8, 16),                              // OP-3: 480 minutes on 1 day
                Line(5, 10, 2, 2, 8, 9),                               // OP-1 in the other unit: 60 minutes
            ]);

        var all = KpiCalculator.Build(facts, From, To, Run, Local, null).Operators;
        // OP-1 has the most minutes (480 + 60); OP-2 and OP-3 tie on 480 minutes and OP-2 worked more distinct days.
        Assert.Equal(new[] { "OP-1", "OP-2", "OP-3" }, all.Select(o => o.Code).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, all.Select(o => o.Rank).ToArray());
        Assert.Equal(new[] { (540, 2), (480, 2), (480, 1) }, all.Select(o => (o.ProductiveMinutes, o.WorkedDays)).ToArray());

        // With equal minutes and days the code decides.
        var tie = KpiCalculator.Build(Facts(lines: [Line(1, 12, 1, 0, 8, 12), Line(2, 11, 1, 0, 8, 12)]), From, To, Run, Local, null).Operators;
        Assert.Equal(new[] { "OP-2", "OP-3" }, tie.Select(o => o.Code).ToArray());

        var supervisor = KpiCalculator.Build(facts, From, To, Run, Local, new HashSet<long> { 2 });
        Assert.Equal(new[] { "TURNING" }, supervisor.Units.Select(u => u.Code).ToArray());
        Assert.Equal(new[] { "OP-1" }, supervisor.Operators.Select(o => o.Code).ToArray());
        Assert.Equal(60, supervisor.Operators.Single().ProductiveMinutes);
    }
}
