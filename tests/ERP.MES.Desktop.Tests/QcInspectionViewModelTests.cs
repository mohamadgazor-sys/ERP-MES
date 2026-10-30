using ERP.MES.Application.Requests;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The QC inspection screen against the real Application services (no window is opened).
public class QcInspectionViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional\n%%EOF\n");

    private static QcInspectionViewModel For(WorkflowHarness h, ERP.MES.Application.Security.Principal user) => new(h.Workflow.QcInspection, h.Dates, DigitStyle.Persian, () => user);

    private static async Task<(long Id, long ItemId)> SubmittedPairItemAsync(WorkflowHarness h)
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
        var unit = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Admin, "MILLING", "فرزکاری", Token)).Value!.Value.Id;
        var pair = (await h.Workflow.QcMaster.LoadAsync(h.Qc, Token)).Value!.QuantityTypes.First(t => t.Value.Code == "PAIR").Value.Id;
        var material = (await h.Workflow.Materials.LoadAsync(h.Warehouse, Token)).Value!.Materials.First().Value.Id;
        var version = (await h.Workflow.Requests.GetAsync(h.Qc, created.Value.Id, Token)).Value!.Request.Version;
        var defined = await h.Workflow.ItemDefinition.DefineItemAsync(h.Planning, created.Value.Id, version, new DefineItemCommand("A", "قطعه", 10m, Priority.Normal, pair, material, 1m, null, null), Token);
        var itemId = defined.Value!.Value.Items.Single().Id;
        var current = (await h.Workflow.Requests.GetAsync(h.Planning, created.Value.Id, Token)).Value!;
        Assert.True((await h.Workflow.ItemDefinition.DefineStageAsync(h.Planning, created.Value.Id, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand("فرز", 2m, unit, null), Token)).Succeeded);
        Assert.True((await h.Workflow.UnitDesignations.AssignAsync(h.Admin, h.Production.UserId, unit, Token)).Succeeded);
        foreach (var (user, action) in new[] { (h.Planning, StageAction.Release), (h.Production, StageAction.Start), (h.Production, StageAction.SubmitForQc) })
        {
            var latest = (await h.Workflow.Requests.GetAsync(h.Planning, created.Value.Id, Token)).Value!;
            var target = new StageTarget(created.Value.Id, itemId, 1, latest.Request.Version, latest.Versions.ItemVersion(itemId), latest.Versions.Stages.Where(s => s.Key.ItemId == itemId).ToDictionary(s => s.Key.StageNo, s => s.Value));
            Assert.True((await h.Workflow.StageWork.PerformAsync(user, action, target, Token)).Succeeded);
        }

        return (created.Value.Id, itemId);
    }

    [Fact]
    public async Task Qc_decides_base_units_the_screen_shows_persian_digits_the_shortfall_decision_and_the_history()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await SubmittedPairItemAsync(h);
        var qc = For(h, h.Qc);
        await qc.LoadAsync(Token);

        Assert.True(qc.CanInspect);
        var row = Assert.Single(qc.Rows);
        Assert.Equal("۲۰", row.AwaitingText); // 10 pairs = 20 base units
        Assert.Equal("هر واحد = ۲ واحد پایه", row.UnitsText);
        await qc.SelectAsync(row);

        await qc.InspectAsync("۱۷", "0", "۳", " ");
        Assert.True(qc.IsErrorMessage);
        Assert.Equal(Messages.ForIssue(ValidationIssue.InspectionEntriesInvalid, JalaliDateError.None), qc.Message);
        await qc.InspectAsync("25", null, null, null);
        Assert.Equal(Messages.ForIssue(ValidationIssue.InspectionExceedsAwaiting, JalaliDateError.None), qc.Message);

        await qc.SelectAsync(qc.Rows.Single());
        await qc.InspectAsync("۱۷", null, "۳", "ترک");
        Assert.False(qc.IsErrorMessage, qc.Message);
        var waiting = Assert.Single(qc.Rows);
        Assert.Equal("در انتظار تصمیم دربارهٔ کسری", waiting.StatusText);
        Assert.Equal(("۱۷", "۳"), (waiting.AcceptedText, waiting.ScrapText));

        // QC cannot decide the shortfall; Management can.
        await qc.SelectAsync(waiting);
        await qc.DecideShortfallAsync(ShortfallKind.ManagementAccepted, "x");
        Assert.True(qc.IsErrorMessage);
        var manager = For(h, h.Manager);
        await manager.LoadAsync(Token);
        Assert.True(manager.CanAcceptShortfall);
        Assert.False(manager.CanInspect);
        await manager.SelectAsync(manager.Rows.Single());
        await manager.DecideShortfallAsync(ShortfallKind.ManagementAccepted, "پذیرش کسری");
        Assert.False(manager.IsErrorMessage, manager.Message);
        Assert.Empty(manager.Rows);

        await qc.LoadAsync(Token);
        Assert.Empty(qc.Rows);
    }

    [Fact]
    public async Task Other_departments_are_denied_the_qc_inspection_screen()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        foreach (var user in new[] { h.Drawing, h.Central, h.Warehouse, h.Production })
        {
            var vm = For(h, user);
            await vm.LoadAsync(Token);
            Assert.True(vm.IsDenied);
            Assert.Empty(vm.Rows);
            Assert.False(vm.CanInspect);
        }
    }
}
