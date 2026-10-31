using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Overtime assignments and limits on <see cref="ErpMesDbContext"/> (provider-neutral). A change, its context reads and its audit entry are one unit of work.</summary>
public sealed class EfOvertimeRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IOvertimeRepository
{
    public async Task<IReadOnlyList<Stored<OvertimeAssignment>>> ListAsync(DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await context.OvertimeAssignments.Where(a => a.WorkDate >= from && a.WorkDate <= toInclusive).OrderBy(a => a.WorkDate).ThenBy(a => a.Id).ToListAsync(cancellationToken);
        return list.Select(a => new Stored<OvertimeAssignment>(a, context.GetRowVersion(a))).ToList();
    }

    public async Task<Stored<OvertimeLimits>?> GetLimitsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var limits = await context.OvertimeLimits.SingleOrDefaultAsync(cancellationToken);
        return limits is null ? null : new Stored<OvertimeLimits>(limits, context.GetRowVersion(limits));
    }

    public async Task<Stored<OvertimeLimits>> SetLimitsAsync(
        long expectedVersion, int maxMinutesPerDay, int maxMinutesPerWeek, long userId, DateTime atUtc, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var limits = await context.OvertimeLimits.SingleOrDefaultAsync(cancellationToken);
        if (limits is null)
        {
            if (expectedVersion != 0)
            {
                throw new ConcurrencyConflictException("The overtime limits do not exist at the version that was expected. Nothing was saved; reload and try again.");
            }

            limits = new OvertimeLimits(maxMinutesPerDay, maxMinutesPerWeek, userId, atUtc);
            context.OvertimeLimits.Add(limits);
        }
        else
        {
            PersistenceSave.CheckExpectedVersion(context, limits, expectedVersion);
            limits.Set(maxMinutesPerDay, maxMinutesPerWeek, userId, atUtc);
        }

        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<OvertimeLimits>(limits, context.GetRowVersion(limits));
    }

    public async Task<(Stored<OvertimeAssignment>? Row, ValidationIssue Refusal)> AddAsync(
        OvertimeAssignment row, Func<OvertimeContext, ValidationIssue> validate, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(validate);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var refusal = validate(await LoadContextAsync(context, row, cancellationToken));
        if (refusal != ValidationIssue.None)
        {
            return (null, refusal);
        }

        context.OvertimeAssignments.Add(row);
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return (new Stored<OvertimeAssignment>(row, context.GetRowVersion(row)), ValidationIssue.None);
    }

    public async Task<(Stored<OvertimeAssignment>? Row, ValidationIssue Refusal)> ChangeAsync(
        long id, long expectedVersion, Func<OvertimeAssignment, OvertimeContext, ValidationIssue> change, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.OvertimeAssignments.SingleOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw new RecordNotFoundException("overtime assignment", id);
        PersistenceSave.CheckExpectedVersion(context, row, expectedVersion);
        var refusal = change(row, await LoadContextAsync(context, row, cancellationToken));
        if (refusal != ValidationIssue.None)
        {
            return (null, refusal);
        }

        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return (new Stored<OvertimeAssignment>(row, context.GetRowVersion(row)), ValidationIssue.None);
    }

    private static async Task<OvertimeContext> LoadContextAsync(ErpMesDbContext context, OvertimeAssignment row, CancellationToken cancellationToken)
    {
        var limits = await context.OvertimeLimits.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var operatorActive = await context.Operators.AnyAsync(o => o.Id == row.OperatorId && o.IsActive, cancellationToken);
        var machineUsable = await context.Machines.AnyAsync(m => m.Id == row.MachineId && m.IsActive && m.ProductionUnitId == row.ProductionUnitId, cancellationToken);
        var from = row.WorkDate.AddDays(-1);
        var to = row.WorkDate.AddDays(2);
        var weekFrom = row.WorkDate.AddDays(-7);
        var weekTo = row.WorkDate.AddDays(7);
        var offDays = await context.OperatorOffDays.AsNoTracking().Where(o => o.OperatorId == row.OperatorId && o.IsActive && o.FromDate <= to && o.ToDate >= from).ToListAsync(cancellationToken);
        var planned = await context.WorkerSchedules.AsNoTracking()
            .Where(w => w.Status == WorkerScheduleStatus.Planned && w.Date >= from && w.Date <= to && (w.OperatorId == row.OperatorId || w.MachineId == row.MachineId))
            .ToListAsync(cancellationToken);
        var templates = await context.ShiftTemplates.AsNoTracking().ToListAsync(cancellationToken);
        var assignments = await context.OvertimeAssignments.AsNoTracking()
            .Where(a => a.Id != row.Id && a.WorkDate >= weekFrom && a.WorkDate <= weekTo && (a.OperatorId == row.OperatorId || a.MachineId == row.MachineId))
            .ToListAsync(cancellationToken);
        // The same effective calendar as the forecast, the session checks and the job reports, so regular unit hours (for example a standing Friday schedule) are never booked as overtime (Decision Log #123).
        var weekly = await context.WeeklyWorkdays.AsNoTracking().ToListAsync(cancellationToken);
        var exceptions = await context.CalendarExceptions.AsNoTracking().ToListAsync(cancellationToken);
        var unitShifts = await context.UnitShifts.AsNoTracking().ToListAsync(cancellationToken);
        return new OvertimeContext(limits, operatorActive, machineUsable, offDays, planned, templates, assignments, new WorkingCalendar(weekly, exceptions, unitShifts, templates));
    }
}
