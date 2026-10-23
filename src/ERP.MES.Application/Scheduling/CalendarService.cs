using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Application.Scheduling;

/// <summary>What the signed-in user may do with the calendar and the worker schedule now (from the database).</summary>
public sealed record CalendarCapabilities(bool CanView, bool CanMaintain, bool CanScheduleWorkers)
{
    public static CalendarCapabilities None { get; } = new(false, false, false);
}

public sealed record CalendarView(CalendarConfiguration Configuration, CalendarCapabilities Capabilities);

/// <summary>
/// The Planning-owned working calendar (Decision Log #13, #100): shift templates as data, the regular weekly pattern, holidays and approved extra working days, and the shifts each unit runs with
/// its capacity. Planning maintains it (permission <see cref="Permission.MaintainHolidayCalendar"/>); Planning, Management and Production read it. Every change is audited and uses row versions. A
/// change that makes already planned workers invalid is allowed and reports how many rows are affected; nothing is cancelled silently.
/// </summary>
public sealed class CalendarService(
    ISchedulingRepository store,
    IPlanningMasterRepository planning,
    CurrentAuthorization authorization,
    IClock clock,
    IAuditContext auditContext)
{
    private static readonly Permission[] All = [Permission.ViewCalendar, Permission.MaintainHolidayCalendar, Permission.ScheduleWorkers];

    public async Task<CalendarCapabilities> GetCapabilitiesAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, All, cancellationToken);
        return set.Current is null ? CalendarCapabilities.None : new CalendarCapabilities(set.Has(Permission.ViewCalendar), set.Has(Permission.MaintainHolidayCalendar), set.Has(Permission.ScheduleWorkers));
    }

    public async Task<Outcome<CalendarView>> LoadAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, All, cancellationToken);
        if (!set.Has(Permission.ViewCalendar) || set.Current is null)
        {
            return Outcome<CalendarView>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
            Outcome<CalendarView>.Ok(new CalendarView(await store.LoadConfigurationAsync(cancellationToken), new CalendarCapabilities(true, set.Has(Permission.MaintainHolidayCalendar), set.Has(Permission.ScheduleWorkers)))));
    }

    public Task<Outcome<Stored<ShiftTemplate>>> AddTemplateAsync(
        Principal? principal, string? code, string? name, TimeOnly start, TimeOnly end, TimeOnly? breakStart, TimeOnly? breakEnd, CancellationToken cancellationToken = default) =>
        MaintainAsync<Stored<ShiftTemplate>>(principal, async (user, now) =>
        {
            ShiftTemplate template;
            try
            {
                template = new ShiftTemplate(0, code ?? string.Empty, name ?? string.Empty, start, end, breakStart, breakEnd, user.UserId, now);
            }
            catch (ArgumentException)
            {
                return Outcome<Stored<ShiftTemplate>>.Invalid(ValidationIssue.ShiftTimesInvalid);
            }

            var existing = await store.LoadConfigurationAsync(cancellationToken);
            if (existing.Templates.Any(t => string.Equals(t.Value.Code, template.Code, StringComparison.Ordinal)))
            {
                return Outcome<Stored<ShiftTemplate>>.Invalid(ValidationIssue.ShiftCodeDuplicate);
            }

            return Outcome<Stored<ShiftTemplate>>.Ok(await store.AddTemplateAsync(
                template, Audit(user, now, AuditActions.ShiftTemplateAdded, "ShiftTemplate", template.Code, $"Shift template {template.Code} ({template.Name}) {template.StartTime:HH\\:mm}-{template.EndTime:HH\\:mm} added"), cancellationToken));
        }, cancellationToken);

    public Task<Outcome<Stored<ShiftTemplate>>> UpdateTemplateAsync(
        Principal? principal, long id, long expectedVersion, string? name, TimeOnly start, TimeOnly end, TimeOnly? breakStart, TimeOnly? breakEnd, CancellationToken cancellationToken = default) =>
        MaintainAsync<Stored<ShiftTemplate>>(principal, async (user, now) =>
        {
            try
            {
                return Outcome<Stored<ShiftTemplate>>.Ok(await store.UpdateTemplateAsync(
                    id, expectedVersion, t => t.Define(name ?? string.Empty, start, end, breakStart, breakEnd, now),
                    Audit(user, now, AuditActions.ShiftTemplateChanged, "ShiftTemplate", id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Shift template {id} changed to {start:HH\\:mm}-{end:HH\\:mm}"), cancellationToken));
            }
            catch (ArgumentException)
            {
                return Outcome<Stored<ShiftTemplate>>.Invalid(ValidationIssue.ShiftTimesInvalid);
            }
        }, cancellationToken);

    public Task<Outcome<Stored<ShiftTemplate>>> SetTemplateActiveAsync(Principal? principal, long id, long expectedVersion, bool active, CancellationToken cancellationToken = default) =>
        MaintainAsync<Stored<ShiftTemplate>>(principal, async (user, now) =>
            Outcome<Stored<ShiftTemplate>>.Ok(await store.UpdateTemplateAsync(
                id, expectedVersion, t => t.SetActive(active, now),
                Audit(user, now, AuditActions.ShiftTemplateChanged, "ShiftTemplate", id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Shift template {id} {(active ? "activated" : "retired")}"), cancellationToken)), cancellationToken);

    public Task<Outcome<Stored<WeeklyWorkday>>> SetWeekdayAsync(Principal? principal, DayOfWeek day, long expectedVersion, bool isWorkday, CancellationToken cancellationToken = default) =>
        MaintainAsync<Stored<WeeklyWorkday>>(principal, async (user, now) =>
            Outcome<Stored<WeeklyWorkday>>.Ok(await store.SetWeekdayAsync(
                day, expectedVersion, isWorkday, now, Audit(user, now, AuditActions.WeeklyWorkdayChanged, "WeeklyWorkday", day.ToString(), $"{day} is {(isWorkday ? "a regular workday" : "a regular day off")}"), cancellationToken)), cancellationToken);

    /// <summary>Records a holiday, closure or approved extra working day. The value is the number of planned worker rows the new exception makes invalid (they are flagged, not cancelled).</summary>
    public Task<Outcome<int>> AddExceptionAsync(
        Principal? principal, DateOnly date, CalendarExceptionKind kind, long? unitId, long? templateId, string? reason, CancellationToken cancellationToken = default) =>
        MaintainAsync<int>(principal, async (user, now) =>
        {
            CalendarException value;
            try
            {
                value = new CalendarException(0, date, kind, unitId, templateId, reason ?? string.Empty, user.UserId, now);
            }
            catch (ArgumentException)
            {
                return Outcome<int>.Invalid(ValidationIssue.CalendarReasonRequired);
            }

            var config = await store.LoadConfigurationAsync(cancellationToken);
            if (unitId is { } u && (await planning.ListUnitsAsync(cancellationToken)).All(x => x.Value.Id != u || !x.Value.IsActive))
            {
                return Outcome<int>.Invalid(ValidationIssue.ProductionUnitInvalid);
            }

            if (templateId is { } t && config.Templates.All(x => x.Value.Id != t))
            {
                return Outcome<int>.Invalid(ValidationIssue.ScheduleUnitShiftNotConfigured);
            }

            if (config.Exceptions.Any(e => e.Value.IsActive && e.Value.Date == date && e.Value.ProductionUnitId == unitId && e.Value.ShiftTemplateId == templateId && e.Value.Kind == kind))
            {
                return Outcome<int>.Invalid(ValidationIssue.CalendarExceptionExists);
            }

            await store.AddExceptionAsync(value, Audit(user, now, AuditActions.CalendarExceptionAdded, "CalendarException", date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                $"{kind} on {date:yyyy-MM-dd} (unit {unitId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "all"}, shift {templateId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "all"}): {value.Reason}"), cancellationToken);
            return Outcome<int>.Ok(await CountAffectedRowsAsync(date, cancellationToken));
        }, cancellationToken);

    public Task<Outcome<int>> CancelExceptionAsync(Principal? principal, long id, long expectedVersion, CancellationToken cancellationToken = default) =>
        MaintainAsync<int>(principal, async (user, now) =>
        {
            var stored = await store.CancelExceptionAsync(id, expectedVersion,
                Audit(user, now, AuditActions.CalendarExceptionCancelled, "CalendarException", id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Calendar exception {id} cancelled"), cancellationToken);
            return Outcome<int>.Ok(await CountAffectedRowsAsync(stored.Value.Date, cancellationToken));
        }, cancellationToken);

    public Task<Outcome<Stored<UnitShift>>> AddUnitShiftAsync(
        Principal? principal, long unitId, long templateId, int maxOperators, int minOperators, bool overlapAllowed, CancellationToken cancellationToken = default) =>
        MaintainAsync<Stored<UnitShift>>(principal, async (user, now) =>
        {
            if ((await planning.ListUnitsAsync(cancellationToken)).All(x => x.Value.Id != unitId || !x.Value.IsActive))
            {
                return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.ProductionUnitInvalid);
            }

            var config = await store.LoadConfigurationAsync(cancellationToken);
            var template = config.Templates.FirstOrDefault(t => t.Value.Id == templateId && t.Value.IsActive);
            if (template is null)
            {
                return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.ScheduleUnitShiftNotConfigured);
            }

            if (config.UnitShifts.Any(u => u.Value.ProductionUnitId == unitId && u.Value.ShiftTemplateId == templateId))
            {
                return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.UnitShiftExists);
            }

            UnitShift value;
            try
            {
                value = new UnitShift(0, unitId, templateId, maxOperators, minOperators, overlapAllowed, user.UserId, now);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.CapacityInvalid);
            }

            if (!overlapAllowed && OverlapsAnotherShift(config, unitId, template.Value))
            {
                return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.UnitShiftOverlapNotMarked);
            }

            return Outcome<Stored<UnitShift>>.Ok(await store.AddUnitShiftAsync(value,
                Audit(user, now, AuditActions.UnitShiftAdded, "UnitShift", $"{unitId}/{templateId}", $"Unit {unitId} runs shift {template.Value.Code} with capacity {minOperators}..{maxOperators}{(overlapAllowed ? " (overlap marked)" : string.Empty)}"), cancellationToken));
        }, cancellationToken);

    public Task<Outcome<Stored<UnitShift>>> UpdateUnitShiftAsync(
        Principal? principal, long id, long expectedVersion, int maxOperators, int minOperators, bool overlapAllowed, CancellationToken cancellationToken = default) =>
        MaintainAsync<Stored<UnitShift>>(principal, async (user, now) =>
        {
            try
            {
                var config = await store.LoadConfigurationAsync(cancellationToken);
                var current = config.UnitShifts.FirstOrDefault(u => u.Value.Id == id);
                var template = current is null ? null : config.Templates.FirstOrDefault(t => t.Value.Id == current.Value.ShiftTemplateId);
                if (current is not null && template is not null && !overlapAllowed && OverlapsAnotherShift(config, current.Value.ProductionUnitId, template.Value, current.Value.Id))
                {
                    return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.UnitShiftOverlapNotMarked);
                }

                return Outcome<Stored<UnitShift>>.Ok(await store.UpdateUnitShiftAsync(id, expectedVersion, u => u.SetCapacity(maxOperators, minOperators, overlapAllowed, now),
                    Audit(user, now, AuditActions.UnitShiftChanged, "UnitShift", id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Unit shift {id} capacity {minOperators}..{maxOperators}{(overlapAllowed ? " (overlap marked)" : string.Empty)}"), cancellationToken));
            }
            catch (ArgumentOutOfRangeException)
            {
                return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.CapacityInvalid);
            }
        }, cancellationToken);

    public Task<Outcome<Stored<UnitShift>>> SetUnitShiftActiveAsync(Principal? principal, long id, long expectedVersion, bool active, CancellationToken cancellationToken = default) =>
        MaintainAsync<Stored<UnitShift>>(principal, async (user, now) =>
            Outcome<Stored<UnitShift>>.Ok(await store.UpdateUnitShiftAsync(id, expectedVersion, u => u.SetActive(active, now),
                Audit(user, now, AuditActions.UnitShiftChanged, "UnitShift", id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Unit shift {id} {(active ? "activated" : "retired")}"), cancellationToken)), cancellationToken);

    private async Task<Outcome<T>> MaintainAsync<T>(Principal? principal, Func<Principal, DateTime, Task<Outcome<T>>> action, CancellationToken cancellationToken)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainHolidayCalendar, cancellationToken);
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

    private async Task<int> CountAffectedRowsAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var config = await store.LoadConfigurationAsync(cancellationToken);
        var rows = await store.ListSchedulesAsync(date, date, cancellationToken);
        return rows.Count(r => WorkerScheduleRules.Flags(r.Value, config, []) != ScheduleFlags.None);
    }

    /// <summary>True when the template overlaps another active shift of the unit (occurrences on the same day or on neighbouring days are compared).</summary>
    internal static bool OverlapsAnotherShift(CalendarConfiguration config, long unitId, ShiftTemplate template, long? exceptUnitShiftId = null)
    {
        var reference = new DateOnly(2026, 1, 1);
        foreach (var other in config.UnitShifts.Where(u => u.Value.IsActive && u.Value.ProductionUnitId == unitId && u.Value.Id != exceptUnitShiftId))
        {
            var otherTemplate = config.Templates.FirstOrDefault(t => t.Value.Id == other.Value.ShiftTemplateId && t.Value.IsActive)?.Value;
            if (otherTemplate is null || otherTemplate.Id == template.Id)
            {
                continue;
            }

            for (var shift = -1; shift <= 1; shift++)
            {
                var a = reference.ToDateTime(template.StartTime);
                var b = reference.AddDays(shift).ToDateTime(otherTemplate.StartTime);
                if (a < b.AddMinutes(otherTemplate.TotalMinutes) && b < a.AddMinutes(template.TotalMinutes))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private AuditLogEntry Audit(Principal user, DateTime now, string code, string targetType, string targetId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, targetType, targetId, summary);
}
