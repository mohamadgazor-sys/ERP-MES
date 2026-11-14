using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Warehouse completion records on <see cref="ErpMesDbContext"/> (provider-neutral). The facts are read in the same context that saves the new row; a unique key backs the duplicate rules.</summary>
public sealed class EfWarehouseCompletionRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IWarehouseCompletionRepository
{
    public async Task<IReadOnlyList<CompletionItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var requests = await EfRequestRepository.LoadAggregate(context).AsNoTracking().ToListAsync(cancellationToken);
        var invalidations = (await context.RecordInvalidations.AsNoTracking().OrderBy(i => i.Id).ToListAsync(cancellationToken)).ToLookup(i => (i.Kind, i.TargetId));
        var corrections = (await context.QcCorrections.AsNoTracking().Include(c => c.Sources).OrderBy(c => c.Id).ToListAsync(cancellationToken)).ToLookup(c => c.RequestItemId);

        // The CURRENT record of an item is its highest sequence; an invalidated one stays visible with its marker (it is history, never deleted).
        var receipts = (await context.WarehouseReceipts.AsNoTracking().ToListAsync(cancellationToken)).GroupBy(r => r.RequestItemId).ToDictionary(g => g.Key, g => g.OrderBy(r => r.Sequence).Last());
        var finished = (await context.ItemFinishedSent.AsNoTracking().ToListAsync(cancellationToken)).GroupBy(f => f.RequestItemId).ToDictionary(g => g.Key, g => g.OrderBy(f => f.Sequence).Last());
        var dispatches = (await context.CustomerDispatches.AsNoTracking().OrderBy(d => d.Id).ToListAsync(cancellationToken)).ToLookup(d => d.RequestItemId);
        var calculations = await context.ItemCostCalculations.AsNoTracking().Select(c => new { c.Id, c.RequestItemId, c.SequenceNo }).ToListAsync(cancellationToken);
        var decisions = await context.ItemPriceDecisions.AsNoTracking().ToListAsync(cancellationToken);
        var result = new List<CompletionItem>();
        foreach (var request in requests)
        {
            foreach (var item in request.Items.Where(i => i.IsRouteCompleted || receipts.ContainsKey(i.Id) || finished.ContainsKey(i.Id)))
            {
                var latest = calculations.Where(c => c.RequestItemId == item.Id).OrderBy(c => c.SequenceNo).LastOrDefault();
                var decision = latest is null ? null : decisions.FirstOrDefault(d => d.CalculationId == latest.Id);
                var receipt = receipts.GetValueOrDefault(item.Id);
                var mark = finished.GetValueOrDefault(item.Id);
                var itemDispatches = dispatches[item.Id].ToList();
                result.Add(new CompletionItem(
                    request.Id, request.RequestNo.Value, item.Id, item.ItemNo, item.PartName, item.Stages.OrderBy(s => s.StageNo).Last().AcceptedBaseUnits,
                    receipt, decision?.Status, decision?.ApprovedPriceRial, mark, itemDispatches)
                {
                    RouteComplete = item.IsRouteCompleted,
                    ReceiptInvalidation = receipt is null ? null : invalidations[(InvalidatedRecordKind.WarehouseReceipt, receipt.Id)].LastOrDefault(),
                    FinishedSentInvalidation = mark is null ? null : invalidations[(InvalidatedRecordKind.FinishedSent, mark.Id)].LastOrDefault(),
                    CostInvalidation = latest is null ? null : invalidations[(InvalidatedRecordKind.CostSnapshot, latest.Id)].LastOrDefault(),
                    FlaggedDispatchIds = itemDispatches.Where(d => invalidations[(InvalidatedRecordKind.CustomerDispatch, d.Id)].Any()).Select(d => d.Id).ToHashSet(),
                    Corrections = corrections[item.Id].ToList(),
                });
            }
        }

        return result.OrderBy(r => r.RequestNo, StringComparer.Ordinal).ThenBy(r => r.ItemNo, StringComparer.Ordinal).ToList();
    }

    public async Task<CompletionRecord> RecordAsync(long requestItemId, Func<CompletionFacts, CompletionRecord> decide, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(decide);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var request = await EfRequestRepository.LoadAggregate(context).AsNoTracking().FirstOrDefaultAsync(r => r.Items.Any(i => i.Id == requestItemId), cancellationToken)
            ?? throw new RecordNotFoundException("request item", requestItemId);
        var item = request.Items.Single(i => i.Id == requestItemId);
        // The CURRENT receipt and mark are the highest sequence unless a marker says they are no longer current (QC correction, reversal): then the item has none and the next sequence is free.
        var latestReceipt = await context.WarehouseReceipts.AsNoTracking().Where(r => r.RequestItemId == requestItemId).OrderByDescending(r => r.Sequence).FirstOrDefaultAsync(cancellationToken);
        var receipt = latestReceipt is not null && !await context.RecordInvalidations.AnyAsync(i => i.Kind == InvalidatedRecordKind.WarehouseReceipt && i.TargetId == latestReceipt.Id, cancellationToken) ? latestReceipt : null;
        var latestMark = await context.ItemFinishedSent.AsNoTracking().Where(f => f.RequestItemId == requestItemId).OrderByDescending(f => f.Sequence).FirstOrDefaultAsync(cancellationToken);
        var finished = latestMark is not null && !await context.RecordInvalidations.AnyAsync(i => i.Kind == InvalidatedRecordKind.FinishedSent && i.TargetId == latestMark.Id, cancellationToken);
        var latest = await context.ItemCostCalculations.AsNoTracking().Where(c => c.RequestItemId == requestItemId).OrderByDescending(c => c.SequenceNo).Select(c => c.Id).FirstOrDefaultAsync(cancellationToken);

        // A snapshot marked not current has no usable price decision: the item needs a new calculation and a new Management approval.
        var snapshotCurrent = latest != 0 && !await context.RecordInvalidations.AnyAsync(i => i.Kind == InvalidatedRecordKind.CostSnapshot && i.TargetId == latest, cancellationToken);
        var decision = latest == 0 || !snapshotCurrent ? null : await context.ItemPriceDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.CalculationId == latest, cancellationToken);

        var facts = new CompletionFacts(
            item.Id, request.Id, item.IsRouteCompleted, item.Stages.OrderBy(s => s.StageNo).LastOrDefault()?.AcceptedBaseUnits ?? 0, receipt, decision?.Id, decision?.Status, decision?.ApprovedPriceRial, finished)
        {
            NextReceiptSequence = (latestReceipt?.Sequence ?? 0) + 1,
            NextFinishedSentSequence = (latestMark?.Sequence ?? 0) + 1,
        };
        var record = decide(facts);
        if (record.Refusal != ValidationIssue.None)
        {
            return record;
        }

        if (record.Receipt is not null)
        {
            context.WarehouseReceipts.Add(record.Receipt);
        }

        if (record.FinishedSent is not null)
        {
            context.ItemFinishedSent.Add(record.FinishedSent);
        }

        if (record.Dispatch is not null)
        {
            context.CustomerDispatches.Add(record.Dispatch);
        }

        context.AuditLog.Add(audit);
        try
        {
            await PersistenceSave.SaveAsync(context, cancellationToken);
        }
        catch (PersistenceException) when (record.Receipt is not null || record.FinishedSent is not null)
        {
            // Two Warehouse users recording the same step at once: the unique key refuses the second, which is reported as the duplicate it is.
            return new CompletionRecord(record.Receipt is not null ? ValidationIssue.ReceiptDuplicate : ValidationIssue.FinishedSentDuplicate);
        }

        return record;
    }
}
