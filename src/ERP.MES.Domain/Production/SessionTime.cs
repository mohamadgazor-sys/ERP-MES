namespace ERP.MES.Domain.Production;

/// <summary>
/// Durations derived from recorded instants, never from editable numbers (Decision Log #112). Active time is the sum of a session's intervals; paused time is the sum of its pauses; elapsed time
/// runs from the first start to the finish (or <c>asOf</c>); a machine's runtime is the UNION of the intervals that ran on it, so a handover (or any overlap) is counted once.
/// </summary>
public static class SessionTime
{
    public static TimeSpan Active(WorkSession session, DateTime asOfUtc) => Sum(session.Intervals.Select(i => i.Duration(asOfUtc)));

    public static TimeSpan Paused(WorkSession session, DateTime asOfUtc) => Sum(session.Pauses.Select(p => p.Duration(asOfUtc)));

    /// <summary>First start to finish (or <paramref name="asOfUtc"/> while unfinished); zero for a session that never started.</summary>
    public static TimeSpan Elapsed(WorkSession session, DateTime asOfUtc) =>
        session.StartedAtUtc is not { } start ? TimeSpan.Zero : (session.FinishedAtUtc ?? (asOfUtc < start ? start : asOfUtc)) - start;

    /// <summary>The total length covered by the given intervals, overlaps counted once. An open interval counts up to <paramref name="asOfUtc"/>.</summary>
    public static TimeSpan Union(IEnumerable<WorkSessionInterval> intervals, DateTime asOfUtc)
    {
        var spans = intervals.Select(i => (Start: i.StartedAtUtc, End: i.EndedAtUtc ?? (asOfUtc < i.StartedAtUtc ? i.StartedAtUtc : asOfUtc))).OrderBy(s => s.Start).ToList();
        var total = TimeSpan.Zero;
        DateTime? currentStart = null;
        DateTime currentEnd = default;
        foreach (var (start, end) in spans)
        {
            if (currentStart is null)
            {
                currentStart = start;
                currentEnd = end;
            }
            else if (start <= currentEnd)
            {
                if (end > currentEnd)
                {
                    currentEnd = end;
                }
            }
            else
            {
                total += currentEnd - currentStart.Value;
                currentStart = start;
                currentEnd = end;
            }
        }

        return currentStart is null ? total : total + (currentEnd - currentStart.Value);
    }

    private static TimeSpan Sum(IEnumerable<TimeSpan> spans) => spans.Aggregate(TimeSpan.Zero, (a, b) => a + b);
}
