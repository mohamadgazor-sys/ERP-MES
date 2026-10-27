using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Reporting;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Cost snapshots and price decisions on <see cref="ErpMesDbContext"/> (provider-neutral). Inputs are read in the same context that saves the snapshot, so the figures cannot drift between reading and saving.</summary>
public sealed class EfCostingRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : ICostingRepository
{
    public async Task<IReadOnlyList<CostingItemSummary>> ListItemsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var requests = await EfRequestRepository.LoadAggregate(context).AsNoTracking().ToListAsync(cancellationToken);
        var contracts = (await context.RequestContracts.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(c => c.RequestId);
        var notRequired = await NotRequiredItemsAsync(context, cancellationToken);
        var calculations = await context.ItemCostCalculations.Include(c => c.Lines).AsNoTracking().OrderBy(c => c.SequenceNo).ToListAsync(cancellationToken);
        var decisions = await context.ItemPriceDecisions.AsNoTracking().OrderBy(d => d.Id).ToListAsync(cancellationToken);
        var result = new List<CostingItemSummary>();
        foreach (var request in requests)
        {
            foreach (var item in request.Items.Where(i => i.IsRouteCompleted))
            {
                var latest = calculations.LastOrDefault(c => c.RequestItemId == item.Id);
                var decision = latest is null ? null : decisions.LastOrDefault(d => d.CalculationId == latest.Id);
                result.Add(new CostingItemSummary(
                    request.Id, request.RequestNo.Value, item.Id, item.ItemNo, item.PartName, request.WorkType, IsContractApproved(contracts, request.Id) && !notRequired.Contains(item.Id), item.Stages.OrderBy(s => s.StageNo).Last().AcceptedBaseUnits,
                    latest is null ? null : new Stored<ItemCostCalculation>(latest, 0),
                    decision is null ? null : new Stored<ItemPriceDecision>(decision, VersionOf(context, decision.Id))));
            }
        }

        return result.OrderBy(r => r.RequestNo, StringComparer.Ordinal).ThenBy(r => r.ItemNo, StringComparer.Ordinal).ToList();
    }

    public async Task<CostingHistory> HistoryAsync(long requestItemId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var calculations = await context.ItemCostCalculations.Include(c => c.Lines.OrderBy(l => l.LineNo)).Where(c => c.RequestItemId == requestItemId).OrderBy(c => c.SequenceNo).ToListAsync(cancellationToken);
        var decisions = await context.ItemPriceDecisions.Where(d => d.RequestItemId == requestItemId).OrderBy(d => d.Id).ToListAsync(cancellationToken);
        return new CostingHistory(
            calculations.Select(c => new Stored<ItemCostCalculation>(c, 0)).ToList(),
            decisions.Select(d => new Stored<ItemPriceDecision>(d, context.GetRowVersion(d))).ToList());
    }

    public async Task<(Stored<ItemCostCalculation>? Calculation, Stored<ItemPriceDecision>? Decision, CostBuild Build)> CalculateAsync(
        long requestItemId, Func<CostInputs, CostBuild> build, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var request = await EfRequestRepository.LoadAggregate(context).FirstOrDefaultAsync(r => r.Items.Any(i => i.Id == requestItemId), cancellationToken)
            ?? throw new RecordNotFoundException("request item", requestItemId);
        var item = request.Items.Single(i => i.Id == requestItemId);
        var contract = await context.RequestContracts.AsNoTracking().FirstOrDefaultAsync(c => c.RequestId == request.Id, cancellationToken);
        var itemNotRequired = (await NotRequiredItemsAsync(context, cancellationToken)).Contains(item.Id);
        var stages = item.Stages.OrderBy(s => s.StageNo).ToList();

        var reports = await context.DailyJobReports.Include(r => r.Lines).AsNoTracking()
            .Where(r => r.Status == JobReportStatus.Approved && r.Lines.Any(l => l.RequestItemId == requestItemId)).ToListAsync(cancellationToken);
        var lines = reports.SelectMany(r => r.Lines.Where(l => l.RequestItemId == requestItemId).Select(l => new ApprovedTimeLine(l.Id, l.MachineId, r.WorkDate, l.ProductiveMinutes))).ToList();
        var machineIds = lines.Select(l => l.MachineId).Distinct().ToList();
        var rates = await context.MachineHourlyRates.AsNoTracking().Where(r => machineIds.Contains(r.MachineId)).ToListAsync(cancellationToken);
        var codes = await context.Machines.AsNoTracking().Where(m => machineIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Code, cancellationToken);
        var material = item.MaterialId is { } materialId ? await context.Materials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == materialId, cancellationToken) : null;
        var prices = item.MaterialId is { } priceMaterial ? await context.MaterialPrices.AsNoTracking().Where(p => p.MaterialId == priceMaterial).ToListAsync(cancellationToken) : [];
        var next = (await context.ItemCostCalculations.Where(c => c.RequestItemId == requestItemId).Select(c => (int?)c.SequenceNo).MaxAsync(cancellationToken) ?? 0) + 1;

        var inputs = new CostInputs(
            item.Id, request.Id, request.RequestNo.Value, item.ItemNo, request.WorkType, IsContractApproved(contract) && !itemNotRequired, item.IsRouteCompleted, stages.FirstOrDefault()?.AvailableSinceUtc,
            stages.FirstOrDefault()?.InputBaseUnits, stages.LastOrDefault()?.AcceptedBaseUnits ?? 0, item.MaterialId, material?.Code, item.WeightKgPerBaseUnit, lines, rates, codes, prices, next);
        var built = build(inputs);
        if (built.Refusal != ValidationIssue.None)
        {
            return (null, null, built);
        }

        context.ItemCostCalculations.Add(built.Calculation!);
        context.ItemPriceDecisions.Add(built.Decision!);
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return (new Stored<ItemCostCalculation>(built.Calculation!, 0), new Stored<ItemPriceDecision>(built.Decision!, context.GetRowVersion(built.Decision!)), built);
    }

    public async Task<(Stored<ItemPriceDecision>? Row, ValidationIssue Refusal)> ChangeDecisionAsync(
        long decisionId, long expectedVersion, Func<ItemPriceDecision, ValidationIssue> change, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.ItemPriceDecisions.SingleOrDefaultAsync(d => d.Id == decisionId, cancellationToken) ?? throw new RecordNotFoundException("price decision", decisionId);
        PersistenceSave.CheckExpectedVersion(context, row, expectedVersion);

        // Only the latest snapshot of an item can still be decided: an older one was superseded by a recalculation.
        var latest = await context.ItemCostCalculations.Where(c => c.RequestItemId == row.RequestItemId).MaxAsync(c => c.SequenceNo, cancellationToken);
        var own = await context.ItemCostCalculations.Where(c => c.Id == row.CalculationId).Select(c => c.SequenceNo).SingleAsync(cancellationToken);
        if (own != latest)
        {
            throw new InvalidOperationException("The price decision belongs to a superseded cost snapshot.");
        }

        var refusal = change(row);
        if (refusal != ValidationIssue.None)
        {
            return (null, refusal);
        }

        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return (new Stored<ItemPriceDecision>(row, context.GetRowVersion(row)), ValidationIssue.None);
    }

    // Items whose latest contract decision is "not required" (Decision Log #111): the approved request contract does not make their price contract-basis.
    private static async Task<HashSet<long>> NotRequiredItemsAsync(ErpMesDbContext context, CancellationToken cancellationToken)
    {
        var rows = await context.ItemContractDecisions.AsNoTracking().OrderBy(d => d.Id).Select(d => new { d.RequestItemId, d.Requirement }).ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.RequestItemId).Where(g => g.Last().Requirement == ContractRequirement.NotRequired).Select(g => g.Key).ToHashSet();
    }

    private static bool IsContractApproved(IReadOnlyDictionary<long, RequestContract> contracts, long requestId) => contracts.TryGetValue(requestId, out var c) && IsContractApproved(c);

    private static bool IsContractApproved(RequestContract? contract) => contract is { Requirement: ContractRequirement.Required, Status: ContractStatus.Approved };

    private static long VersionOf(ErpMesDbContext context, long decisionId) =>
        context.ItemPriceDecisions.AsNoTracking().Where(d => d.Id == decisionId).Select(d => EF.Property<long>(d, ErpMesDbContext.RowVersionPropertyName)).Single();
}
