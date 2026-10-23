using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Tests.Requests;

// Planning position, availability time and stop/resume of a stage (Decision Log #78, #99).
public class StageQueueStateTests
{
    private static readonly DateTime T1 = RequestFixtures.Now.AddHours(1);
    private static readonly DateTime T2 = RequestFixtures.Now.AddHours(2);

    [Fact]
    public void Releasing_and_approving_record_when_the_stage_became_available()
    {
        var item = RequestFixtures.ItemWithThreeStages();
        Assert.All(item.Stages, s => Assert.Null(s.AvailableSinceUtc));

        item.ReleaseFirstStage(T1);
        Assert.Equal(T1, item.Stages[0].AvailableSinceUtc);
        item.StartStage(1);
        item.SubmitStageForQc(1);
        item.ApproveStageWholeQuantity(1, 7, T2);

        Assert.Equal(T2, item.Stages[1].AvailableSinceUtc);
        Assert.Null(item.Stages[2].AvailableSinceUtc);
    }

    [Fact]
    public void A_planning_position_is_for_released_stages_only_is_bounded_and_cleared_when_the_stage_leaves_the_queue()
    {
        var item = RequestFixtures.ItemWithThreeStages();
        Assert.Throws<InvalidOperationException>(() => item.SetPlanningPosition(1, 1));

        item.ReleaseFirstStage(T1);
        Assert.True(item.SetPlanningPosition(1, 3));
        Assert.False(item.SetPlanningPosition(1, 3));
        Assert.Equal(3, item.Stages[0].PlanningPosition);
        Assert.Throws<ArgumentOutOfRangeException>(() => item.SetPlanningPosition(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.SetPlanningPosition(1, RequestItem.MaxPlanningPosition + 1));
        Assert.True(item.SetPlanningPosition(1, null));

        item.SetPlanningPosition(1, 2);
        item.StartStage(1);
        item.SubmitStageForQc(1);
        Assert.Null(item.Stages[0].PlanningPosition);
    }

    [Fact]
    public void A_stopped_stage_cannot_start_submit_or_be_approved_until_resumed_and_a_reason_is_required()
    {
        var item = RequestFixtures.ItemWithThreeStages();
        item.ReleaseFirstStage(T1);
        Assert.Throws<ArgumentException>(() => item.StopStage(1, "  ", 5, T1));
        Assert.Throws<ArgumentException>(() => item.StopStage(1, new string('x', RequestItem.MaxStopReasonLength + 1), 5, T1));

        item.StopStage(1, "  machine broke ", 5, T1);
        var stage = item.Stages[0];
        Assert.True(stage.IsStopped);
        Assert.Equal("machine broke", stage.StopReason);
        Assert.Equal(5, stage.StoppedByUserId);
        Assert.Equal(ItemStageStatus.Waiting, stage.Status);
        Assert.Throws<InvalidOperationException>(() => item.StopStage(1, "again", 5, T2));
        Assert.Throws<InvalidOperationException>(() => item.StartStage(1));

        item.ResumeStage(1);
        Assert.False(stage.IsStopped);
        Assert.Null(stage.StopReason);
        Assert.Throws<InvalidOperationException>(() => item.ResumeStage(1));

        item.StartStage(1);
        item.StopStage(1, "tool", 5, T2);
        Assert.Throws<InvalidOperationException>(() => item.SubmitStageForQc(1));
        item.ResumeStage(1);
        item.SubmitStageForQc(1);
        Assert.Throws<InvalidOperationException>(() => item.StopStage(1, "late", 5, T2));
        Assert.Throws<InvalidOperationException>(() => item.StopStage(2, "not released", 5, T2));
    }
}
