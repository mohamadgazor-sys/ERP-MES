using ERP.MES.Domain.Production;

namespace ERP.MES.Application.Production;

/// <summary>Part of one day (local) covered by a stretch of time.</summary>
public sealed record DaySlice(DateOnly Date, TimeSpan Time);

/// <summary>Splits an instant interval at LOCAL midnights (the configured application time zone), so a stretch that crosses midnight is attributed to the right work dates. Durations themselves are differences of UTC instants.</summary>
public static class IntervalDays
{
    public static IReadOnlyList<DaySlice> Split(DateTime startUtc, DateTime endUtc, Func<DateTime, DateOnly> localDate, Func<DateOnly, DateTime> localMidnightUtc)
    {
        ArgumentNullException.ThrowIfNull(localDate);
        ArgumentNullException.ThrowIfNull(localMidnightUtc);
        var slices = new List<DaySlice>();
        var cursor = startUtc;
        while (cursor < endUtc)
        {
            var date = localDate(cursor);
            var boundary = localMidnightUtc(date.AddDays(1));
            var sliceEnd = boundary < endUtc ? boundary : endUtc;
            if (sliceEnd <= cursor)
            {
                sliceEnd = endUtc;
            }

            slices.Add(new DaySlice(date, sliceEnd - cursor));
            cursor = sliceEnd;
        }

        return slices;
    }
}

/// <summary>One stage as the metrics need it.</summary>
public sealed record StagePlan(long ItemId, int StageNo, decimal PlannedMinutes);

/// <summary>Actual machine time of one machine on one stage: the UNION of the intervals that ran on it, so a handover is counted once.</summary>
public sealed record MachineHoursRow(long MachineId, long ItemId, int StageNo, TimeSpan Runtime);

/// <summary>Actual labour time of one operator on one stage: the sum of their intervals. It is a different quantity from machine time and is never added to it.</summary>
public sealed record LaborHoursRow(long OperatorId, long ItemId, int StageNo, TimeSpan Time);

/// <summary>Calendar time of a stage from its first start to its last finish (to <c>asOf</c> while work is still open).</summary>
public sealed record StageElapsedRow(long ItemId, int StageNo, DateTime? FirstStartUtc, DateTime? LastEndUtc, TimeSpan Elapsed, bool IsOpen);

public sealed record PausedByReasonRow(long ReasonId, TimeSpan Time, int Pauses);

/// <summary>Planned minutes (estimate: minutes per piece x quantity) against what actually happened, side by side and not equated.</summary>
public sealed record PlannedVsActualRow(long ItemId, int StageNo, decimal PlannedMinutes, TimeSpan MachineRuntime, TimeSpan Elapsed, TimeSpan Labor);

public sealed record StageMetrics(
    IReadOnlyList<MachineHoursRow> MachineHours,
    IReadOnlyList<LaborHoursRow> LaborHours,
    IReadOnlyList<StageElapsedRow> Elapsed,
    IReadOnlyList<PausedByReasonRow> PausedByReason,
    IReadOnlyList<PlannedVsActualRow> PlannedVsActual);

/// <summary>
/// The five distinct actual-time metrics (owner direction of 2026-10-27, Decision Log #114), computed from recorded instants only: machine-hours by machine and stage, labour-hours by operator and
/// stage, elapsed stage time, paused time by reason, and planned against actual. Two machines running two hours are four machine-hours and two hours elapsed; two operators working two hours each are
/// four labour-hours. Cancelled sessions never count. <paramref name="include"/> chooses which sessions enter (all started sessions for reports; only time-approved ones for costing).
/// </summary>
public static class SessionMetricsCalculator
{
    public static StageMetrics Compute(IEnumerable<WorkSession> sessions, IReadOnlyList<StagePlan> stages, DateTime asOfUtc, Func<WorkSession, bool>? include = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        var used = sessions.Where(s => s.Status != WorkSessionStatus.Cancelled && s.Intervals.Count > 0 && (include?.Invoke(s) ?? true)).ToList();

        var machine = used.GroupBy(s => (s.MachineId, s.RequestItemId, s.StageNo))
            .Select(g => new MachineHoursRow(g.Key.MachineId, g.Key.RequestItemId, g.Key.StageNo, SessionTime.Union(g.SelectMany(s => s.Intervals), asOfUtc)))
            .OrderBy(r => r.ItemId).ThenBy(r => r.StageNo).ThenBy(r => r.MachineId).ToList();
        var labor = used.SelectMany(s => s.Intervals.Select(i => (s.RequestItemId, s.StageNo, i.OperatorId, Duration: i.Duration(asOfUtc))))
            .GroupBy(x => (x.OperatorId, x.RequestItemId, x.StageNo))
            .Select(g => new LaborHoursRow(g.Key.OperatorId, g.Key.RequestItemId, g.Key.StageNo, g.Aggregate(TimeSpan.Zero, (a, x) => a + x.Duration)))
            .OrderBy(r => r.ItemId).ThenBy(r => r.StageNo).ThenBy(r => r.OperatorId).ToList();
        var elapsed = used.GroupBy(s => (s.RequestItemId, s.StageNo)).Select(g =>
        {
            var first = g.Min(s => s.StartedAtUtc!.Value);
            var open = g.Any(s => s.IsUnfinished);
            var last = open ? asOfUtc : g.Max(s => s.FinishedAtUtc!.Value);
            return new StageElapsedRow(g.Key.RequestItemId, g.Key.StageNo, first, last, last > first ? last - first : TimeSpan.Zero, open);
        }).OrderBy(r => r.ItemId).ThenBy(r => r.StageNo).ToList();
        var paused = used.SelectMany(s => s.Pauses).GroupBy(p => p.ReasonId)
            .Select(g => new PausedByReasonRow(g.Key, g.Aggregate(TimeSpan.Zero, (a, p) => a + p.Duration(asOfUtc)), g.Count()))
            .OrderBy(r => r.ReasonId).ToList();
        var planned = stages.Select(st =>
        {
            var m = machine.Where(r => r.ItemId == st.ItemId && r.StageNo == st.StageNo).Aggregate(TimeSpan.Zero, (a, r) => a + r.Runtime);
            var e = elapsed.FirstOrDefault(r => r.ItemId == st.ItemId && r.StageNo == st.StageNo)?.Elapsed ?? TimeSpan.Zero;
            var l = labor.Where(r => r.ItemId == st.ItemId && r.StageNo == st.StageNo).Aggregate(TimeSpan.Zero, (a, r) => a + r.Time);
            return new PlannedVsActualRow(st.ItemId, st.StageNo, st.PlannedMinutes, m, e, l);
        }).ToList();
        return new StageMetrics(machine, labor, elapsed, paused, planned);
    }

    /// <summary>
    /// Machine time per machine and local work date of the given sessions' intervals, overlaps counted once (handover safe), as whole minutes rounded half away from zero PER MACHINE-DAY
    /// (labelled default; at most 30 seconds of rounding per machine-day). This is what machine-rate costing multiplies by the rate in force on that date.
    /// </summary>
    public static IReadOnlyList<MachineDayTime> MachineDays(IEnumerable<WorkSession> sessions, DateTime asOfUtc, Func<DateTime, DateOnly> localDate, Func<DateOnly, DateTime> localMidnightUtc)
    {
        var byMachine = sessions.Where(s => s.Status != WorkSessionStatus.Cancelled).GroupBy(s => s.MachineId);
        var result = new List<MachineDayTime>();
        foreach (var machine in byMachine)
        {
            var merged = Merge(machine.SelectMany(s => s.Intervals.Select(i => (Start: i.StartedAtUtc, End: i.EndedAtUtc ?? asOfUtc))));
            var perDay = new Dictionary<DateOnly, TimeSpan>();
            foreach (var (start, end) in merged)
            {
                foreach (var slice in IntervalDays.Split(start, end, localDate, localMidnightUtc))
                {
                    perDay[slice.Date] = perDay.GetValueOrDefault(slice.Date) + slice.Time;
                }
            }

            foreach (var (date, time) in perDay.OrderBy(p => p.Key))
            {
                var minutes = (int)decimal.Round((decimal)time.TotalMinutes, 0, MidpointRounding.AwayFromZero);
                if (minutes > 0)
                {
                    result.Add(new MachineDayTime(machine.Key, date, minutes, machine.Select(s => s.Id).Distinct().OrderBy(id => id).ToList()));
                }
            }
        }

        return result.OrderBy(r => r.Date).ThenBy(r => r.MachineId).ToList();
    }

    /// <summary>Labour time per operator and local work date of the given sessions (an operator's intervals never overlap), in whole minutes rounded half away from zero per operator-day.</summary>
    public static IReadOnlyList<OperatorDayTime> OperatorDays(IEnumerable<WorkSession> sessions, DateTime asOfUtc, Func<DateTime, DateOnly> localDate, Func<DateOnly, DateTime> localMidnightUtc)
    {
        var perKey = new Dictionary<(long, DateOnly), TimeSpan>();
        foreach (var interval in sessions.Where(s => s.Status != WorkSessionStatus.Cancelled).SelectMany(s => s.Intervals))
        {
            foreach (var slice in IntervalDays.Split(interval.StartedAtUtc, interval.EndedAtUtc ?? asOfUtc, localDate, localMidnightUtc))
            {
                var key = (interval.OperatorId, slice.Date);
                perKey[key] = perKey.GetValueOrDefault(key) + slice.Time;
            }
        }

        return perKey.OrderBy(p => p.Key.Item2).ThenBy(p => p.Key.Item1)
            .Select(p => new OperatorDayTime(p.Key.Item1, p.Key.Item2, (int)decimal.Round((decimal)p.Value.TotalMinutes, 0, MidpointRounding.AwayFromZero))).Where(r => r.Minutes > 0).ToList();
    }

    private static List<(DateTime Start, DateTime End)> Merge(IEnumerable<(DateTime Start, DateTime End)> spans)
    {
        var merged = new List<(DateTime Start, DateTime End)>();
        foreach (var span in spans.OrderBy(s => s.Start))
        {
            if (merged.Count > 0 && span.Start <= merged[^1].End)
            {
                if (span.End > merged[^1].End)
                {
                    merged[^1] = (merged[^1].Start, span.End);
                }
            }
            else
            {
                merged.Add(span);
            }
        }

        return merged;
    }
}

/// <summary>Machine runtime of one machine on one local date, with the sessions that contributed.</summary>
public sealed record MachineDayTime(long MachineId, DateOnly Date, int Minutes, IReadOnlyList<long> SessionIds);

public sealed record OperatorDayTime(long OperatorId, DateOnly Date, int Minutes);
