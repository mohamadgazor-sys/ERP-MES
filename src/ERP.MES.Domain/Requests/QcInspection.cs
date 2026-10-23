namespace ERP.MES.Domain.Requests;

public enum InspectionDisposition
{
    Accepted = 1,
    Rework,
    Scrap,
}

/// <summary>One decision QC makes about a number of base units of a stage.</summary>
public sealed record InspectionEntry(InspectionDisposition Disposition, long BaseUnits, string? Reason);

/// <summary>
/// One append-only QC decision on base units of an item's stage (Decision Log #95 D2, #101): how many base units, accepted / rework / scrap, the reason (mandatory for rework and scrap), who and when.
/// There are no per-piece identities. A line is never edited or removed; <see cref="InspectionId"/> groups the lines of one inspection.
/// </summary>
public sealed class QcInspectionLine
{
    public const int MaxReasonLength = 300;

    private QcInspectionLine()
    {
    }

    public QcInspectionLine(long requestItemId, int stageNo, string inspectionId, InspectionDisposition disposition, long baseUnits, string? reason, long decidedByUserId, DateTime decidedAtUtc)
    {
        RequestItemId = requestItemId;
        StageNo = stageNo;
        InspectionId = inspectionId;
        Disposition = disposition;
        BaseUnits = baseUnits;
        Reason = reason;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = Customer.RequireUtc(decidedAtUtc, nameof(decidedAtUtc));
    }

    public long Id { get; private set; }

    public long RequestItemId { get; private set; }

    public int StageNo { get; private set; }

    public string InspectionId { get; private set; } = string.Empty;

    public InspectionDisposition Disposition { get; private set; }

    public long BaseUnits { get; private set; }

    public string? Reason { get; private set; }

    public long DecidedByUserId { get; private set; }

    public DateTime DecidedAtUtc { get; private set; }
}

public enum ShortfallKind
{
    /// <summary>Management explicitly accepts delivering fewer units than ordered.</summary>
    ManagementAccepted = 1,

    /// <summary>Planning records that replacement production was ordered (no replacement is created automatically; the reference is a note).</summary>
    ReplacementOrdered,
}

/// <summary>The decision that closes a shortfall created by scrap (Decision Log #95 D3, #101). Append-only: who, when, the shortfall in base units and the note or reference.</summary>
public sealed class ShortfallDecision
{
    private ShortfallDecision()
    {
    }

    public ShortfallDecision(long requestItemId, int stageNo, ShortfallKind kind, long shortfallBaseUnits, string note, long decidedByUserId, DateTime decidedAtUtc)
    {
        RequestItemId = requestItemId;
        StageNo = stageNo;
        Kind = kind;
        ShortfallBaseUnits = shortfallBaseUnits;
        Note = note;
        DecidedByUserId = decidedByUserId;
        DecidedAtUtc = Customer.RequireUtc(decidedAtUtc, nameof(decidedAtUtc));
    }

    public long Id { get; private set; }

    public long RequestItemId { get; private set; }

    public int StageNo { get; private set; }

    public ShortfallKind Kind { get; private set; }

    public long ShortfallBaseUnits { get; private set; }

    public string Note { get; private set; } = string.Empty;

    public long DecidedByUserId { get; private set; }

    public DateTime DecidedAtUtc { get; private set; }
}

/// <summary>What recording an inspection did: the appended lines, the stage's new status and whether the stage is complete (QC approved).</summary>
public sealed record InspectionOutcome(IReadOnlyList<QcInspectionLine> Lines, ItemStageStatus NewStatus, bool Completed);

/// <summary>QC tried to decide more base units than are awaiting QC at the stage (no unit is counted twice and nothing is overproduced).</summary>
public sealed class InspectionExceedsAwaitingException(string message) : InvalidOperationException(message);

/// <summary>The stage has no whole number of base units, so it cannot be inspected by quantity.</summary>
public sealed class StageNotCountableException(string message) : InvalidOperationException(message);
