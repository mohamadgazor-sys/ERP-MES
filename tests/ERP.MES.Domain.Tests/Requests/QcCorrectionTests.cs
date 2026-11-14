using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Tests.Requests;

// QC correction of a final stage decision (Master Specification 1.10, 15; owner decisions Q1-Q4, Q6, Q7, Decision Log #142): append-only, partial quantities, rework returns as executable work,
// scrap waits for the shortfall decision, downstream stages are marked and never reset, an unstarted assembly parent returns to waiting.
public class QcCorrectionTests
{
    private static readonly DateTime T = RequestFixtures.Now.AddHours(1);

    private static InspectionEntry Accept(long units) => new(InspectionDisposition.Accepted, units, null);

    private static IReadOnlyList<QcCorrectionSource> Sources(long units) => [new QcCorrectionSource(11, units)];

    // 10 base units, three stages; stage 1 is approved with 10 accepted units and stage 2 has been released.
    private static RequestItem AfterFirstApproval()
    {
        var request = RequestFixtures.NewRequest();
        var item = request.DefineItem(0, "1.10", "Ring", 10, new ItemSpecification(1, 1, 1m, null, null, 1), 1, RequestFixtures.Now);
        item.DefineInitialStage("Turning", 5m);
        item.DefineInitialStage("Grinding", 3m);
        item.DefineInitialStage("Finishing", 1m);
        item.ReleaseFirstStage(T);
        item.StartStage(1);
        item.SubmitStageForQc(1);
        item.RecordInspection(1, [Accept(10)], 5, T, "i1");
        return item;
    }

    [Fact]
    public void Only_a_final_QC_approved_stage_can_be_corrected_and_a_reason_and_a_valid_quantity_are_required()
    {
        var item = AfterFirstApproval();
        Assert.Throws<InvalidOperationException>(() => item.CorrectQcDecision(2, 1, 0, "x", Sources(1), false, 5, T)); // stage 2 is not approved
        Assert.Throws<ArgumentException>(() => item.CorrectQcDecision(1, 1, 0, " ", Sources(1), false, 5, T));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.CorrectQcDecision(1, 0, 0, "x", [], false, 5, T));
        Assert.Throws<CorrectionExceedsAcceptedException>(() => item.CorrectQcDecision(1, 8, 3, "x", Sources(11), false, 5, T)); // more than the 10 accepted
        Assert.Throws<ArgumentException>(() => item.CorrectQcDecision(1, 3, 0, "x", Sources(2), false, 5, T)); // sources do not add up
        Assert.Equal(ItemStageStatus.QcApproved, item.Stages[0].Status);
        Assert.Equal(10, item.Stages[0].AcceptedBaseUnits);
    }

    [Fact]
    public void A_partial_correction_to_rework_keeps_the_rest_accepted_and_returns_the_stage_as_executable_work_and_marks_the_downstream_stage()
    {
        var item = AfterFirstApproval();
        var correction = item.CorrectQcDecision(1, 3, 0, "ابعاد نادرست", Sources(3), false, 5, T.AddHours(1));

        var stage = item.Stages[0];
        Assert.Equal((7L, 3L, 0L), (stage.AcceptedBaseUnits, stage.ReworkBaseUnits, stage.ScrapBaseUnits));
        Assert.Equal(ItemStageStatus.InProgress, stage.Status);   // executable again; it has not been reset
        Assert.False(item.IsRouteCompleted);
        Assert.Equal((10L, 7L, 3L, 3L), (correction.AcceptedBeforeBaseUnits, correction.AcceptedAfterBaseUnits, correction.CorrectedBaseUnits, correction.ReworkBaseUnits));
        Assert.Same(correction, stage.AffectedByCorrection);
        Assert.Same(correction, item.Stages[1].AffectedByCorrection);   // stage 2 had been released: marked, not reset
        Assert.Equal(ItemStageStatus.Waiting, item.Stages[1].Status);
        Assert.Null(item.Stages[2].AffectedByCorrection);               // stage 3 never progressed
        Assert.True(item.HasQcCorrection);
        Assert.Single(item.QcApprovals); // the original approval is history and is kept

        // The corrected stage re-enters the normal loop: the reworked units are submitted and QC accepts them; the stage is approved again with all 10 units.
        item.SubmitStageForQc(1);
        Assert.Equal(3, item.Stages[0].AwaitingQcBaseUnits);
        item.RecordInspection(1, [Accept(3)], 5, T.AddHours(2), "i2");
        Assert.Equal((ItemStageStatus.QcApproved, 10L), (item.Stages[0].Status, item.Stages[0].AcceptedBaseUnits));
        Assert.Equal(2, item.QcApprovals.Count);
        Assert.Equal(10, item.Stages[1].InputBaseUnits);
    }

    [Fact]
    public void The_sequential_gate_keeps_a_downstream_stage_from_proceeding_until_the_corrected_stage_is_approved_again()
    {
        var item = AfterFirstApproval();
        item.CorrectQcDecision(1, 2, 0, "ابعاد", Sources(2), false, 5, T.AddHours(1));
        Assert.Throws<InvalidOperationException>(() => item.StartStage(2));
    }

    [Fact]
    public void Scrap_alone_waits_for_the_shortfall_decision_and_the_unstarted_next_stage_then_receives_only_the_valid_quantity()
    {
        var item = AfterFirstApproval();
        item.CorrectQcDecision(1, 0, 4, "ترک", Sources(4), false, 5, T.AddHours(1));
        Assert.Equal((ItemStageStatus.WaitingShortfallDecision, 6L, 4L), (item.Stages[0].Status, item.Stages[0].AcceptedBaseUnits, item.Stages[0].ScrapBaseUnits));

        var decision = item.DecideShortfall(1, ShortfallKind.ManagementAccepted, "پذیرش کسری", 7, T.AddHours(2));
        Assert.Equal(4, decision.ShortfallBaseUnits);
        Assert.Equal(ItemStageStatus.QcApproved, item.Stages[0].Status);
        Assert.Equal(6, item.Stages[1].InputBaseUnits);                 // the scrapped units never arrive
        Assert.Equal(ItemStageStatus.Waiting, item.Stages[1].Status);   // and the stage was not reset
    }

    [Fact]
    public void Scrap_and_rework_in_one_correction_are_never_counted_twice()
    {
        var item = AfterFirstApproval();
        item.CorrectQcDecision(1, 2, 3, "ترکیبی", Sources(5), false, 5, T.AddHours(1));
        var stage = item.Stages[0];
        Assert.Equal((5L, 2L, 3L), (stage.AcceptedBaseUnits, stage.ReworkBaseUnits, stage.ScrapBaseUnits));
        Assert.Equal(10, stage.AcceptedBaseUnits + stage.ReworkBaseUnits + stage.ScrapBaseUnits);
        Assert.Equal(ItemStageStatus.InProgress, stage.Status);
        // The stage is no longer final (QC_APPROVED) until it is approved again, so a second correction is refused meanwhile.
        Assert.Throws<InvalidOperationException>(() => item.CorrectQcDecision(1, 6, 0, "x", Sources(6), false, 5, T.AddHours(2)));
    }

    [Fact]
    public void An_unstarted_assembly_parent_returns_to_waiting_and_a_parent_in_work_is_only_marked()
    {
        var request = RequestFixtures.NewRequest();
        var parent = request.AddItem(0, "A.00", "Assembly", 1, 1, RequestFixtures.Now, Priority.Normal, null, isAssembly: true);
        parent.DefineInitialStage("Assemble", 2m);
        parent.ReleaseFirstStage(T);
        var correction = AfterFirstApproval().CorrectQcDecision(1, 1, 0, "x", Sources(1), false, 5, T.AddHours(1));

        Assert.True(parent.ApplyChildQcCorrection(correction, firstStageHasWorkSessions: false));
        Assert.Equal((ItemStageStatus.Planned, null), (parent.Stages[0].Status, parent.Stages[0].InputBaseUnits));
        Assert.Same(correction, parent.Stages[0].AffectedByCorrection);

        // The parent that has work sessions is left exactly as it is, only marked.
        var busy = request.AddItem(0, "B.00", "Assembly", 1, 1, RequestFixtures.Now, Priority.Normal, null, isAssembly: true);
        busy.DefineInitialStage("Assemble", 2m);
        busy.ReleaseFirstStage(T);
        Assert.False(busy.ApplyChildQcCorrection(correction, firstStageHasWorkSessions: true));
        Assert.Equal(ItemStageStatus.Waiting, busy.Stages[0].Status);
        Assert.Same(correction, busy.Stages[0].AffectedByCorrection);
    }
}
