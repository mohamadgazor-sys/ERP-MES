using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Infrastructure.Tests;

// Releasing a whole request to the production unit queues (Decision Log #137): same gates as the single-stage release, all-or-nothing, audited, no duplicate release.
public class BulkReleaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(long ItemId, string ItemNo)> DefineAsync(KitWorld w, long requestId, string customerRef, long? parentId = null, bool assembly = false)
    {
        var version = await w.RequestVersionAsync(requestId);
        var material = long.Parse(w.Fixture.Db.Query("SELECT Id FROM Materials LIMIT 1").Single(), System.Globalization.CultureInfo.InvariantCulture);
        var piece = await w.PieceTypeAsync();
        var defined = await w.Services.ItemDefinition.DefineItemAsync(w.Planning, requestId, version, new DefineItemCommand(customerRef, "Part " + customerRef, 10m, Priority.Normal, piece, material, 1m, 100m, 20m, parentId, assembly), Token);
        Assert.True(defined.Succeeded, defined.Status + " " + defined.Issue);
        var item = defined.Value!.Value.Items[^1];
        w.Tick();
        return (item.Id, item.ItemNo);
    }

    private static async Task StageAsync(KitWorld w, long requestId, long itemId, long unit, string title = "Turning")
    {
        var current = (await w.Services.Requests.GetAsync(w.Qc, requestId, Token)).Value!;
        var staged = await w.Services.ItemDefinition.DefineStageAsync(w.Planning, requestId, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand(title, 5m, unit, null), Token);
        Assert.True(staged.Succeeded, staged.Status + " " + staged.Issue);
        w.Tick();
    }

    private static async Task<(long RequestId, long Unit)> StartAsync(KitWorld w, string requestNo)
    {
        var (requestId, _) = await w.ReadyRequestAsync(requestNo, contractRequired: false);
        var unit = await w.AddUnitAsync("MILLING");
        await w.AddMachineAsync("FCC-06", unit);
        await w.MaterialAsync();
        Assert.True((await w.Services.UnitDesignations.AssignAsync(w.Admin, w.Production.UserId, unit, Token)).Succeeded);
        return (requestId, unit);
    }

    private static async Task<IReadOnlyList<ItemStageStatus>> FirstStatusesAsync(KitWorld w, long requestId) =>
        (await w.Services.Requests.GetAsync(w.Qc, requestId, Token)).Value!.Request.Value.Items.Select(i => i.Stages[0].Status).ToList();

    [Fact]
    public async Task Planning_releases_every_item_with_one_history_entry_audit_rows_and_a_second_attempt_is_refused()
    {
        using var w = await KitWorld.StartAsync();
        var (requestId, unit) = await StartAsync(w, "BR-1");
        var a = await DefineAsync(w, requestId, "A");
        var b = await DefineAsync(w, requestId, "B");
        await StageAsync(w, requestId, a.ItemId, unit);
        await StageAsync(w, requestId, a.ItemId, unit, "برش");
        await StageAsync(w, requestId, b.ItemId, unit);

        // Only Planning may release; Production is refused in the service.
        Assert.Equal(Application.Requests.OperationStatus.NotAuthorized, (await w.Services.StageWork.ReleaseAllAsync(w.Production, requestId, Token)).Status);
        Assert.All(await FirstStatusesAsync(w, requestId), s => Assert.Equal(ItemStageStatus.Planned, s));

        var result = await w.Services.StageWork.ReleaseAllAsync(w.Planning, requestId, Token);
        Assert.True(result.Succeeded, result.Status + " " + result.Issue);
        Assert.Equal(2, result.Value!.Released.Count);
        var request = (await w.Services.Requests.GetAsync(w.Qc, requestId, Token)).Value!.Request.Value;
        Assert.All(request.Items, i => Assert.Equal(ItemStageStatus.Waiting, i.Stages[0].Status));
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, request.Items.Single(i => i.Id == a.ItemId).Stages[1].Status);

        var entry = request.WorkflowHistory.Last();
        Assert.Equal(RequestAction.ReleaseToProduction, entry.Action);
        Assert.Equal(w.Planning.UserId, entry.ActionByUserId);
        Assert.Equal(request.Status, entry.ToStatus);
        Assert.Equal(2, w.Fixture.Db.Query("SELECT Id FROM AuditLog WHERE ActionCode = 'StageReleased'").Count);
        var summary = Assert.Single(w.Fixture.Db.Query("SELECT Summary FROM AuditLog WHERE ActionCode = 'RequestReleasedToProduction'"));
        Assert.Contains(w.Planning.Username, summary, StringComparison.Ordinal);
        Assert.Contains("BR-1", summary, StringComparison.Ordinal);

        // The queue shows both items for the unit.
        var queued = (await w.Services.UnitQueue.LoadAsync(w.Planning, unit, Token)).Value!;
        Assert.Equal(2, queued.Queue.Count);

        var again = await w.Services.StageWork.ReleaseAllAsync(w.Planning, requestId, Token);
        Assert.False(again.Succeeded);
        Assert.Equal(ValidationIssue.NothingToRelease, again.Issue);
        Assert.Equal(1, request.WorkflowHistory.Count(e => e.Action == RequestAction.ReleaseToProduction));
    }

    [Fact]
    public async Task One_blocked_item_refuses_the_whole_release_and_names_it_nothing_is_released()
    {
        using var w = await KitWorld.StartAsync();
        var (requestId, unit) = await StartAsync(w, "BR-2");
        var ok = await DefineAsync(w, requestId, "OK");
        var bare = await DefineAsync(w, requestId, "BARE");
        await StageAsync(w, requestId, ok.ItemId, unit);

        var refused = await w.Services.StageWork.ReleaseAllAsync(w.Planning, requestId, Token);
        Assert.False(refused.Succeeded);
        Assert.Equal(ValidationIssue.ReleaseBlocked, refused.Issue);
        var blocker = Assert.Single(refused.Value!.Blockers);
        Assert.Equal(bare.ItemNo, blocker.ItemNo);
        Assert.Equal(ValidationIssue.ItemHasNoStages, blocker.Issue);
        Assert.Equal(ItemStageStatus.Planned, (await w.Services.Requests.GetAsync(w.Qc, requestId, Token)).Value!.Request.Value.Items.Single(i => i.Id == ok.ItemId).Stages[0].Status);
        Assert.Empty(w.Fixture.Db.Query("SELECT Id FROM AuditLog WHERE ActionCode IN ('StageReleased','RequestReleasedToProduction')"));
    }

    [Fact]
    public async Task An_assembly_waits_for_its_children_and_is_reported_then_released_by_a_later_release_after_them()
    {
        using var w = await KitWorld.StartAsync();
        var (requestId, unit) = await StartAsync(w, "BR-3");
        var parent = await DefineAsync(w, requestId, "ASM", assembly: true);
        var child = await DefineAsync(w, requestId, "CH", parent.ItemId);
        await StageAsync(w, requestId, parent.ItemId, unit);
        await StageAsync(w, requestId, child.ItemId, unit);

        var first = await w.Services.StageWork.ReleaseAllAsync(w.Planning, requestId, Token);
        Assert.True(first.Succeeded, first.Status + " " + first.Issue);
        Assert.Equal([child.ItemNo], first.Value!.Released);
        Assert.Equal([parent.ItemNo], first.Value.WaitingForChildren);
        var items = (await w.Services.Requests.GetAsync(w.Qc, requestId, Token)).Value!.Request.Value.Items;
        Assert.Equal(ItemStageStatus.Planned, items.Single(i => i.Id == parent.ItemId).Stages[0].Status);

        // The waiting state is a valid business state: recorded in the history note and the audit trail, visible to Planning and the Production Manager as waiting (not as a released stage), and the operator cannot start it.
        var entry = (await w.Services.Requests.GetAsync(w.Qc, requestId, Token)).Value!.Request.Value.WorkflowHistory.Last(e => e.Action == RequestAction.ReleaseToProduction);
        Assert.Contains(parent.ItemNo, entry.Note, StringComparison.Ordinal);
        Assert.Contains("منتظر زیرمجموعه", entry.Note, StringComparison.Ordinal);
        Assert.Equal(1, w.Count("AuditLog WHERE ActionCode = 'AssemblyWaitingForChildren'"));
        foreach (var viewer in new[] { w.Planning, w.ProductionManager })
        {
            var view = (await w.Services.UnitQueue.LoadAsync(viewer, unit, Token)).Value!;
            var waitingEntry = view.Pending.Single(e => e.ItemId == parent.ItemId);
            Assert.Equal(QueueBlock.WaitingChildren, waitingEntry.Block);
            Assert.Null(waitingEntry.Category);
            Assert.DoesNotContain(view.Queue, e => e.ItemId == parent.ItemId);
            Assert.Contains(view.Queue, e => e.ItemId == child.ItemId);
        }

        // The designated operator cannot start it, and Planning cannot start it either: the stage is not executable until the dependencies are satisfied.
        var refusedStart = await w.Services.StageWork.PerformAsync(w.Production, StageAction.Start, await w.TargetAsync(requestId, parent.ItemId, 1), Token);
        Assert.Equal(OperationStatus.InvalidState, refusedStart.Status);
        Assert.Equal(ItemStageStatus.Planned, (await w.Services.Requests.GetAsync(w.Qc, requestId, Token)).Value!.Request.Value.Items.Single(i => i.Id == parent.ItemId).Stages[0].Status);

        // The child finishes its route with QC approval; only then does the assembly release.
        Assert.True((await w.Services.StageWork.PerformAsync(w.Production, StageAction.Start, await w.TargetAsync(requestId, child.ItemId, 1), Token)).Succeeded);
        Assert.True((await w.Services.StageWork.PerformAsync(w.Production, StageAction.SubmitForQc, await w.TargetAsync(requestId, child.ItemId, 1), Token)).Succeeded);
        Assert.True((await w.Services.QcInspection.RecordInspectionAsync(w.Qc, await w.TargetAsync(requestId, child.ItemId, 1), [new InspectionEntry(InspectionDisposition.Accepted, 10, null)], Token)).Succeeded);
        var second = await w.Services.StageWork.ReleaseAllAsync(w.Planning, requestId, Token);
        Assert.True(second.Succeeded, second.Status + " " + second.Issue);
        Assert.Equal([parent.ItemNo], second.Value!.Released);
        Assert.Equal([child.ItemNo], second.Value.AlreadyReleased);
    }
}
