using ERP.MES.Application.Costing;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Materials;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Tests;

// The v1 cost formula (docs/COSTING_DESIGN.md; Decision Log #91, #95 D3/D4, #104) over fictional numbers, without a database.
public class CostCalculatorTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Func<DateTime, DateOnly> LocalDate = DateOnly.FromDateTime;

    private static MachineHourlyRate Rate(long id, long machine, decimal rate, DateOnly from) => new(id, machine, rate, from, null, 1, Now);

    private static MaterialPrice Price(long id, decimal price, DateOnly from) => new(id, 7, price, from, null, 1, Now);

    private static CostInputs Inputs(
        IReadOnlyList<ApprovedTimeLine>? lines = null, IReadOnlyList<MachineHourlyRate>? rates = null, IReadOnlyList<MaterialPrice>? prices = null, long accepted = 100, long consumed = 100,
        bool contract = false, WorkType type = WorkType.Make, long? material = 7, decimal? weight = 0.4m, bool complete = true, DateTime? release = null) =>
        new(
            1, 1, "R-1", "A", type, contract, complete, release ?? new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), consumed, accepted, material, material is null ? null : "ST37", weight,
            lines ?? [new ApprovedTimeLine(11, 3, new DateOnly(2026, 10, 10), 60)],
            rates ?? [Rate(1, 3, 1_200_000m, new DateOnly(2026, 1, 1))],
            new Dictionary<long, string> { [3] = "FCC-06", [4] = "TCC-12" },
            prices ?? [Price(1, 90_000m, new DateOnly(2026, 1, 1))],
            1);

    [Fact]
    public void The_example_of_the_costing_design_gives_machining_plus_material_over_the_accepted_units()
    {
        // 25 h at 1,200,000 + 12 h at 900,000 = 40,800,000; 0.4 kg x 100 pieces x 90,000 = 3,600,000; total 44,400,000 over 100 accepted = 444,000 each.
        var build = CostCalculator.Build(
            Inputs(
                lines: [new ApprovedTimeLine(11, 3, new DateOnly(2026, 10, 10), 25 * 60), new ApprovedTimeLine(12, 4, new DateOnly(2026, 10, 11), 12 * 60)],
                rates: [Rate(1, 3, 1_200_000m, new DateOnly(2026, 1, 1)), Rate(2, 4, 900_000m, new DateOnly(2026, 1, 1))]),
            9, Now, LocalDate);

        Assert.Equal(ValidationIssue.None, build.Refusal);
        var calc = build.Calculation!;
        Assert.Equal((40_800_000m, 3_600_000m, 44_400_000m, 444_000m), (calc.MachiningTotalRial, calc.MaterialTotalRial, calc.TotalRial, calc.UnitCostRial));
        Assert.Equal(new[] { 1, 2, 3 }, calc.Lines.Select(l => l.LineNo).ToArray());
        Assert.Equal(PriceBasis.Calculated, build.Decision!.Basis);
        Assert.Equal(44_400_000m, build.Decision.ProposedPriceRial);
    }

    [Fact]
    public void Each_line_uses_the_rate_in_force_on_its_work_date_and_is_rounded_half_away_from_zero_per_line()
    {
        var build = CostCalculator.Build(
            Inputs(
                lines: [new ApprovedTimeLine(11, 3, new DateOnly(2026, 6, 30), 90), new ApprovedTimeLine(12, 3, new DateOnly(2026, 7, 1), 90), new ApprovedTimeLine(13, 3, new DateOnly(2026, 7, 2), 1)],
                rates: [Rate(1, 3, 1_000_001m, new DateOnly(2026, 1, 1)), Rate(2, 3, 2_000_000m, new DateOnly(2026, 7, 1))],
                material: null),
            9, Now, LocalDate);

        var lines = build.Calculation!.Lines;
        Assert.Equal(new decimal[] { 1_500_002m, 3_000_000m, 33_333m }, lines.Select(l => l.AmountRial).ToArray()); // 1,500,001.5 rounds up; 33,333.33 rounds down
        Assert.Equal(new long?[] { 1, 2, 2 }, lines.Select(l => l.RateId).ToArray());
        Assert.Equal(4_533_335m, build.Calculation.TotalRial);
    }

    [Fact]
    public void Scrap_and_rework_are_absorbed_the_material_of_all_units_is_divided_over_the_accepted_units()
    {
        var build = CostCalculator.Build(Inputs(accepted: 80, consumed: 100, lines: [new ApprovedTimeLine(11, 3, new DateOnly(2026, 10, 10), 600)]), 9, Now, LocalDate);

        var calc = build.Calculation!;
        Assert.Equal((10m * 1_200_000m, 0.4m * 100 * 90_000m), (calc.MachiningTotalRial, calc.MaterialTotalRial));
        Assert.Equal((80L, 100L), (calc.AcceptedBaseUnits, calc.ConsumedBaseUnits));
        Assert.Equal(decimal.Round((12_000_000m + 3_600_000m) / 80, 0, MidpointRounding.AwayFromZero), calc.UnitCostRial);
    }

    [Fact]
    public void The_material_price_is_taken_on_the_release_day_and_a_repair_item_without_material_has_only_machining()
    {
        var release = new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
        var prices = new[] { Price(1, 80_000m, new DateOnly(2026, 1, 1)), Price(2, 100_000m, new DateOnly(2026, 6, 1)) };
        var built = CostCalculator.Build(Inputs(prices: prices, release: release, lines: [new ApprovedTimeLine(11, 3, new DateOnly(2026, 10, 10), 60)]), 9, Now, LocalDate).Calculation!;
        Assert.Equal((1L, new DateOnly(2026, 3, 1), 3_200_000m), (built.MaterialPriceId, built.PriceDate, built.MaterialTotalRial));

        var repair = CostCalculator.Build(Inputs(material: null, weight: null, type: WorkType.Repair, prices: []), 9, Now, LocalDate);
        Assert.Equal(ValidationIssue.None, repair.Refusal);
        Assert.Equal((0m, 1_200_000m, (long?)null), (repair.Calculation!.MaterialTotalRial, repair.Calculation.TotalRial, repair.Calculation.MaterialId));
        Assert.Equal(CostLineKind.Machining, Assert.Single(repair.Calculation.Lines).Kind);
    }

    [Fact]
    public void A_make_item_of_an_approved_contract_gets_a_contract_decision_but_a_repair_never_does()
    {
        var make = CostCalculator.Build(Inputs(contract: true), 9, Now, LocalDate);
        Assert.Equal((PriceBasis.Contract, (decimal?)null), (make.Decision!.Basis, make.Decision.ProposedPriceRial));
        Assert.True(make.Calculation!.TotalRial > 0);

        var repair = CostCalculator.Build(Inputs(contract: true, type: WorkType.Repair, material: null, weight: null), 9, Now, LocalDate);
        Assert.Equal(PriceBasis.Calculated, repair.Decision!.Basis);
    }

    [Fact]
    public void Nothing_is_defaulted_not_ready_no_time_missing_rates_and_prices_are_refused_with_named_gaps()
    {
        Assert.Equal(ValidationIssue.CostingNotReady, CostCalculator.Build(Inputs(complete: false), 9, Now, LocalDate).Refusal);
        Assert.Equal(ValidationIssue.CostingNotReady, CostCalculator.Build(Inputs(accepted: 0), 9, Now, LocalDate).Refusal);
        Assert.Equal(ValidationIssue.CostingNotReady, CostCalculator.Build(Inputs(accepted: 101, consumed: 100), 9, Now, LocalDate).Refusal);
        Assert.Equal(ValidationIssue.CostingNoApprovedTime, CostCalculator.Build(Inputs(lines: []), 9, Now, LocalDate).Refusal);

        // A rate that starts after the work date does not apply; the other machine has none at all.
        var gaps = CostCalculator.Build(
            Inputs(
                lines: [new ApprovedTimeLine(11, 3, new DateOnly(2026, 10, 10), 60), new ApprovedTimeLine(12, 4, new DateOnly(2026, 10, 11), 60)],
                rates: [Rate(1, 3, 1_200_000m, new DateOnly(2026, 10, 11))],
                prices: []),
            9, Now, LocalDate);
        Assert.Equal(ValidationIssue.CostingRateMissing, gaps.Refusal);
        Assert.Equal(new[] { "RATE|FCC-06|2026-10-10", "RATE|TCC-12|2026-10-11", "PRICE|ST37|2026-10-01" }, gaps.Gaps.ToArray());
        Assert.Null(gaps.Calculation);

        var priceOnly = CostCalculator.Build(Inputs(prices: [Price(1, 1m, new DateOnly(2026, 10, 2))]), 9, Now, LocalDate);
        Assert.Equal((ValidationIssue.CostingPriceMissing, "PRICE|ST37|2026-10-01"), (priceOnly.Refusal, Assert.Single(priceOnly.Gaps)));
    }
}
