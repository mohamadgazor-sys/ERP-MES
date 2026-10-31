using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Infrastructure.Tests;

// The first stage of an assembly is released only after every child and descendant finished its route with every QC approval (Decision Log #121 Q5), end to end through the real services.
public class AssemblyReleaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(long ItemId, string ItemNo)> DefineAsync(KitWorld w, long requestId, string? customerRef, long unit, long? parentId = null, bool assembly = false)
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

    // The hierarchy is locked once a stage exists (Decision Log #121 Q6), so every item is defined before any stage.
    private static async Task StageAsync(KitWorld w, long requestId, long itemId, long unit)
    {
        var current = (await w.Services.Requests.GetAsync(w.Qc, requestId, Token)).Value!;
        var staged = await w.Services.ItemDefinition.DefineStageAsync(w.Planning, requestId, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand("Turning", 5m, unit, null), Token);
        Assert.True(staged.Succeeded, staged.Status + " " + staged.Issue);
        w.Tick();
    }

    private static async Task CompleteRouteAsync(KitWorld w, long requestId, long itemId, long unit)
    {
        Assert.True((await w.Services.StageWork.PerformAsync(w.Planning, StageAction.Release, await w.TargetAsync(requestId, itemId, 1), Token)).Succeeded);
        Assert.True((await w.Services.StageWork.PerformAsync(w.Production, StageAction.Start, await w.TargetAsync(requestId, itemId, 1), Token)).Succeeded);
        Assert.True((await w.Services.StageWork.PerformAsync(w.Production, StageAction.SubmitForQc, await w.TargetAsync(requestId, itemId, 1), Token)).Succeeded);
        var inspected = await w.Services.QcInspection.RecordInspectionAsync(w.Qc, await w.TargetAsync(requestId, itemId, 1), [new InspectionEntry(InspectionDisposition.Accepted, 10, null)], Token);
        Assert.True(inspected.Succeeded, inspected.Status + " " + inspected.Issue);
        w.Tick();
    }

    [Fact]
    public async Task The_assembly_waits_in_the_queue_and_cannot_be_released_until_all_descendants_are_complete()
    {
        using var w = await KitWorld.StartAsync();
        var (requestId, _) = await w.ReadyRequestAsync("AS-1", contractRequired: false);
        var unit = await w.AddUnitAsync("MILLING");
        await w.AddMachineAsync("FCC-06", unit);
        await w.MaterialAsync();
        Assert.True((await w.Services.UnitDesignations.AssignAsync(w.Admin, w.Production.UserId, unit, Token)).Succeeded);

        var parent = await DefineAsync(w, requestId, "ASM", unit, assembly: true);
        var child = await DefineAsync(w, requestId, "CH1", unit, parent.ItemId, assembly: true);
        var grandChild = await DefineAsync(w, requestId, "CH11", unit, child.ItemId);
        Assert.Equal(new[] { "AS-1-001", "AS-1-001.001", "AS-1-001.001.001" }, new[] { parent.ItemNo, child.ItemNo, grandChild.ItemNo });
        foreach (var id in new[] { parent.ItemId, child.ItemId, grandChild.ItemId })
        {
            await StageAsync(w, requestId, id, unit);
        }

        // Nothing below is finished: the assembly's first stage is refused and the queue says why.
        var refused = await w.Services.StageWork.PerformAsync(w.Planning, StageAction.Release, await w.TargetAsync(requestId, parent.ItemId, 1), Token);
        Assert.Equal(ValidationIssue.AssemblyChildrenNotComplete, refused.Issue);
        var pending = (await w.Services.UnitQueue.LoadAsync(w.Planning, unit, Token)).Value!.Pending;
        Assert.Equal(QueueBlock.WaitingChildren, pending.Single(e => e.ItemId == parent.ItemId).Block);
        Assert.Equal(QueueBlock.NotReleased, pending.Single(e => e.ItemId == grandChild.ItemId).Block);

        // The grandchild is done, the child is not: still refused.
        await CompleteRouteAsync(w, requestId, grandChild.ItemId, unit);
        Assert.Equal(ValidationIssue.AssemblyChildrenNotComplete, (await w.Services.StageWork.PerformAsync(w.Planning, StageAction.Release, await w.TargetAsync(requestId, parent.ItemId, 1), Token)).Issue);

        // The child's only descendant is done, so the child itself may now run its route; the assembly still waits for the child.
        await CompleteRouteAsync(w, requestId, child.ItemId, unit);
        var released = await w.Services.StageWork.PerformAsync(w.Planning, StageAction.Release, await w.TargetAsync(requestId, parent.ItemId, 1), Token);
        Assert.True(released.Succeeded, released.Status + " " + released.Issue);
    }
}
