using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Application.Persistence;

/// <summary>The facts read INSIDE the transaction that adds or changes an overtime assignment, so the rules are checked against current data.</summary>
public sealed record OvertimeContext(
    OvertimeLimits? Limits,
    bool OperatorActive,
    bool MachineUsable,
    IReadOnlyList<OperatorOffDay> OperatorOffDays,
    IReadOnlyList<WorkerSchedule> PlannedRows,
    IReadOnlyList<ShiftTemplate> Templates,
    IReadOnlyList<OvertimeAssignment> Assignments,
    WorkingCalendar? Calendar = null);

/// <summary>Persistence of overtime assignments and the overtime limits (Decision Log #103). Every write is one transaction with its audit entry and uses row versions.</summary>
public interface IOvertimeRepository
{
    Task<IReadOnlyList<Stored<OvertimeAssignment>>> ListAsync(DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken = default);

    Task<Stored<OvertimeLimits>?> GetLimitsAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the limits; <paramref name="expectedVersion"/> is 0 while none are configured. A stale version fails with the concurrency exception.</summary>
    Task<Stored<OvertimeLimits>> SetLimitsAsync(long expectedVersion, int maxMinutesPerDay, int maxMinutesPerWeek, long userId, DateTime atUtc, AuditLogEntry audit, CancellationToken cancellationToken = default);

    /// <summary>Adds the assignment after <paramref name="validate"/> accepted it against a context read in the same transaction; a refusal saves nothing.</summary>
    Task<(Stored<OvertimeAssignment>? Row, ValidationIssue Refusal)> AddAsync(
        OvertimeAssignment row, Func<OvertimeContext, ValidationIssue> validate, AuditLogEntry audit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the assignment under <paramref name="expectedVersion"/> with the current context and lets <paramref name="change"/> apply a transition; a refusal saves nothing.
    /// A domain rule violation inside <paramref name="change"/> surfaces as <see cref="InvalidOperationException"/>.
    /// </summary>
    Task<(Stored<OvertimeAssignment>? Row, ValidationIssue Refusal)> ChangeAsync(
        long id, long expectedVersion, Func<OvertimeAssignment, OvertimeContext, ValidationIssue> change, AuditLogEntry audit, CancellationToken cancellationToken = default);
}
