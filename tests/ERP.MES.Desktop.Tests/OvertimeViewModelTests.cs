using ERP.MES.Application.Requests;
using ERP.MES.Domain.Calendar;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Tests;

// The overtime screen against the real Application services (no window is opened).
public class OvertimeViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static OvertimeViewModel For(WorkflowHarness h, ERP.MES.Application.Security.Principal user) =>
        new(h.Workflow.Overtime, h.Workflow.Operators, h.Workflow.PlanningMaster, h.Dates, DigitStyle.Persian, () => new DateTime(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc), () => user);

    [Fact]
    public async Task A_supervisor_proposes_planning_needs_the_limits_and_management_decides_what_is_outside_them()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var unit = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Planning, "MILLING", "فرزکاری", Token)).Value!.Value.Id;
        Assert.True((await h.Workflow.PlanningMaster.AddMachineAsync(h.Planning, "FCC-06", "فرز", unit, Token)).Succeeded);
        Assert.True((await h.Workflow.Operators.RegisterAsync(h.Planning, "OP-1", "علی رضایی", null, Token)).Succeeded);
        Assert.True((await h.Workflow.UnitDesignations.AssignAsync(h.Planning, h.Production.UserId, unit, Token)).Succeeded);
        var designation = (await h.Workflow.UnitDesignations.LoadAsync(h.Planning, Token)).Value!.Rows.Single();
        Assert.True((await h.Workflow.UnitDesignations.SetSupervisorAsync(h.Planning, designation.DesignationId, designation.Version, true, Token)).Succeeded);

        var supervisor = For(h, h.Production);
        await supervisor.LoadAsync(Token);
        Assert.True(supervisor.CanPropose);
        Assert.False(supervisor.CanPlanningDecide || supervisor.CanManagementDecide || supervisor.CanConfigureLimits);
        Assert.Equal("MILLING", Assert.Single(supervisor.Units).Value.Code);

        // 1405/08/02 is Saturday 2026-10-24.
        await supervisor.ProposeAsync(supervisor.Operators.Single(), supervisor.Units.Single(), supervisor.Machines.Single(), "۱۴۰۵/۰۸/۰۲", "x", "۱۹:۰۰", "سفارش فوری");
        Assert.True(supervisor.IsErrorMessage);
        Assert.Equal(Messages.ForIssue(ValidationIssue.OvertimeInvalid, ERP.MES.Domain.Calendar.JalaliDateError.None), supervisor.Message);
        await supervisor.ProposeAsync(supervisor.Operators.Single(), supervisor.Units.Single(), supervisor.Machines.Single(), "۱۴۰۵/۰۸/۰۲", "۱۷:۳۰", "۱۹:۰۰", "سفارش فوری");
        Assert.False(supervisor.IsErrorMessage, supervisor.Message);
        var proposed = Assert.Single(supervisor.Rows);
        Assert.Equal("۱۷:۳۰ تا ۱۹:۰۰ (۹۰ دقیقه)", proposed.TimeText);
        Assert.Contains("پیشنهادشده", proposed.StatusText, StringComparison.Ordinal);

        var planning = For(h, h.Planning);
        await planning.LoadAsync(Token);
        Assert.True(planning.CanPlanningDecide && planning.CanConfigureLimits && !planning.CanManagementDecide);
        Assert.Contains("تعریف نشده", planning.LimitsText, StringComparison.Ordinal);
        await planning.PlanningApproveAsync(planning.Rows.Single(), null);
        Assert.True(planning.IsErrorMessage);
        Assert.Equal(Messages.ForIssue(ValidationIssue.OvertimeLimitsNotConfigured, ERP.MES.Domain.Calendar.JalaliDateError.None), planning.Message);

        await planning.SaveLimitsAsync("0", "10");
        Assert.True(planning.IsErrorMessage);
        await planning.SaveLimitsAsync("۶۰", "۴۸۰");
        Assert.False(planning.IsErrorMessage, planning.Message);
        Assert.Contains("۶۰", planning.LimitsText, StringComparison.Ordinal);

        await planning.PlanningApproveAsync(planning.Rows.Single(), null);
        Assert.False(planning.IsErrorMessage, planning.Message);
        Assert.Equal("خارج از سقف؛ در انتظار مدیریت", planning.Rows.Single().StatusText);

        var management = For(h, h.Manager);
        await management.LoadAsync(Token);
        Assert.True(management.CanManagementDecide);
        await management.ManagementRejectAsync(management.Rows.Single(), " ");
        Assert.True(management.IsErrorMessage);
        await management.ManagementApproveAsync(management.Rows.Single(), null);
        Assert.False(management.IsErrorMessage, management.Message);
        Assert.Equal("تأییدشده (خارج از سقف، با تأیید مدیریت)", management.Rows.Single().StatusText);

        var drawing = For(h, h.Drawing);
        await drawing.LoadAsync(Token);
        Assert.True(drawing.IsDenied);
        Assert.Empty(drawing.Rows);
    }
}
