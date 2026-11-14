using ERP.MES.Domain.Quality;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Costing;

/// <summary>Which kind of record a <see cref="RecordInvalidation"/> marks as no longer current.</summary>
public enum InvalidatedRecordKind
{
    /// <summary>An item cost snapshot (<c>ItemCostCalculations</c>).</summary>
    CostSnapshot = 1,

    /// <summary>The Management price decision of a cost snapshot (<c>ItemPriceDecisions</c>).</summary>
    PriceDecision,

    /// <summary>The Warehouse receipt of an item (<c>WarehouseReceipts</c>).</summary>
    WarehouseReceipt,

    /// <summary>The finished/sent mark of an item (<c>ItemFinishedSent</c>).</summary>
    FinishedSent,

    /// <summary>A customer dispatch (<c>CustomerDispatches</c>): it physically happened and stays visible; it is only flagged.</summary>
    CustomerDispatch,
}

/// <summary>What the marking means for the record.</summary>
public enum InvalidationEffect
{
    /// <summary>The record is no longer current (a cost snapshot, a price decision, a finished/sent mark); a new one must be made.</summary>
    Invalidated = 1,

    /// <summary>The physical position must be reconciled by the Warehouse (a receipt): a real receipt or a compensating transaction is recorded, never a duplicate to satisfy a gate.</summary>
    RequiresReconciliation,

    /// <summary>The record stays exactly as it is and is only shown as affected (a customer dispatch that already happened).</summary>
    Flagged,
}

/// <summary>
/// The shared "no longer current" marker for cost and Warehouse records (owner decisions Q5 and B2, Decision Log #142). Append-only: the marked record is never changed or deleted; this row says that, why,
/// by whom and because of what it stopped being current. A record is current when no invalidation of it exists. The source is exactly one of: a QC correction (a change of accepted quantity or quality)
/// or a job-report revision (a change of the data costing used) - the two are never conflated, and a report revision never invalidates a Warehouse record.
/// </summary>
public sealed class RecordInvalidation
{
    public const int MaxReasonLength = 300;

    private RecordInvalidation()
    {
        Reason = string.Empty;
    }

    public RecordInvalidation(long requestItemId, InvalidatedRecordKind kind, long targetId, InvalidationEffect effect, string reason, long byUserId, DateTime atUtc, QcCorrection? qcCorrection = null, long? jobReportRevisionId = null)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(effect))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Unknown record kind or effect.");
        }

        if (qcCorrection is null == jobReportRevisionId is null)
        {
            throw new ArgumentException("An invalidation has exactly one source: a QC correction or a job-report revision.");
        }

        if (kind is InvalidatedRecordKind.WarehouseReceipt or InvalidatedRecordKind.FinishedSent or InvalidatedRecordKind.CustomerDispatch && qcCorrection is null)
        {
            throw new ArgumentException("A job-report revision never invalidates a Warehouse record (owner decision B2).", nameof(kind));
        }

        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxReasonLength)
        {
            throw new ArgumentException("A reason of 1 to " + MaxReasonLength + " characters is required.", nameof(reason));
        }

        RequestItemId = requestItemId;
        Kind = kind;
        TargetId = targetId;
        Effect = effect;
        Reason = text;
        ByUserId = byUserId;
        AtUtc = Requirements.Utc(atUtc, nameof(atUtc));
        QcCorrection = qcCorrection;
        JobReportRevisionId = jobReportRevisionId;
    }

    public long Id { get; private set; }

    public long RequestItemId { get; private set; }

    public InvalidatedRecordKind Kind { get; private set; }

    /// <summary>The id of the marked record in its own table.</summary>
    public long TargetId { get; private set; }

    public InvalidationEffect Effect { get; private set; }

    public string Reason { get; private set; }

    public long ByUserId { get; private set; }

    public DateTime AtUtc { get; private set; }

    /// <summary>The QC correction that caused it (null for a job-report revision).</summary>
    public QcCorrection? QcCorrection { get; private set; }

    public long? QcCorrectionId { get; private set; }

    /// <summary>The job-report revision that caused it (null for a QC correction); used by owner decision B2.</summary>
    public long? JobReportRevisionId { get; private set; }
}
