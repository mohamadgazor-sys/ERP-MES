using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Production;

public enum QueueOverrideAction
{
    /// <summary>Management placed the stage in the override order (first time).</summary>
    Set = 1,

    /// <summary>The stage was already overridden and its rank changed (a new override operation moved it).</summary>
    Moved,

    /// <summary>Management cleared the override; the stage returns to the ordinary Planning order.</summary>
    Cleared,

    /// <summary>Planning changed the item's priority while the override was active: the override stays and is flagged for Management review (Decision Log #78).</summary>
    FlaggedForReview,

    /// <summary>Management confirmed an override after a flagged review; it stays active.</summary>
    ReviewConfirmed,
}

/// <summary>
/// A Management override of one stage's place in its production unit's queue (Decision Log #78, #99). It is persisted state, not screen state, and stays active until Management
/// explicitly clears it. It never changes the underlying Planning priority or Planning position; those stay on the item and the stage. At most one row exists per stage (a cleared override
/// is kept as an inactive row, its history is in <see cref="QueueOverrideHistoryEntry"/>). <see cref="Rank"/> orders the overridden stages of a unit among themselves (1 = first);
/// overridden stages come before the ordinary order within the same operational category.
/// </summary>
public sealed class QueueOverride
{
    private QueueOverride()
    {
    }

    public QueueOverride(long requestItemId, int stageNo, long productionUnitId, int rank, string reason, long setByUserId, DateTime setAtUtc)
    {
        RequestItemId = requestItemId;
        StageNo = stageNo;
        ProductionUnitId = productionUnitId;
        Activate(productionUnitId, rank, reason, setByUserId, setAtUtc);
    }

    public long Id { get; private set; }

    public long RequestItemId { get; private set; }

    public int StageNo { get; private set; }

    public long ProductionUnitId { get; private set; }

    public int Rank { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>The reason given with the latest operation that set or moved this override.</summary>
    public string Reason { get; private set; } = string.Empty;

    public long SetByUserId { get; private set; }

    public DateTime SetAtUtc { get; private set; }

    /// <summary>True while Management has to review the override (Planning changed the item's priority after it was set).</summary>
    public bool NeedsReview { get; private set; }

    public string? ReviewNote { get; private set; }

    public DateTime? ReviewFlaggedAtUtc { get; private set; }

    public const int MaxReasonLength = 500;

    public void Activate(long productionUnitId, int rank, string? reason, long userId, DateTime atUtc)
    {
        if (rank < 1 || rank > 100000)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "An override rank is between 1 and 100000.");
        }

        ProductionUnitId = productionUnitId;
        Rank = rank;
        Reason = RequireReason(reason);
        SetByUserId = userId;
        SetAtUtc = Customer.RequireUtc(atUtc, nameof(atUtc));
        IsActive = true;
        NeedsReview = false;
        ReviewNote = null;
        ReviewFlaggedAtUtc = null;
    }

    public void Clear()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("The override is not active.");
        }

        IsActive = false;
        NeedsReview = false;
        ReviewNote = null;
        ReviewFlaggedAtUtc = null;
    }

    public void FlagForReview(string note, DateTime atUtc)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("The override is not active.");
        }

        NeedsReview = true;
        ReviewNote = note;
        ReviewFlaggedAtUtc = Customer.RequireUtc(atUtc, nameof(atUtc));
    }

    public void ConfirmReview()
    {
        if (!IsActive || !NeedsReview)
        {
            throw new InvalidOperationException("The override is not waiting for review.");
        }

        NeedsReview = false;
        ReviewNote = null;
        ReviewFlaggedAtUtc = null;
    }

    public static string RequireReason(string? reason)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxReasonLength)
        {
            throw new ArgumentException("A reason of 1 to " + MaxReasonLength + " characters is required.", nameof(reason));
        }

        return text;
    }
}

/// <summary>
/// One append-only record of an override operation on one stage: who, when, the reason, the rank and the position in the unit queue before and after (Decision Log #78, Architecture 9.2).
/// <see cref="OperationId"/> groups the rows of one multi-row operation.
/// </summary>
public sealed class QueueOverrideHistoryEntry
{
    private QueueOverrideHistoryEntry()
    {
    }

    public QueueOverrideHistoryEntry(
        string operationId, long requestItemId, int stageNo, long productionUnitId, QueueOverrideAction action, long userId, DateTime atUtc, string reason, int? oldRank, int? newRank, int? oldOrder, int? newOrder)
    {
        OperationId = operationId;
        RequestItemId = requestItemId;
        StageNo = stageNo;
        ProductionUnitId = productionUnitId;
        Action = action;
        UserId = userId;
        AtUtc = Customer.RequireUtc(atUtc, nameof(atUtc));
        Reason = reason;
        OldRank = oldRank;
        NewRank = newRank;
        OldOrder = oldOrder;
        NewOrder = newOrder;
    }

    public long Id { get; private set; }

    public string OperationId { get; private set; } = string.Empty;

    public long RequestItemId { get; private set; }

    public int StageNo { get; private set; }

    public long ProductionUnitId { get; private set; }

    public QueueOverrideAction Action { get; private set; }

    public long UserId { get; private set; }

    public DateTime AtUtc { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public int? OldRank { get; private set; }

    public int? NewRank { get; private set; }

    /// <summary>Position (1 = first) of the stage in its unit's queue before the operation; null when it was not in the queue order.</summary>
    public int? OldOrder { get; private set; }

    public int? NewOrder { get; private set; }
}

/// <summary>
/// Designates a user to a production unit (Decision Log #99): a Production user reads and acts on the queue of the units they are designated to, and only those (owner decision D8).
/// Planning maintains the designations (labelled default). One row per user and unit.
/// </summary>
public sealed class UserUnitDesignation
{
    private UserUnitDesignation()
    {
    }

    public UserUnitDesignation(long userId, long productionUnitId, long assignedByUserId, DateTime assignedAtUtc)
    {
        UserId = userId;
        ProductionUnitId = productionUnitId;
        AssignedByUserId = assignedByUserId;
        AssignedAtUtc = Customer.RequireUtc(assignedAtUtc, nameof(assignedAtUtc));
    }

    public long Id { get; private set; }

    public long UserId { get; private set; }

    public long ProductionUnitId { get; private set; }

    public long AssignedByUserId { get; private set; }

    public DateTime AssignedAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public long? RevokedByUserId { get; private set; }

    public bool IsActive => RevokedAtUtc is null;

    /// <summary>True when the user supervises the unit: reviews and approves the unit's job reports (Decision Log #95 D8, #102). Set by Planning.</summary>
    public bool IsSupervisor { get; private set; }

    public void SetSupervisor(bool isSupervisor)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("The designation is withdrawn.");
        }

        IsSupervisor = isSupervisor;
    }

    /// <summary>Withdraws the designation. The row is kept (who designated and who withdrew stays on record).</summary>
    public void Revoke(long revokedByUserId, DateTime revokedAtUtc)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("The designation is already withdrawn.");
        }

        RevokedAtUtc = Customer.RequireUtc(revokedAtUtc, nameof(revokedAtUtc));
        RevokedByUserId = revokedByUserId;
    }

    /// <summary>Designates the user again after a withdrawal.</summary>
    public void Reinstate(long assignedByUserId, DateTime assignedAtUtc)
    {
        if (IsActive)
        {
            throw new InvalidOperationException("The designation is already active.");
        }

        AssignedByUserId = assignedByUserId;
        AssignedAtUtc = Customer.RequireUtc(assignedAtUtc, nameof(assignedAtUtc));
        RevokedAtUtc = null;
        RevokedByUserId = null;
        IsSupervisor = false;
    }
}
