using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Tests.Requests;

// External supply / processing stages (Master Specification 10.2, 10.4, 16; Decision Log #129): definition, release and the QC gate. Dispatches and returns follow in the next step.
public class ExternalStageTests
{
    private static readonly DateTime T1 = RequestFixtures.Now.AddHours(1);
    private static readonly DateTime T2 = RequestFixtures.Now.AddHours(2);

    // 5 pairs = 10 base units.
    private static RequestItem Item(decimal quantity = 5)
    {
        var request = RequestFixtures.NewRequest();
        return request.DefineItem(0, "1.10", "Ring", quantity, new ItemSpecification(1, 1, 1m, null, null, 2), 1, RequestFixtures.Now);
    }

    [Fact]
    public void An_external_stage_has_a_lead_time_in_calendar_days_no_machine_and_whole_base_units()
    {
        var item = Item();
        var stage = item.DefineInitialStage("Plating", 0m, null, 7, null, 14);
        Assert.True(stage.IsExternal);
        Assert.Equal(14, stage.ExpectedLeadTimeDays);
        Assert.Equal(0m, stage.PlannedMinutesPerPiece);
        Assert.False(item.DefineInitialStage("Turning", 2m, null, 3).IsExternal);

        Assert.Throws<ArgumentException>(() => Item().DefineInitialStage("Plating", 0m, null, 7, null, 0));
        Assert.Throws<ArgumentException>(() => Item().DefineInitialStage("Plating", 0m, null, 7, null, 366));
        Assert.Throws<ArgumentException>(() => Item().DefineInitialStage("Plating", 0m, null, 7, 4, 10)); // a machine
        Assert.Throws<ArgumentException>(() => Item().DefineInitialStage("Plating", 0m, null, null, null, 10)); // no unit
        Assert.Throws<ArgumentOutOfRangeException>(() => Item().DefineInitialStage("Plating", -1m, null, 7, null, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => Item().DefineInitialStage("Turning", 0m, null, 3)); // an internal stage still needs minutes
        Assert.Throws<ArgumentException>(() => Item().DefineInitialStage("Plating", 0m, 2.25m, 7, null, 10)); // 4.5 base units
        Assert.Empty(Item().Stages);
    }

    [Fact]
    public void Releasing_an_external_first_stage_waits_for_the_warehouse_and_never_enters_a_queue()
    {
        var item = Item();
        item.DefineInitialStage("Casting supply", 0m, null, 7, null, 30);
        item.DefineInitialStage("Turning", 2m, null, 3);

        item.ReleaseFirstStage(T1);
        Assert.Equal(ItemStageStatus.WaitingExternalSend, item.Stages[0].Status);
        Assert.Equal("WAITING_EXTERNAL_SEND", item.Stages[0].Status.Code());
        Assert.Equal(10, item.Stages[0].InputBaseUnits);
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, item.Stages[1].Status);

        // It is not an internal production stage: it cannot be started, submitted or stopped.
        Assert.Throws<InvalidOperationException>(() => item.StartStage(1));
        Assert.Throws<InvalidOperationException>(() => item.SubmitStageForQc(1));
        Assert.Throws<InvalidOperationException>(() => item.StopStage(1, "x", 1, T1));
        Assert.Equal("EXTERNAL_SENT", ItemStageStatus.ExternalSent.Code());
    }

    [Fact]
    public void After_QC_approves_the_previous_stage_a_later_external_stage_waits_for_the_send()
    {
        var item = Item();
        item.DefineInitialStage("Turning", 2m, null, 3);
        item.DefineInitialStage("Hardening", 0m, null, 7, null, 5);
        item.ReleaseFirstStage(T1);
        item.StartStage(1);
        item.SubmitStageForQc(1);
        item.RecordInspection(1, [new InspectionEntry(InspectionDisposition.Accepted, 8, null), new InspectionEntry(InspectionDisposition.Scrap, 2, "ترک")], 9, T2, "i-1");
        item.DecideShortfall(1, ShortfallKind.ManagementAccepted, "قبول", 10, T2);

        Assert.Equal(ItemStageStatus.WaitingExternalSend, item.Stages[1].Status);
        Assert.Equal(8, item.Stages[1].InputBaseUnits);
        Assert.Equal(T2, item.Stages[1].AvailableSinceUtc);
    }

    [Fact]
    public void A_unit_category_is_one_of_the_nine_access_codes_and_decides_whether_the_unit_is_external()
    {
        Assert.Equal(9, ProductionUnitCategory.All.Count);
        Assert.Equal(
            ["INTERNAL_PRODUCTION", "EXTERNAL_PLATING", "EXTERNAL_HEAT_TREATMENT", "EXTERNAL_CASTING", "EXTERNAL_CERAMIC", "EXTERNAL_GRINDING", "EXTERNAL_GEAR_CUTTING", "EXTERNAL_TEHRAN", "EXTERNAL_OTHER"],
            ProductionUnitCategory.All);
        var unit = new ProductionUnit(1, "MILLING", "فرز", 1, RequestFixtures.Now);
        Assert.Equal(ProductionUnitCategory.InternalProduction, unit.Category);
        Assert.False(unit.IsExternal);
        unit.SetCategory(ProductionUnitCategory.ExternalPlating, T1);
        Assert.True(unit.IsExternal);
        Assert.Throws<ArgumentException>(() => unit.SetCategory("EXTERNAL_PAINTING", T1));
        Assert.Throws<ArgumentException>(() => unit.SetCategory("external_plating", T1));
        Assert.Throws<ArgumentException>(() => new ProductionUnit(2, "X", "x", 1, RequestFixtures.Now, "OTHER"));
    }

    private static long _ids;

    // Shipments get their identity from the database; the tests give them one so that returns can name their dispatch.
    private static ExternalShipment Saved(ExternalShipment shipment)
    {
        typeof(ExternalShipment).GetProperty(nameof(ExternalShipment.Id))!.SetValue(shipment, Interlocked.Increment(ref _ids));
        return shipment;
    }

    private static RequestItem Released()
    {
        var item = Item();
        item.DefineInitialStage("Plating", 0m, null, 7, null, 14);
        item.DefineInitialStage("Turning", 2m, null, 3);
        item.ReleaseFirstStage(T1);
        return item;
    }

    [Fact]
    public void Partial_dispatches_and_returns_are_tracked_and_a_return_never_exceeds_its_dispatch()
    {
        var item = Released();
        var stage = item.Stages[0];
        Assert.Equal(10, stage.StillToSendBaseUnits);

        Assert.Throws<ExternalQuantityException>(() => item.DispatchExternal(1, 11, null, null, 5, T1));
        Assert.Throws<ExternalQuantityException>(() => item.DispatchExternal(1, 0, null, null, 5, T1));
        var first = Saved(item.DispatchExternal(1, 6, "6 جفت رینگ", null, 5, T1));
        Assert.Equal((ExternalShipmentKind.Dispatch, 6L, 5L, T1), (first.Kind, first.BaseUnits, first.RecordedByUserId, first.RecordedAtUtc));
        Assert.Equal((ItemStageStatus.ExternalSent, 6L, 4L), (stage.Status, stage.AtContractorBaseUnits, stage.StillToSendBaseUnits!.Value));
        var second = Saved(item.DispatchExternal(1, 4, null, null, 5, T1));
        Assert.Equal((10L, 0L), (stage.ExternalDispatchedBaseUnits, stage.StillToSendBaseUnits!.Value));
        Assert.Throws<ExternalQuantityException>(() => item.DispatchExternal(1, 1, null, null, 5, T1));

        // Returns: per dispatch, never more than it still has outstanding.
        Assert.Throws<ExternalQuantityException>(() => item.ReturnExternal(1, second, [], 5, null, 6, T2));
        var back = Saved(item.ReturnExternal(1, first, [], 4, "برگشت اول", 6, T2));
        Assert.Equal((ExternalShipmentKind.Return, first.Id), (back.Kind, back.DispatchId!.Value));
        Assert.Throws<ExternalQuantityException>(() => item.ReturnExternal(1, first, [back], 3, null, 6, T2));
        Assert.Equal((ItemStageStatus.WaitingQc, 4L, 6L, 0L), (stage.Status, stage.AwaitingQcBaseUnits, stage.AtContractorBaseUnits, stage.StillToSendBaseUnits!.Value));
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, item.Stages[1].Status);
    }

    [Fact]
    public void QC_decides_what_came_back_while_the_rest_is_away_and_the_stage_completes_only_when_everything_is_back_and_decided()
    {
        var item = Released();
        var stage = item.Stages[0];
        var a = Saved(item.DispatchExternal(1, 6, null, null, 5, T1));
        var b = Saved(item.DispatchExternal(1, 4, null, null, 5, T1));
        var backA = Saved(item.ReturnExternal(1, a, [], 6, null, 6, T2));

        // QC accepts 5 and sends 1 to rework; 4 are still at the contractor, so the stage is not complete.
        var outcome = item.RecordInspection(1, [new InspectionEntry(InspectionDisposition.Accepted, 5, null), new InspectionEntry(InspectionDisposition.Rework, 1, "پوشش ناقص")], 9, T2, "q-1");
        Assert.False(outcome.Completed);
        Assert.Equal((ItemStageStatus.ExternalSent, 1L, 4L, 1L), (stage.Status, stage.ReworkBaseUnits, stage.AtContractorBaseUnits, stage.StillToSendBaseUnits!.Value));

        // A decided return cannot be cancelled any more; the rework unit is sent again.
        Assert.Throws<ExternalQuantityException>(() => item.CancelExternalReturn(1, backA, "اشتباه", 6, T2));
        var again = Saved(item.DispatchExternal(1, 1, null, "بازکاری", 5, T2));
        Assert.Equal(0, stage.ReworkBaseUnits);

        var backB = Saved(item.ReturnExternal(1, b, [], 4, null, 6, T2));
        item.RecordInspection(1, [new InspectionEntry(InspectionDisposition.Accepted, 4, null)], 9, T2, "q-2");
        Assert.Equal(ItemStageStatus.ExternalSent, stage.Status); // the reworked unit is still away
        item.ReturnExternal(1, again, [], 1, null, 6, T2);
        var last = item.RecordInspection(1, [new InspectionEntry(InspectionDisposition.Accepted, 1, null)], 9, T2, "q-3");
        Assert.True(last.Completed);
        Assert.Equal(ItemStageStatus.QcApproved, stage.Status);
        Assert.Equal((ItemStageStatus.Waiting, 10L), (item.Stages[1].Status, item.Stages[1].InputBaseUnits!.Value));
        Assert.NotNull(backB);
    }

    [Fact]
    public void Scrap_of_returned_units_leads_to_the_existing_shortfall_decision()
    {
        var item = Released();
        var all = Saved(item.DispatchExternal(1, 10, null, null, 5, T1));
        item.ReturnExternal(1, all, [], 10, null, 6, T2);
        item.RecordInspection(1, [new InspectionEntry(InspectionDisposition.Accepted, 8, null), new InspectionEntry(InspectionDisposition.Scrap, 2, "سوختگی")], 9, T2, "q-1");
        Assert.Equal(ItemStageStatus.WaitingShortfallDecision, item.Stages[0].Status);
        item.DecideShortfall(1, ShortfallKind.ManagementAccepted, "قبول کسری", 10, T2);
        Assert.Equal((ItemStageStatus.Waiting, 8L), (item.Stages[1].Status, item.Stages[1].InputBaseUnits!.Value));
    }

    [Fact]
    public void Cancellations_need_a_reason_keep_the_row_and_restore_the_quantities()
    {
        var item = Released();
        var stage = item.Stages[0];
        var dispatch = Saved(item.DispatchExternal(1, 10, null, null, 5, T1));
        var back = Saved(item.ReturnExternal(1, dispatch, [], 3, null, 6, T2));

        Assert.Throws<ExternalQuantityException>(() => item.CancelExternalDispatch(1, dispatch, [back], "اشتباه", 6, T2)); // has a return
        Assert.Throws<ArgumentException>(() => item.CancelExternalReturn(1, back, " ", 6, T2));
        item.CancelExternalReturn(1, back, "تعداد اشتباه ثبت شد", 6, T2);
        Assert.False(back.IsActive);
        Assert.Equal(("تعداد اشتباه ثبت شد", 6L), (back.CancelReason, back.CancelledByUserId!.Value));
        Assert.Equal((ItemStageStatus.ExternalSent, 0L, 10L), (stage.Status, stage.AwaitingQcBaseUnits, stage.AtContractorBaseUnits));
        Assert.Throws<InvalidOperationException>(() => item.CancelExternalReturn(1, back, "دوباره", 6, T2));

        item.CancelExternalDispatch(1, dispatch, [back], "ارسال نشد", 6, T2);
        Assert.Equal((ItemStageStatus.WaitingExternalSend, 0L, 10L), (stage.Status, stage.ExternalDispatchedBaseUnits, stage.StillToSendBaseUnits!.Value));

        // Internal stages and stages not yet executable take no shipments.
        Assert.Throws<InvalidOperationException>(() => item.DispatchExternal(2, 1, null, null, 5, T2));
        var waiting = Item();
        waiting.DefineInitialStage("Plating", 0m, null, 7, null, 14);
        Assert.Throws<InvalidOperationException>(() => waiting.DispatchExternal(1, 1, null, null, 5, T2));
    }
}
