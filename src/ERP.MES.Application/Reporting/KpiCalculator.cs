using ERP.MES.Application.Persistence;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Reporting;

/// <summary>The separate, labelled metrics of one production unit for a period (Decision Log #95 D6, #106). There is no composite score and no unit ranking.</summary>
public sealed record UnitKpi(
    long UnitId,
    string Code,
    string Name,
    int ProductiveMinutes,
    int EstimatedOvertimeMinutes,
    long AcceptedBaseUnits,
    long ReworkBaseUnits,
    long ScrapBaseUnits,
    decimal? AcceptanceRate,
    decimal? ReworkRate,
    decimal? ScrapRate,
    int CompletedStages,
    decimal PlannedMinutes,
    int ActualMinutes,
    decimal? ActualToPlanned,
    int ScheduledMinutes,
    decimal? Utilization,
    int PendingReports,
    bool IsAuthoritative);

/// <summary>An operator's valid hours in the period; the rank is an hour ranking only.</summary>
public sealed record OperatorHours(int Rank, long OperatorId, string Code, string Name, int ProductiveMinutes, int WorkedDays);

public sealed record KpiReport(DateOnly From, DateOnly To, DateTime RunAtUtc, IReadOnlyList<UnitKpi> Units, IReadOnlyList<OperatorHours> Operators, int TotalPendingReports);

/// <summary>
/// The v1 unit metrics (docs/JOB_REPORTS_KPI_AND_OVERTIME_DESIGN.md section 7; Decision Log #95 D6, #106), pure over the facts. Productive hours = approved line minutes less downtime; accepted /
/// rework / scrap base units are the QC decisions of the period attributed to the unit that performed the stage, and the rates are shares of the units decided in the period (a reworked unit
/// inspected again is decided again); plan versus actual compares the planned minutes of the stages completed in the period with the approved minutes reported for them; utilization is productive minutes
/// over the scheduled shift minutes of the operators planned in the unit (so overtime can push it above 100%); the overtime share is ESTIMATED from the time overlap of the lines with the operator's
/// approved overtime windows. A unit is authoritative only when none of its reports in the period is still waiting for approval; the number waiting is always shown. Operators are ranked by valid hours,
/// then distinct worked days, then code, and the ranking is labelled as an hour ranking only.
/// </summary>
public static class KpiCalculator
{
    public static KpiReport Build(KpiFacts facts, DateOnly from, DateOnly toInclusive, DateTime runAtUtc, Func<DateTime, DateTime> toLocal, IReadOnlySet<long>? visibleUnits)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(toLocal);
        bool Visible(long unit) => visibleUnits is null || visibleUnits.Contains(unit);
        bool InRange(DateTime utc)
        {
            var date = DateOnly.FromDateTime(toLocal(utc));
            return date >= from && date <= toInclusive;
        }

        var lines = facts.Lines.Concat(SessionLines(facts.SessionIntervals ?? [], toLocal)).Where(l => l.WorkDate >= from && l.WorkDate <= toInclusive && Visible(l.UnitId)).ToList();
        var inspections = facts.Inspections.Where(i => InRange(i.DecidedAtUtc) && Visible(i.UnitId)).ToList();
        var completed = facts.CompletedStages.Where(c => InRange(c.CompletedAtUtc) && Visible(c.UnitId)).ToList();
        var scheduled = facts.Scheduled.Where(r => r.Date >= from && r.Date <= toInclusive && Visible(r.UnitId)).ToList();
        var windows = facts.Overtime.ToLookup(w => w.OperatorId);

        var units = new List<UnitKpi>();
        foreach (var unit in facts.Units.Where(u => Visible(u.UnitId)).OrderBy(u => u.Code, StringComparer.Ordinal))
        {
            var unitLines = lines.Where(l => l.UnitId == unit.UnitId).ToList();
            var minutes = unitLines.Sum(l => l.ProductiveMinutes);
            var overtime = unitLines.Sum(l => OverlapMinutes(l, windows[l.OperatorId].ToList(), toLocal));
            var accepted = inspections.Where(i => i.UnitId == unit.UnitId && i.Disposition == InspectionDisposition.Accepted).Sum(i => i.BaseUnits);
            var rework = inspections.Where(i => i.UnitId == unit.UnitId && i.Disposition == InspectionDisposition.Rework).Sum(i => i.BaseUnits);
            var scrap = inspections.Where(i => i.UnitId == unit.UnitId && i.Disposition == InspectionDisposition.Scrap).Sum(i => i.BaseUnits);
            var decided = accepted + rework + scrap;
            var done = completed.Where(c => c.UnitId == unit.UnitId).ToList();
            var planned = done.Sum(c => c.PlannedMinutes);
            var actual = done.Sum(c => c.ActualApprovedMinutes);
            var shift = scheduled.Where(r => r.UnitId == unit.UnitId).Sum(r => r.WorkingMinutes);
            var pending = facts.Pending.Where(p => p.UnitId == unit.UnitId).Sum(p => p.SubmittedReports);
            units.Add(new UnitKpi(
                unit.UnitId, unit.Code, unit.Name, minutes, overtime, accepted, rework, scrap,
                Ratio(accepted, decided), Ratio(rework, decided), Ratio(scrap, decided),
                done.Count, planned, actual, planned > 0 ? decimal.Round(actual / planned, 4) : null,
                shift, shift > 0 ? decimal.Round((decimal)minutes / shift, 4) : null, pending, pending == 0));
        }

        var names = facts.Operators.ToDictionary(o => o.OperatorId);
        var ranked = lines.GroupBy(l => l.OperatorId)
            .Select(g => new { OperatorId = g.Key, Minutes = g.Sum(l => l.ProductiveMinutes), Days = g.Select(l => l.WorkDate).Distinct().Count(), Code = names.TryGetValue(g.Key, out var o) ? o.Code : string.Empty, Name = names.TryGetValue(g.Key, out var n) ? n.Name : string.Empty })
            .OrderByDescending(x => x.Minutes).ThenByDescending(x => x.Days).ThenBy(x => x.Code, StringComparer.Ordinal)
            .Select((x, i) => new OperatorHours(i + 1, x.OperatorId, x.Code, x.Name, x.Minutes, x.Days))
            .ToList();

        return new KpiReport(from, toInclusive, runAtUtc, units, ranked, units.Sum(u => u.PendingReports));
    }

    // Time-approved work sessions enter as operator labour lines, one per local day (a stretch across midnight is split at local midnight); ReportId is the negative session id.
    private static IEnumerable<KpiApprovedLine> SessionLines(IReadOnlyList<KpiSessionInterval> intervals, Func<DateTime, DateTime> toLocal)
    {
        foreach (var interval in intervals)
        {
            var cursor = interval.StartUtc;
            while (cursor < interval.EndUtc)
            {
                var local = toLocal(cursor);
                var nextMidnightLocal = local.Date.AddDays(1);
                var sliceEnd = cursor + (nextMidnightLocal - local);
                if (sliceEnd > interval.EndUtc || sliceEnd <= cursor)
                {
                    sliceEnd = interval.EndUtc;
                }

                var minutes = (int)decimal.Round((decimal)(sliceEnd - cursor).TotalMinutes, 0, MidpointRounding.AwayFromZero);
                if (minutes > 0)
                {
                    yield return new KpiApprovedLine(-interval.SessionId, interval.OperatorId, interval.UnitId, DateOnly.FromDateTime(local), cursor, sliceEnd, minutes);
                }

                cursor = sliceEnd;
            }
        }
    }

    private static decimal? Ratio(long part, long whole) => whole > 0 ? decimal.Round((decimal)part / whole, 4) : null;

    private static int OverlapMinutes(KpiApprovedLine line, IReadOnlyList<KpiOvertimeWindow> windows, Func<DateTime, DateTime> toLocal)
    {
        if (windows.Count == 0)
        {
            return 0;
        }

        var start = toLocal(line.StartUtc);
        var end = toLocal(line.EndUtc);
        var total = 0;
        foreach (var window in windows)
        {
            var from = start > window.StartLocal ? start : window.StartLocal;
            var to = end < window.EndLocal ? end : window.EndLocal;
            if (to > from)
            {
                total += (int)(to - from).TotalMinutes;
            }
        }

        return Math.Min(total, line.ProductiveMinutes);
    }
}
