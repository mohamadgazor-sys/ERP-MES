using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Tests.Requests;

public class ItemRouteTests
{
    private static readonly DateTime T1 = RequestFixtures.Now.AddHours(1);

    [Fact]
    public void Stages_are_numbered_in_order_and_start_in_their_initial_status()
    {
        var item = RequestFixtures.ItemWithThreeStages();

        Assert.Equal(new[] { 1, 2, 3 }, item.Stages.Select(s => s.StageNo).ToArray());
        Assert.Equal(new[] { "Turning", "Grinding", "Inspection prep" }, item.Stages.Select(s => s.StageTitle).ToArray());
        Assert.Equal(
            new[] { ItemStageStatus.Planned, ItemStageStatus.WaitingPreviousQc, ItemStageStatus.WaitingPreviousQc },
            item.Stages.Select(s => s.Status).ToArray());
        Assert.False(item.IsRouteCompleted);
    }

    [Fact]
    public void Planned_total_minutes_is_minutes_per_piece_times_quantity_without_rounding()
    {
        var item = RequestFixtures.ItemWithThreeStages(quantity: 10);

        Assert.Equal(new[] { 50m, 30m, 15m }, item.Stages.Select(s => s.PlannedTotalMinutes).ToArray());

        var fraction = RequestFixtures.NewRequest().AddMake("9", quantity: 3m);
        var stage = fraction.AddStage("Odd", 0.3333m);
        Assert.Equal(0.9999m, stage.PlannedTotalMinutes);
    }

    [Fact]
    public void The_planned_quantity_defaults_to_the_requested_quantity_and_can_be_set()
    {
        var item = RequestFixtures.NewRequest().AddMake("1.10", quantity: 12);

        var defaulted = item.AddStage("Turning", 2m);
        var explicitQuantity = item.AddStage("Grinding", 2m, plannedQuantity: 10m);

        Assert.Equal(12m, defaulted.PlannedQuantity);
        Assert.Equal(10m, explicitQuantity.PlannedQuantity);
        Assert.Equal(20m, explicitQuantity.PlannedTotalMinutes);
    }

    [Fact]
    public void Stage_data_is_validated()
    {
        var item = RequestFixtures.NewRequest().AddMake();

        Assert.Throws<ArgumentException>(() => item.AddStage(" ", 1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.AddStage("Turning", 0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.AddStage("Turning", -1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.AddStage("Turning", 1m, plannedQuantity: 0m));
        Assert.Empty(item.Stages);
    }

    [Fact]
    public void The_first_stage_must_be_released_before_it_can_start()
    {
        var item = RequestFixtures.ItemWithThreeStages();

        Assert.Throws<InvalidOperationException>(() => item.StartStage(1));

        item.ReleaseFirstStage();
        Assert.Equal(ItemStageStatus.Waiting, item.Stages[0].Status);
        Assert.Throws<InvalidOperationException>(() => item.ReleaseFirstStage());

        item.StartStage(1);
        Assert.Equal(ItemStageStatus.InProgress, item.Stages[0].Status);
    }

    [Fact]
    public void Releasing_an_item_without_stages_fails()
    {
        var item = RequestFixtures.NewRequest().AddMake();

        Assert.Throws<InvalidOperationException>(() => item.ReleaseFirstStage());
    }

    [Fact]
    public void The_next_stage_cannot_proceed_until_qc_approves_the_previous_one()
    {
        var item = RequestFixtures.ItemWithThreeStages();
        item.ReleaseFirstStage();
        item.StartStage(1);

        // Stage 2 is blocked while stage 1 is in progress and while it waits for QC.
        Assert.Throws<InvalidOperationException>(() => item.StartStage(2));
        item.SubmitStageForQc(1);
        Assert.Equal(ItemStageStatus.WaitingQc, item.Stages[0].Status);
        Assert.Throws<InvalidOperationException>(() => item.StartStage(2));
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, item.Stages[1].Status);

        item.ApproveStageWholeQuantity(1, approvedByUserId: 9, T1);

        Assert.Equal(ItemStageStatus.QcApproved, item.Stages[0].Status);
        Assert.Equal(ItemStageStatus.Waiting, item.Stages[1].Status);
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, item.Stages[2].Status);
        item.StartStage(2);
        Assert.Equal(ItemStageStatus.InProgress, item.Stages[1].Status);
    }

    [Fact]
    public void Qc_approval_is_recorded_for_the_whole_planned_quantity()
    {
        var item = RequestFixtures.ItemWithThreeStages(quantity: 10);
        item.ReleaseFirstStage();
        item.StartStage(1);
        item.SubmitStageForQc(1);

        var approval = item.ApproveStageWholeQuantity(1, approvedByUserId: 9, T1);

        Assert.Equal(new QcApproval(1, 10m, 9, T1), approval);
        Assert.Equal(new[] { approval }, item.QcApprovals.ToArray());
        Assert.Equal(T1, item.UpdatedAtUtc);
    }

    [Fact]
    public void A_stage_cannot_be_approved_before_it_was_submitted_or_twice()
    {
        var item = RequestFixtures.ItemWithThreeStages();
        item.ReleaseFirstStage();
        item.StartStage(1);

        Assert.Throws<InvalidOperationException>(() => item.ApproveStageWholeQuantity(1, 9, T1));

        item.SubmitStageForQc(1);
        item.ApproveStageWholeQuantity(1, 9, T1);

        Assert.Throws<InvalidOperationException>(() => item.ApproveStageWholeQuantity(1, 9, T1));
        Assert.Single(item.QcApprovals);
    }

    [Fact]
    public void A_later_stage_cannot_be_approved_out_of_order()
    {
        var item = RequestFixtures.ItemWithThreeStages();

        Assert.Throws<InvalidOperationException>(() => item.ApproveStageWholeQuantity(2, 9, T1));
        Assert.Throws<InvalidOperationException>(() => item.SubmitStageForQc(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.StartStage(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.StartStage(0));
        Assert.Empty(item.QcApprovals);
    }

    [Fact]
    public void Approval_requires_a_utc_time()
    {
        var item = RequestFixtures.ItemWithThreeStages();
        item.ReleaseFirstStage();
        item.StartStage(1);
        item.SubmitStageForQc(1);

        Assert.Throws<ArgumentException>(() => item.ApproveStageWholeQuantity(1, 9, new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Local)));
        Assert.Equal(ItemStageStatus.WaitingQc, item.Stages[0].Status);
    }

    [Fact]
    public void Walking_the_whole_route_completes_the_item_and_keeps_every_approval()
    {
        var item = RequestFixtures.ItemWithThreeStages();
        item.ReleaseFirstStage();

        for (var stageNo = 1; stageNo <= 3; stageNo++)
        {
            item.StartStage(stageNo);
            item.SubmitStageForQc(stageNo);
            item.ApproveStageWholeQuantity(stageNo, approvedByUserId: 9, T1.AddMinutes(stageNo));
        }

        Assert.True(item.IsRouteCompleted);
        Assert.All(item.Stages, s => Assert.Equal(ItemStageStatus.QcApproved, s.Status));
        Assert.Equal(new[] { 1, 2, 3 }, item.QcApprovals.Select(a => a.StageNo).ToArray());
    }

    [Fact]
    public void The_domain_does_not_forbid_appending_a_stage_after_release()
    {
        // Controlled route revision after production starts is approved (DS 6.2). The domain therefore keeps no
        // "no changes after release" invariant; the audited revision process and the downstream-stage handling are later work.
        var item = RequestFixtures.ItemWithThreeStages();
        item.ReleaseFirstStage();

        var appended = item.AddStage("Late stage", 1m);

        Assert.Equal(4, item.Stages.Count);
        Assert.Equal(4, appended.StageNo);
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, appended.Status);
        Assert.Equal(ItemStageStatus.Waiting, item.Stages[0].Status);
    }

    [Fact]
    public void A_stage_appended_behind_an_approved_stage_follows_the_qc_gate()
    {
        var item = RequestFixtures.NewRequest().AddMake("1.10", quantity: 2);
        item.AddStage("Turning", 1m);
        item.ReleaseFirstStage();
        item.StartStage(1);
        item.SubmitStageForQc(1);
        item.ApproveStageWholeQuantity(1, 9, T1);
        Assert.True(item.IsRouteCompleted);

        var appended = item.AddStage("Added later", 1m);

        Assert.Equal(ItemStageStatus.Waiting, appended.Status);
        Assert.False(item.IsRouteCompleted);
        Assert.Equal(new[] { 1 }, item.QcApprovals.Select(a => a.StageNo).ToArray());
    }

    [Fact]
    public void A_late_stage_still_cannot_skip_the_qc_gate()
    {
        var item = RequestFixtures.ItemWithThreeStages();
        item.ReleaseFirstStage();
        item.StartStage(1);
        var appended = item.AddStage("Late stage", 1m);

        Assert.Throws<InvalidOperationException>(() => item.StartStage(appended.StageNo));
    }

    [Fact]
    public void Stages_have_no_priority_of_their_own_they_inherit_the_items()
    {
        Assert.Null(typeof(ItemStage).GetProperty("Priority"));
        Assert.NotNull(typeof(RequestItem).GetProperty("Priority"));
    }

    [Fact]
    public void Stage_status_codes_follow_the_specification_names()
    {
        Assert.Equal(
            new[] { "PLANNED", "WAITING_PREVIOUS_QC", "WAITING", "IN_PROGRESS", "WAITING_QC", "QC_APPROVED" },
            Enum.GetValues<ItemStageStatus>().Select(s => s.Code()).ToArray());
    }

    [Fact]
    public void Items_of_one_request_have_independent_routes()
    {
        var request = RequestFixtures.NewRequest();
        var make = request.AddMake("1.10");
        var repair = request.AddRepair("2.10");

        make.AddStage("Turning", 2m);
        make.AddStage("Grinding", 2m);
        repair.AddStage("Repair welding", 3m);
        make.ReleaseFirstStage();

        Assert.Equal(2, make.Stages.Count);
        Assert.Single(repair.Stages);
        Assert.Equal(ItemStageStatus.Waiting, make.Stages[0].Status);
        Assert.Equal(ItemStageStatus.Planned, repair.Stages[0].Status);
    }
}
