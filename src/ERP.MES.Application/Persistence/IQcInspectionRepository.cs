using ERP.MES.Application.Security;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>What a QC command appends beside the changed request: the inspection lines and/or a shortfall decision (both append-only).</summary>
public sealed record QcAppended(IReadOnlyList<QcInspectionLine> Lines, ShortfallDecision? Decision);

/// <summary>The facts a QC correction needs, read INSIDE the transaction that saves it (owner decisions Q2, Q5, Decision Log #142).</summary>
public sealed record CorrectionFacts(
    IReadOnlyList<AcceptedLineFact> AcceptedLines,
    long? CostSnapshotId,
    long? PriceDecisionId,
    long? ReceiptId,
    long? FinishedSentId,
    IReadOnlyList<long> CustomerDispatchIds);

/// <summary>An accepted QC line of the corrected stage with the units earlier corrections already superseded (the valid remainder is what a new correction may take).</summary>
public sealed record AcceptedLineFact(long LineId, long BaseUnits, long AlreadySupersededBaseUnits)
{
    public long ValidBaseUnits => BaseUnits - AlreadySupersededBaseUnits;
}

/// <summary>What a QC correction appends beside the changed request: the correction (with its sources) and the invalidations of the cost and Warehouse records it made stale.</summary>
public sealed record CorrectionAppended(QcCorrection Correction, IReadOnlyList<RecordInvalidation> Invalidations);

/// <summary>Persistence of QC quantity decisions (Decision Log #101): the stage change, the append-only lines or decision and the audit entries are one transaction under the named row versions.</summary>
public interface IQcInspectionRepository
{
    Task<VersionedRequest> CommitAsync(
        long requestId, RowVersionExpectations expected, Func<Request, QcAppended> change, Func<Request, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Corrects a final stage decision (Master Specification 1.10, 15): in ONE transaction under the named row versions the changed stage rows, the append-only correction with its sources and the
    /// invalidations of the item's latest cost snapshot, its price decision, its Warehouse receipt and finished/sent mark (customer dispatches are only flagged) are saved with the audit entries.
    /// </summary>
    Task<VersionedRequest> CorrectAsync(
        long requestId, long itemId, int stageNo, RowVersionExpectations expected, Func<Request, CorrectionFacts, CorrectionAppended> change,
        Func<Request, CorrectionAppended, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QcCorrection>> ListCorrectionsAsync(long requestItemId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QcInspectionLine>> ListLinesAsync(long requestItemId, int stageNo, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ShortfallDecision>> ListDecisionsAsync(long requestItemId, int stageNo, CancellationToken cancellationToken = default);
}
