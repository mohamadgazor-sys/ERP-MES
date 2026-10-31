namespace ERP.MES.Domain.Scheduling;

/// <summary>One occurrence of a shift: the template and its local start and end (the end may be on the next date).</summary>
public sealed record ShiftOccurrence(ShiftTemplate Template, long UnitShiftId, DateTime LocalStart, DateTime LocalEnd)
{
    public DateOnly StartDate => DateOnly.FromDateTime(LocalStart);

    public bool Overlaps(ShiftOccurrence other) => LocalStart < other.LocalEnd && other.LocalStart < LocalEnd;
}

/// <summary>
/// The working calendar of the production units as a pure function of configuration (Decision Log #13, #100): the regular weekly pattern, calendar exceptions, the shifts each unit runs and the
/// shift templates. All times are LOCAL wall-clock times of the display zone; conversion to stored UTC happens at the boundary. Nothing here is a constant of the company's schedule.
///
/// A shift occurrence of a unit exists on a date when the unit-shift and the template are active and the shift is not switched off. The state of a day is decided from the most specific rule: weekly
/// pattern (or, for a shift with its own weekdays, the recurring unit schedule), then a company exception, then a unit exception, then a shift exception (all units) and last a unit-and-shift
/// exception; at equal scope an Off exception wins over a Working one.
/// </summary>
public sealed class WorkingCalendar
{
    private readonly Dictionary<DayOfWeek, bool> _weekly;
    private readonly IReadOnlyList<CalendarException> _exceptions;
    private readonly IReadOnlyList<UnitShift> _unitShifts;
    private readonly IReadOnlyDictionary<long, ShiftTemplate> _templates;

    public WorkingCalendar(
        IEnumerable<WeeklyWorkday> weekly, IEnumerable<CalendarException> exceptions, IEnumerable<UnitShift> unitShifts, IEnumerable<ShiftTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(weekly);
        ArgumentNullException.ThrowIfNull(exceptions);
        ArgumentNullException.ThrowIfNull(unitShifts);
        ArgumentNullException.ThrowIfNull(templates);
        _weekly = weekly.ToDictionary(w => w.Day, w => w.IsWorkday);
        _exceptions = exceptions.Where(e => e.IsActive).ToList();
        _unitShifts = unitShifts.Where(u => u.IsActive).ToList();
        _templates = templates.Where(t => t.IsActive).ToDictionary(t => t.Id);
    }

    /// <summary>True when the unit has at least one active shift (otherwise nothing can be forecast for it).</summary>
    public bool HasShifts(long unitId) => _unitShifts.Any(u => u.ProductionUnitId == unitId && _templates.ContainsKey(u.ShiftTemplateId));

    /// <summary>Whether the shift of the unit works on the date.</summary>
    public bool IsWorking(long unitId, long shiftTemplateId, DateOnly date)
    {
        // The regular state: a shift with its own weekdays (a recurring unit schedule) follows them; any other shift follows the company's weekly pattern (Decision Log #123 Q13).
        var own = _unitShifts.FirstOrDefault(u => u.ProductionUnitId == unitId && u.ShiftTemplateId == shiftTemplateId)?.RegularDays;
        var working = own is { } mask ? (mask >> (int)date.DayOfWeek & 1) == 1 : _weekly.TryGetValue(date.DayOfWeek, out var regular) && regular;
        foreach (var scope in new Func<CalendarException, bool>[]
        {
            e => e.ProductionUnitId is null && e.ShiftTemplateId is null,
            e => e.ProductionUnitId == unitId && e.ShiftTemplateId is null,
            e => e.ProductionUnitId is null && e.ShiftTemplateId == shiftTemplateId,
            e => e.ProductionUnitId == unitId && e.ShiftTemplateId == shiftTemplateId,
        })
        {
            var matching = _exceptions.Where(e => e.Date == date && scope(e)).ToList();
            if (matching.Count > 0)
            {
                working = !matching.Any(e => e.Kind == CalendarExceptionKind.Off);
            }
        }

        return working;
    }

    /// <summary>The shift occurrences of a unit that START on the date, ordered by start time.</summary>
    public IReadOnlyList<ShiftOccurrence> Occurrences(long unitId, DateOnly date)
    {
        var list = new List<ShiftOccurrence>();
        foreach (var unitShift in _unitShifts.Where(u => u.ProductionUnitId == unitId))
        {
            if (!_templates.TryGetValue(unitShift.ShiftTemplateId, out var template) || !IsWorking(unitId, template.Id, date))
            {
                continue;
            }

            var start = date.ToDateTime(template.StartTime);
            list.Add(new ShiftOccurrence(template, unitShift.Id, start, start.AddMinutes(template.TotalMinutes)));
        }

        return list.OrderBy(o => o.LocalStart).ThenBy(o => o.Template.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>The local working intervals (breaks excluded, overlapping shifts merged so overlapped hours are not counted twice) of a unit that intersect [fromLocal, +days).</summary>
    public IReadOnlyList<(DateTime Start, DateTime End)> WorkingIntervals(long unitId, DateTime fromLocal, int days)
    {
        var pieces = new List<(DateTime Start, DateTime End)>();
        var first = DateOnly.FromDateTime(fromLocal).AddDays(-1);
        for (var i = 0; i <= days + 1; i++)
        {
            foreach (var occurrence in Occurrences(unitId, first.AddDays(i)))
            {
                foreach (var (from, to) in occurrence.Template.WorkingPieces())
                {
                    pieces.Add((occurrence.LocalStart.AddMinutes(from), occurrence.LocalStart.AddMinutes(to)));
                }
            }
        }

        var merged = new List<(DateTime Start, DateTime End)>();
        foreach (var piece in pieces.OrderBy(p => p.Start).ThenBy(p => p.End))
        {
            if (merged.Count > 0 && piece.Start <= merged[^1].End)
            {
                if (piece.End > merged[^1].End)
                {
                    merged[^1] = (merged[^1].Start, piece.End);
                }
            }
            else
            {
                merged.Add(piece);
            }
        }

        return merged.Where(p => p.End > fromLocal).ToList();
    }

    /// <summary>
    /// The local time at which <paramref name="minutes"/> of work, started at <paramref name="startLocal"/>, are done if the unit works only in its working intervals. Null when the unit has
    /// no shifts or the work does not fit into <paramref name="horizonDays"/>. Zero minutes end at the start.
    /// </summary>
    public DateTime? AddWorkingMinutes(long unitId, DateTime startLocal, decimal minutes, int horizonDays = 366)
    {
        if (minutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Minutes cannot be negative.");
        }

        if (minutes == 0)
        {
            return startLocal;
        }

        if (!HasShifts(unitId))
        {
            return null;
        }

        var remaining = minutes;
        foreach (var (start, end) in WorkingIntervals(unitId, startLocal, horizonDays))
        {
            var from = start > startLocal ? start : startLocal;
            var available = (decimal)(end - from).TotalMinutes;
            if (available <= 0)
            {
                continue;
            }

            if (available >= remaining)
            {
                return from.AddTicks((long)(remaining * TimeSpan.TicksPerMinute));
            }

            remaining -= available;
        }

        return null;
    }
}
