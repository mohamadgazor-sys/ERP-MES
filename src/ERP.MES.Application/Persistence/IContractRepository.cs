using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>One line of the contract list: a MAKE request that has a contract record or is waiting for Planning's decision.</summary>
public sealed record ContractListItem(
    long RequestId, string RequestNo, string CustomerName, RequestStatus RequestStatus, ContractStatus? ContractStatus, string? ContractNo, DateOnly? ContractDate, int? VersionNo, DateTime? UpdatedAtUtc);

/// <summary>A request's contract record with the request facts it was read with and the row version.</summary>
public sealed record ContractDetail(
    long RequestId, string RequestNo, string CustomerName, WorkType WorkType, ContractEligibility Eligibility, Stored<RequestContract>? Contract, IReadOnlyDictionary<long, ContractRequirement>? ItemDecisions = null, IReadOnlyList<ContractItemInfo>? ItemList = null)
{
    /// <summary>The request's items with whether a stage was released (an item in production can no longer change its contract decision).</summary>
    public IReadOnlyList<ContractItemInfo> Items { get; } = ItemList ?? [];

    /// <summary>The latest item-level contract decision (Decision Log #111); an item without an entry follows the request's contract.</summary>
    public IReadOnlyDictionary<long, ContractRequirement> LatestItemDecisions { get; } = ItemDecisions ?? new Dictionary<long, ContractRequirement>();

    /// <summary>The contract status that gates one item.</summary>
    public ContractStatus? StatusForItem(long itemId) =>
        ItemContractDecision.EffectiveStatus(Contract?.Value.Status, LatestItemDecisions.TryGetValue(itemId, out var r) ? r : null);
}

/// <summary>One item of a request as the contract screen needs it.</summary>
public sealed record ContractItemInfo(long Id, string ItemNo, string PartName, bool HasReleasedStage);

/// <summary>What a Planning item contract decision needs to know and what it saved.</summary>
public sealed record ItemContractDecisionRecord(long ItemId, string ItemNo, ContractRequirement Requirement, string Reason, long DecidedByUserId, DateTime DecidedAtUtc);

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

    /// <summary>How many items the request has.</summary>
    Task<int> CountItemsAsync(long requestId, CancellationToken cancellationToken = default);

    /// <summary>The append-only item-level contract decisions of a request, oldest first.</summary>
    Task<IReadOnlyList<ItemContractDecisionRecord>> ListItemDecisionsAsync(long requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an item's contract decision in one transaction. The delegate receives the request facts, the request's contract (null when none), the item's current stage count/status flag and the item's latest
    /// decision, and returns the row to append; it throws to refuse. The request row and the contract row are fenced.
    /// </summary>
    Task<ItemContractDecisionRecord> DecideItemAsync(
        long requestId,
        long itemId,
        Func<ItemContractContext, ItemContractDecision> decide,
        Func<ItemContractDecision, string, string, IReadOnlyList<AuditLogEntry>> audit,
        CancellationToken cancellationToken = default);

    /// <summary>The PDF of one contract version with its content, for viewing.</summary>
    Task<Domain.Documents.StoredDocument?> FindPdfAsync(long requestId, int versionNo, CancellationToken cancellationToken = default);
}

/// <summary>The facts an item contract decision is validated against, read in the saving transaction.</summary>
public sealed record ItemContractContext(
    ContractEligibility Eligibility, ContractRequirement? RequestRequirement, bool ItemExists, bool ItemHasReleasedStage, ContractRequirement? LatestItemRequirement);
