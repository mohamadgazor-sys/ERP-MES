using ERP.MES.Application.Requests;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The daily job report screen against the real Application services (no window is opened).
public class JobReportsViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional\n%%EOF\n");

    private static JobReportsViewModel For(WorkflowHarness h, ERP.MES.Application.Security.Principal user, DateTime? now = null) =>
        new(h.Workflow.JobReports, h.Workflow.UnitQueue, h.Workflow.PlanningMaster, h.Workflow.Calendar, h.Dates, DigitStyle.Persian, () => now ?? new DateTime(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc), () => user);

    private static async Task<(long Id, long ItemId, long Unit)> RunningItemAsync(WorkflowHarness h)
    {
        var customer = (await h.Workflow.Customers.CreateAsync(h.Central, "Fictional Customer", Token)).Value!;
        var created = (await h.Workflow.Requests.CreateAsync(h.Central, new CreateRequestCommand(customer.Value.Id, "Q-1", WorkType.Make, "1405/07/20", null, null), Token)).Value!;
        var withPdf = (await h.Workflow.Documents.AttachRequestPdfAsync(h.Central, created.Value.Id, created.Version, "a.pdf", Pdf(), Token)).Value!;
        var sent = (await h.Workflow.Requests.SubmitToManagementAsync(h.Central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        var approved = (await h.Workflow.Lifecycle.ApproveAsync(h.Manager, sent.Value.Id, sent.Version, Token)).Value!;
        var forwarded = (await h.Workflow.Lifecycle.ForwardToDrawingAsync(h.Planning, approved.Value.Id, approved.Version, Token)).Value!;
        Assert.True((await h.Workflow.Lifecycle.RecordDrawingResultAsync(h.Drawing, forwarded.Value.Id, forwarded.Version, DrawingOutcome.FullyAccepted, null, Token)).Succeeded);
        Assert.True((await h.Workflow.Contracts.DecideRequirementAsync(h.Planning, created.Value.Id, ContractRequirement.NotRequired, null, Token)).Succeeded);
        Assert.True((await h.Workflow.Materials.AddMaterialAsync(h.Warehouse, "ST37-R30", "میلگرد ST37", "ST37", "میلگرد", Token)).Succeeded);
        var unit = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Planning, "MILLING", "فرزکاری", Token)).Value!.Value.Id;
        var pair = (await h.Workflow.QcMaster.LoadAsync(h.Qc, Token)).Value!.QuantityTypes.First(t => t.Value.Code == "PIECE").Value.Id;
        var material = (await h.Workflow.Materials.LoadAsync(h.Warehouse, Token)).Value!.Materials.First().Value.Id;
        var version = (await h.Workflow.Requests.GetAsync(h.Qc, created.Value.Id, Token)).Value!.Request.Version;
        var defined = await h.Workflow.ItemDefinition.DefineItemAsync(h.Planning, created.Value.Id, version, new DefineItemCommand("A", "قطعه", 10m, Priority.Normal, pair, material, 1m, null, null), Token);
        var itemId = defined.Value!.Value.Items.Single().Id;
        var current = (await h.Workflow.Requests.GetAsync(h.Planning, created.Value.Id, Token)).Value!;
        Assert.True((await h.Workflow.ItemDefinition.DefineStageAsync(h.Planning, created.Value.Id, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand("فرز", 2m, unit, null), Token)).Succeeded);
        Assert.True((await h.Workflow.UnitDesignations.AssignAsync(h.Planning, h.Production.UserId, unit, Token)).Succeeded);
        foreach (var (user, action) in new[] { (h.Planning, StageAction.Release), (h.Production, StageAction.Start) })
        {
            var latest = (await h.Workflow.Requests.GetAsync(h.Planning, created.Value.Id, Token)).Value!;
            var target = new StageTarget(created.Value.Id, itemId, 1, latest.Request.Version, latest.Versions.ItemVersion(itemId), latest.Versions.Stages.Where(s => s.Key.ItemId == itemId).ToDictionary(s => s.Key.StageNo, s => s.Value));
            Assert.True((await h.Workflow.StageWork.PerformAsync(user, action, target, Token)).Succeeded);
        }

        return (created.Value.Id, itemId, unit);
    }

    [Fact]
    public async Task An_operator_builds_and_submits_a_report_and_the_supervisor_approves_it_on_the_same_screen()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        h.Clock.UtcNow = new DateTime(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc);
        var (_, _, unit) = await RunningItemAsync(h);
        var template = (await h.Workflow.Calendar.AddTemplateAsync(h.Planning, "D", "روزکار", new TimeOnly(8, 0), new TimeOnly(17, 0), null, null, Token)).Value!.Value.Id;
        Assert.True((await h.Workflow.Calendar.AddUnitShiftAsync(h.Planning, unit, template, 3, 1, false, Token)).Succeeded);
        var machine = (await h.Workflow.PlanningMaster.AddMachineAsync(h.Planning, "FCC-06", "فرز", unit, Token)).Value!.Value.Id;
        Assert.True((await h.Workflow.Operators.RegisterAsync(h.Planning, "OP-1", "علی رضایی", h.Production.UserId, Token)).Succeeded);

        var operatorVm = For(h, h.Production);
        await operatorVm.LoadAsync(Token);
        Assert.True(operatorVm.CanSubmit);
        await operatorVm.SelectUnitAsync(unit);
        var stage = Assert.Single(operatorVm.Stages);
        var machineChoice = operatorVm.Machines.Single(m => m.Value.Id == machine);
        var shiftChoice = operatorVm.Templates.Single();

        // 1405/08/02 is Saturday 2026-10-24.
        operatorVm.AddLine(stage, machineChoice, "۱۴۰۵/۰۸/۰۲", "۰۸:۰۰", "۱۲:۰۰", "۳۰", "خرابی ابزار", "۶", null);
        Assert.False(operatorVm.IsErrorMessage, operatorVm.Message);
        Assert.Equal("۰۸:۰۰ تا ۱۲:۰۰", Assert.Single(operatorVm.Draft).TimeText);
        operatorVm.AddLine(stage, machineChoice, "۱۴۰۵/۰۸/۰۲", "۱۱:۰۰", "x", "0", null, "1", null);
        Assert.True(operatorVm.IsErrorMessage);
        Assert.Equal(Messages.ForIssue(ValidationIssue.JobReportLineInvalid, JalaliDateError.None), operatorVm.Message);

        await operatorVm.SaveDraftAsync("1405/08/02", shiftChoice);
        Assert.False(operatorVm.IsErrorMessage, operatorVm.Message);
        var draftRow = Assert.Single(operatorVm.Rows);
        Assert.Equal("پیش‌نویس", draftRow.StatusText);
        Assert.Equal("۱ خط، ۲۱۰ دقیقهٔ مفید", draftRow.LinesText);
        await operatorVm.SubmitAsync();
        Assert.False(operatorVm.IsErrorMessage, operatorVm.Message);
        Assert.Equal("ارسال‌شده", operatorVm.Rows.Single().StatusText);
        Assert.Equal("۱ خط علامت‌دار", operatorVm.Rows.Single().FlagsText); // the operator was not planned that day

        // Planning reviews as the fallback because the unit has no supervisor; the screen says so.
        var planning = For(h, h.Planning);
        await planning.LoadAsync(Token);
        Assert.True(planning.CanReview);
        var row = planning.Rows.Single();
        Assert.Contains("جایگزین سرپرست", row.ReviewText, StringComparison.Ordinal);
        await planning.ReturnAsync(row, " ");
        Assert.True(planning.IsErrorMessage);
        Assert.Equal(Messages.ForIssue(ValidationIssue.JobReportReasonRequired, JalaliDateError.None), planning.Message);
        await planning.ApproveAsync(row);
        Assert.False(planning.IsErrorMessage, planning.Message);
        Assert.Contains("تأییدشده", planning.Rows.Single().StatusText, StringComparison.Ordinal);
        Assert.Contains("برنامه‌ریزی", planning.Rows.Single().StatusText, StringComparison.Ordinal);

        // Other departments are denied.
        var drawing = For(h, h.Drawing);
        await drawing.LoadAsync(Token);
        Assert.True(drawing.IsDenied);
        Assert.Empty(drawing.Rows);
        Assert.False(drawing.CanSubmit || drawing.CanReview);
    }
}
