using ERP.MES.Application.Persistence;
using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Sqlite;

namespace ERP.MES.Infrastructure.Tests;

// Actual-time costing and KPI from work sessions on a real SQLite file (Decision Log #114): approved sessions only, machine runtime once across a handover, labour separate.
public class SessionCostingTests
{
    private static CancellationToken Token => KitWorld.Token;

    private static async Task RatesAsync(SessionScene s, decimal rate = 1_200_000m, decimal price = 90_000m)
    {
        Assert.True((await s.W.Services.PlanningMaster.AddRateAsync(s.W.Planning, s.M1, rate, "1405/01/01", "نرخ", Token)).Succeeded);
        var material = (await s.W.Services.Materials.LoadAsync(s.W.Warehouse, Token)).Value!.Materials.First().Value.Id;
        Assert.True((await s.W.Services.Materials.AddPriceAsync(s.W.Warehouse, material, price, "1405/01/01", "قیمت", Token)).Succeeded);
    }

    private static async Task CompleteAsync(SessionScene s, long item)
    {
        var submitted = await s.W.Services.StageWork.PerformAsync(s.Operator1, StageAction.SubmitForQc, await s.W.TargetAsync(s.RequestId, item, 1), Token);
        Assert.True(submitted.Succeeded, submitted.Status + " " + submitted.Issue);
        var inspected = await s.W.Services.QcInspection.RecordInspectionAsync(s.W.Qc, await s.W.TargetAsync(s.RequestId, item, 1), [new InspectionEntry(InspectionDisposition.Accepted, 10, null)], Token);
        Assert.True(inspected.Succeeded, inspected.Status + " " + inspected.Issue);
    }

    /// <summary>Item B: Op1 runs 09:00-10:00 on M1, hands over to Op2 who runs until 12:00; both sessions finished. Returns the two finished sessions.</summary>
    private static async Task<IReadOnlyList<Stored<WorkSession>>> HandoverRunAsync(SessionScene s)
    {
        var first = await s.StartedAsync(s.ItemB, s.M1, s.Op1);
        s.SetNow(10);
        var handed = (await s.W.Services.WorkSessions.HandOverAsync(s.Supervisor, first.Value.Id, first.Version, s.Op2, null, Token)).Value!;
        s.SetNow(12);
        var final = (await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, handed[1].Value.Id, handed[1].Version, null, Token)).Value!;
        return [await s.ReloadAsync(handed[0].Value.Id), final];
    }

    [Fact]
    public async Task Costing_uses_approved_session_machine_time_once_across_a_handover_and_adds_no_labour_cost()
    {
        using var s = await SessionScene.CreateAsync();
        var sessions = await HandoverRunAsync(s);
        await RatesAsync(s);
        await CompleteAsync(s, s.ItemB);

        // The finished sessions are not approved yet: the calculation stops instead of leaving their time out.
        var blocked = await s.W.Services.Costing.CalculateAsync(s.W.Warehouse, s.ItemB, Token);
        Assert.Equal(ValidationIssue.CostingSessionsNotApproved, blocked.Issue);

        foreach (var session in sessions)
        {
            var fresh = await s.ReloadAsync(session.Value.Id);
            Assert.True((await s.W.Services.WorkSessions.ApproveTimeAsync(s.W.Planning, fresh.Value.Id, fresh.Version, Token)).Succeeded);
        }

        var calculated = await s.W.Services.Costing.CalculateAsync(s.W.Warehouse, s.ItemB, Token);
        Assert.True(calculated.Succeeded, calculated.Status + " " + calculated.Issue);
        var calc = calculated.Value!.Calculation.Value;
        var machining = Assert.Single(calc.Lines, l => l.Kind == CostLineKind.Machining);
        Assert.Equal((180, 1_200_000m, 3_600_000m, new DateOnly(2026, 10, 24)), (machining.Minutes, machining.UnitPriceRial, machining.AmountRial, machining.WorkDate));
        Assert.Equal(sessions.Select(x => x.Value.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)).Order().ToArray(), machining.SessionIds!.Split(',').Order().ToArray());
        Assert.Null(machining.JobReportLineId);
        Assert.Equal(3_600_000m, calc.MachiningTotalRial);
        Assert.Equal(1, s.W.Count("ItemCostLines WHERE SessionIds IS NOT NULL"));
    }

    [Fact]
    public async Task A_correction_withdraws_the_approval_so_the_corrected_time_must_be_approved_before_costing()
    {
        using var s = await SessionScene.CreateAsync();
        var running = await s.StartedAsync(s.ItemB, s.M1, s.Op1);
        s.SetNow(11);
        var finished = (await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, running.Value.Id, running.Version, null, Token)).Value!;
        var approved = (await s.W.Services.WorkSessions.ApproveTimeAsync(s.W.Planning, finished.Value.Id, finished.Version, Token)).Value!;
        s.SetNow(12);
        var corrected = (await s.W.Services.WorkSessions.CorrectAsync(s.W.Planning, approved.Value.Id, approved.Version, SessionBoundary.Finish, 0, SessionScene.At(10, 30), "اصلاح", Token)).Value!;
        await RatesAsync(s);
        await CompleteAsync(s, s.ItemB);

        Assert.Equal(ValidationIssue.CostingSessionsNotApproved, (await s.W.Services.Costing.CalculateAsync(s.W.Warehouse, s.ItemB, Token)).Issue);
        Assert.True((await s.W.Services.WorkSessions.ApproveTimeAsync(s.W.Planning, corrected.Value.Id, corrected.Version, Token)).Succeeded);

        var calc = (await s.W.Services.Costing.CalculateAsync(s.W.Warehouse, s.ItemB, Token)).Value!.Calculation.Value;
        Assert.Equal(90, Assert.Single(calc.Lines, l => l.Kind == CostLineKind.Machining).Minutes);
    }

    [Fact]
    public async Task Stage_metrics_report_the_distinct_quantities_for_concurrent_sessions_pauses_and_planned_time()
    {
        using var s = await SessionScene.CreateAsync();
        var a = await s.StartedAsync(s.ItemA, s.M1, s.Op1);
        var b = await s.StartedAsync(s.ItemA, s.M2, s.Op2);
        s.SetNow(10);
        var paused = (await s.W.Services.WorkSessions.PauseAsync(s.Supervisor, a.Value.Id, a.Version, 3, null, Token)).Value!;
        s.SetNow(10, 30);
        var resumed = (await s.W.Services.WorkSessions.ResumeAsync(s.Supervisor, paused.Value.Id, paused.Version, null, Token)).Value!;
        s.SetNow(11);
        await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, resumed.Value.Id, resumed.Version, null, Token);
        await s.W.Services.WorkSessions.FinishAsync(s.Supervisor, b.Value.Id, b.Version, null, Token);

        var metrics = (await s.W.Services.WorkSessions.LoadStageMetricsAsync(s.Supervisor, s.ItemA, 1, false, Token)).Value!;

        Assert.Equal(90 + 120, metrics.MachineHours.Sum(r => r.Runtime.TotalMinutes));
        Assert.Equal(90 + 120, metrics.LaborHours.Sum(r => r.Time.TotalMinutes));
        Assert.Equal(120, Assert.Single(metrics.Elapsed).Elapsed.TotalMinutes);
        Assert.Equal(30, Assert.Single(metrics.PausedByReason).Time.TotalMinutes);
        var planned = Assert.Single(metrics.PlannedVsActual);
        Assert.Equal(20m, planned.PlannedMinutes);
        Assert.Equal(210, planned.MachineRuntime.TotalMinutes);
        Assert.Equal(OperationStatus.NotAuthorized, (await s.W.Services.WorkSessions.LoadStageMetricsAsync(s.W.Qc, s.ItemA, 1, false, Token)).Status);
    }

    [Fact]
    public async Task Approved_sessions_enter_the_unit_metrics_as_operator_labour_by_local_day_and_pending_ones_make_the_unit_non_authoritative()
    {
        using var s = await SessionScene.CreateAsync();
        var sessions = await HandoverRunAsync(s);
        var day = DateOnly.FromDateTime(SessionScene.Day);

        var before = (await s.W.Services.Kpi.ReportAsync(s.W.Planning, day, day, Token)).Value!;
        var unitBefore = before.Units.Single(u => u.UnitId == s.Milling);
        Assert.Equal((0, 2, false), (unitBefore.ProductiveMinutes, unitBefore.PendingReports, unitBefore.IsAuthoritative));

        foreach (var session in sessions)
        {
            var fresh = await s.ReloadAsync(session.Value.Id);
            Assert.True((await s.W.Services.WorkSessions.ApproveTimeAsync(s.W.Planning, fresh.Value.Id, fresh.Version, Token)).Succeeded);
        }

        var after = (await s.W.Services.Kpi.ReportAsync(s.W.Planning, day, day, Token)).Value!;
        var unit = after.Units.Single(u => u.UnitId == s.Milling);
        Assert.Equal((180, 0, true), (unit.ProductiveMinutes, unit.PendingReports, unit.IsAuthoritative));
        Assert.Equal([(s.Op2, 120), (s.Op1, 60)], after.Operators.Select(o => (o.OperatorId, o.ProductiveMinutes)).ToArray());
    }

    [Fact]
    public async Task The_session_cost_migration_only_adds_a_nullable_column_so_existing_cost_lines_keep_their_meaning()
    {
        using var db = new TempDatabase();
        await SqliteDatabase.MigrateAsync(db.Path, Token);

        var column = Assert.Single(db.Query("SELECT name || '|' || \"notnull\" FROM pragma_table_info('ItemCostLines') WHERE name = 'SessionIds'"));

        Assert.Equal("SessionIds|0", column);
        Assert.Empty(db.Query("SELECT * FROM ItemCostLines"));
    }
}
