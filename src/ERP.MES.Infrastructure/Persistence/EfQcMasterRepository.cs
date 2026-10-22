using ERP.MES.Application.Persistence;
using ERP.MES.Domain.Quality;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>QC master data persistence on <see cref="ErpMesDbContext"/> (provider-neutral). Each call uses its own short-lived context and one transaction; rules stay in the Domain.</summary>
public sealed class EfQcMasterRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IQcMasterRepository
{
    public async Task<IReadOnlyList<Stored<UnitOfMeasure>>> ListUnitsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var units = await context.UnitsOfMeasure.OrderBy(u => u.Id).ToListAsync(cancellationToken);
        return units.Select(u => new Stored<UnitOfMeasure>(u, context.GetRowVersion(u))).ToList();
    }

    public async Task<Stored<UnitOfMeasure>?> FindUnitAsync(long unitId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var unit = await context.UnitsOfMeasure.SingleOrDefaultAsync(u => u.Id == unitId, cancellationToken);
        return unit is null ? null : new Stored<UnitOfMeasure>(unit, context.GetRowVersion(unit));
    }

    public async Task<Stored<UnitOfMeasure>> AddUnitAsync(UnitOfMeasure unit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);
        RequireNew(unit.Id);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.UnitsOfMeasure.Add(unit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<UnitOfMeasure>(unit, context.GetRowVersion(unit));
    }

    public async Task<Stored<UnitOfMeasure>> UpdateUnitAsync(long unitId, long expectedVersion, Action<UnitOfMeasure> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var unit = await context.UnitsOfMeasure.SingleOrDefaultAsync(u => u.Id == unitId, cancellationToken) ?? throw new RecordNotFoundException("unit of measure", unitId);
        PersistenceSave.CheckExpectedVersion(context, unit, expectedVersion);
        change(unit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<UnitOfMeasure>(unit, context.GetRowVersion(unit));
    }

    public async Task<IReadOnlyList<Stored<UnitConversion>>> ListConversionsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var conversions = await context.UnitConversions.OrderBy(c => c.Id).ToListAsync(cancellationToken);
        return conversions.Select(c => new Stored<UnitConversion>(c, context.GetRowVersion(c))).ToList();
    }

    public async Task<Stored<UnitConversion>> AddConversionAsync(UnitConversion conversion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversion);
        RequireNew(conversion.Id);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.UnitConversions.Add(conversion);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<UnitConversion>(conversion, context.GetRowVersion(conversion));
    }

    public async Task<Stored<UnitConversion>> UpdateConversionAsync(long conversionId, long expectedVersion, Action<UnitConversion> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var conversion = await context.UnitConversions.SingleOrDefaultAsync(c => c.Id == conversionId, cancellationToken) ?? throw new RecordNotFoundException("unit conversion", conversionId);
        PersistenceSave.CheckExpectedVersion(context, conversion, expectedVersion);
        change(conversion);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<UnitConversion>(conversion, context.GetRowVersion(conversion));
    }

    public async Task<IReadOnlyList<Stored<QuantityType>>> ListQuantityTypesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var types = await context.QuantityTypes.Include(q => q.Members.OrderBy(m => m.MemberNo)).OrderBy(q => q.Id).ToListAsync(cancellationToken);
        return types.Select(q => new Stored<QuantityType>(q, context.GetRowVersion(q))).ToList();
    }

    public async Task<Stored<QuantityType>?> FindQuantityTypeAsync(long quantityTypeId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var type = await context.QuantityTypes.Include(q => q.Members.OrderBy(m => m.MemberNo)).SingleOrDefaultAsync(q => q.Id == quantityTypeId, cancellationToken);
        return type is null ? null : new Stored<QuantityType>(type, context.GetRowVersion(type));
    }

    public async Task<Stored<QuantityType>> AddQuantityTypeAsync(QuantityType quantityType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quantityType);
        RequireNew(quantityType.Id);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.QuantityTypes.Add(quantityType);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<QuantityType>(quantityType, context.GetRowVersion(quantityType));
    }

    public async Task<Stored<QuantityType>> UpdateQuantityTypeAsync(long quantityTypeId, long expectedVersion, Action<QuantityType> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var type = await context.QuantityTypes.Include(q => q.Members.OrderBy(m => m.MemberNo)).SingleOrDefaultAsync(q => q.Id == quantityTypeId, cancellationToken)
            ?? throw new RecordNotFoundException("quantity type", quantityTypeId);
        PersistenceSave.CheckExpectedVersion(context, type, expectedVersion);
        change(type);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<QuantityType>(type, context.GetRowVersion(type));
    }

    private static void RequireNew(long id)
    {
        if (id != 0)
        {
            throw new ArgumentException("The record already has an id; use the update method to change a stored record.");
        }
    }
}
