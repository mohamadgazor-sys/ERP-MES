using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Materials;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Material list and price-history persistence on <see cref="ErpMesDbContext"/> (provider-neutral). Each call uses its own short-lived context and one transaction.</summary>
public sealed class EfMaterialRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IMaterialRepository
{
    public async Task<IReadOnlyList<Stored<Material>>> ListMaterialsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var materials = await context.Materials.OrderBy(m => m.Id).ToListAsync(cancellationToken);
        return materials.Select(m => new Stored<Material>(m, context.GetRowVersion(m))).ToList();
    }

    public async Task<Stored<Material>> AddMaterialAsync(Material material, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(audit);
        RequireNew(material.Id);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Materials.Add(material);
        context.AuditLog.AddRange(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<Material>(material, context.GetRowVersion(material));
    }

    public async Task<Stored<Material>> UpdateMaterialAsync(long materialId, long expectedVersion, Action<Material> change, IReadOnlyList<AuditLogEntry>? audit = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var material = await context.Materials.SingleOrDefaultAsync(m => m.Id == materialId, cancellationToken) ?? throw new RecordNotFoundException("material", materialId);
        PersistenceSave.CheckExpectedVersion(context, material, expectedVersion);
        change(material);
        if (audit is not null)
        {
            context.AuditLog.AddRange(audit);
        }

        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<Material>(material, context.GetRowVersion(material));
    }

    public async Task<IReadOnlyList<MaterialPrice>> ListPricesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.MaterialPrices.OrderBy(p => p.MaterialId).ThenBy(p => p.EffectiveFrom).ToListAsync(cancellationToken);
    }

    public async Task<MaterialPrice> AddPriceAsync(MaterialPrice price, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(price);
        ArgumentNullException.ThrowIfNull(audit);
        RequireNew(price.Id);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Materials.AnyAsync(m => m.Id == price.MaterialId, cancellationToken))
        {
            throw new RecordNotFoundException("material", price.MaterialId);
        }

        var latest = await context.MaterialPrices.Where(p => p.MaterialId == price.MaterialId).Select(p => (DateOnly?)p.EffectiveFrom).MaxAsync(cancellationToken);
        if (latest is { } existing && price.EffectiveFrom <= existing)
        {
            throw new ConcurrencyConflictException("The material already has a price that applies from " + existing.ToString("yyyy-MM-dd") + " or later. Nothing was saved; reload and try again.");
        }

        context.MaterialPrices.Add(price);
        context.AuditLog.AddRange(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return price;
    }

    private static void RequireNew(long id)
    {
        if (id != 0)
        {
            throw new ArgumentException("The record already has an id; use the update method to change a stored record.");
        }
    }
}
