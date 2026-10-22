using ERP.MES.Application.Security;
using ERP.MES.Domain.Materials;

namespace ERP.MES.Application.Persistence;

/// <summary>
/// Saves and loads the Warehouse material list and its price history (Decision Log #10). Every method is one transaction; loaded objects are detached; a change is applied through the
/// Domain methods to a freshly loaded object under its row version. Audit entries are saved in the SAME transaction as the record. Codes are unique; price entries are append-only.
/// </summary>
public interface IMaterialRepository
{
    Task<IReadOnlyList<Stored<Material>>> ListMaterialsAsync(CancellationToken cancellationToken = default);

    Task<Stored<Material>> AddMaterialAsync(Material material, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default);

    Task<Stored<Material>> UpdateMaterialAsync(long materialId, long expectedVersion, Action<Material> change, IReadOnlyList<AuditLogEntry>? audit = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaterialPrice>> ListPricesAsync(CancellationToken cancellationToken = default);

    /// <summary>Appends a price entry and its audit entries in one transaction; the latest-entry rule is checked in the same transaction (a concurrent add gives <see cref="ConcurrencyConflictException"/>).</summary>
    Task<MaterialPrice> AddPriceAsync(MaterialPrice price, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default);
}
