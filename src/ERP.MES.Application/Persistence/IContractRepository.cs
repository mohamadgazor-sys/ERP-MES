using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>One line of the contract list: a MAKE request that has a contract record or is waiting for Planning's decision.</summary>
public sealed record ContractListItem(
    long RequestId, string RequestNo, string CustomerName, RequestStatus RequestStatus, ContractStatus? ContractStatus, string? ContractNo, DateOnly? ContractDate, int? VersionNo, DateTime? UpdatedAtUtc);

/// <summary>A request's contract record with the request facts it was read with and the row version.</summary>
public sealed record ContractDetail(long RequestId, string RequestNo, string CustomerName, WorkType WorkType, ContractEligibility Eligibility, Stored<RequestContract>? Contract);

/// <summary>
/// Saves and loads the contract gate (Decision Log #85). Every change runs in ONE transaction that also reads the request's current facts (type, status, Drawing's latest result)
/// and fences the request row, so a request that moved on a moment ago cannot be given a contract decision from stale facts. Audit entries are saved in the same transaction.
/// Submitted versions, their PDF links, decisions and the history are never overwritten or deleted.
/// </summary>
public interface IContractRepository
{
    Task<IReadOnlyList<ContractListItem>> ListAsync(CancellationToken cancellationToken = default);

    Task<ContractDetail?> FindAsync(long requestId, CancellationToken cancellationToken = default);

    /// <summary>Applies <paramref name="change"/> to the request's contract (or creates it when there is none and <paramref name="expectedVersion"/> is null). The delegate receives the current contract (null when none) and the request facts and returns the contract to save.</summary>
    Task<Stored<RequestContract>> ApplyAsync(
        long requestId,
        long? expectedVersion,
        Func<RequestContract?, ContractEligibility, RequestContract> change,
        Func<RequestContract, string, IReadOnlyList<AuditLogEntry>> audit,
        CancellationToken cancellationToken = default);

    /// <summary>The PDF of one contract version with its content, for viewing.</summary>
    Task<Domain.Documents.StoredDocument?> FindPdfAsync(long requestId, int versionNo, CancellationToken cancellationToken = default);
}
