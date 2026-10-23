using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Application.Scheduling;

/// <summary>A worker schedule row with its flags (a row that no longer satisfies the calendar is flagged, never cancelled silently).</summary>
public sealed record ScheduleRowView(Stored<WorkerSchedule> Row, string OperatorName, ScheduleFlags Flags);

public sealed record ScheduleView(IReadOnlyList<ScheduleRowView> Rows, IReadOnlyList<Stored<OperatorOffDay>> OffDays, CalendarConfiguration Configuration, CalendarCapabilities Capabilities);

/// <summary>
/// Plans operators on unit shifts and records their off-days (Decision Log #95 D5, #100). Planning only. Creating a row checks, inside the same transaction, that the unit runs the shift and it
/// works that day, the operator is active and not absent, a worker is never in two overlapping occurrences, the unit-shift's maximum is not exceeded and a machine has one operator at a time
/// (except an explicit handover overlap). Marking a day off or recording an off-day afterwards is allowed; the rows that become invalid are flagged for Planning, not cancelled.
/// </summary>
public sealed class WorkerScheduleService(
    ISchedulingRepository store,
    IOperatorRepository operators,
    CurrentAuthorization authorization,
    IClock clock,
    IAuditContext auditContext)
{
    private static readonly Permission[] All = [Permission.ViewCalendar, Permission.MaintainHolidayCalendar, Permission.ScheduleWorkers];

    public async Task<Outcome<ScheduleView>> LoadAsync(Principal? principal, DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, All, cancellationToken);
        if (!set.Has(Permission.ViewCalendar) || set.Current is null)
        {
            return Outcome<ScheduleView>.Fail(OperationStatus.NotAuthorized);
        }

        if (toInclusive < from || toInclusive.DayNumber - from.DayNumber > 400)
        {
            return Outcome<ScheduleView>.Invalid(ValidationIssue.EndBeforeStart);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var config = await store.LoadConfigurationAsync(cancellationToken);
            var offDays = await store.ListOffDaysAsync(cancellationToken);
            var names = (await operators.ListOperatorsAsync(cancellationToken)).ToDictionary(o => o.Value.Id, o => o.Value.FullName);
            var rows = (await store.ListSchedulesAsync(from, toInclusive, cancellationToken))
                .Select(r => new ScheduleRowView(r, names.GetValueOrDefault(r.Value.OperatorId, string.Empty), WorkerScheduleRules.Flags(r.Value, config, offDays.Select(o => o.Value).Where(o => o.IsActive).ToList())))
                .OrderBy(r => r.Row.Value.Date).ThenBy(r => r.Row.Value.ProductionUnitId).ThenBy(r => r.Row.Value.ShiftTemplateId).ThenBy(r => r.OperatorName, StringComparer.Ordinal).ToList();
            return Outcome<ScheduleView>.Ok(new ScheduleView(rows, offDays, config, new CalendarCapabilities(true, set.Has(Permission.MaintainHolidayCalendar), set.Has(Permission.ScheduleWorkers))));
        });
    }

    public Task<Outcome<Stored<WorkerSchedule>>> ScheduleAsync(
        Principal? principal, long operatorId, DateOnly date, long unitId, long templateId, long? machineId, int? handoverMinutes, string? handoverNote, CancellationToken cancellationToken = default) =>
        ScheduleWorkersAsync<Stored<WorkerSchedule>>(principal, async (user, now) =>
        {
            WorkerSchedule row;
            try
            {
                row = new WorkerSchedule(0, operatorId, date, unitId, templateId, machineId, handoverMinutes, handoverNote, user.UserId, now);
            }
            catch (ArgumentException)
            {
                return Outcome<Stored<WorkerSchedule>>.Invalid(ValidationIssue.HandoverInvalid);
            }

            var (stored, refusal) = await store.AddScheduleAsync(
                row,
                snapshot => WorkerScheduleRules.Check(row, snapshot),
                Audit(user, now, AuditActions.WorkerScheduled, "WorkerSchedule", $"{operatorId}/{date:yyyy-MM-dd}", $"Operator {operatorId} planned on unit {unitId}, shift {templateId}, {date:yyyy-MM-dd}{(machineId is null ? string.Empty : ", machine " + machineId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}"),
                cancellationToken);
            return refusal == ValidationIssue.None ? Outcome<Stored<WorkerSchedule>>.Ok(stored!) : Outcome<Stored<WorkerSchedule>>.Invalid(refusal);
        }, cancellationToken);

    public Task<Outcome<Stored<WorkerSchedule>>> CancelScheduleAsync(Principal? principal, long id, long expectedVersion, string? reason, CancellationToken cancellationToken = default) =>
        ScheduleWorkersAsync<Stored<WorkerSchedule>>(principal, async (user, now) =>
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > CalendarException.MaxReasonLength)
            {
                return Outcome<Stored<WorkerSchedule>>.Invalid(ValidationIssue.CalendarReasonRequired);
            }

            return Outcome<Stored<WorkerSchedule>>.Ok(await store.CancelScheduleAsync(id, expectedVersion, reason.Trim(), user.UserId, now,
                Audit(user, now, AuditActions.WorkerScheduleCancelled, "WorkerSchedule", id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Worker schedule row {id} cancelled: {reason.Trim()}"), cancellationToken));
        }, cancellationToken);

    /// <summary>Records an operator absence. The value is the number of planned rows it makes invalid (flagged, not cancelled).</summary>
    public Task<Outcome<int>> AddOffDayAsync(Principal? principal, long operatorId, DateOnly from, DateOnly to, OffDayKind kind, string? note, CancellationToken cancellationToken = default) =>
        ScheduleWorkersAsync<int>(principal, async (user, now) =>
        {
            OperatorOffDay value;
            try
            {
                value = new OperatorOffDay(0, operatorId, from, to, kind, note, user.UserId, now);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Outcome<int>.Invalid(ValidationIssue.EndBeforeStart);
            }
            catch (ArgumentException)
            {
                return Outcome<int>.Invalid(ValidationIssue.EndBeforeStart);
            }

            if ((await operators.ListOperatorsAsync(cancellationToken)).All(o => o.Value.Id != operatorId || !o.Value.IsActive))
            {
                return Outcome<int>.Invalid(ValidationIssue.OperatorInvalid);
            }

            await store.AddOffDayAsync(value, Audit(user, now, AuditActions.OperatorOffDayAdded, "OperatorOffDay", operatorId.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Operator {operatorId} absent {from:yyyy-MM-dd}..{to:yyyy-MM-dd} ({kind})"), cancellationToken);
            var rows = await store.ListSchedulesAsync(from, to, cancellationToken);
            return Outcome<int>.Ok(rows.Count(r => r.Value.IsPlanned && r.Value.OperatorId == operatorId));
        }, cancellationToken);

    public Task<Outcome<Stored<OperatorOffDay>>> CancelOffDayAsync(Principal? principal, long id, long expectedVersion, CancellationToken cancellationToken = default) =>
        ScheduleWorkersAsync<Stored<OperatorOffDay>>(principal, async (user, now) =>
            Outcome<Stored<OperatorOffDay>>.Ok(await store.CancelOffDayAsync(id, expectedVersion,
                Audit(user, now, AuditActions.OperatorOffDayCancelled, "OperatorOffDay", id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Operator off-day {id} cancelled"), cancellationToken)), cancellationToken);

    private async Task<Outcome<T>> ScheduleWorkersAsync<T>(Principal? principal, Func<Principal, DateTime, Task<Outcome<T>>> action, CancellationToken cancellationToken)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ScheduleWorkers, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<T>.Fail(OperationStatus.NotAuthorized);
        }

        try
        {
            return await CustomerService.GuardAsync(() => action(decision.Current!, clock.UtcNow));
        }
        catch (InvalidOperationException)
        {
            return Outcome<T>.Fail(OperationStatus.InvalidState);
        }
    }

    private AuditLogEntry Audit(Principal user, DateTime now, string code, string targetType, string targetId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, targetType, targetId, summary);
}
