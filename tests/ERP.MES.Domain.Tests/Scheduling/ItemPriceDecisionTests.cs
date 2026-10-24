using ERP.MES.Domain.Costing;

namespace ERP.MES.Domain.Tests.Scheduling;

// The snapshot invariants and the price decision rules (docs/COSTING_DESIGN.md; Decision Log #95 D4, #104).
public class ItemPriceDecisionTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);

    private static ItemCostCalculation Calculation(long accepted = 10, long consumed = 10) =>
        new(1, 1, accepted, consumed, 7, 1, new DateOnly(2026, 10, 1),
            [new ItemCostLine(1, CostLineKind.Machining, 5, 3, 1, new DateOnly(2026, 10, 10), 240, null, 1_200_000m, 4_800_000m), new ItemCostLine(2, CostLineKind.Material, null, null, 1, new DateOnly(2026, 10, 1), null, 10m, 90_000m, 900_000m)],
            9, Now);

    [Fact]
    public void A_snapshot_sums_the_rounded_lines_and_derives_the_unit_cost_over_the_accepted_units()
    {
        var calc = Calculation(accepted: 8, consumed: 10);
        Assert.Equal((4_800_000m, 900_000m, 5_700_000m, 712_500m), (calc.MachiningTotalRial, calc.MaterialTotalRial, calc.TotalRial, calc.UnitCostRial));
        Assert.Equal("v1", calc.Formula);
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculation(accepted: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calculation(accepted: 11, consumed: 10));
        Assert.Throws<ArgumentException>(() => new ItemCostCalculation(1, 1, 1, 1, null, null, null, [], 9, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemCostLine(1, CostLineKind.Material, null, null, null, null, null, 1m, 1m, 10.5m));
    }

    [Fact]
    public void A_calculated_price_is_submitted_then_approved_as_proposed_or_changed_with_a_reason()
    {
        var decision = new ItemPriceDecision(Calculation(), PriceBasis.Calculated);
        Assert.Equal((PriceDecisionStatus.Draft, 5_700_000m), (decision.Status, decision.ProposedPriceRial));
        Assert.Throws<InvalidOperationException>(() => decision.Approve(null, null, null, 2, Now));

        decision.Submit(1, Now);
        Assert.Throws<InvalidOperationException>(() => decision.Submit(1, Now));
        Assert.Throws<ArgumentException>(() => decision.Approve(6_000_000m, null, null, 2, Now)); // a changed price needs a reason
        Assert.Throws<ArgumentOutOfRangeException>(() => decision.Approve(0m, "x", null, 2, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => decision.Approve(100.5m, "x", null, 2, Now));
        Assert.Equal(PriceDecisionStatus.Submitted, decision.Status);

        decision.Approve(null, null, null, 2, Now);
        Assert.Equal((PriceDecisionStatus.Approved, 5_700_000m, (long?)2), (decision.Status, decision.ApprovedPriceRial, decision.ApprovedByUserId));
        Assert.Throws<InvalidOperationException>(() => decision.Approve(null, null, null, 2, Now));

        var changed = new ItemPriceDecision(Calculation(), PriceBasis.Calculated);
        changed.Submit(1, Now);
        changed.Approve(6_000_000m, " حاشیه سود ", null, 2, Now);
        Assert.Equal((5_700_000m, 6_000_000m, "حاشیه سود"), (changed.ProposedPriceRial, changed.ApprovedPriceRial, changed.Reason));
    }

    [Fact]
    public void A_contract_price_is_never_filled_from_the_calculation_it_needs_an_amount_and_a_reference()
    {
        var decision = new ItemPriceDecision(Calculation(), PriceBasis.Contract);
        Assert.Null(decision.ProposedPriceRial);
        decision.Submit(1, Now);
        Assert.Throws<ArgumentException>(() => decision.Approve(null, null, "CN-1", 2, Now));
        Assert.Throws<ArgumentException>(() => decision.Approve(8_000_000m, null, null, 2, Now));
        Assert.Throws<ArgumentException>(() => decision.Approve(8_000_000m, null, new string('x', 101), 2, Now));

        decision.Approve(8_000_000m, null, " CN-1 ", 2, Now);
        Assert.Equal((8_000_000m, "CN-1"), (decision.ApprovedPriceRial, decision.ContractReference));
    }
}
