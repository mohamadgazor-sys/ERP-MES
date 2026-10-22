using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Planning;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Planning resource master data persistence on <see cref="ErpMesDbContext"/> (provider-neutral). Each call uses its own short-lived context and one transaction; rules stay in the Domain.</summary>
public sealed class EfPlanningMasterRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IPlanningMasterRepository
{
    public async Task<IReadOnlyList<Stored<ProductionUnit>>> ListUnitsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var units = await context.ProductionUnits.OrderBy(u => u.Id).ToListAsync(cancellationToken);
        return units.Select(u => new Stored<ProductionUnit>(u, context.GetRowVersion(u))).ToList();
    }

    public async Task<Stored<ProductionUnit>> AddUnitAsync(ProductionUnit unit, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(audit);
        RequireNew(unit.Id);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.ProductionUnits.Add(unit);
        context.AuditLog.AddRange(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<ProductionUnit>(unit, context.GetRowVersion(unit));
    }

    public async Task<Stored<ProductionUnit>> UpdateUnitAsync(long unitId, long expectedVersion, Action<ProductionUnit> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var unit = await context.ProductionUnits.SingleOrDefaultAsync(u => u.Id == unitId, cancellationToken) ?? throw new RecordNotFoundException("production unit", unitId);
        PersistenceSave.CheckExpectedVersion(context, unit, expectedVersion);
        change(unit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<ProductionUnit>(unit, context.GetRowVersion(unit));
    }

    public async Task<IReadOnlyList<Stored<Machine>>> ListMachinesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var machines = await context.Machines.OrderBy(m => m.Id).ToListAsync(cancellationToken);
        return machines.Select(m => new Stored<Machine>(m, context.GetRowVersion(m))).ToList();
    }

    public async Task<Stored<Machine>> AddMachineAsync(Machine machine, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(audit);
        RequireNew(machine.Id);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Machines.Add(machine);
        context.AuditLog.AddRange(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<Machine>(machine, context.GetRowVersion(machine));
    }

    public async Task<Stored<Machine>> UpdateMachineAsync(long machineId, long expectedVersion, Action<Machine> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var machine = await context.Machines.SingleOrDefaultAsync(m => m.Id == machineId, cancellationToken) ?? throw new RecordNotFoundException("machine", machineId);
        PersistenceSave.CheckExpectedVersion(context, machine, expectedVersion);
        change(machine);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<Machine>(machine, context.GetRowVersion(machine));
    }

    public async Task<IReadOnlyList<MachineHourlyRate>> ListRatesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.MachineHourlyRates.OrderBy(r => r.MachineId).ThenBy(r => r.EffectiveFrom).ToListAsync(cancellationToken);
    }

    public async Task<MachineHourlyRate> AddRateAsync(MachineHourlyRate rate, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rate);
        ArgumentNullException.ThrowIfNull(audit);
        RequireNew(rate.Id);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // The latest-entry rule is checked in the same context that saves, so a rate added by someone else a moment ago is seen here.
        if (!await context.Machines.AnyAsync(m => m.Id == rate.MachineId, cancellationToken))
        {
            throw new RecordNotFoundException("machine", rate.MachineId);
        }

        var latest = await context.MachineHourlyRates.Where(r => r.MachineId == rate.MachineId).Select(r => (DateOnly?)r.EffectiveFrom).MaxAsync(cancellationToken);
        if (latest is { } existing && rate.EffectiveFrom <= existing)
        {
            throw new ConcurrencyConflictException("The machine already has a rate that applies from " + existing.ToString("yyyy-MM-dd") + " or later. Nothing was saved; reload and try again.");
        }

        context.MachineHourlyRates.Add(rate);
        context.AuditLog.AddRange(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return rate;
    }

    private static void RequireNew(long id)
    {
        if (id != 0)
        {
            throw new ArgumentException("The record already has an id; use the update method to change a stored record.");
        }
    }
}
