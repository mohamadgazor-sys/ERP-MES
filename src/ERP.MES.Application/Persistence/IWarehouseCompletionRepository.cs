using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Requests;
using ERP.MES.Domain.Warehouse;

namespace ERP.MES.Application.Persistence;

/// <summary>The facts read INSIDE the transaction that records a completion step.</summary>
public sealed record CompletionFacts(
    long ItemId,
    long RequestId,
    bool RouteComplete,
    long AcceptedBaseUnits,
    WarehouseReceipt? Receipt,
    long? LatestDecisionId,
    PriceDecisionStatus? LatestDecisionStatus,
    decimal? ApprovedPriceRial,
    bool FinishedSent)
{
    /// <summary>The sequence number the next receipt of the item gets (1 first; later only after the earlier receipt was invalidated).</summary>
    public int NextReceiptSequence { get; init; } = 1;

    /// <summary>The sequence number the next finished/sent mark gets.</summary>
    public int NextFinishedSentSequence { get; init; } = 1;
}

/// <summary>The row a completion step adds (exactly one set) or the refusal.</summary>
public sealed record CompletionRecord(ValidationIssue Refusal, WarehouseReceipt? Receipt = null, ItemFinishedSent? FinishedSent = null, CustomerDispatch? Dispatch = null);

/// <summary>An item whose route is complete with its completion records, for the Warehouse list.</summary>
public sealed record CompletionItem(
    long RequestId, string RequestNo, long ItemId, string ItemNo, string PartName, long AcceptedBaseUnits, WarehouseReceipt? Receipt, PriceDecisionStatus? LatestDecisionStatus,
    decimal? ApprovedPriceRial, ItemFinishedSent? FinishedSent, IReadOnlyList<CustomerDispatch> Dispatches)
{
    /// <summary>False while a stage of the item is reopened (QC correction) and the route is not complete again; the item stays in the list because Warehouse records still need reconciling.</summary>
    public bool RouteComplete { get; init; } = true;

    /// <summary>The marker that made the receipt no longer current (reconciliation required), null while it is current. The receipt row itself is kept.</summary>
    public RecordInvalidation? ReceiptInvalidation { get; init; }

    /// <summary>The marker on the finished/sent mark, null while it is current.</summary>
    public RecordInvalidation? FinishedSentInvalidation { get; init; }

    /// <summary>The marker on the latest cost snapshot (the price approval is then no longer current), null while it is current.</summary>
    public RecordInvalidation? CostInvalidation { get; init; }

    /// <summary>The customer dispatches that were flagged because a QC correction affected the item after they happened (they are never undone).</summary>
    public IReadOnlySet<long> FlaggedDispatchIds { get; init; } = new HashSet<long>();

    /// <summary>The QC corrections of the item (visible indicator and history for Management and the Warehouse).</summary>
    public IReadOnlyList<QcCorrection> Corrections { get; init; } = [];

    /// <summary>The Warehouse reversal proposals of the item with their decisions (owner decision B9), oldest first.</summary>
    public IReadOnlyList<WarehouseReversal> Reversals { get; init; } = [];

    /// <summary>The customer dispatches that were reversed after Management approved a reversal; the rows are kept.</summary>
    public IReadOnlySet<long> ReversedDispatchIds { get; init; } = new HashSet<long>();

    /// <summary>True when the Warehouse records or the cost approval of the item are not current and must be reconciled before the item can be finished/sent again.</summary>
    public bool NotCurrent => ReceiptInvalidation is not null || FinishedSentInvalidation is not null || CostInvalidation is not null;
}

/// <summary>What is true, read INSIDE the transaction that records a reversal proposal or decision.</summary>
public sealed record ReversalFacts(bool TargetExists, bool TargetCurrent, bool OpenProposalExists, bool CurrentFinishedMarkExists, bool RequestClosed);

/// <summary>The outcome of a reversal step: a refusal, or the saved reversal (for an approval, also the marker added to the original record).</summary>
public sealed record ReversalRecord(ValidationIssue Refusal, WarehouseReversal? Reversal = null, ERP.MES.Domain.Costing.RecordInvalidation? Marker = null);

/// <summary>Persistence of the warehouse completion records (Decision Log #105): receipt, finished/sent and customer dispatch. All are append-only; each step is one transaction with its audit entry.</summary>
public interface IWarehouseCompletionRepository
{
    Task<IReadOnlyList<CompletionItem>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads the facts of the target, lets <paramref name="decide"/> build the proposal, and saves it with the audit entry; a refusal saves nothing.</summary>
    Task<ReversalRecord> ProposeReversalAsync(
        long requestItemId, ERP.MES.Domain.Costing.InvalidatedRecordKind kind, long targetId, Func<ReversalFacts, ReversalRecord> decide, AuditLogEntry audit, CancellationToken cancellationToken = default);

    /// <summary>Loads the proposal with the facts of its target, lets <paramref name="decide"/> approve or reject it (it returns the marker for an approval), and saves both with the audit entry; a refusal saves nothing.</summary>
    Task<ReversalRecord> DecideReversalAsync(
        long reversalId, Func<WarehouseReversal, ReversalFacts, ReversalRecord> decide, Func<WarehouseReversal, AuditLogEntry> audit, CancellationToken cancellationToken = default);

    /// <summary>Reads the facts, lets <paramref name="decide"/> build the row, and saves it with the audit entry; a refusal saves nothing.</summary>
    Task<CompletionRecord> RecordAsync(long requestItemId, Func<CompletionFacts, CompletionRecord> decide, AuditLogEntry audit, CancellationToken cancellationToken = default);
}
