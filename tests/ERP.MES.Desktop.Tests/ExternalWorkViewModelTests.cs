using ERP.MES.Application.Requests;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The external-work screen, the unit category and the lead time on the definition screen against the real Application services (no window is opened); Decision Log #129.
public class ExternalWorkViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional\n%%EOF\n");

    private static ExternalWorkViewModel For(WorkflowHarness h, ERP.MES.Application.Security.Principal user) => new(h.Workflow.ExternalWork, h.Dates, DigitStyle.Persian, () => user, h.Workflow.Users);

    /// <summary>A released item of 10 pieces whose first stage is on an external plating unit (lead time 7 calendar days).</summary>
    private static async Task<long> ReleasedExternalAsync(WorkflowHarness h)
    {
        var customer = (await h.Workflow.Customers.CreateAsync(h.Central, "Fictional Customer", Token)).Value!;
        var created = (await h.Workflow.Requests.CreateAsync(h.Central, new CreateRequestCommand(customer.Value.Id, "X-1", WorkType.Make, "1405/07/20", null, null), Token)).Value!;
        var withPdf = (await h.Workflow.Documents.AttachRequestPdfAsync(h.Central, created.Value.Id, created.Version, "a.pdf", Pdf(), Token)).Value!;
        var sent = (await h.Workflow.Requests.SubmitToManagementAsync(h.Central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        var approved = (await h.Workflow.Lifecycle.ApproveAsync(h.Manager, sent.Value.Id, sent.Version, Token)).Value!;
        var forwarded = (await h.Workflow.Lifecycle.ForwardToDrawingAsync(h.Planning, approved.Value.Id, approved.Version, Token)).Value!;
        Assert.True((await h.Workflow.Lifecycle.RecordDrawingResultAsync(h.Drawing, forwarded.Value.Id, forwarded.Version, DrawingOutcome.FullyAccepted, null, Token)).Succeeded);
        Assert.True((await h.Workflow.Contracts.DecideRequirementAsync(h.Planning, created.Value.Id, ContractRequirement.NotRequired, null, Token)).Succeeded);
        Assert.True((await h.Workflow.Materials.AddMaterialAsync(h.Warehouse, "ST37-R30", "میلگرد ST37", "ST37", "میلگرد", Token)).Succeeded);

        // Program Admin registers the external route on the units page with its category.
        var master = new PlanningMasterViewModel(h.Workflow.PlanningMaster, h.Dates, DigitStyle.Persian, () => h.Admin);
        await master.LoadAsync(Token);
        Assert.Equal(9, PlanningMasterViewModel.CategoryChoices.Count);
        Assert.True(await master.AddUnitAsync("PLATING-CO", "آبکاری بیرونی", Token, ProductionUnitCategory.ExternalPlating));
        Assert.Equal("بیرونی — آبکاری (EXTERNAL_PLATING)", master.Units.Single().CategoryText);
        Assert.Empty(master.UnitChoices); // no machine can be registered on an external route

        var piece = (await h.Workflow.QcMaster.LoadAsync(h.Qc, Token)).Value!.QuantityTypes.First(t => t.Value.Code == "PIECE").Value.Id;
        var material = (await h.Workflow.Materials.LoadAsync(h.Warehouse, Token)).Value!.Materials.First().Value.Id;
        var version = (await h.Workflow.Requests.GetAsync(h.Qc, created.Value.Id, Token)).Value!.Request.Version;
        var itemId = (await h.Workflow.ItemDefinition.DefineItemAsync(h.Planning, created.Value.Id, version, new DefineItemCommand("A", "قطعه", 10m, Priority.Normal, piece, material, 1m, null, null), Token)).Value!.Value.Items.Single().Id;

        // Planning types the lead time on the definition screen; a missing one is refused for an external unit.
        var definition = new ItemDefinitionViewModel(h.Workflow.ItemDefinition, h.Workflow.Requests, h.Workflow.Gates, h.Workflow.QcMaster, h.Workflow.Materials, h.Workflow.PlanningMaster, DigitStyle.Persian, () => h.Planning);
        await definition.LoadAsync(Token);
        await definition.SelectRequestAsync(created.Value.Id, Token);
        definition.SelectedItem = definition.Items.Single();
        var unitId = master.Units.Single().Id;
        Assert.False(await definition.DefineStageAsync("فرز", "0", unitId, null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.ExternalStageInvalid, JalaliDateError.None), definition.Message);
        Assert.False(await definition.DefineStageAsync("فرز", "0", unitId, null, Token, "۴۰۰"));
        Assert.True(await definition.DefineStageAsync("فرز", "0", unitId, null, Token, "۷"));
        Assert.Contains("زمان تحویل ۷ روز تقویمی", definition.Stages.Single().MachineText, StringComparison.Ordinal);

        var latest = (await h.Workflow.Requests.GetAsync(h.Planning, created.Value.Id, Token)).Value!;
        var target = new StageTarget(created.Value.Id, itemId, 1, latest.Request.Version, latest.Versions.ItemVersion(itemId), latest.Versions.Stages.Where(s => s.Key.ItemId == itemId).ToDictionary(s => s.Key.StageNo, s => s.Value));
        Assert.True((await h.Workflow.StageWork.PerformAsync(h.Planning, StageAction.Release, target, Token)).Succeeded);
        return itemId;
    }

    [Fact]
    public async Task The_warehouse_dispatches_and_returns_in_parts_and_corrects_a_mistake_while_others_only_read()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await ReleasedExternalAsync(h);

        var warehouse = For(h, h.Warehouse);
        await warehouse.LoadAsync(Token);
        Assert.True(warehouse.CanRecord);
        var row = Assert.Single(warehouse.Rows);
        Assert.Equal("در انتظار ارسال توسط انبار", row.StatusText);
        Assert.Contains("مانده برای ارسال ۱۰", row.QuantitiesText, StringComparison.Ordinal);

        await warehouse.DispatchAsync("۱۲", null, null);
        Assert.Equal(Messages.ForIssue(ValidationIssue.ExternalQuantityInvalid, JalaliDateError.None), warehouse.Message);
        await warehouse.DispatchAsync("۶", "۶ قطعه", null);
        Assert.False(warehouse.IsErrorMessage, warehouse.Message);
        await warehouse.DispatchAsync("۴", null, "اشتباه");
        Assert.Equal("ارسال‌شده، در انتظار برگشت", warehouse.Rows.Single().StatusText);
        Assert.Equal(2, warehouse.OpenDispatches.Count);
        Assert.Contains("برگشت مورد انتظار", warehouse.Rows.Single().OpenText, StringComparison.Ordinal);

        // The second dispatch was a mistake: cancelled with a reason, kept in the history.
        var mistaken = warehouse.History.Single(r => r.NoteText == "اشتباه");
        await warehouse.CancelAsync(mistaken, "ارسال نشده بود");
        Assert.False(warehouse.IsErrorMessage, warehouse.Message);
        Assert.StartsWith("لغوشده", warehouse.History.Single(r => r.Shipment.Id == mistaken.Shipment.Id).StateText, StringComparison.Ordinal);
        Assert.Contains("ارسال نشده بود", warehouse.History.Single(r => r.Shipment.Id == mistaken.Shipment.Id).StateText, StringComparison.Ordinal);

        // A return over the outstanding quantity is refused; a partial return goes to QC.
        await warehouse.ReturnAsync(warehouse.OpenDispatches.Single(), "۷", null);
        Assert.Equal(Messages.ForIssue(ValidationIssue.ExternalQuantityInvalid, JalaliDateError.None), warehouse.Message);
        await warehouse.ReturnAsync(warehouse.OpenDispatches.Single(), "۴", "برگشت جزئی");
        Assert.False(warehouse.IsErrorMessage, warehouse.Message);
        Assert.Equal("برگشته، در انتظار کنترل کیفیت", warehouse.Rows.Single().StatusText);
        Assert.Contains("نزد پیمانکار ۲", warehouse.Rows.Single().QuantitiesText, StringComparison.Ordinal);
        Assert.Contains("warehouse.one", warehouse.History.Last().UserText, StringComparison.Ordinal);

        foreach (var reader in new[] { h.Planning, h.Qc, h.ProductionManager })
        {
            var view = For(h, reader);
            await view.LoadAsync(Token);
            Assert.False(view.CanRecord);
            Assert.Single(view.Rows);
        }

        var denied = For(h, h.Production);
        await denied.LoadAsync(Token);
        Assert.True(denied.IsDenied);
    }
}
