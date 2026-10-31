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
        Principal? principal, long unitId, long templateId, int maxOperators, int minOperators, bool overlapAllowed, CancellationToken cancellationToken = default, int? regularDays = null) =>
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
                if (regularDays is not null)
                {
                    value.SetRegularDays(regularDays, now);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.CapacityInvalid);
            }

            if (!overlapAllowed && OverlapsAnotherShift(config, unitId, template.Value, null, regularDays))
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
                if (current is not null && template is not null && !overlapAllowed && OverlapsAnotherShift(config, current.Value.ProductionUnitId, template.Value, current.Value.Id, current.Value.RegularDays))
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

    /// <summary>
    /// Sets the weekdays a unit-shift runs on (a recurring unit schedule, Decision Log #123 Q13): a mask makes it run on exactly those weekdays even where the company pattern says off, null makes it follow the
    /// company pattern again. A change that makes the shift overlap another shift of the unit is refused unless overlap was marked allowed.
    /// </summary>
    public Task<Outcome<Stored<UnitShift>>> SetUnitShiftDaysAsync(Principal? principal, long id, long expectedVersion, int? regularDays, CancellationToken cancellationToken = default) =>
        MaintainAsync<Stored<UnitShift>>(principal, async (user, now) =>
        {
            if (regularDays is < 0 or > UnitShift.AllDaysMask)
            {
                return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.UnitShiftDaysInvalid);
            }

            var config = await store.LoadConfigurationAsync(cancellationToken);
            var current = config.UnitShifts.FirstOrDefault(u => u.Value.Id == id);
            var template = current is null ? null : config.Templates.FirstOrDefault(t => t.Value.Id == current.Value.ShiftTemplateId);
            if (current is not null && template is not null && !current.Value.OverlapAllowed && OverlapsAnotherShift(config, current.Value.ProductionUnitId, template.Value, current.Value.Id, regularDays))
            {
                return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.UnitShiftOverlapNotMarked);
            }

            return Outcome<Stored<UnitShift>>.Ok(await store.UpdateUnitShiftAsync(id, expectedVersion, u => u.SetRegularDays(regularDays, now),
                Audit(user, now, AuditActions.UnitShiftChanged, "UnitShift", id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"Unit shift {id} runs on {(regularDays is null ? "the company pattern" : "weekday mask " + regularDays.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}"), cancellationToken));
        }, cancellationToken);

    /// <summary>
    /// A date-specific exception of one unit with its own working hours (Decision Log #123 Q17): on that date the unit works only the given interval (which may cross midnight), whatever its regular shifts or
    /// the company pattern say; several intervals on one date are added one after another. Stored in one transaction as a dedicated shift that runs on no regular day, a unit-level Off exception for the date
    /// and a Working exception for that shift. Refused when the interval overlaps another interval the unit works around that date.
    /// </summary>
    public Task<Outcome<Stored<UnitShift>>> AddUnitDateHoursAsync(
        Principal? principal, long unitId, DateOnly date, TimeOnly start, TimeOnly end, int maxOperators, string? reason, CancellationToken cancellationToken = default) =>
        MaintainAsync<Stored<UnitShift>>(principal, async (user, now) =>
        {
            try
            {
                _ = CalendarException.RequireReason(reason);
            }
            catch (ArgumentException)
            {
                return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.CalendarReasonRequired);
            }

            if ((await planning.ListUnitsAsync(cancellationToken)).All(x => x.Value.Id != unitId || !x.Value.IsActive))
            {
                return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.ProductionUnitInvalid);
            }

            var config = await store.LoadConfigurationAsync(cancellationToken);
            // At most 20 characters of A-Z, 0-9 and '-': D + yyMMdd + the two times + the unit in base 36.
            var code = $"D{date:yyMMdd}{start:HHmm}{end:HHmm}-{Base36(unitId)}";
            ShiftTemplate template;
            try
            {
                template = new ShiftTemplate(0, code, $"ساعت ویژه {date:yyyy-MM-dd} {start:HH\\:mm}-{end:HH\\:mm}", start, end, null, null, user.UserId, now);
            }
            catch (ArgumentException)
            {
                return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.ShiftTimesInvalid);
            }

            if (config.Templates.Any(t => string.Equals(t.Value.Code, code, StringComparison.Ordinal)))
            {
                return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.CalendarExceptionExists);
            }

            UnitShift probeShift;
            try
            {
                probeShift = new UnitShift(-2, unitId, -1, maxOperators, 0, false, user.UserId, now);
                probeShift.SetRegularDays(0, now);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.CapacityInvalid);
            }

            var unitOffExists = config.Exceptions.Any(e => e.Value.IsActive && e.Value.Date == date && e.Value.ProductionUnitId == unitId && e.Value.ShiftTemplateId is null && e.Value.Kind == CalendarExceptionKind.Off);
            var probeTemplate = new ShiftTemplate(-1, code, "probe", start, end, null, null, user.UserId, now);
            var probeExceptions = new List<CalendarException>(config.Exceptions.Select(e => e.Value))
            {
                new(0, date, CalendarExceptionKind.Working, unitId, -1, reason!, user.UserId, now),
            };
            if (!unitOffExists)
            {
                probeExceptions.Add(new CalendarException(0, date, CalendarExceptionKind.Off, unitId, null, reason!, user.UserId, now));
            }

            var probe = new WorkingCalendar(config.Weekly.Select(w => w.Value), probeExceptions, [.. config.UnitShifts.Select(u => u.Value), probeShift], [.. config.Templates.Select(t => t.Value), probeTemplate]);
            for (var offset = -1; offset <= 1; offset++)
            {
                var occurrences = probe.Occurrences(unitId, date.AddDays(offset));
                var mine = occurrences.FirstOrDefault(o => o.Template.Id == -1);
                if (mine is null)
                {
                    continue;
                }

                if (probe.Occurrences(unitId, date.AddDays(-1)).Concat(probe.Occurrences(unitId, date)).Concat(probe.Occurrences(unitId, date.AddDays(1))).Any(o => o.Template.Id != -1 && o.Overlaps(mine)))
                {
                    return Outcome<Stored<UnitShift>>.Invalid(ValidationIssue.UnitShiftOverlapNotMarked);
                }
            }

            var added = await store.AddDateHoursAsync(
                template,
                templateId =>
                {
                    var shift = new UnitShift(0, unitId, templateId, maxOperators, 0, false, user.UserId, now);
                    shift.SetRegularDays(0, now);
                    return shift;
                },
                templateId =>
                {
                    var list = new List<CalendarException> { new(0, date, CalendarExceptionKind.Working, unitId, templateId, reason!, user.UserId, now) };
                    if (!unitOffExists)
                    {
                        list.Add(new CalendarException(0, date, CalendarExceptionKind.Off, unitId, null, reason!, user.UserId, now));
                    }

                    return list;
                },
                Audit(user, now, AuditActions.CalendarExceptionAdded, "CalendarException", date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    $"Unit {unitId} works {start:HH\\:mm}-{end:HH\\:mm} on {date:yyyy-MM-dd} (own hours): {reason}"),
                cancellationToken);
            return Outcome<Stored<UnitShift>>.Ok(added);
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

    private static string Base36(long value)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var text = string.Empty;
        do
        {
            text = digits[(int)(value % 36)] + text;
            value /= 36;
        }
        while (value > 0);
        return text;
    }

    private async Task<int> CountAffectedRowsAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var config = await store.LoadConfigurationAsync(cancellationToken);
        var rows = await store.ListSchedulesAsync(date, date, cancellationToken);
        return rows.Count(r => WorkerScheduleRules.Flags(r.Value, config, []) != ScheduleFlags.None);
    }

    /// <summary>True when the template overlaps another active shift of the unit (occurrences on the same day or on neighbouring days are compared).</summary>
    internal static bool OverlapsAnotherShift(CalendarConfiguration config, long unitId, ShiftTemplate template, long? exceptUnitShiftId = null, int? candidateRegularDays = null)
    {
        // Two shifts can collide only on weekdays both can run (a shift without its own weekdays can run on any day), including a midnight spill into the next day (Decision Log #123 Q13).
        // A shift without its own weekdays follows the company pattern, so it runs only on the company's workdays.
        bool CanRun(int? mask, DayOfWeek day) => mask is { } m ? (m >> (int)day & 1) == 1 : config.Weekly.Where(w => w.Value.Day == day).Select(w => w.Value.IsWorkday).DefaultIfEmpty(true).First();
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
                    // The overlap exists on some weekday d (candidate) when the other shift runs on d + shift; check all seven.
                    for (var d = 0; d < 7; d++)
                    {
                        var day = (DayOfWeek)d;
                        var otherDay = (DayOfWeek)(((d + shift) % 7 + 7) % 7);
                        if (CanRun(candidateRegularDays, day) && CanRun(other.Value.RegularDays, otherDay))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private AuditLogEntry Audit(Principal user, DateTime now, string code, string targetType, string targetId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, targetType, targetId, summary);
}
