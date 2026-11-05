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
}
