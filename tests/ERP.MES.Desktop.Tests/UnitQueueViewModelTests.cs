using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The unit-queue screen against the real Application services (no window is opened).
public class UnitQueueViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional\n%%EOF\n");

    private static UnitQueueViewModel For(WorkflowHarness h, Principal user) =>
        new(h.Workflow.UnitQueue, h.Workflow.StageWork, h.Workflow.UnitDesignations, DigitStyle.Persian, () => user);

    /// <summary>A request that passed Management and Drawing; for MAKE also the contract decision (not required).</summary>
    private static async Task<long> ReadyAsync(WorkflowHarness h, WorkType type, string requestNo)
    {
        var customer = (await h.Workflow.Customers.CreateAsync(h.Central, "Fictional Customer " + requestNo, Token)).Value!;
        var created = (await h.Workflow.Requests.CreateAsync(h.Central, new CreateRequestCommand(customer.Value.Id, requestNo, type, "1405/07/20", null, null), Token)).Value!;
        var withPdf = (await h.Workflow.Documents.AttachRequestPdfAsync(h.Central, created.Value.Id, created.Version, "a.pdf", Pdf(), Token)).Value!;
        var sent = (await h.Workflow.Requests.SubmitToManagementAsync(h.Central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        var approved = (await h.Workflow.Lifecycle.ApproveAsync(h.Manager, sent.Value.Id, sent.Version, Token)).Value!;
        var forwarded = (await h.Workflow.Lifecycle.ForwardToDrawingAsync(h.Planning, approved.Value.Id, approved.Version, Token)).Value!;
        Assert.True((await h.Workflow.Lifecycle.RecordDrawingResultAsync(h.Drawing, forwarded.Value.Id, forwarded.Version, DrawingOutcome.FullyAccepted, null, Token)).Succeeded);
        if (type == WorkType.Make)
        {
            Assert.True((await h.Workflow.Contracts.DecideRequirementAsync(h.Planning, created.Value.Id, ContractRequirement.NotRequired, null, Token)).Succeeded);
        }

        return created.Value.Id;
    }

    private static async Task<(long UnitId, long RequestId, long ItemId)> ItemAsync(WorkflowHarness h, string requestNo, string itemNo, long unitId, Priority priority = Priority.Normal)
    {
        var id = await ReadyAsync(h, WorkType.Make, requestNo);
        var piece = (await h.Workflow.QcMaster.LoadAsync(h.Qc, Token)).Value!.QuantityTypes.First(t => t.Value.Code == "PIECE").Value.Id;
        var material = (await h.Workflow.Materials.LoadAsync(h.Warehouse, Token)).Value!.Materials.First().Value.Id;
        var version = (await h.Workflow.Requests.GetAsync(h.Qc, id, Token)).Value!.Request.Version;
        var defined = await h.Workflow.ItemDefinition.DefineItemAsync(h.Planning, id, version, new DefineItemCommand(itemNo, "قطعه " + itemNo, 10m, priority, piece, material, 1m, null, null), Token);
        Assert.True(defined.Succeeded, defined.Status + " " + defined.Issue);
        var itemId = defined.Value!.Value.Items.Single(i => i.ItemNo == itemNo).Id;
        var current = (await h.Workflow.Requests.GetAsync(h.Planning, id, Token)).Value!;
        Assert.True((await h.Workflow.ItemDefinition.DefineStageAsync(h.Planning, id, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand("فرز", 2m, unitId, null), Token)).Succeeded);
        var latest = (await h.Workflow.Requests.GetAsync(h.Planning, id, Token)).Value!;
        var target = new StageTarget(id, itemId, 1, latest.Request.Version, latest.Versions.ItemVersion(itemId), latest.Versions.Stages.Where(s => s.Key.ItemId == itemId).ToDictionary(s => s.Key.StageNo, s => s.Value));
        Assert.True((await h.Workflow.StageWork.PerformAsync(h.Planning, StageAction.Release, target, Token)).Succeeded);
        return (unitId, id, itemId);
    }

    [Fact]
    public async Task Planning_reorders_management_overrides_and_production_runs_and_stops_the_stage_with_persian_digits_and_reasons()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        Assert.True((await h.Workflow.Materials.AddMaterialAsync(h.Warehouse, "ST37-R30", "میلگرد ST37", "ST37", "میلگرد", Token)).Succeeded);
        var unit = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Planning, "MILLING", "فرزکاری", Token)).Value!.Value.Id;
        var a = await ItemAsync(h, "R-1", "A", unit);
        var b = await ItemAsync(h, "R-2", "B", unit);
        Assert.True((await h.Workflow.UnitDesignations.AssignAsync(h.Planning, h.Production.UserId, unit, Token)).Succeeded);

        // Planning
        var planning = For(h, h.Planning);
        await planning.LoadAsync(Token);
        Assert.True(planning.CanOrder);
        Assert.False(planning.CanOverride);
        Assert.Equal(["A", "B"], planning.Rows.Select(r => r.ItemNo).ToArray());
        Assert.Equal("۱", planning.Rows[0].OrderText);
        Assert.Contains("مرحلهٔ ۱ از ۱", planning.Rows[0].StageText, StringComparison.Ordinal);
        Assert.Equal("قابل اجرا", planning.Rows[0].ReasonText);
        Assert.Equal("—", planning.Rows[0].PlannedEndText);
        await planning.MoveAsync(planning.Rows[0], +1);
        Assert.False(planning.IsErrorMessage, planning.Message);
        Assert.Equal(["B", "A"], planning.Rows.Select(r => r.ItemNo).ToArray());
        Assert.Equal("۱", planning.Rows[0].PositionText);

        // Management overrides A to the head with a reason; the screen shows it and Planning's position is untouched.
        var manager = For(h, h.Manager);
        await manager.LoadAsync(Token);
        Assert.True(manager.CanOverride);
        Assert.False(manager.CanOrder);
        await manager.OverrideAsync([manager.Rows.Single(r => r.ItemNo == "A")], "  ");
        Assert.True(manager.IsErrorMessage);
        Assert.Equal(Messages.ForIssue(ValidationIssue.QueueReasonRequired, JalaliDateError.None), manager.Message);
        await manager.OverrideAsync([manager.Rows.Single(r => r.ItemNo == "A")], "مشتری کلیدی");
        Assert.False(manager.IsErrorMessage, manager.Message);
        Assert.Equal(["A", "B"], manager.Rows.Select(r => r.ItemNo).ToArray());
        Assert.Contains("رتبهٔ ۱", manager.Rows[0].OverrideText, StringComparison.Ordinal);
        Assert.Contains("مشتری کلیدی", manager.Rows[0].OverrideText, StringComparison.Ordinal);

        // Production sees only the designated unit, starts and stops work; an undesignated Production user sees nothing.
        var production = For(h, h.Production);
        await production.LoadAsync(Token);
        Assert.Equal(["فرزکاری"], production.Units.Select(u => u.Name).ToArray());
        Assert.True(production.CanRun);
        Assert.False(production.CanOrder || production.CanOverride);
        await production.StartAsync(production.Rows[0]);
        Assert.False(production.IsErrorMessage, production.Message);
        Assert.Equal("در حال اجرا", production.Rows[0].CategoryText);
        await production.StopAsync(production.Rows[1], "خرابی دستگاه");
        Assert.Equal("متوقف / مشکل‌دار", production.Rows.Single(r => r.ItemNo == "B").CategoryText);
        Assert.Contains("خرابی دستگاه", production.Rows.Single(r => r.ItemNo == "B").ReasonText, StringComparison.Ordinal);
        await production.StartAsync(production.Rows.Single(r => r.ItemNo == "B"));
        Assert.True(production.IsErrorMessage);
        Assert.Equal(Messages.ForIssue(ValidationIssue.StageStopped, JalaliDateError.None), production.Message);
        await production.ResumeAsync(production.Rows.Single(r => r.ItemNo == "B"));
        Assert.False(production.IsErrorMessage, production.Message);

        // The same screen for a user of another department is denied; nothing is shown.
        var drawing = For(h, h.Drawing);
        await drawing.LoadAsync(Token);
        Assert.True(drawing.IsDenied);
        Assert.Equal(UnitQueueViewModel.DeniedText, drawing.Message);
        Assert.Empty(drawing.Rows);
        Assert.False(drawing.CanOrder || drawing.CanOverride || drawing.CanRun || drawing.CanHold);
        _ = (a, b);
    }

    [Fact]
    public async Task A_stale_screen_is_told_somebody_else_changed_the_queue_and_it_reloads_the_truth()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        Assert.True((await h.Workflow.Materials.AddMaterialAsync(h.Warehouse, "ST37-R30", "میلگرد ST37", "ST37", "میلگرد", Token)).Succeeded);
        var unit = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Planning, "MILLING", "فرزکاری", Token)).Value!.Value.Id;
        await ItemAsync(h, "R-1", "A", unit);
        await ItemAsync(h, "R-2", "B", unit);
        var first = For(h, h.Planning);
        var second = For(h, h.Planning);
        await first.LoadAsync(Token);
        await second.LoadAsync(Token);

        await second.MoveAsync(second.Rows[0], +1);
        Assert.False(second.IsErrorMessage, second.Message);
        await first.MoveAsync(first.Rows[0], +1);

        Assert.True(first.IsErrorMessage);
        Assert.Equal(UnitQueueViewModel.ConflictText, first.Message);
        Assert.Equal(["B", "A"], first.Rows.Select(r => r.ItemNo).ToArray());
    }
}
