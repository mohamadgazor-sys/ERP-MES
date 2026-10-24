using ERP.MES.Application.Requests;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;

namespace ERP.MES.Desktop.Tests;

// The unit metrics screen against the real Application services (no window is opened).
public class KpiViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static KpiViewModel For(WorkflowHarness h, ERP.MES.Application.Security.Principal user) =>
        new(h.Workflow.Kpi, h.Dates, DigitStyle.Persian, () => new DateTime(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc), () => user);

    [Fact]
    public async Task Planning_and_management_see_every_unit_labelled_without_a_score_a_supervisor_only_their_own_and_others_are_denied()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var milling = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Planning, "MILLING", "فرزکاری", Token)).Value!.Value.Id;
        var turning = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Planning, "TURNING", "تراشکاری", Token)).Value!.Value.Id;
        Assert.True((await h.Workflow.UnitDesignations.AssignAsync(h.Planning, h.Production.UserId, milling, Token)).Succeeded);
        var row = (await h.Workflow.UnitDesignations.LoadAsync(h.Planning, Token)).Value!.Rows.Single();
        Assert.True((await h.Workflow.UnitDesignations.SetSupervisorAsync(h.Planning, row.DesignationId, row.Version, true, Token)).Succeeded);

        var planning = For(h, h.Planning);
        await planning.LoadAsync(Token);
        Assert.False(planning.IsDenied);
        Assert.Equal(2, planning.Units.Count);
        var first = planning.Units[0];
        Assert.Equal("فرزکاری (MILLING)", first.UnitText);
        Assert.Equal("۰ ساعت", first.HoursText);
        Assert.Equal("پذیرش — / بازکاری — / ضایعات —", first.RatesText);
        Assert.Equal("مرحلهٔ تکمیل‌شده‌ای نیست", first.PlanText);
        Assert.Equal("شیفت برنامه‌ریزی‌شده‌ای نیست", first.UtilizationText);
        Assert.Equal("قطعی", first.StatusText);
        Assert.Contains("۱۴۰۵/۰۷/۰۳ تا ۱۴۰۵/۰۸/۰۲", planning.RangeText, StringComparison.Ordinal);
        Assert.Contains("اجرا:", planning.RunText, StringComparison.Ordinal);

        await planning.SetRangeAsync("x", "۱۴۰۵/۰۸/۰۲");
        Assert.True(planning.IsErrorMessage);
        await planning.SetRangeAsync("۱۴۰۵/۰۸/۰۲", "۱۴۰۵/۰۸/۰۱");
        Assert.True(planning.IsErrorMessage);

        var supervisor = For(h, h.Production);
        await supervisor.LoadAsync(Token);
        Assert.Equal("فرزکاری (MILLING)", Assert.Single(supervisor.Units).UnitText);
        Assert.NotEqual(turning, milling);

        foreach (var user in new[] { h.Drawing, h.Qc, h.Warehouse, h.Central })
        {
            var denied = For(h, user);
            await denied.LoadAsync(Token);
            Assert.True(denied.IsDenied);
            Assert.Empty(denied.Units);
        }
    }
}
