using ERP.MES.Application.Persistence;
using ERP.MES.Application.Reporting;
using ERP.MES.Application.Requests;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The Warehouse completion screen against the real Application services (no window is opened).
public class CompletionViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CompletionViewModel For(WorkflowHarness h, ERP.MES.Application.Security.Principal user) =>
        new(h.Workflow.Completion, h.Dates, DigitStyle.Persian, () => user);

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional\n%%EOF\n");

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
        var unit = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Admin, "MILLING", "فرزکاری", Token)).Value!.Value.Id;
        var pair = (await h.Workflow.QcMaster.LoadAsync(h.Qc, Token)).Value!.QuantityTypes.First(t => t.Value.Code == "PIECE").Value.Id;
        var material = (await h.Workflow.Materials.LoadAsync(h.Warehouse, Token)).Value!.Materials.First().Value.Id;
        var version = (await h.Workflow.Requests.GetAsync(h.Qc, created.Value.Id, Token)).Value!.Request.Version;
        var defined = await h.Workflow.ItemDefinition.DefineItemAsync(h.Planning, created.Value.Id, version, new DefineItemCommand("A", "قطعه", 10m, Priority.Normal, pair, material, 1m, null, null), Token);
        var itemId = defined.Value!.Value.Items.Single().Id;
        var current = (await h.Workflow.Requests.GetAsync(h.Planning, created.Value.Id, Token)).Value!;
        Assert.True((await h.Workflow.ItemDefinition.DefineStageAsync(h.Planning, created.Value.Id, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand("فرز", 2m, unit, null), Token)).Succeeded);
        Assert.True((await h.Workflow.UnitDesignations.AssignAsync(h.Admin, h.Production.UserId, unit, Token)).Succeeded);
        foreach (var (user, action) in new[] { (h.Planning, StageAction.Release), (h.Production, StageAction.Start) })
        {
            var latest = (await h.Workflow.Requests.GetAsync(h.Planning, created.Value.Id, Token)).Value!;
            var target = new StageTarget(created.Value.Id, itemId, 1, latest.Request.Version, latest.Versions.ItemVersion(itemId), latest.Versions.Stages.Where(s => s.Key.ItemId == itemId).ToDictionary(s => s.Key.StageNo, s => s.Value));
            Assert.True((await h.Workflow.StageWork.PerformAsync(user, action, target, Token)).Succeeded);
        }

        return (created.Value.Id, itemId, unit);
    }

    [Fact]
    public async Task Warehouse_receives_is_blocked_from_finishing_without_a_price_then_finishes_and_dispatches_on_the_same_screen()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        h.Clock.UtcNow = new DateTime(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc);
        var (requestId, itemId, unit) = await RunningItemAsync(h);
        var template = (await h.Workflow.Calendar.AddTemplateAsync(h.Planning, "D", "روزکار", new TimeOnly(8, 0), new TimeOnly(17, 0), null, null, Token)).Value!.Value.Id;
        Assert.True((await h.Workflow.Calendar.AddUnitShiftAsync(h.Planning, unit, template, 3, 1, false, Token)).Succeeded);
        var machine = (await h.Workflow.PlanningMaster.AddMachineAsync(h.Admin, "FCC-06", "فرز", unit, Token)).Value!.Value.Id;
        Assert.True((await h.Workflow.Operators.RegisterAsync(h.Admin, "OP-1", "علی رضایی", h.Production.UserId, Token)).Succeeded);

        // One approved report line of 240 minutes; then the stage is submitted and QC accepts all 10 base units.
        var start = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);
        var draft = (await h.Workflow.JobReports.SaveDraftAsync(h.Production, null, null, new DateOnly(2026, 10, 24), unit, template, [new JobLineInput(requestId, itemId, 1, machine, start, start.AddHours(4), 0, null, 10, null)], Token)).Value!;
        var submitted = (await h.Workflow.JobReports.SubmitAsync(h.Production, draft.Value.Id, draft.Version, Token)).Value!;
        Assert.True((await h.Workflow.JobReports.ReviewAsync(h.Planning, submitted.Value.Id, submitted.Version, true, null, Token)).Succeeded);
        async Task<StageTarget> TargetAsync()
        {
            var latest = (await h.Workflow.Requests.GetAsync(h.Planning, requestId, Token)).Value!;
            return new StageTarget(requestId, itemId, 1, latest.Request.Version, latest.Versions.ItemVersion(itemId), latest.Versions.Stages.Where(s => s.Key.ItemId == itemId).ToDictionary(s => s.Key.StageNo, s => s.Value));
        }

        Assert.True((await h.Workflow.StageWork.PerformAsync(h.Production, StageAction.SubmitForQc, await TargetAsync(), Token)).Succeeded);
        Assert.True((await h.Workflow.QcInspection.RecordInspectionAsync(h.Qc, await TargetAsync(), [new InspectionEntry(InspectionDisposition.Accepted, 10, null)], Token)).Succeeded);

        Assert.True((await h.Workflow.PlanningMaster.AddRateAsync(h.Planning, machine, 1_200_000m, "1405/01/01", null, Token)).Succeeded);
        var material = (await h.Workflow.Materials.LoadAsync(h.Warehouse, Token)).Value!.Materials.First().Value.Id;
        Assert.True((await h.Workflow.Materials.AddPriceAsync(h.Warehouse, material, 90_000m, "1405/01/01", null, Token)).Succeeded);

        var warehouse = For(h, h.Warehouse);
        await warehouse.LoadAsync(Token);
        Assert.True(warehouse.CanReceive && warehouse.CanMarkFinished && warehouse.CanDispatch);
        var row = Assert.Single(warehouse.Rows);
        Assert.Equal(("دریافت نشده", "هزینه محاسبه نشده", "۱۰"), (row.ReceiptText, row.PriceText, row.QuantityText));

        // Finishing before the receipt, and dispatching before the receipt, are refused with a message.
        await warehouse.MarkFinishedAsync(row);
        Assert.Equal(Messages.ForIssue(ValidationIssue.ReceiptRequired, JalaliDateError.None), warehouse.Message);
        await warehouse.DispatchAsync(row, "1405/08/02", "WB-1", null);
        Assert.True(warehouse.IsErrorMessage);

        await warehouse.ReceiveAsync(warehouse.Rows.Single(), "تحویل از QC");
        Assert.False(warehouse.IsErrorMessage, warehouse.Message);
        Assert.StartsWith("دریافت‌شده", warehouse.Rows.Single().ReceiptText, StringComparison.Ordinal);
        await warehouse.ReceiveAsync(warehouse.Rows.Single(), null);
        Assert.Equal(Messages.ForIssue(ValidationIssue.ReceiptDuplicate, JalaliDateError.None), warehouse.Message);
        await warehouse.MarkFinishedAsync(warehouse.Rows.Single());
        Assert.Equal(Messages.ForIssue(ValidationIssue.FinishedSentNeedsPrice, JalaliDateError.None), warehouse.Message);

        // Cost, price submission and Management approval, then the mark.
        var calculated = (await h.Workflow.Costing.CalculateAsync(h.Warehouse, itemId, Token)).Value!;
        var priced = (await h.Workflow.Costing.SubmitAsync(h.Warehouse, calculated.Decision.Value.Id, calculated.Decision.Version, Token)).Value!;
        await warehouse.LoadAsync(Token);
        Assert.Equal("قیمت در انتظار مدیریت", warehouse.Rows.Single().PriceText);
        Assert.True((await h.Workflow.Costing.ApproveAsync(h.Manager, priced.Value.Id, priced.Version, null, null, null, Token)).Succeeded);
        await warehouse.LoadAsync(Token);
        Assert.Equal("قیمت تأییدشده: ۵٬۷۰۰٬۰۰۰ ریال", warehouse.Rows.Single().PriceText);
        await warehouse.MarkFinishedAsync(warehouse.Rows.Single());
        Assert.False(warehouse.IsErrorMessage, warehouse.Message);
        Assert.StartsWith("تکمیل/ارسال‌شده", warehouse.Rows.Single().FinishedText, StringComparison.Ordinal);

        await warehouse.DispatchAsync(warehouse.Rows.Single(), "1405/08/02", "WB-1001", null);
        Assert.False(warehouse.IsErrorMessage, warehouse.Message);
        Assert.Equal("۱۴۰۵/۰۸/۰۲ (WB-1001)", warehouse.Rows.Single().DispatchText);

        var planning = For(h, h.Planning);
        await planning.LoadAsync(Token);
        Assert.False(planning.CanReceive || planning.CanMarkFinished || planning.CanDispatch);
        Assert.Single(planning.Rows);
        var drawing = For(h, h.Drawing);
        await drawing.LoadAsync(Token);
        Assert.True(drawing.IsDenied);
        Assert.Empty(drawing.Rows);
    }
}
