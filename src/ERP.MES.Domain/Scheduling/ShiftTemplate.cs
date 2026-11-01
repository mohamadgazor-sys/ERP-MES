using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Scheduling;

/// <summary>
/// A shift pattern, stored as DATA (Decision Log #13, #100): code, local start and end time and an optional break. The reference values (08:00-17:00 with a 12:00-13:00 break;
/// 06:00-14:00, 14:00-22:00, 22:00-06:00) are examples Planning enters, never constants. A shift whose end is not after its start crosses midnight; an occurrence is identified by its START
/// date (a shift starting on date D at 22:00 and ending at 06:00 belongs to D). Templates are retired, never deleted.
/// </summary>
public sealed class ShiftTemplate
{
    private ShiftTemplate()
    {
    }

    public ShiftTemplate(long id, string code, string name, TimeOnly startTime, TimeOnly endTime, TimeOnly? breakStart, TimeOnly? breakEnd, long createdByUserId, DateTime createdAtUtc)
    {
        Id = id;
        Code = UnitOfMeasure.RequireCode(code);
        Define(name, startTime, endTime, breakStart, breakEnd, createdAtUtc);
        IsActive = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Requirements.Utc(createdAtUtc, nameof(createdAtUtc));
    }

    public long Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public TimeOnly? BreakStart { get; private set; }

    public TimeOnly? BreakEnd { get; private set; }

    public bool IsActive { get; private set; }

    public long CreatedByUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>True when the shift ends after midnight (its end time is not after its start time).</summary>
    public bool CrossesMidnight => EndTime <= StartTime;

    /// <summary>Length from start to end, break included, in minutes (1..1439).</summary>
    public int TotalMinutes => (int)(CrossesMidnight ? TimeSpan.FromHours(24) - (StartTime - EndTime) : EndTime - StartTime).TotalMinutes;

    public int BreakMinutes => BreakStart is { } s && BreakEnd is { } e ? OffsetMinutes(e) - OffsetMinutes(s) : 0;

    /// <summary>Minutes of work in one occurrence: the length minus the break.</summary>
    public int WorkingMinutes => TotalMinutes - BreakMinutes;

    /// <summary>Minutes from the start of the shift to a clock time of the shift (past midnight counts on).</summary>
    public int OffsetMinutes(TimeOnly time)
    {
        var diff = (int)(time - StartTime).TotalMinutes;
        return diff < 0 ? diff + 1440 : diff;
    }

    /// <summary>The working pieces of one occurrence as minute offsets from the start: the whole shift, or the part before and the part after the break.</summary>
    public IReadOnlyList<(int From, int To)> WorkingPieces()
    {
        if (BreakStart is not { } bs || BreakEnd is not { } be)
        {
            return [(0, TotalMinutes)];
        }

        var from = OffsetMinutes(bs);
        var to = OffsetMinutes(be);
        var pieces = new List<(int, int)>();
        if (from > 0)
        {
            pieces.Add((0, from));
        }

        if (to < TotalMinutes)
        {
            pieces.Add((to, TotalMinutes));
        }

        return pieces;
    }

    public void Define(string name, TimeOnly startTime, TimeOnly endTime, TimeOnly? breakStart, TimeOnly? breakEnd, DateTime changedAtUtc)
    {
        var cleaned = UnitOfMeasure.RequireName(name);
        if (startTime == endTime)
        {
            throw new ArgumentException("A shift must have a start time different from its end time.", nameof(endTime));
        }

        if ((breakStart is null) != (breakEnd is null))
        {
            throw new ArgumentException("A break needs both a start and an end time.", nameof(breakEnd));
        }

        var candidate = new ShiftTemplate { StartTime = startTime, EndTime = endTime };
        if (breakStart is { } bs && breakEnd is { } be)
        {
            var from = candidate.OffsetMinutes(bs);
            var to = candidate.OffsetMinutes(be);
            if (from <= 0 || to <= from || to >= candidate.TotalMinutes)
            {
                throw new ArgumentException("The break must lie inside the shift, after its start and before its end.", nameof(breakStart));
            }
        }

        Name = cleaned;
        StartTime = startTime;
        EndTime = endTime;
        BreakStart = breakStart;
        BreakEnd = breakEnd;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    public void SetActive(bool active, DateTime changedAtUtc)
    {
        IsActive = active;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }
}

/// <summary>The company's regular workdays by weekday (Decision Log #13: Saturday to Thursday by default, Friday normally off). Seven rows exist; Planning edits them.</summary>
public sealed class WeeklyWorkday
{
    private WeeklyWorkday()
    {
    }

    public WeeklyWorkday(DayOfWeek day, bool isWorkday, DateTime atUtc)
    {
        Day = day;
        IsWorkday = isWorkday;
        UpdatedAtUtc = Requirements.Utc(atUtc, nameof(atUtc));
    }

    public DayOfWeek Day { get; private set; }

    public bool IsWorkday { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void Set(bool isWorkday, DateTime atUtc)
    {
        IsWorkday = isWorkday;
        UpdatedAtUtc = Requirements.Utc(atUtc, nameof(atUtc));
    }
}

public enum CalendarExceptionKind
{
    /// <summary>The day (or unit, or shift) does not work although the regular pattern says it does (holiday, closure).</summary>
    Off = 1,

    /// <summary>The day works although the regular pattern says it does not (for example an approved Friday).</summary>
    Working,
}

/// <summary>
/// A holiday, closure or approved extra working day (Decision Log #13, #100). Scope: the whole company (no unit, no shift), one unit, one shift of every unit or one shift of one unit; the
/// more specific scope wins, and an Off exception wins over a Working one of the same scope. A reason is required (for an extra working day it names the approval). Cancelled, never deleted.
/// </summary>
public sealed class CalendarException
{
    private CalendarException()
    {
    }

    public CalendarException(long id, DateOnly date, CalendarExceptionKind kind, long? productionUnitId, long? shiftTemplateId, string reason, long recordedByUserId, DateTime recordedAtUtc)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown exception kind.");
        }

        Id = id;
        Date = date;
        Kind = kind;
        ProductionUnitId = productionUnitId;
        ShiftTemplateId = shiftTemplateId;
        Reason = RequireReason(reason);
        RecordedByUserId = recordedByUserId;
        RecordedAtUtc = Requirements.Utc(recordedAtUtc, nameof(recordedAtUtc));
        IsActive = true;
    }

    public const int MaxReasonLength = 300;

    public long Id { get; private set; }

    public DateOnly Date { get; private set; }

    public CalendarExceptionKind Kind { get; private set; }

    public long? ProductionUnitId { get; private set; }

    public long? ShiftTemplateId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public long RecordedByUserId { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    public bool IsActive { get; private set; }

    public void Cancel() => IsActive = false;

    public static string RequireReason(string? reason)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxReasonLength)
        {
            throw new ArgumentException("A reason of 1 to " + MaxReasonLength + " characters is required.", nameof(reason));
        }

        return text;
    }
}

/// <summary>
/// A shift a production unit runs, with its own staffing capacity (Decision Log #95 D5, #100). Creating a row for a template that overlaps another shift of the same unit is the explicit
/// marking Planning must make (<see cref="OverlapAllowed"/>); each row has its own capacity. <see cref="MaxOperators"/> is a hard limit on scheduled workers; a shortage below
/// <see cref="MinOperators"/> is only a warning.
/// </summary>
public sealed class UnitShift
{
    private UnitShift()
    {
    }

    public UnitShift(long id, long productionUnitId, long shiftTemplateId, int maxOperators, int minOperators, bool overlapAllowed, long createdByUserId, DateTime createdAtUtc)
    {
        Id = id;
        ProductionUnitId = productionUnitId;
        ShiftTemplateId = shiftTemplateId;
        SetCapacity(maxOperators, minOperators, overlapAllowed, createdAtUtc);
        IsActive = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Requirements.Utc(createdAtUtc, nameof(createdAtUtc));
    }

    public const int MaxCapacity = 1000;

    public long Id { get; private set; }

    public long ProductionUnitId { get; private set; }

    public long ShiftTemplateId { get; private set; }

    public int MaxOperators { get; private set; }

    public int MinOperators { get; private set; }

    public bool OverlapAllowed { get; private set; }

    /// <summary>
    /// The weekdays this shift runs regularly, as a bit mask (bit n = <see cref="DayOfWeek"/> n); null means it follows the company's weekly pattern, which is how every shift behaved before recurring unit
    /// schedules (Decision Log #123 Q13). A mask makes it a recurring unit schedule: it runs on exactly those weekdays, even where the company pattern says off (for example Friday 06:00-12:00 of a CNC
    /// unit); 0 means it runs on no regular day, which is how a date-specific "own hours" shift exists only where a Working exception names the date.
    /// </summary>
    public int? RegularDays { get; private set; }

    public const int AllDaysMask = 0b111_1111;

    /// <summary>True when the shift can run on this weekday according to its own schedule; a shift without a mask follows the company pattern, so it can run on any weekday.</summary>
    public bool CanRunOn(DayOfWeek day) => RegularDays is not { } mask || (mask >> (int)day & 1) == 1;

    public static int MaskOf(IEnumerable<DayOfWeek> days) => days.Aggregate(0, (mask, day) => mask | 1 << (int)day);

    public bool IsActive { get; private set; }

    public long CreatedByUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Sets the regular weekdays (null = follow the company pattern; 0 = no regular day). Only the seven weekday bits are valid.</summary>
    public void SetRegularDays(int? mask, DateTime changedAtUtc)
    {
        if (mask is < 0 or > AllDaysMask)
        {
            throw new ArgumentOutOfRangeException(nameof(mask), mask, "Only the seven weekday bits are valid.");
        }

        RegularDays = mask;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    public void SetCapacity(int maxOperators, int minOperators, bool overlapAllowed, DateTime changedAtUtc)
    {
        if (maxOperators < 1 || maxOperators > MaxCapacity || minOperators < 0 || minOperators > maxOperators)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOperators), maxOperators, "Capacity needs 1 <= max <= " + MaxCapacity + " and 0 <= min <= max.");
        }

        MaxOperators = maxOperators;
        MinOperators = minOperators;
        OverlapAllowed = overlapAllowed;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    public void SetActive(bool active, DateTime changedAtUtc)
    {
        IsActive = active;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }
}

public enum OffDayKind
{
    Leave = 1,
    Sick,
    Mission,
    Other,
}

/// <summary>An operator's approved absence (inclusive date range). Recorded by Planning; cancelled, never deleted.</summary>
public sealed class OperatorOffDay
{
    private OperatorOffDay()
    {
    }

    public OperatorOffDay(long id, long operatorId, DateOnly fromDate, DateOnly toDate, OffDayKind kind, string? note, long recordedByUserId, DateTime recordedAtUtc)
    {
        if (toDate < fromDate)
        {
            throw new ArgumentException("The last day cannot be before the first day.", nameof(toDate));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown off-day kind.");
        }

        var text = note?.Trim();
        if (text is { Length: > 300 })
        {
            throw new ArgumentException("The note is at most 300 characters.", nameof(note));
        }

        Id = id;
        OperatorId = operatorId;
        FromDate = fromDate;
        ToDate = toDate;
        Kind = kind;
        Note = string.IsNullOrEmpty(text) ? null : text;
        RecordedByUserId = recordedByUserId;
        RecordedAtUtc = Requirements.Utc(recordedAtUtc, nameof(recordedAtUtc));
        IsActive = true;
    }

    public long Id { get; private set; }

    public long OperatorId { get; private set; }

    public DateOnly FromDate { get; private set; }

    public DateOnly ToDate { get; private set; }

    public OffDayKind Kind { get; private set; }

    public string? Note { get; private set; }

    public long RecordedByUserId { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    public bool IsActive { get; private set; }

    public bool Covers(DateOnly date) => IsActive && date >= FromDate && date <= ToDate;

    public void Cancel() => IsActive = false;
}

public enum WorkerScheduleStatus
{
    Planned = 1,
    Cancelled,
}

/// <summary>
/// An operator planned on a unit's shift on a date (optionally on a machine). The Application checks the D5 rules when it is created (unit-shift working that day, operator not absent, no
/// overlapping occurrence for the operator, capacity, one operator per machine at a time except a recorded handover). Cancelled with a reason, never deleted.
/// </summary>
public sealed class WorkerSchedule
{
    private WorkerSchedule()
    {
    }

    public WorkerSchedule(
        long id, long operatorId, DateOnly date, long productionUnitId, long shiftTemplateId, long? machineId, int? handoverMinutes, string? handoverNote, long createdByUserId, DateTime createdAtUtc)
    {
        if (handoverMinutes is < 1 or > 480)
        {
            throw new ArgumentOutOfRangeException(nameof(handoverMinutes), handoverMinutes, "A handover overlap is between 1 and 480 minutes.");
        }

        var note = handoverNote?.Trim();
        if (handoverMinutes is not null && (machineId is null || string.IsNullOrEmpty(note) || note.Length > 300))
        {
            throw new ArgumentException("A handover overlap needs a machine and a note of 1 to 300 characters.", nameof(handoverNote));
        }

        Id = id;
        OperatorId = operatorId;
        Date = date;
        ProductionUnitId = productionUnitId;
        ShiftTemplateId = shiftTemplateId;
        MachineId = machineId;
        HandoverMinutes = handoverMinutes;
        HandoverNote = handoverMinutes is null ? null : note;
        Status = WorkerScheduleStatus.Planned;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Requirements.Utc(createdAtUtc, nameof(createdAtUtc));
    }

    public long Id { get; private set; }

    public long OperatorId { get; private set; }

    public DateOnly Date { get; private set; }

    public long ProductionUnitId { get; private set; }

    public long ShiftTemplateId { get; private set; }

    public long? MachineId { get; private set; }

    /// <summary>The stated length of an explicit handover overlap on the machine; null for an ordinary booking.</summary>
    public int? HandoverMinutes { get; private set; }

    public string? HandoverNote { get; private set; }

    public WorkerScheduleStatus Status { get; private set; }

    public long CreatedByUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public string? CancelReason { get; private set; }

    public long? CancelledByUserId { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public bool IsPlanned => Status == WorkerScheduleStatus.Planned;

    public void Cancel(string? reason, long userId, DateTime atUtc)
    {
        if (!IsPlanned)
        {
            throw new InvalidOperationException("The schedule row is already cancelled.");
        }

        CancelReason = CalendarException.RequireReason(reason);
        CancelledByUserId = userId;
        CancelledAtUtc = Requirements.Utc(atUtc, nameof(atUtc));
        Status = WorkerScheduleStatus.Cancelled;
    }
}
