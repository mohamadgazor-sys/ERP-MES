using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Desktop.Tests;

// The machine-downtime screen against the real Application services (no window is opened); Decision Log #128.
public class DowntimeViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // 1405/08/02 is Saturday 2026-10-24; the harness zone is the display zone.
    private static DowntimeViewModel For(WorkflowHarness h, Principal user) =>
        new(h.Workflow.Downtime, h.Dates, DigitStyle.Persian, () => new DateTime(2026, 10, 24, 6, 0, 0, DateTimeKind.Utc), () => user, h.Workflow.Users);

    private static DowntimeReasonOption Reason(DowntimeReason reason) => DowntimeViewModel.Reasons.Single(r => r.Reason == reason);

    [Fact]
    public async Task Maintenance_reports_planning_decides_and_only_the_permitted_actions_are_offered()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var unit = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Admin, "MILLING", "فرزکاری", Token)).Value!.Value.Id;
        Assert.True((await h.Workflow.PlanningMaster.AddMachineAsync(h.Admin, "FCC-06", "فرز", unit, Token)).Succeeded);

        var maintenance = For(h, h.Maintenance);
        await maintenance.LoadAsync(Token);
        Assert.True(maintenance.CanReport);
        Assert.False(maintenance.CanDecide);
        Assert.Equal("FCC-06", Assert.Single(maintenance.Machines).Code);
        Assert.Equal(9, DowntimeViewModel.Reasons.Count);
        Assert.Equal("قطع برق", DowntimeViewModel.ReasonText(DowntimeReason.PowerOutage));

        // A wrong time or a missing note for "other" is refused with the Application's message; nothing is saved.
        await maintenance.ReportAsync(maintenance.Machines.Single(), Reason(DowntimeReason.Breakdown), "۱۴۰۵/۰۸/۰۲", "x", null, "۱۱:۰۰", null);
        Assert.True(maintenance.IsErrorMessage);
        Assert.Equal(Messages.ForIssue(ValidationIssue.DowntimeInvalid, JalaliDateError.None), maintenance.Message);
        await maintenance.ReportAsync(maintenance.Machines.Single(), Reason(DowntimeReason.Other), "۱۴۰۵/۰۸/۰۲", "۰۹:۰۰", null, "۱۱:۰۰", " ");
        Assert.True(maintenance.IsErrorMessage);
        Assert.Empty(maintenance.Rows);

        // An empty end date means the start date; an end time before the start time means the next day.
        await maintenance.ReportAsync(maintenance.Machines.Single(), Reason(DowntimeReason.Breakdown), "۱۴۰۵/۰۸/۰۲", "۰۹:۰۰", null, "۱۱:۳۰", "یاتاقان");
        Assert.False(maintenance.IsErrorMessage, maintenance.Message);
        var row = Assert.Single(maintenance.Rows);
        Assert.Equal(("FCC-06", "فرزکاری", "خرابی", "۱۵۰ دقیقه", "یاتاقان"), (row.MachineText, row.UnitText, row.ReasonText, row.DurationText, row.NoteText));
        Assert.Equal("۱۴۰۵/۰۸/۰۲ ۰۹:۰۰", row.StartText);
        Assert.Contains("ثبت‌شده", row.StatusText, StringComparison.Ordinal);
        Assert.Contains("maintenance.one", row.ReporterText, StringComparison.Ordinal);
        await maintenance.ReportAsync(maintenance.Machines.Single(), Reason(DowntimeReason.PowerOutage), "۱۴۰۵/۰۸/۰۲", "۲۲:۰۰", null, "۰۲:۰۰", null);
        Assert.False(maintenance.IsErrorMessage, maintenance.Message);
        Assert.Equal("۲۴۰ دقیقه", maintenance.Rows.Single(r => r.ReasonText == "قطع برق").DurationText);
        Assert.Equal("۱۴۰۵/۰۸/۰۳ ۰۲:۰۰", maintenance.Rows.Single(r => r.ReasonText == "قطع برق").EndText);

        // An overlapping report on the same machine is refused.
        await maintenance.ReportAsync(maintenance.Machines.Single(), Reason(DowntimeReason.NoMaterial), "۱۴۰۵/۰۸/۰۲", "۱۰:۰۰", null, "۱۲:۰۰", null);
        Assert.Equal(Messages.ForIssue(ValidationIssue.DowntimeOverlap, JalaliDateError.None), maintenance.Message);

        // Planning decides; Management and the Production Manager only see the register; Production and QC do not.
        var planning = For(h, h.Planning);
        await planning.LoadAsync(Token);
        Assert.True(planning.CanDecide);
        Assert.False(planning.CanReport);
        await planning.RejectAsync(planning.Rows.Single(r => r.ReasonText == "خرابی"), " ");
        Assert.True(planning.IsErrorMessage);
        await planning.AcceptAsync(planning.Rows.Single(r => r.ReasonText == "خرابی"), null);
        Assert.False(planning.IsErrorMessage, planning.Message);
        Assert.Contains("تأییدشده — ", planning.Rows.Single(r => r.ReasonText == "خرابی").StatusText, StringComparison.Ordinal);
        Assert.Contains("planning.one", planning.Rows.Single(r => r.ReasonText == "خرابی").StatusText, StringComparison.Ordinal);

        foreach (var user in new[] { h.Manager, h.ProductionManager })
        {
            var reader = For(h, user);
            await reader.LoadAsync(Token);
            Assert.False(reader.CanReport || reader.CanDecide);
            Assert.Equal(2, reader.Rows.Count);
        }

        foreach (var user in new[] { h.Production, h.Qc })
        {
            var denied = For(h, user);
            await denied.LoadAsync(Token);
            Assert.True(denied.IsDenied);
            Assert.Empty(denied.Rows);
        }

        // Maintenance corrects the end (the accepted record returns to Planning) and cancels with a reason.
        await maintenance.LoadAsync(Token);
        await maintenance.ChangeEndAsync(maintenance.Rows.Single(r => r.ReasonText == "خرابی"), null, "۱۳:۰۰", "طول کشید");
        Assert.False(maintenance.IsErrorMessage, maintenance.Message);
        Assert.Equal("۲۴۰ دقیقه", maintenance.Rows.Single(r => r.ReasonText == "خرابی").DurationText);
        Assert.Contains("ثبت‌شده", maintenance.Rows.Single(r => r.ReasonText == "خرابی").StatusText, StringComparison.Ordinal);
        await maintenance.CancelAsync(maintenance.Rows.Single(r => r.ReasonText == "قطع برق"), "اشتباه");
        Assert.False(maintenance.IsErrorMessage, maintenance.Message);
        Assert.StartsWith("لغوشده", maintenance.Rows.Single(r => r.ReasonText == "قطع برق").StatusText, StringComparison.Ordinal);

        // The range is typed in Jalali; nothing in the next month.
        await maintenance.SetRangeAsync("۱۴۰۵/۰۹/۰۱", "۱۴۰۵/۰۹/۱۰");
        Assert.Empty(maintenance.Rows);
        await maintenance.SetRangeAsync("x", "۱۴۰۵/۰۹/۱۰");
        Assert.True(maintenance.IsErrorMessage);
    }
}
