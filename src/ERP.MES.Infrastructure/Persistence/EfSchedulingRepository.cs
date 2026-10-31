using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>The Planning calendar and worker schedule on <see cref="ErpMesDbContext"/> (provider-neutral). Each write is one SaveChanges with its audit entry under the row's expected version.</summary>
public sealed class EfSchedulingRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : ISchedulingRepository
{
    public async Task<CalendarConfiguration> LoadConfigurationAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await LoadConfigurationAsync(context, cancellationToken);
    }

    public async Task<IReadOnlyList<Stored<OperatorOffDay>>> ListOffDaysAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await context.OperatorOffDays.OrderBy(o => o.FromDate).ThenBy(o => o.Id).ToListAsync(cancellationToken);
        return list.Select(o => new Stored<OperatorOffDay>(o, context.GetRowVersion(o))).ToList();
    }

    public async Task<IReadOnlyList<Stored<WorkerSchedule>>> ListSchedulesAsync(DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await context.WorkerSchedules.Where(w => w.Date >= from && w.Date <= toInclusive).OrderBy(w => w.Date).ThenBy(w => w.Id).ToListAsync(cancellationToken);
        return list.Select(w => new Stored<WorkerSchedule>(w, context.GetRowVersion(w))).ToList();
    }

    public async Task<Stored<ShiftTemplate>> AddTemplateAsync(ShiftTemplate template, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.ShiftTemplates.Add(template);
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<ShiftTemplate>(template, context.GetRowVersion(template));
    }

    public Task<Stored<ShiftTemplate>> UpdateTemplateAsync(long id, long expectedVersion, Action<ShiftTemplate> change, AuditLogEntry audit, CancellationToken cancellationToken = default) =>
        UpdateAsync(c => c.ShiftTemplates.SingleOrDefaultAsync(t => t.Id == id, cancellationToken), "shift template", id, expectedVersion, change, audit, cancellationToken);

    public async Task<Stored<WeeklyWorkday>> SetWeekdayAsync(DayOfWeek day, long expectedVersion, bool isWorkday, DateTime atUtc, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.WeeklyWorkdays.SingleOrDefaultAsync(w => w.Day == day, cancellationToken) ?? throw new RecordNotFoundException("weekday", (long)day);
        PersistenceSave.CheckExpectedVersion(context, row, expectedVersion);
        row.Set(isWorkday, atUtc);
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<WeeklyWorkday>(row, context.GetRowVersion(row));
    }

    public async Task<Stored<UnitShift>> AddDateHoursAsync(
        ShiftTemplate template, Func<long, UnitShift> unitShift, Func<long, IReadOnlyList<CalendarException>> exceptions, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(unitShift);
        ArgumentNullException.ThrowIfNull(exceptions);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.ShiftTemplates.Add(template);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        var shift = unitShift(template.Id);
        context.UnitShifts.Add(shift);
        context.CalendarExceptions.AddRange(exceptions(template.Id));
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new Stored<UnitShift>(shift, context.GetRowVersion(shift));
    }

    public async Task<Stored<CalendarException>> AddExceptionAsync(CalendarException value, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.CalendarExceptions.Add(value);
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<CalendarException>(value, context.GetRowVersion(value));
    }

    public Task<Stored<CalendarException>> CancelExceptionAsync(long id, long expectedVersion, AuditLogEntry audit, CancellationToken cancellationToken = default) =>
        UpdateAsync(c => c.CalendarExceptions.SingleOrDefaultAsync(e => e.Id == id, cancellationToken), "calendar exception", id, expectedVersion, e => e.Cancel(), audit, cancellationToken);

    public async Task<Stored<UnitShift>> AddUnitShiftAsync(UnitShift value, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.UnitShifts.Add(value);
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<UnitShift>(value, context.GetRowVersion(value));
    }

    public Task<Stored<UnitShift>> UpdateUnitShiftAsync(long id, long expectedVersion, Action<UnitShift> change, AuditLogEntry audit, CancellationToken cancellationToken = default) =>
        UpdateAsync(c => c.UnitShifts.SingleOrDefaultAsync(u => u.Id == id, cancellationToken), "unit shift", id, expectedVersion, change, audit, cancellationToken);

    public async Task<Stored<OperatorOffDay>> AddOffDayAsync(OperatorOffDay value, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.OperatorOffDays.Add(value);
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<OperatorOffDay>(value, context.GetRowVersion(value));
    }

    public Task<Stored<OperatorOffDay>> CancelOffDayAsync(long id, long expectedVersion, AuditLogEntry audit, CancellationToken cancellationToken = default) =>
        UpdateAsync(c => c.OperatorOffDays.SingleOrDefaultAsync(o => o.Id == id, cancellationToken), "operator off-day", id, expectedVersion, o => o.Cancel(), audit, cancellationToken);

    public async Task<(Stored<WorkerSchedule>? Row, ValidationIssue Refusal)> AddScheduleAsync(
        WorkerSchedule row, Func<ScheduleSnapshot, ValidationIssue> validate, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validate);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // The snapshot is read in the same context (one unit of work) as the insert, so the rules see current data.
        var operatorActive = await context.Operators.AnyAsync(o => o.Id == row.OperatorId && o.IsActive, cancellationToken);
        var machineUsable = row.MachineId is not { } machineId
            || await context.Machines.AnyAsync(m => m.Id == machineId && m.IsActive && m.ProductionUnitId == row.ProductionUnitId, cancellationToken);
        var from = row.Date.AddDays(-1);
        var to = row.Date.AddDays(1);
        var offDays = await context.OperatorOffDays.Where(o => o.OperatorId == row.OperatorId && o.IsActive && o.FromDate <= to && o.ToDate >= from).ToListAsync(cancellationToken);
        var planned = await context.WorkerSchedules
            .Where(w => w.Status == WorkerScheduleStatus.Planned && w.Date >= from && w.Date <= to
                && (w.OperatorId == row.OperatorId || (w.ProductionUnitId == row.ProductionUnitId && w.ShiftTemplateId == row.ShiftTemplateId && w.Date == row.Date) || (row.MachineId != null && w.MachineId == row.MachineId)))
            .ToListAsync(cancellationToken);
        var snapshot = new ScheduleSnapshot(await LoadConfigurationAsync(context, cancellationToken), operatorActive, offDays, machineUsable, planned);
        var refusal = validate(snapshot);
        if (refusal != ValidationIssue.None)
        {
            return (null, refusal);
        }

        context.WorkerSchedules.Add(row);
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return (new Stored<WorkerSchedule>(row, context.GetRowVersion(row)), ValidationIssue.None);
    }

    public Task<Stored<WorkerSchedule>> CancelScheduleAsync(long id, long expectedVersion, string reason, long userId, DateTime atUtc, AuditLogEntry audit, CancellationToken cancellationToken = default) =>
        UpdateAsync(c => c.WorkerSchedules.SingleOrDefaultAsync(w => w.Id == id, cancellationToken), "worker schedule row", id, expectedVersion, w => w.Cancel(reason, userId, atUtc), audit, cancellationToken);

    private async Task<Stored<T>> UpdateAsync<T>(
        Func<ErpMesDbContext, Task<T?>> find, string what, long id, long expectedVersion, Action<T> change, AuditLogEntry audit, CancellationToken cancellationToken)
        where T : class
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await find(context) ?? throw new RecordNotFoundException(what, id);
        PersistenceSave.CheckExpectedVersion(context, row, expectedVersion);
        change(row);
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<T>(row, context.GetRowVersion(row));
    }

    private static async Task<CalendarConfiguration> LoadConfigurationAsync(ErpMesDbContext context, CancellationToken cancellationToken)
    {
        var templates = await context.ShiftTemplates.OrderBy(t => t.Id).ToListAsync(cancellationToken);
        var weekly = await context.WeeklyWorkdays.OrderBy(w => w.Day).ToListAsync(cancellationToken);
        var exceptions = await context.CalendarExceptions.OrderBy(e => e.Date).ThenBy(e => e.Id).ToListAsync(cancellationToken);
        var unitShifts = await context.UnitShifts.OrderBy(u => u.Id).ToListAsync(cancellationToken);
        return new CalendarConfiguration(
            templates.Select(t => new Stored<ShiftTemplate>(t, context.GetRowVersion(t))).ToList(),
            weekly.Select(w => new Stored<WeeklyWorkday>(w, context.GetRowVersion(w))).ToList(),
            exceptions.Select(e => new Stored<CalendarException>(e, context.GetRowVersion(e))).ToList(),
            unitShifts.Select(u => new Stored<UnitShift>(u, context.GetRowVersion(u))).ToList());
    }
}
