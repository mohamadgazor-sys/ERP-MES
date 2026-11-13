namespace ERP.MES.Domain.Requests;

public enum ExternalShipmentKind
{
    /// <summary>Units sent to the external route (Master Specification §16.3 "SENT").</summary>
    Dispatch = 1,

    /// <summary>Units back from the external route, handed to QC (§16.6 "RETURNED_TO_QC"); never the final warehouse receipt (§17.3).</summary>
    Return = 2,

    /// <summary>Units the contractor never returned, recorded by the Warehouse against the original dispatch with a mandatory reason (Decision Log #136). Never a return and never QC approved.</summary>
    Missing = 3,
}

/// <summary>
/// One external-stage transaction recorded by the Warehouse (Decision Log #129): a dispatch of base units to the external route, or a return of base units from one dispatch. Each is a separate
/// historical row with its quantity, time (the transaction time) and user; a mistaken row is cancelled with a reason, never deleted or edited, so the history and the audit trail stay complete.
/// A correction is a cancellation followed by a new row.
/// </summary>
public sealed class ExternalShipment
{
    public const int MaxTextLength = 300;

    private ExternalShipment()
    {
    }

    internal ExternalShipment(
        long requestItemId, int stageNo, ExternalShipmentKind kind, long? dispatchId, long baseUnits, string? pieceDescription, string? note, long recordedByUserId, DateTime recordedAtUtc)
    {
        if (baseUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baseUnits), baseUnits, "A shipment moves at least one base unit.");
        }

        RequestItemId = requestItemId;
        StageNo = stageNo;
        Kind = kind;
        DispatchId = dispatchId;
        BaseUnits = baseUnits;
        PieceDescription = Optional(pieceDescription, nameof(pieceDescription));
        Note = Optional(note, nameof(note));
        if (kind == ExternalShipmentKind.Missing && Note is null)
        {
            throw new ArgumentException("A reason is required to record units as missing.", nameof(note));
        }
        RecordedByUserId = recordedByUserId;
        RecordedAtUtc = Customer.RequireUtc(recordedAtUtc, nameof(recordedAtUtc));
    }

    public long Id { get; private set; }

    public long RequestItemId { get; private set; }

    public int StageNo { get; private set; }

    public ExternalShipmentKind Kind { get; private set; }

    /// <summary>For a return: the dispatch the units come back from. Null for a dispatch.</summary>
    public long? DispatchId { get; private set; }

    public long BaseUnits { get; private set; }

    /// <summary>What was sent (optional; §16.3 PieceDescription).</summary>
    public string? PieceDescription { get; private set; }

    public string? Note { get; private set; }

    public long RecordedByUserId { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    public long? CancelledByUserId { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public string? CancelReason { get; private set; }

    public bool IsActive => CancelledAtUtc is null;

    internal void MarkCancelled(string? reason, long userId, DateTime atUtc)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("The shipment is already cancelled.");
        }

        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxTextLength)
        {
            throw new ArgumentException("A reason of 1 to " + MaxTextLength + " characters is required.", nameof(reason));
        }

        CancelReason = text;
        CancelledByUserId = userId;
        CancelledAtUtc = Customer.RequireUtc(atUtc, nameof(atUtc));
    }

    private static string? Optional(string? text, string name)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.Length > MaxTextLength ? throw new ArgumentException("At most " + MaxTextLength + " characters.", name) : value;
    }
}

/// <summary>An external dispatch, return or cancellation would break a quantity rule (more than still to send, more than outstanding, already decided by QC, has returns).</summary>
public sealed class ExternalQuantityException(string message) : InvalidOperationException(message);
