using ERP.MES.Domain.Quality;

namespace ERP.MES.Application.Persistence;

/// <summary>
/// Saves and loads the QC master data: units of measure, their conversions and the quantity types (Decision Log #9, #90). Every method is one transaction; loaded objects are detached;
/// a change is applied through the Domain methods to a freshly loaded object and saved under its row version (<see cref="ConcurrencyConflictException"/> on a stale version). Codes are
/// unique; the database enforces it as well, and a violation surfaces as a <see cref="PersistenceException"/>. No database type appears in this contract.
/// </summary>
public interface IQcMasterRepository
{
    Task<IReadOnlyList<Stored<UnitOfMeasure>>> ListUnitsAsync(CancellationToken cancellationToken = default);

    Task<Stored<UnitOfMeasure>?> FindUnitAsync(long unitId, CancellationToken cancellationToken = default);

    Task<Stored<UnitOfMeasure>> AddUnitAsync(UnitOfMeasure unit, CancellationToken cancellationToken = default);

    Task<Stored<UnitOfMeasure>> UpdateUnitAsync(long unitId, long expectedVersion, Action<UnitOfMeasure> change, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Stored<UnitConversion>>> ListConversionsAsync(CancellationToken cancellationToken = default);

    Task<Stored<UnitConversion>> AddConversionAsync(UnitConversion conversion, CancellationToken cancellationToken = default);

    Task<Stored<UnitConversion>> UpdateConversionAsync(long conversionId, long expectedVersion, Action<UnitConversion> change, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Stored<QuantityType>>> ListQuantityTypesAsync(CancellationToken cancellationToken = default);

    Task<Stored<QuantityType>?> FindQuantityTypeAsync(long quantityTypeId, CancellationToken cancellationToken = default);

    Task<Stored<QuantityType>> AddQuantityTypeAsync(QuantityType quantityType, CancellationToken cancellationToken = default);

    Task<Stored<QuantityType>> UpdateQuantityTypeAsync(long quantityTypeId, long expectedVersion, Action<QuantityType> change, CancellationToken cancellationToken = default);
}
