using ERP.MES.Application.Security;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Application.Persistence;

/// <summary>Everything the working calendar and the schedule rules are computed from, as stored (with row versions).</summary>
public sealed record CalendarConfiguration(
    IReadOnlyList<Stored<ShiftTemplate>> Templates,
    IReadOnlyList<Stored<WeeklyWorkday>> Weekly,
    IReadOnlyList<Stored<CalendarException>> Exceptions,
    IReadOnlyList<Stored<UnitShift>> UnitShifts)
{
    public WorkingCalendar ToCalendar() =>
        new(Weekly.Select(w => w.Value), Exceptions.Select(e => e.Value), UnitShifts.Select(u => u.Value), Templates.Select(t => t.Value));
}

/// <summary>The facts read INSIDE the transaction that adds a schedule row, so the D5 rules are checked against current data.</summary>
public sealed record ScheduleSnapshot(
    CalendarConfiguration Configuration,
    bool OperatorActive,
    IReadOnlyList<OperatorOffDay> OperatorOffDays,
    bool MachineUsable,
    IReadOnlyList<WorkerSchedule> PlannedRows);

/// <summary>Persistence of the Planning calendar and the worker schedule (Decision Log #100). Every write is one transaction with its audit entry and uses row versions.</summary>
public interface ISchedulingRepository
{
    Task<CalendarConfiguration> LoadConfigurationAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Stored<OperatorOffDay>>> ListOffDaysAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Stored<WorkerSchedule>>> ListSchedulesAsync(DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken = default);

    Task<Stored<ShiftTemplate>> AddTemplateAsync(ShiftTemplate template, AuditLogEntry audit, CancellationToken cancellationToken = default);

    Task<Stored<ShiftTemplate>> UpdateTemplateAsync(long id, long expectedVersion, Action<ShiftTemplate> change, AuditLogEntry audit, CancellationToken cancellationToken = default);

    Task<Stored<WeeklyWorkday>> SetWeekdayAsync(DayOfWeek day, long expectedVersion, bool isWorkday, DateTime atUtc, AuditLogEntry audit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a date-specific "own hours" shift of one unit in ONE transaction (Decision Log #123 Q17): the shift template, the unit-shift that runs on no regular day, and the exceptions that
    /// make the unit work only these hours on the date. The factories receive the new template id. Either everything is saved or nothing.
    /// </summary>
    Task<Stored<UnitShift>> AddDateHoursAsync(
        ShiftTemplate template, Func<long, UnitShift> unitShift, Func<long, IReadOnlyList<CalendarException>> exceptions, AuditLogEntry audit, CancellationToken cancellationToken = default);

    Task<Stored<CalendarException>> AddExceptionAsync(CalendarException value, AuditLogEntry audit, CancellationToken cancellationToken = default);

    Task<Stored<CalendarException>> CancelExceptionAsync(long id, long expectedVersion, AuditLogEntry audit, CancellationToken cancellationToken = default);

    Task<Stored<UnitShift>> AddUnitShiftAsync(UnitShift value, AuditLogEntry audit, CancellationToken cancellationToken = default);

    Task<Stored<UnitShift>> UpdateUnitShiftAsync(long id, long expectedVersion, Action<UnitShift> change, AuditLogEntry audit, CancellationToken cancellationToken = default);

    Task<Stored<OperatorOffDay>> AddOffDayAsync(OperatorOffDay value, AuditLogEntry audit, CancellationToken cancellationToken = default);

    Task<Stored<OperatorOffDay>> CancelOffDayAsync(long id, long expectedVersion, AuditLogEntry audit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a schedule row after <paramref name="validate"/> accepted it against a snapshot read in the same transaction; a refusal saves nothing. The refusal is returned, not thrown.
    /// </summary>
    Task<(Stored<WorkerSchedule>? Row, Requests.ValidationIssue Refusal)> AddScheduleAsync(
        WorkerSchedule row, Func<ScheduleSnapshot, Requests.ValidationIssue> validate, AuditLogEntry audit, CancellationToken cancellationToken = default);

    Task<Stored<WorkerSchedule>> CancelScheduleAsync(long id, long expectedVersion, string reason, long userId, DateTime atUtc, AuditLogEntry audit, CancellationToken cancellationToken = default);
}
