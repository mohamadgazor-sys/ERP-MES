using ERP.MES.Application.Persistence;
using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Infrastructure.Tests;

// QC correction of a final stage decision through the real services (Master Specification 1.10, 15; owner decisions Q1-Q8, Decision Log #142).
public class QcCorrectionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed record Setup(KitWorld W, long RequestId, long Unit, long Piece, long Material);

    private static async Task<Setup> StartAsync(string requestNo)
    {
        var w = await KitWorld.StartAsync();
        var (requestId, _) = await w.ReadyRequestAsync(requestNo, contractRequired: false);
        var unit = await w.AddUnitAsync("MILLING");
        await w.AddMachineAsync("FCC-06", unit);
        await w.MaterialAsync();
        Assert.True((await w.Services.UnitDesignations.AssignAsync(w.Admin, w.Production.UserId, unit, Token)).Succeeded);
        var material = long.Parse(w.Fixture.Db.Query("SELECT Id FROM Materials LIMIT 1").Single(), System.Globalization.CultureInfo.InvariantCulture);
        return new Setup(w, requestId, unit, await w.PieceTypeAsync(), material);
    }

    private static async Task<long> DefineAsync(Setup s, string customerRef, long? parent = null, bool assembly = false)
    {
        var w = s.W;
        var defined = await w.Services.ItemDefinition.DefineItemAsync(w.Planning, s.RequestId, await w.RequestVersionAsync(s.RequestId), new DefineItemCommand(customerRef, "Part " + customerRef, 10m, Priority.Normal, s.Piece, s.Material, 1m, 100m, 20m, parent, assembly), Token);
        Assert.True(defined.Succeeded, defined.Status + " " + defined.Issue);
        return defined.Value!.Value.Items.Single(i => i.CustomerItemRef == customerRef).Id;
    }

    private static async Task StageAsync(Setup s, long itemId, int stages)
    {
        var w = s.W;
        for (var n = 0; n < stages; n++)
        {
            var current = (await w.Services.Requests.GetAsync(w.Qc, s.RequestId, Token)).Value!;
            Assert.True((await w.Services.ItemDefinition.DefineStageAsync(w.Planning, s.RequestId, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand("Turning", 5m, s.Unit, null), Token)).Succeeded);
        }
    }

    private static async Task<long> ItemAsync(Setup s, string customerRef, int stages)
    {
        var id = await DefineAsync(s, customerRef);
        await StageAsync(s, id, stages);
        return id;
    }

    private static async Task RunStageAsync(Setup s, long itemId, int stageNo, params InspectionEntry[] entries)
    {
        var w = s.W;
        if (stageNo == 1)
        {
            Assert.True((await w.Services.StageWork.PerformAsync(w.Planning, StageAction.Release, await w.TargetAsync(s.RequestId, itemId, 1), Token)).Succeeded);
        }

        Assert.True((await w.Services.StageWork.PerformAsync(w.Production, StageAction.Start, await w.TargetAsync(s.RequestId, itemId, stageNo), Token)).Succeeded);
        Assert.True((await w.Services.StageWork.PerformAsync(w.Production, StageAction.SubmitForQc, await w.TargetAsync(s.RequestId, itemId, stageNo), Token)).Succeeded);
        var inspected = await w.Services.QcInspection.RecordInspectionAsync(w.Qc, await w.TargetAsync(s.RequestId, itemId, stageNo), entries, Token);
        Assert.True(inspected.Succeeded, inspected.Status + " " + inspected.Issue);
    }

    private static async Task<Request> ReadAsync(Setup s) => (await s.W.Services.Requests.GetAsync(s.W.Qc, s.RequestId, Token)).Value!.Request.Value;

    private static InspectionEntry Accept(long units) => new(InspectionDisposition.Accepted, units, null);

    [Fact]
    public async Task Only_QC_can_correct_a_final_stage_with_a_reason_a_valid_quantity_and_before_the_request_is_closed()
    {
        var s = await StartAsync("QC-1");
        using var _ = s.W;
        var w = s.W;
        var item = await ItemAsync(s, "A", 2);
        await RunStageAsync(s, item, 1, Accept(10));

        // Authorization is enforced in the service: nobody but QC.
        foreach (var user in new[] { w.Planning, w.Manager, w.Warehouse, w.Production, w.ProductionManager, w.Central, w.Drawing })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.QcInspection.CorrectAsync(user, await w.TargetAsync(s.RequestId, item, 1), 3, 0, "x", Token)).Status);
        }

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.QcInspection.CorrectAsync(null, await w.TargetAsync(s.RequestId, item, 1), 3, 0, "x", Token)).Status);
        Assert.Equal(ValidationIssue.CorrectionInvalid, (await w.Services.QcInspection.CorrectAsync(w.Qc, await w.TargetAsync(s.RequestId, item, 1), 3, 0, " ", Token)).Issue);
        Assert.Equal(ValidationIssue.CorrectionInvalid, (await w.Services.QcInspection.CorrectAsync(w.Qc, await w.TargetAsync(s.RequestId, item, 1), 0, 0, "x", Token)).Issue);
        Assert.Equal(ValidationIssue.CorrectionExceedsAccepted, (await w.Services.QcInspection.CorrectAsync(w.Qc, await w.TargetAsync(s.RequestId, item, 1), 8, 3, "x", Token)).Issue);
        Assert.Equal(OperationStatus.InvalidState, (await w.Services.QcInspection.CorrectAsync(w.Qc, await w.TargetAsync(s.RequestId, item, 2), 1, 0, "x", Token)).Status); // stage 2 is not final
        Assert.Equal(0, w.Count("QcCorrections"));
    }

    [Fact]
    public async Task A_partial_rework_correction_keeps_every_record_marks_the_downstream_stage_and_the_normal_loop_completes_it_again()
    {
        var s = await StartAsync("QC-2");
        using var _ = s.W;
        var w = s.W;
        var item = await ItemAsync(s, "A", 2);
        await RunStageAsync(s, item, 1, Accept(6), Accept(4));
        Assert.Equal(ItemStageStatus.Waiting, (await ReadAsync(s)).Items.Single().Stages[1].Status);
        var linesBefore = w.Count("QcInspectionLines");

        var corrected = await w.Services.QcInspection.CorrectAsync(w.Qc, await w.TargetAsync(s.RequestId, item, 1), 5, 0, "ابعاد نادرست", Token);
        Assert.True(corrected.Succeeded, corrected.Status + " " + corrected.Issue);
        var correction = corrected.Value!;
        Assert.Equal((10L, 5L, 5L, 5L, 0L), (correction.AcceptedBeforeBaseUnits, correction.AcceptedAfterBaseUnits, correction.CorrectedBaseUnits, correction.ReworkBaseUnits, correction.ScrapBaseUnits));
        Assert.Equal(w.Qc.UserId, correction.CorrectedByUserId);

        // History preserved: the QC lines and the approval stay; the correction links to the decisions it supersedes (newest first: the line of 4, then 1 of the line of 6).
        Assert.Equal(linesBefore, w.Count("QcInspectionLines"));
        Assert.Equal(1, w.Count("QcCorrections"));
        Assert.Equal(["1", "4"], w.Fixture.Db.Query("SELECT SupersededBaseUnits FROM QcCorrectionSources ORDER BY SupersededBaseUnits"));
        var stage1 = (await ReadAsync(s)).Items.Single().Stages[0];
        Assert.Equal((ItemStageStatus.InProgress, 5L, 5L, 0L), (stage1.Status, stage1.AcceptedBaseUnits, stage1.ReworkBaseUnits, stage1.ScrapBaseUnits));
        Assert.Single((await ReadAsync(s)).Items.Single().QcApprovals);

        // The item carries the visible indicator; stage 2 had been released and is marked, not reset; the sequential gate keeps it from starting.
        var current = (await ReadAsync(s)).Items.Single();
        Assert.True(current.HasQcCorrection);
        Assert.NotNull(current.Stages[1].AffectedByQcCorrectionId);
        Assert.Equal(ItemStageStatus.Waiting, current.Stages[1].Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Services.StageWork.PerformAsync(w.Production, StageAction.Start, await w.TargetAsync(s.RequestId, item, 2), Token)).Status);
        Assert.Equal(1, w.Count("AuditLog WHERE ActionCode = 'QcCorrectionRecorded'"));

        // The reworked units go through production and QC again: the stage is approved again with all ten units and a second approval is appended.
        Assert.True((await w.Services.StageWork.PerformAsync(w.Production, StageAction.SubmitForQc, await w.TargetAsync(s.RequestId, item, 1), Token)).Succeeded);
        Assert.True((await w.Services.QcInspection.RecordInspectionAsync(w.Qc, await w.TargetAsync(s.RequestId, item, 1), [Accept(5)], Token)).Succeeded);
        var again = (await ReadAsync(s)).Items.Single();
        Assert.Equal((ItemStageStatus.QcApproved, 10L, 2), (again.Stages[0].Status, again.Stages[0].AcceptedBaseUnits, again.QcApprovals.Count));
        Assert.Equal(1, w.Count("QcCorrections")); // the history keeps the one correction
    }

    [Fact]
    public async Task A_scrap_correction_waits_for_Management_and_a_second_correction_can_only_take_the_still_valid_units()
    {
        var s = await StartAsync("QC-3");
        using var _ = s.W;
        var w = s.W;
        var item = await ItemAsync(s, "A", 2);
        await RunStageAsync(s, item, 1, Accept(10));

        Assert.True((await w.Services.QcInspection.CorrectAsync(w.Qc, await w.TargetAsync(s.RequestId, item, 1), 0, 4, "ترک", Token)).Succeeded);
        var stage = (await ReadAsync(s)).Items.Single().Stages[0];
        Assert.Equal((ItemStageStatus.WaitingShortfallDecision, 6L, 4L), (stage.Status, stage.AcceptedBaseUnits, stage.ScrapBaseUnits));

        // QC alone cannot close the shortfall; Management does through the existing mechanism, and the unstarted next stage then receives only the six valid units.
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.QcInspection.DecideShortfallAsync(w.Qc, await w.TargetAsync(s.RequestId, item, 1), ShortfallKind.ManagementAccepted, "x", Token)).Status);
        var decided = await w.Services.QcInspection.DecideShortfallAsync(w.Manager, await w.TargetAsync(s.RequestId, item, 1), ShortfallKind.ManagementAccepted, "پذیرش کسری", Token);
        Assert.True(decided.Succeeded, decided.Status + " " + decided.Issue);
        var after = (await ReadAsync(s)).Items.Single();
        Assert.Equal((ItemStageStatus.QcApproved, 6L, 6L), (after.Stages[0].Status, after.Stages[0].AcceptedBaseUnits, after.Stages[1].InputBaseUnits ?? 0));

        // A second correction may take at most the six valid units; the first correction's units are not available a second time.
        Assert.Equal(ValidationIssue.CorrectionExceedsAccepted, (await w.Services.QcInspection.CorrectAsync(w.Qc, await w.TargetAsync(s.RequestId, item, 1), 7, 0, "x", Token)).Issue);
        Assert.True((await w.Services.QcInspection.CorrectAsync(w.Qc, await w.TargetAsync(s.RequestId, item, 1), 6, 0, "همه", Token)).Succeeded);
        Assert.Equal(2, w.Count("QcCorrections"));
    }

    [Fact]
    public async Task An_unstarted_assembly_parent_goes_back_to_waiting_and_a_parent_in_work_is_only_marked()
    {
        var s = await StartAsync("QC-4");
        using var _ = s.W;
        var w = s.W;
        var parent = await DefineAsync(s, "ASM", assembly: true);
        var child = await DefineAsync(s, "CH", parent);
        await StageAsync(s, parent, 1);
        await StageAsync(s, child, 1);
        await RunStageAsync(s, child, 1, Accept(10)); // the child is complete: the assembly's first stage can now be released
        Assert.True((await w.Services.StageWork.PerformAsync(w.Planning, StageAction.Release, await w.TargetAsync(s.RequestId, parent, 1), Token)).Succeeded);
        Assert.Equal(ItemStageStatus.Waiting, (await ReadAsync(s)).Items.Single(i => i.Id == parent).Stages[0].Status);

        Assert.True((await w.Services.QcInspection.CorrectAsync(w.Qc, await w.TargetAsync(s.RequestId, child, 1), 2, 0, "ابعاد", Token)).Succeeded);
        var request = await ReadAsync(s);
        Assert.Equal(ItemStageStatus.Planned, request.Items.Single(i => i.Id == parent).Stages[0].Status); // back to the waiting state (#140), not executable
        Assert.True(request.Items.Single(i => i.Id == parent).HasQcCorrection);
        var queue = (await w.Services.UnitQueue.LoadAsync(w.Planning, s.Unit, Token)).Value!;
        Assert.Equal(QueueBlock.WaitingChildren, queue.Pending.Single(e => e.ItemId == parent).Block);
        Assert.Equal(OperationStatus.InvalidState, (await w.Services.StageWork.PerformAsync(w.Production, StageAction.Start, await w.TargetAsync(s.RequestId, parent, 1), Token)).Status);
    }
}
