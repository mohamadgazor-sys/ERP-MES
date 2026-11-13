using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Infrastructure.Tests;

// Request and item progress is derived from the persisted stage state (Master Specification 12.5), never typed.
public class RequestProgressTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_request_progress_counts_and_the_item_stage_progress_follow_the_real_stage_state()
    {
        using var w = await KitWorld.StartAsync();
        var (requestId, _) = await w.ReadyRequestAsync("PG-1", contractRequired: false);
        var unit = await w.AddUnitAsync("MILLING");
        await w.AddMachineAsync("FCC-06", unit);
        await w.MaterialAsync();
        Assert.True((await w.Services.UnitDesignations.AssignAsync(w.Admin, w.Production.UserId, unit, Token)).Succeeded);
        var material = long.Parse(w.Fixture.Db.Query("SELECT Id FROM Materials LIMIT 1").Single(), System.Globalization.CultureInfo.InvariantCulture);
        var piece = await w.PieceTypeAsync();

        async Task<long> ItemAsync(string customerRef, int stages)
        {
            var defined = await w.Services.ItemDefinition.DefineItemAsync(w.Planning, requestId, await w.RequestVersionAsync(requestId), new DefineItemCommand(customerRef, "Part " + customerRef, 10m, Priority.Normal, piece, material, 1m, 100m, 20m), Token);
            Assert.True(defined.Succeeded, defined.Status + " " + defined.Issue);
            var id = defined.Value!.Value.Items.Single(i => i.CustomerItemRef == customerRef).Id;
            for (var n = 0; n < stages; n++)
            {
                var current = (await w.Services.Requests.GetAsync(w.Qc, requestId, Token)).Value!;
                Assert.True((await w.Services.ItemDefinition.DefineStageAsync(w.Planning, requestId, current.Request.Version, id, current.Versions.ItemVersion(id), new DefineStageCommand("Turning", 5m, unit, null), Token)).Succeeded);
            }

            return id;
        }

        async Task<RequestProgress> ProgressAsync() => RequestProgress.From((await w.Services.Requests.GetAsync(w.Qc, requestId, Token)).Value!.Request.Value);

        var a = await ItemAsync("A", 2);
        var b = await ItemAsync("B", 1);
        var c = await ItemAsync("C", 0);

        // Nothing released yet: three items, none completed, none active, all waiting; the stage progress is "0 of n".
        var start = await ProgressAsync();
        Assert.Equal((3, 0, 0, 3, 0), (start.TotalItems, start.Completed, start.Active, start.WaitingOrBlocked, start.Rework));
        Assert.Equal((0, 2), (start.Items.Single(i => i.ItemId == a).StagesCompleted, start.Items.Single(i => i.ItemId == a).StageCount));
        Assert.Equal(0, start.Items.Single(i => i.ItemId == c).StageCount);

        // Item C has no stage, so the release is refused; give it none and release the rest by removing it from the picture: release A and B one by one.
        Assert.True((await w.Services.StageWork.PerformAsync(w.Planning, StageAction.Release, await w.TargetAsync(requestId, a, 1), Token)).Succeeded);
        Assert.True((await w.Services.StageWork.PerformAsync(w.Planning, StageAction.Release, await w.TargetAsync(requestId, b, 1), Token)).Succeeded);
        Assert.Equal(0, (await ProgressAsync()).Active); // queued, not started: waiting

        // B runs its single stage and QC accepts everything: completed.
        Assert.True((await w.Services.StageWork.PerformAsync(w.Production, StageAction.Start, await w.TargetAsync(requestId, b, 1), Token)).Succeeded);
        Assert.True((await w.Services.StageWork.PerformAsync(w.Production, StageAction.SubmitForQc, await w.TargetAsync(requestId, b, 1), Token)).Succeeded);
        Assert.True((await w.Services.QcInspection.RecordInspectionAsync(w.Qc, await w.TargetAsync(requestId, b, 1), [new InspectionEntry(InspectionDisposition.Accepted, 10, null)], Token)).Succeeded);
        // A starts: active.
        Assert.True((await w.Services.StageWork.PerformAsync(w.Production, StageAction.Start, await w.TargetAsync(requestId, a, 1), Token)).Succeeded);

        var running = await ProgressAsync();
        Assert.Equal((3, 1, 1, 1, 0), (running.TotalItems, running.Completed, running.Active, running.WaitingOrBlocked, running.Rework));
        Assert.Equal((1, 1), (running.Items.Single(i => i.ItemId == b).StagesCompleted, running.Items.Single(i => i.ItemId == b).StageCount));
        Assert.Equal(ItemProgressKind.Completed, running.Items.Single(i => i.ItemId == b).Kind);
        Assert.Equal(0.3333d, Math.Round(running.CompletedItemRatio, 4));

        // A is submitted and QC sends three units to rework: the item counts as rework, and its progress stays 0 of 2 stages.
        Assert.True((await w.Services.StageWork.PerformAsync(w.Production, StageAction.SubmitForQc, await w.TargetAsync(requestId, a, 1), Token)).Succeeded);
        Assert.True((await w.Services.QcInspection.RecordInspectionAsync(w.Qc, await w.TargetAsync(requestId, a, 1), [new InspectionEntry(InspectionDisposition.Accepted, 7, null), new InspectionEntry(InspectionDisposition.Rework, 3, "سطح ناصاف")], Token)).Succeeded);
        var rework = await ProgressAsync();
        Assert.Equal((3, 1, 0, 1, 1), (rework.TotalItems, rework.Completed, rework.Active, rework.WaitingOrBlocked, rework.Rework));
        Assert.Equal((0, 2, ItemProgressKind.Rework), (rework.Items.Single(i => i.ItemId == a).StagesCompleted, rework.Items.Single(i => i.ItemId == a).StageCount, rework.Items.Single(i => i.ItemId == a).Kind));
    }
}
