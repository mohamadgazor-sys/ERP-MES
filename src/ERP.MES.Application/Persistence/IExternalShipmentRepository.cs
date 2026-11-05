using ERP.MES.Application.Security;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>
/// Persistence of external dispatches and returns (Decision Log #129): the changed stage, the new or cancelled shipment row and the audit entries are one transaction under the named row
/// versions. The shipments of the stage are read inside that transaction and tracked, so a cancellation updates the row it names.
/// </summary>
public interface IExternalShipmentRepository
{
    /// <summary>
    /// Loads the request aggregate and the shipments of (<paramref name="itemId"/>, <paramref name="stageNo"/>), compares the row versions, lets <paramref name="change"/> run the Domain
    /// transition and adds the shipment it returns (null when it only cancelled an existing row).
    /// </summary>
    Task<VersionedRequest> CommitAsync(
        long requestId, RowVersionExpectations expected, long itemId, int stageNo, Func<Request, IReadOnlyList<ExternalShipment>, ExternalShipment?> change,
        Func<Request, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default);

    /// <summary>All shipments (active and cancelled) of the given items, oldest first.</summary>
    Task<IReadOnlyList<ExternalShipment>> ListAsync(IReadOnlyCollection<long> itemIds, CancellationToken cancellationToken = default);
}
