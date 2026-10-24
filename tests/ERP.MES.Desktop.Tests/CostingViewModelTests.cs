using ERP.MES.Application.Persistence;
using ERP.MES.Application.Reporting;
using ERP.MES.Application.Requests;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The cost and price screen against the real Application services (no window is opened).
public class CostingViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CostingViewModel For(WorkflowHarness h, ERP.MES.Application.Security.Principal user) =>
        new(h.Workflow.Costing, h.Workflow.PlanningMaster, h.Dates, DigitStyle.Persian, () => user);

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
    public async Task Warehouse_calculates_names_the_gaps_submits_and_management_approves_on_the_same_screen()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        h.Clock.UtcNow = new DateTime(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc);
        var (requestId, itemId, unit) = await RunningItemAsync(h);
        var template = (await h.Workflow.Calendar.AddTemplateAsync(h.Planning, "D", "روزکار", new TimeOnly(8, 0), new TimeOnly(17, 0), null, null, Token)).Value!.Value.Id;
        Assert.True((await h.Workflow.Calendar.AddUnitShiftAsync(h.Planning, unit, template, 3, 1, false, Token)).Succeeded);
        var machine = (await h.Workflow.PlanningMaster.AddMachineAsync(h.Planning, "FCC-06", "فرز", unit, Token)).Value!.Value.Id;
        Assert.True((await h.Workflow.Operators.RegisterAsync(h.Planning, "OP-1", "علی رضایی", h.Production.UserId, Token)).Succeeded);

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

        var warehouse = For(h, h.Warehouse);
        await warehouse.LoadAsync(Token);
        Assert.True(warehouse.CanCalculate && !warehouse.CanApprove);
        var row = Assert.Single(warehouse.Rows);
        Assert.Equal("محاسبه نشده", row.StatusText);
        Assert.Equal("۱۰", row.QuantityText);

        // No rate and no price: both gaps are named in Persian with Jalali dates.
        await warehouse.CalculateAsync(row);
        Assert.True(warehouse.IsErrorMessage);
        Assert.Contains("نرخ ماشین FCC-06 در تاریخ ۱۴۰۵/۰۸/۰۲", warehouse.Message, StringComparison.Ordinal);
        Assert.Contains("قیمت ماده ST37-R30 در تاریخ ۱۴۰۵/۰۸/۰۲", warehouse.Message, StringComparison.Ordinal);

        Assert.True((await h.Workflow.PlanningMaster.AddRateAsync(h.Planning, machine, 1_200_000m, "1405/01/01", null, Token)).Succeeded);
        var material = (await h.Workflow.Materials.LoadAsync(h.Warehouse, Token)).Value!.Materials.First().Value.Id;
        Assert.True((await h.Workflow.Materials.AddPriceAsync(h.Warehouse, material, 90_000m, "1405/01/01", null, Token)).Succeeded);

        await warehouse.CalculateAsync(warehouse.Rows.Single());
        Assert.False(warehouse.IsErrorMessage, warehouse.Message);
        row = warehouse.Rows.Single();
        Assert.Equal("محاسبه‌شده (ارسال نشده)", row.StatusText);
        Assert.Equal("۵٬۷۰۰٬۰۰۰ ریال (هر واحد ۵۷۰٬۰۰۰)", row.CostText);
        Assert.Equal("پیشنهاد ۵٬۷۰۰٬۰۰۰ ریال", row.PriceText);
        await warehouse.ShowDetailAsync(row);
        Assert.Equal(3, warehouse.Detail.Count);
        Assert.Contains("ماشین FCC-06", warehouse.Detail[0], StringComparison.Ordinal);
        await warehouse.SubmitAsync(row);
        Assert.Equal("ارسال‌شده به مدیریت", warehouse.Rows.Single().StatusText);

        var management = For(h, h.Manager);
        await management.LoadAsync(Token);
        Assert.True(management.CanApprove && !management.CanCalculate);
        await management.ApproveAsync(management.Rows.Single(), "۶٬۰۰۰٬۰۰۰", null, null);
        Assert.True(management.IsErrorMessage); // a changed price needs a reason
        Assert.Equal(Messages.ForIssue(ValidationIssue.ItemPriceInvalid, JalaliDateError.None), management.Message);
        await management.ApproveAsync(management.Rows.Single(), "۶٬۰۰۰٬۰۰۰", "حاشیه سود", null);
        Assert.False(management.IsErrorMessage, management.Message);
        Assert.Equal("قیمت تأییدشده", management.Rows.Single().StatusText);
        Assert.Equal("۶٬۰۰۰٬۰۰۰ ریال", management.Rows.Single().PriceText);

        var planning = For(h, h.Planning);
        await planning.LoadAsync(Token);
        Assert.False(planning.CanCalculate || planning.CanApprove);
        Assert.Single(planning.Rows);
        var drawing = For(h, h.Drawing);
        await drawing.LoadAsync(Token);
        Assert.True(drawing.IsDenied);
        Assert.Empty(drawing.Rows);
    }
}
