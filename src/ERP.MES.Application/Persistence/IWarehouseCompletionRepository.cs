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
    bool FinishedSent);

/// <summary>The row a completion step adds (exactly one set) or the refusal.</summary>
public sealed record CompletionRecord(ValidationIssue Refusal, WarehouseReceipt? Receipt = null, ItemFinishedSent? FinishedSent = null, CustomerDispatch? Dispatch = null);

/// <summary>An item whose route is complete with its completion records, for the Warehouse list.</summary>
public sealed record CompletionItem(
    long RequestId, string RequestNo, long ItemId, string ItemNo, string PartName, long AcceptedBaseUnits, WarehouseReceipt? Receipt, PriceDecisionStatus? LatestDecisionStatus,
    decimal? ApprovedPriceRial, ItemFinishedSent? FinishedSent, IReadOnlyList<CustomerDispatch> Dispatches);

/// <summary>Persistence of the warehouse completion records (Decision Log #105): receipt, finished/sent and customer dispatch. All are append-only; each step is one transaction with its audit entry.</summary>
public interface IWarehouseCompletionRepository
{
    Task<IReadOnlyList<CompletionItem>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads the facts, lets <paramref name="decide"/> build the row, and saves it with the audit entry; a refusal saves nothing.</summary>
    Task<CompletionRecord> RecordAsync(long requestItemId, Func<CompletionFacts, CompletionRecord> decide, AuditLogEntry audit, CancellationToken cancellationToken = default);
}
