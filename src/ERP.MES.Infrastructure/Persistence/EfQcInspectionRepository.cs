using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
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
