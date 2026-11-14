using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>QC quantity decisions on <see cref="ErpMesDbContext"/> (provider-neutral): the changed stage rows, the append-only lines or shortfall decision and the audit entries are one SaveChanges.</summary>
public sealed class EfQcInspectionRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IQcInspectionRepository
{
    public async Task<VersionedRequest> CommitAsync(
        long requestId, RowVersionExpectations expected, Func<Request, QcAppended> change, Func<Request, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var request = await EfRequestRepository.LoadAggregate(context).SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new RecordNotFoundException("request", requestId);

        // Every named row is compared BEFORE the Domain runs.
        PersistenceSave.CheckExpectedVersion(context, request, expected.Request);
        foreach (var (itemId, version) in expected.Items)
        {
            var item = request.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new RecordNotFoundException("request item", itemId);
            PersistenceSave.CheckExpectedVersion(context, item, version);
        }

        foreach (var (stageRef, version) in expected.Stages)
        {
            var stage = request.Items.FirstOrDefault(i => i.Id == stageRef.ItemId)?.Stages.FirstOrDefault(s => s.StageNo == stageRef.StageNo)
                ?? throw new RecordNotFoundException("stage " + stageRef.StageNo + " of item", stageRef.ItemId);
            PersistenceSave.CheckExpectedVersion(context, stage, version);
        }

        var appended = change(request);
        context.QcInspectionLines.AddRange(appended.Lines);
        if (appended.Decision is { } decision)
        {
            context.ShortfallDecisions.Add(decision);
        }

        context.AuditLog.AddRange(audit(request));
        await PersistenceSave.SaveAsync(context, cancellationToken);

        var items = new Dictionary<long, long>();
        var stages = new Dictionary<StageRef, long>();
        foreach (var item in request.Items)
        {
            items[item.Id] = context.GetRowVersion(item);
            foreach (var stage in item.Stages)
            {
                stages[new StageRef(item.Id, stage.StageNo)] = context.GetRowVersion(stage);
            }
        }

        return new VersionedRequest(request, new RequestVersions(context.GetRowVersion(request), items, stages));
    }

    public async Task<VersionedRequest> CorrectAsync(
        long requestId, long itemId, int stageNo, RowVersionExpectations expected, Func<Request, CorrectionFacts, CorrectionAppended> change,
        Func<Request, CorrectionAppended, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var request = await EfRequestRepository.LoadAggregate(context).SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new RecordNotFoundException("request", requestId);
        PersistenceSave.CheckExpectedVersion(context, request, expected.Request);
        foreach (var (expectedItemId, version) in expected.Items)
        {
            var item = request.Items.FirstOrDefault(i => i.Id == expectedItemId) ?? throw new RecordNotFoundException("request item", expectedItemId);
            PersistenceSave.CheckExpectedVersion(context, item, version);
        }

        foreach (var (stageRef, version) in expected.Stages)
        {
            var stage = request.Items.FirstOrDefault(i => i.Id == stageRef.ItemId)?.Stages.FirstOrDefault(s => s.StageNo == stageRef.StageNo)
                ?? throw new RecordNotFoundException("stage " + stageRef.StageNo + " of item", stageRef.ItemId);
            PersistenceSave.CheckExpectedVersion(context, stage, version);
        }

        var lines = await context.QcInspectionLines.AsNoTracking().Where(l => l.RequestItemId == itemId && l.StageNo == stageNo && l.Disposition == InspectionDisposition.Accepted).OrderBy(l => l.Id).ToListAsync(cancellationToken);
        var lineIds = lines.Select(l => l.Id).ToList();
        var superseded = (await context.QcCorrections.AsNoTracking().Where(c => c.RequestItemId == itemId && c.StageNo == stageNo).SelectMany(c => c.Sources).ToListAsync(cancellationToken))
            .Where(s => lineIds.Contains(s.InspectionLineId)).GroupBy(s => s.InspectionLineId).ToDictionary(g => g.Key, g => g.Sum(s => s.SupersededBaseUnits));
        var marked = context.RecordInvalidations.AsNoTracking();
        var latestCost = await context.ItemCostCalculations.AsNoTracking().Where(c => c.RequestItemId == itemId).OrderByDescending(c => c.SequenceNo).Select(c => (long?)c.Id).FirstOrDefaultAsync(cancellationToken);
        var latestDecision = latestCost is { } costId ? await context.ItemPriceDecisions.AsNoTracking().Where(d => d.CalculationId == costId).Select(d => (long?)d.Id).FirstOrDefaultAsync(cancellationToken) : null;
        var receipt = await context.WarehouseReceipts.AsNoTracking().Where(r => r.RequestItemId == itemId).OrderByDescending(r => r.Sequence).Select(r => (long?)r.Id).FirstOrDefaultAsync(cancellationToken);
        var finished = await context.ItemFinishedSent.AsNoTracking().Where(f => f.RequestItemId == itemId).OrderByDescending(f => f.Sequence).Select(f => (long?)f.Id).FirstOrDefaultAsync(cancellationToken);
        var dispatches = await context.CustomerDispatches.AsNoTracking().Where(d => d.RequestItemId == itemId).Select(d => d.Id).ToListAsync(cancellationToken);
        async Task<long?> StillCurrentAsync(InvalidatedRecordKind kind, long? id) =>
            id is { } value && !await marked.AnyAsync(i => i.Kind == kind && i.TargetId == value, cancellationToken) ? value : null;

        var facts = new CorrectionFacts(
            lines.Select(l => new AcceptedLineFact(l.Id, l.BaseUnits, superseded.GetValueOrDefault(l.Id))).ToList(),
            await StillCurrentAsync(InvalidatedRecordKind.CostSnapshot, latestCost),
            await StillCurrentAsync(InvalidatedRecordKind.PriceDecision, latestDecision),
            await StillCurrentAsync(InvalidatedRecordKind.WarehouseReceipt, receipt),
            await StillCurrentAsync(InvalidatedRecordKind.FinishedSent, finished),
            dispatches);
        var appended = change(request, facts);
        context.QcCorrections.Add(appended.Correction);
        context.RecordInvalidations.AddRange(appended.Invalidations);
        context.AuditLog.AddRange(audit(request, appended));
        await PersistenceSave.SaveAsync(context, cancellationToken);

        var items = new Dictionary<long, long>();
        var stages = new Dictionary<StageRef, long>();
        foreach (var item in request.Items)
        {
            items[item.Id] = context.GetRowVersion(item);
            foreach (var stage in item.Stages)
            {
                stages[new StageRef(item.Id, stage.StageNo)] = context.GetRowVersion(stage);
            }
        }

        return new VersionedRequest(request, new RequestVersions(context.GetRowVersion(request), items, stages));
    }

    public async Task<IReadOnlyList<QcCorrection>> ListCorrectionsAsync(long requestItemId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.QcCorrections.AsNoTracking().Include(c => c.Sources).Where(c => c.RequestItemId == requestItemId).OrderBy(c => c.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<QcInspectionLine>> ListLinesAsync(long requestItemId, int stageNo, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.QcInspectionLines.Where(l => l.RequestItemId == requestItemId && l.StageNo == stageNo).OrderBy(l => l.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ShortfallDecision>> ListDecisionsAsync(long requestItemId, int stageNo, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ShortfallDecisions.Where(d => d.RequestItemId == requestItemId && d.StageNo == stageNo).OrderBy(d => d.Id).ToListAsync(cancellationToken);
    }
}
