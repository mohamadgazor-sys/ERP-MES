using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Machine downtime on <see cref="ErpMesDbContext"/> (provider-neutral). A change, its context reads and its audit entry are one unit of work.</summary>
public sealed class EfMachineDowntimeRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IMachineDowntimeRepository
{
    public async Task<IReadOnlyList<Stored<MachineDowntime>>> ListAsync(DateTime fromLocal, DateTime toLocal, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await context.MachineDowntimes.Where(d => d.StartLocal < toLocal && d.EndLocal > fromLocal).OrderBy(d => d.StartLocal).ThenBy(d => d.Id).ToListAsync(cancellationToken);
        return list.Select(d => new Stored<MachineDowntime>(d, context.GetRowVersion(d))).ToList();
    }

    public async Task<(Stored<MachineDowntime>? Row, ValidationIssue Refusal)> AddAsync(
        MachineDowntime row, Func<DowntimeContext, ValidationIssue> validate, AuditLogEntry audit, CancellationToken cancellationToken = default)
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

        context.MachineDowntimes.Add(row);
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return (new Stored<MachineDowntime>(row, context.GetRowVersion(row)), ValidationIssue.None);
    }

    public async Task<(Stored<MachineDowntime>? Row, ValidationIssue Refusal)> ChangeAsync(
        long id, long expectedVersion, Func<MachineDowntime, DowntimeContext, ValidationIssue> change, Func<MachineDowntime, AuditLogEntry> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.MachineDowntimes.SingleOrDefaultAsync(d => d.Id == id, cancellationToken) ?? throw new RecordNotFoundException("machine downtime", id);
        PersistenceSave.CheckExpectedVersion(context, row, expectedVersion);
        var entry = audit(row);
        var refusal = change(row, await LoadContextAsync(context, row, cancellationToken));
        if (refusal != ValidationIssue.None)
        {
            return (null, refusal);
        }

        context.AuditLog.Add(entry);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return (new Stored<MachineDowntime>(row, context.GetRowVersion(row)), ValidationIssue.None);
    }

    private static async Task<DowntimeContext> LoadContextAsync(ErpMesDbContext context, MachineDowntime row, CancellationToken cancellationToken)
    {
        var machine = await context.Machines.AsNoTracking().SingleOrDefaultAsync(m => m.Id == row.MachineId, cancellationToken);
        // Every other counting record of the machine that could overlap this one, also after a change of its end: a record is at most MaxDays long, so only records that start within MaxDays
        // before or after this start can reach it. The context is read before the change is applied, so the window must not depend on the current end.
        var earliest = row.StartLocal.AddDays(-MachineDowntime.MaxDays);
        var end = row.StartLocal.AddDays(MachineDowntime.MaxDays);
        var others = await context.MachineDowntimes.AsNoTracking()
            .Where(d => d.MachineId == row.MachineId && d.Id != row.Id && (d.Status == DowntimeStatus.Reported || d.Status == DowntimeStatus.Accepted) && d.StartLocal >= earliest && d.StartLocal < end)
            .ToListAsync(cancellationToken);
        return new DowntimeContext(machine, others);
    }
}
