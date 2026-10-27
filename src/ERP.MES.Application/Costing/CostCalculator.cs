using ERP.MES.Application.Persistence;
using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Materials;
using ERP.MES.Domain.Planning;

namespace ERP.MES.Application.Costing;

/// <summary>
/// The v1 cost formula (docs/COSTING_DESIGN.md, Decision Log #91, #95 D3/D4, #104), pure over its inputs. Machining: for each approved report line, the machine rate in force on the line's work date
/// times its productive minutes (downtime is already excluded) divided by 60, rounded to whole Rial per line, half away from zero. Material: weight per base unit times the base units that entered
/// production times the price per kg in force on the price date (the day the first stage was released), rounded the same way. The total is the sum of the rounded lines and is divided over the
/// QC-accepted base units, so scrap and rework are absorbed. Nothing is defaulted: no approved time, no rate or no price is a named refusal. An item of an approved contract gets a contract-basis decision;
/// its calculated cost is information only.
/// </summary>
public static class CostCalculator
{
    public static CostBuild Build(CostInputs inputs, long userId, DateTime nowUtc, Func<DateTime, DateOnly> localDate, Func<DateOnly, DateTime>? localMidnightUtc = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(localDate);
        if (!inputs.RouteComplete || inputs.AcceptedBaseUnits <= 0 || inputs.InputBaseUnits is not { } consumed || consumed < inputs.AcceptedBaseUnits || inputs.FirstReleaseUtc is null)
        {
            return Refuse(ValidationIssue.CostingNotReady);
        }

        if (inputs.UnapprovedFinishedSessions > 0)
        {
            return Refuse(ValidationIssue.CostingSessionsNotApproved);
        }

        var sessionDays = inputs.ApprovedSessions.Count == 0
            ? []
            : SessionMetricsCalculator.MachineDays(inputs.ApprovedSessions, nowUtc, localDate, localMidnightUtc ?? throw new ArgumentException("Costing work sessions needs the local midnight function.", nameof(localMidnightUtc)));
        if (inputs.Lines.Count == 0 && sessionDays.Count == 0)
        {
            return Refuse(ValidationIssue.CostingNoApprovedTime);
        }

        var gaps = new List<string>();
        var lines = new List<ItemCostLine>();
        foreach (var line in inputs.Lines.OrderBy(l => l.WorkDate).ThenBy(l => l.JobReportLineId))
        {
            var rate = MachineRates.RateOn(inputs.Rates, line.MachineId, line.WorkDate);
            if (rate is null)
            {
                gaps.Add($"RATE|{inputs.MachineCodes.GetValueOrDefault(line.MachineId, "?")}|{line.WorkDate:yyyy-MM-dd}");
                continue;
            }

            var amount = decimal.Round(rate.RatePerHourRial * line.ProductiveMinutes / 60m, 0, MidpointRounding.AwayFromZero);
            lines.Add(new ItemCostLine(lines.Count + 1, CostLineKind.Machining, line.JobReportLineId, line.MachineId, rate.Id, line.WorkDate, line.ProductiveMinutes, null, rate.RatePerHourRial, amount));
        }

        // Machine runtime of approved work sessions: one line per machine and local date, handover overlaps counted once, the same rate-history rule and rounding as a report line (Decision Log #114).
        foreach (var day in sessionDays)
        {
            var rate = MachineRates.RateOn(inputs.Rates, day.MachineId, day.Date);
            if (rate is null)
            {
                gaps.Add($"RATE|{inputs.MachineCodes.GetValueOrDefault(day.MachineId, "?")}|{day.Date:yyyy-MM-dd}");
                continue;
            }

            var amount = decimal.Round(rate.RatePerHourRial * day.Minutes / 60m, 0, MidpointRounding.AwayFromZero);
            lines.Add(new ItemCostLine(lines.Count + 1, CostLineKind.Machining, null, day.MachineId, rate.Id, day.Date, day.Minutes, null, rate.RatePerHourRial, amount, string.Join(',', day.SessionIds)));
        }

        long? materialPriceId = null;
        DateOnly? priceDate = null;
        if (inputs.MaterialId is { } materialId && inputs.WeightKgPerBaseUnit is { } weight)
        {
            priceDate = localDate(inputs.FirstReleaseUtc.Value);
            var price = MaterialPrices.PriceOn(inputs.Prices, materialId, priceDate.Value);
            if (price is null)
            {
                gaps.Add($"PRICE|{inputs.MaterialCode ?? "?"}|{priceDate:yyyy-MM-dd}");
            }
            else if (gaps.Count == 0)
            {
                var kilograms = weight * consumed;
                materialPriceId = price.Id;
                lines.Add(new ItemCostLine(lines.Count + 1, CostLineKind.Material, null, null, price.Id, priceDate, null, kilograms, price.PricePerKgRial, decimal.Round(kilograms * price.PricePerKgRial, 0, MidpointRounding.AwayFromZero)));
            }
        }

        if (gaps.Count > 0)
        {
            return new CostBuild(null, null, gaps.Any(g => g.StartsWith("RATE|", StringComparison.Ordinal)) ? ValidationIssue.CostingRateMissing : ValidationIssue.CostingPriceMissing, gaps);
        }

        var calculation = new ItemCostCalculation(inputs.ItemId, inputs.NextSequenceNo, inputs.AcceptedBaseUnits, consumed, inputs.MaterialId, materialPriceId, priceDate, lines, userId, nowUtc);
        var basis = inputs.ContractApproved && inputs.WorkType == ERP.MES.Domain.Requests.WorkType.Make ? PriceBasis.Contract : PriceBasis.Calculated;
        return new CostBuild(calculation, new ItemPriceDecision(calculation, basis), ValidationIssue.None, []);
    }

    private static CostBuild Refuse(ValidationIssue issue) => new(null, null, issue, []);
}
