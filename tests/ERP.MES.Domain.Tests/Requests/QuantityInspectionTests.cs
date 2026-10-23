using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Tests.Requests;

// QC at the level of base units: accepted / rework / scrap, the rework loop, the shortfall decision and the quantity carried to the next stage (Decision Log #92, #95 D2/D3, #101).
public class QuantityInspectionTests
{
    private static readonly DateTime T1 = RequestFixtures.Now.AddHours(1);
    private static readonly DateTime T2 = RequestFixtures.Now.AddHours(2);

    // 5 pairs = 10 base units, three stages.
    private static RequestItem Pairs(int quantity = 5)
    {
        var request = RequestFixtures.NewRequest();
        var item = request.DefineItem(0, "1.10", "Ring", quantity, new ItemSpecification(1, 1, 1m, null, null, 2), 1, RequestFixtures.Now);
        item.DefineInitialStage("Turning", 5m);
        item.DefineInitialStage("Grinding", 3m);
        item.DefineInitialStage("Inspection prep", 1.5m);
        return item;
    }

    private static RequestItem SubmittedFirstStage(int quantity = 5)
    {
        var item = Pairs(quantity);
        item.ReleaseFirstStage(T1);
        item.StartStage(1);
        item.SubmitStageForQc(1);
        return item;
    }

    private static InspectionEntry Accept(long units) => new(InspectionDisposition.Accepted, units, null);

    private static InspectionEntry Rework(long units, string reason = "ابعاد") => new(InspectionDisposition.Rework, units, reason);

    private static InspectionEntry Scrap(long units, string reason = "ترک") => new(InspectionDisposition.Scrap, units, reason);

    [Fact]
    public void A_stage_works_on_base_units_from_the_quantity_type_and_the_first_release_sets_its_input()
    {
        var item = Pairs();
        Assert.Equal(2, item.BaseUnitsPerUnit);
        Assert.Null(item.Stages[0].InputBaseUnits);

        item.ReleaseFirstStage(T1);
        Assert.Equal(10, item.Stages[0].InputBaseUnits);
        item.StartStage(1);
        item.SubmitStageForQc(1);
        Assert.Equal(10, item.Stages[0].AwaitingQcBaseUnits);
        Assert.Equal(0, item.Stages[0].RemainingBaseUnits);

        // A fractional number of base units cannot be inspected by quantity.
        var odd = RequestFixtures.NewRequest().DefineItem(0, "2", "Odd", 2.5m, new ItemSpecification(1, 1, 1m, null, null, 1), 1, RequestFixtures.Now);
        odd.DefineInitialStage("Turning", 1m);
        odd.ReleaseFirstStage(T1);
        Assert.Null(odd.Stages[0].InputBaseUnits);
    }

    [Fact]
    public void Accepting_everything_completes_the_stage_and_gives_the_next_stage_exactly_the_accepted_base_units()
    {
        var item = SubmittedFirstStage();

        var outcome = item.RecordInspection(1, [Accept(10)], 7, T2, "i1");

        Assert.True(outcome.Completed);
        Assert.Equal(ItemStageStatus.QcApproved, item.Stages[0].Status);
        Assert.Equal(10, item.Stages[0].AcceptedBaseUnits);
        Assert.Equal(ItemStageStatus.Waiting, item.Stages[1].Status);
        Assert.Equal(10, item.Stages[1].InputBaseUnits);
        Assert.Equal(T2, item.Stages[1].AvailableSinceUtc);
        Assert.Equal(5m, Assert.Single(item.QcApprovals).ApprovedQuantity); // ordered units: 10 base units = 5 pairs
        var line = Assert.Single(outcome.Lines);
        Assert.Equal((InspectionDisposition.Accepted, 10L, 7L, "i1"), (line.Disposition, line.BaseUnits, line.DecidedByUserId, line.InspectionId));
    }

    [Fact]
    public void Partial_decisions_keep_the_stage_waiting_and_never_count_more_than_was_submitted()
    {
        var item = SubmittedFirstStage();

        var first = item.RecordInspection(1, [Accept(4)], 7, T2, "i1");
        Assert.False(first.Completed);
        Assert.Equal(ItemStageStatus.WaitingQc, item.Stages[0].Status);
        Assert.Equal(6, item.Stages[0].AwaitingQcBaseUnits);
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, item.Stages[1].Status);

        Assert.Throws<InspectionExceedsAwaitingException>(() => item.RecordInspection(1, [Accept(7)], 7, T2, "i2"));
        Assert.Throws<InspectionExceedsAwaitingException>(() => item.RecordInspection(1, [Accept(3), Scrap(4)], 7, T2, "i2"));
        Assert.Equal(6, item.Stages[0].AwaitingQcBaseUnits);
        Assert.Equal(4, item.Stages[0].AcceptedBaseUnits);

        item.RecordInspection(1, [Accept(6)], 7, T2.AddMinutes(5), "i3");
        Assert.Equal(ItemStageStatus.QcApproved, item.Stages[0].Status);
        Assert.Equal(10, item.Stages[1].InputBaseUnits);
    }

    [Fact]
    public void Decisions_are_validated_reasons_are_mandatory_for_rework_and_scrap()
    {
        var item = SubmittedFirstStage();

        Assert.Throws<ArgumentException>(() => item.RecordInspection(1, [], 7, T2, "i"));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.RecordInspection(1, [Accept(0)], 7, T2, "i"));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.RecordInspection(1, [Accept(-1)], 7, T2, "i"));
        Assert.Throws<ArgumentException>(() => item.RecordInspection(1, [new InspectionEntry(InspectionDisposition.Rework, 1, " ")], 7, T2, "i"));
        Assert.Throws<ArgumentException>(() => item.RecordInspection(1, [new InspectionEntry(InspectionDisposition.Scrap, 1, null)], 7, T2, "i"));
        Assert.Throws<ArgumentException>(() => item.RecordInspection(1, [Scrap(1, new string('x', QcInspectionLine.MaxReasonLength + 1))], 7, T2, "i"));
        Assert.Equal(10, item.Stages[0].AwaitingQcBaseUnits);

        // A stage that is not waiting for QC, or whose previous stage is not approved, cannot be inspected.
        Assert.Throws<InvalidOperationException>(() => item.RecordInspection(2, [Accept(1)], 7, T2, "i"));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.RecordInspection(9, [Accept(1)], 7, T2, "i"));
    }

    [Fact]
    public void Rework_returns_the_stage_to_production_and_the_reworked_units_come_back_once_without_double_counting()
    {
        var item = SubmittedFirstStage();

        var outcome = item.RecordInspection(1, [Accept(7), Rework(3)], 7, T2, "i1");

        Assert.False(outcome.Completed);
        Assert.Equal(ItemStageStatus.InProgress, item.Stages[0].Status);
        Assert.Equal(3, item.Stages[0].ReworkBaseUnits);
        Assert.Equal(3, item.Stages[0].RemainingBaseUnits);
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, item.Stages[1].Status); // next stage waits: only completed stages release it

        item.SubmitStageForQc(1);
        Assert.Equal(3, item.Stages[0].AwaitingQcBaseUnits);
        Assert.Equal(0, item.Stages[0].ReworkBaseUnits);
        Assert.Throws<InvalidOperationException>(() => item.SubmitStageForQc(1)); // not in progress any more

        item.RecordInspection(1, [Accept(3)], 7, T2.AddHours(1), "i2");
        Assert.Equal(ItemStageStatus.QcApproved, item.Stages[0].Status);
        Assert.Equal(10, item.Stages[0].AcceptedBaseUnits);
        Assert.Equal(10, item.Stages[1].InputBaseUnits);
        // Invariant: accepted + scrap + awaiting + remaining = input.
        Assert.Equal(10, item.Stages[0].AcceptedBaseUnits + item.Stages[0].ScrapBaseUnits + item.Stages[0].AwaitingQcBaseUnits + item.Stages[0].RemainingBaseUnits);
    }

    [Fact]
    public void Reworked_units_that_fail_again_can_be_scrapped_and_the_total_always_reconciles()
    {
        var item = SubmittedFirstStage();
        item.RecordInspection(1, [Accept(6), Rework(4)], 7, T2, "i1");
        item.SubmitStageForQc(1);

        var outcome = item.RecordInspection(1, [Accept(2), Scrap(2)], 7, T2.AddHours(1), "i2");

        Assert.Equal(ItemStageStatus.WaitingShortfallDecision, outcome.NewStatus);
        var stage = item.Stages[0];
        Assert.Equal((8L, 2L, 0L, 0L), (stage.AcceptedBaseUnits, stage.ScrapBaseUnits, stage.AwaitingQcBaseUnits, stage.RemainingBaseUnits));
        Assert.Equal(stage.InputBaseUnits, stage.AcceptedBaseUnits + stage.ScrapBaseUnits);
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, item.Stages[1].Status);
        Assert.Empty(item.QcApprovals);
    }

    [Fact]
    public void Scrap_creates_a_shortfall_that_only_management_acceptance_or_a_recorded_replacement_order_closes()
    {
        var item = SubmittedFirstStage();
        item.RecordInspection(1, [Accept(8), Scrap(2)], 7, T2, "i1");
        Assert.Equal(ItemStageStatus.WaitingShortfallDecision, item.Stages[0].Status);
        Assert.Throws<InvalidOperationException>(() => item.StartStage(2));

        Assert.Throws<ArgumentException>(() => item.DecideShortfall(1, ShortfallKind.ManagementAccepted, " ", 3, T2));
        Assert.Throws<ArgumentOutOfRangeException>(() => item.DecideShortfall(1, (ShortfallKind)99, "x", 3, T2));
        Assert.Equal(ItemStageStatus.WaitingShortfallDecision, item.Stages[0].Status);

        var decision = item.DecideShortfall(1, ShortfallKind.ReplacementOrdered, "  سفارش جایگزین 1.10-R  ", 3, T2.AddHours(1));

        Assert.Equal((ShortfallKind.ReplacementOrdered, 2L, "سفارش جایگزین 1.10-R"), (decision.Kind, decision.ShortfallBaseUnits, decision.Note));
        Assert.Equal(ItemStageStatus.QcApproved, item.Stages[0].Status);
        Assert.Equal(8, item.Stages[1].InputBaseUnits);
        Assert.Equal(ItemStageStatus.Waiting, item.Stages[1].Status);
        Assert.Equal(4m, Assert.Single(item.QcApprovals).ApprovedQuantity); // 8 base units = 4 pairs
        Assert.Throws<InvalidOperationException>(() => item.DecideShortfall(1, ShortfallKind.ManagementAccepted, "again", 3, T2));
    }

    [Fact]
    public void When_every_unit_is_scrapped_the_next_stage_never_becomes_available()
    {
        var item = SubmittedFirstStage();
        item.RecordInspection(1, [Scrap(10)], 7, T2, "i1");
        item.DecideShortfall(1, ShortfallKind.ManagementAccepted, "پذیرش کسری", 3, T2.AddHours(1));

        Assert.Equal(ItemStageStatus.QcApproved, item.Stages[0].Status);
        Assert.Equal(0, item.Stages[1].InputBaseUnits);
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, item.Stages[1].Status);
    }

    [Fact]
    public void The_demonstration_whole_quantity_approval_still_works_and_refuses_a_stage_with_scrap_or_rework()
    {
        var item = SubmittedFirstStage();
        item.ApproveStageWholeQuantity(1, 7, T2);
        Assert.Equal(ItemStageStatus.QcApproved, item.Stages[0].Status);
        Assert.Equal(10, item.Stages[0].AcceptedBaseUnits);
        Assert.Equal(10, item.Stages[1].InputBaseUnits);

        var reworked = SubmittedFirstStage();
        reworked.RecordInspection(1, [Accept(5), Rework(1), Scrap(1)], 7, T2, "i1");
        Assert.Throws<InvalidOperationException>(() => reworked.ApproveStageWholeQuantity(1, 7, T2));
    }
}
