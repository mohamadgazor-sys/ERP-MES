using ERP.MES.Domain.Quality;
using ERP.MES.Domain.Costing;

namespace ERP.MES.Domain.Warehouse;

/// <summary>What the proposer says the Warehouse record is (owner decision B9, Decision Log #142): a database record entered by mistake, or a physical movement that really happened and is being corrected.</summary>
public enum ReversalNature
{
    /// <summary>The record is wrong; nothing physical happened as recorded. Approval marks the record reversed.</summary>
    RecordedInError = 1,

    /// <summary>The physical movement really happened and is corrected by a COMPENSATING Warehouse transaction (this reversal row is that transaction). The movement is never pretended away.</summary>
    PhysicalMovementCorrection,
}

public enum ReversalStatus
{
    Proposed = 1,
    Approved,
    Rejected,
}

/// <summary>
/// A proposed, then approved or rejected, reversal of one Warehouse record (receipt, finished/sent mark or customer dispatch). The Warehouse proposes with a mandatory reason; Management approves or rejects.
/// The original record is never deleted or changed: it is marked reversed (a <see cref="RecordInvalidation"/> with effect Reversed) only when this reversal is approved, and the reversal links to it with
/// proposer, approver, timestamps and reasons. Append-only apart from the single decision (Proposed to Approved/Rejected), which is guarded by a concurrency token on the status.
/// </summary>
public sealed class WarehouseReversal
{
    public const int MaxReasonLength = 300;

    private WarehouseReversal()
    {
        ProposalReason = string.Empty;
    }

    public WarehouseReversal(long requestItemId, InvalidatedRecordKind targetKind, long targetId, ReversalNature nature, string? reason, long proposedByUserId, DateTime proposedAtUtc)
    {
        if (targetKind is not (InvalidatedRecordKind.WarehouseReceipt or InvalidatedRecordKind.FinishedSent or InvalidatedRecordKind.CustomerDispatch))
        {
            throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, "Only a Warehouse receipt, finished/sent mark or customer dispatch can be reversed.");
        }

        if (!Enum.IsDefined(nature))
        {
            throw new ArgumentOutOfRangeException(nameof(nature), nature, "Unknown reversal nature.");
        }

        RequestItemId = requestItemId;
        TargetKind = targetKind;
        TargetId = targetId;
        Nature = nature;
        ProposalReason = CleanReason(reason, nameof(reason), required: true)!;
        ProposedByUserId = proposedByUserId;
        ProposedAtUtc = Requirements.Utc(proposedAtUtc, nameof(proposedAtUtc));
        Status = ReversalStatus.Proposed;
    }

    public long Id { get; private set; }

    public long RequestItemId { get; private set; }

    public InvalidatedRecordKind TargetKind { get; private set; }

    /// <summary>The id of the reversed record in its own table.</summary>
    public long TargetId { get; private set; }

    public ReversalNature Nature { get; private set; }

    public ReversalStatus Status { get; private set; }

    public string ProposalReason { get; private set; }

    public long ProposedByUserId { get; private set; }

    public DateTime ProposedAtUtc { get; private set; }

    public long? DecidedByUserId { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>Optional on approval, mandatory on rejection.</summary>
    public string? DecisionReason { get; private set; }

    public void Approve(long userId, string? note, DateTime atUtc)
    {
        Decide(ReversalStatus.Approved, userId, CleanReason(note, nameof(note), required: false), atUtc);
    }

    public void Reject(long userId, string? reason, DateTime atUtc)
    {
        Decide(ReversalStatus.Rejected, userId, CleanReason(reason, nameof(reason), required: true), atUtc);
    }

    private void Decide(ReversalStatus status, long userId, string? reason, DateTime atUtc)
    {
        if (Status != ReversalStatus.Proposed)
        {
            throw new InvalidOperationException("The reversal was already decided.");
        }

        Status = status;
        DecidedByUserId = userId;
        DecidedAtUtc = Requirements.Utc(atUtc, nameof(atUtc));
        DecisionReason = reason;
    }

    private static string? CleanReason(string? reason, string name, bool required)
    {
        var text = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (text is null)
        {
            return required ? throw new ArgumentException("A reason of 1 to " + MaxReasonLength + " characters is required.", name) : null;
        }

        return text.Length > MaxReasonLength ? throw new ArgumentException("A reason is at most " + MaxReasonLength + " characters.", name) : text;
    }
}
