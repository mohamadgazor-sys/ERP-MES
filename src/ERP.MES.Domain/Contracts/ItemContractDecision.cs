using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Contracts;

/// <summary>
/// Planning's contract decision for ONE item of a MAKE request (Decision Log #111, owner direction of 2026-10-27). The contract document and Management's approval stay at request level (one
/// record per request, Decision Log #85); this append-only row says whether a given item needs that approved contract before it may be defined, released or started. An item with no row follows the
/// request's contract decision (this is what every pre-existing item does, so no data is reinterpreted). The latest row of an item is its current decision; older rows stay as history.
/// </summary>
public sealed class ItemContractDecision
{
    public const int MaxReasonLength = 300;

    private ItemContractDecision()
    {
    }

    public ItemContractDecision(long requestId, long requestItemId, ContractRequirement requirement, string? reason, long decidedByUserId, DateTime decidedAtUtc)
    {
        if (!Enum.IsDefined(requirement))
        {
            throw new ArgumentOutOfRangeException(nameof(requirement), requirement, "Unknown contract requirement.");
        }

        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxReasonLength)
        {
            throw new ArgumentException("A reason of at most 300 characters is required.", nameof(reason));
        }

        RequestId = requestId;
        RequestItemId = requestItemId;
        Requirement = requirement;
        Reason = text;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = Customer.RequireUtc(decidedAtUtc, nameof(decidedAtUtc));
    }

    public long Id { get; private set; }

    public long RequestId { get; private set; }

    public long RequestItemId { get; private set; }

    public ContractRequirement Requirement { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public long DecidedByUserId { get; private set; }

    public DateTime DecidedAtUtc { get; private set; }

    /// <summary>
    /// The contract status that gates one item: an item decided "not required" is not held by the contract; every other item follows the request's contract status.
    /// <paramref name="latestForItem"/> is the item's latest decision (null when none).
    /// </summary>
    public static ContractStatus? EffectiveStatus(ContractStatus? requestContractStatus, ContractRequirement? latestForItem) =>
        latestForItem == ContractRequirement.NotRequired ? ContractStatus.NotRequired : requestContractStatus;
}
