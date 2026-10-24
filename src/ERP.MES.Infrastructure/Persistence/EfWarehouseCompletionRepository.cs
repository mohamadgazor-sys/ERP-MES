using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Costing;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Warehouse completion records on <see cref="ErpMesDbContext"/> (provider-neutral). The facts are read in the same context that saves the new row; a unique key backs the duplicate rules.</summary>
public sealed class EfWarehouseCompletionRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IWarehouseCompletionRepository
{
    public async Task<IReadOnlyList<CompletionItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var requests = await EfRequestRepository.LoadAggregate(context).AsNoTracking().ToListAsync(cancellationToken);
        var receipts = (await context.WarehouseReceipts.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(r => r.RequestItemId);
        var finished = (await context.ItemFinishedSent.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(f => f.RequestItemId);
        var dispatches = (await context.CustomerDispatches.AsNoTracking().OrderBy(d => d.Id).ToListAsync(cancellationToken)).ToLookup(d => d.RequestItemId);
        var calculations = await context.ItemCostCalculations.AsNoTracking().Select(c => new { c.Id, c.RequestItemId, c.SequenceNo }).ToListAsync(cancellationToken);
        var decisions = await context.ItemPriceDecisions.AsNoTracking().ToListAsync(cancellationToken);
        var result = new List<CompletionItem>();
        foreach (var request in requests)
        {
            foreach (var item in request.Items.Where(i => i.IsRouteCompleted))
            {
                var latest = calculations.Where(c => c.RequestItemId == item.Id).OrderBy(c => c.SequenceNo).LastOrDefault();
                var decision = latest is null ? null : decisions.FirstOrDefault(d => d.CalculationId == latest.Id);
                result.Add(new CompletionItem(
                    request.Id, request.RequestNo.Value, item.Id, item.ItemNo, item.PartName, item.Stages.OrderBy(s => s.StageNo).Last().AcceptedBaseUnits,
                    receipts.GetValueOrDefault(item.Id), decision?.Status, decision?.ApprovedPriceRial, finished.GetValueOrDefault(item.Id), dispatches[item.Id].ToList()));
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
        var receipt = await context.WarehouseReceipts.AsNoTracking().FirstOrDefaultAsync(r => r.RequestItemId == requestItemId, cancellationToken);
        var finished = await context.ItemFinishedSent.AsNoTracking().AnyAsync(f => f.RequestItemId == requestItemId, cancellationToken);
        var latest = await context.ItemCostCalculations.AsNoTracking().Where(c => c.RequestItemId == requestItemId).OrderByDescending(c => c.SequenceNo).Select(c => c.Id).FirstOrDefaultAsync(cancellationToken);
        var decision = latest == 0 ? null : await context.ItemPriceDecisions.AsNoTracking().FirstOrDefaultAsync(d => d.CalculationId == latest, cancellationToken);

        var facts = new CompletionFacts(
            item.Id, request.Id, item.IsRouteCompleted, item.Stages.OrderBy(s => s.StageNo).LastOrDefault()?.AcceptedBaseUnits ?? 0, receipt, decision?.Id, decision?.Status, decision?.ApprovedPriceRial, finished);
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
