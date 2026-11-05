using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>External dispatches and returns on <see cref="ErpMesDbContext"/> (provider-neutral): the changed stage rows, the new or cancelled shipment and the audit entries are one SaveChanges.</summary>
public sealed class EfExternalShipmentRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IExternalShipmentRepository
{
    public async Task<VersionedRequest> CommitAsync(
        long requestId, RowVersionExpectations expected, long itemId, int stageNo, Func<Request, IReadOnlyList<ExternalShipment>, ExternalShipment?> change,
        Func<Request, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var request = await EfRequestRepository.LoadAggregate(context).SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new RecordNotFoundException("request", requestId);

        // Every named row is compared BEFORE the Domain runs.
        PersistenceSave.CheckExpectedVersion(context, request, expected.Request);
        foreach (var (expectedItem, version) in expected.Items)
        {
            var item = request.Items.FirstOrDefault(i => i.Id == expectedItem) ?? throw new RecordNotFoundException("request item", expectedItem);
            PersistenceSave.CheckExpectedVersion(context, item, version);
        }

        foreach (var (stageRef, version) in expected.Stages)
        {
            var stage = request.Items.FirstOrDefault(i => i.Id == stageRef.ItemId)?.Stages.FirstOrDefault(s => s.StageNo == stageRef.StageNo)
                ?? throw new RecordNotFoundException("stage " + stageRef.StageNo + " of item", stageRef.ItemId);
            PersistenceSave.CheckExpectedVersion(context, stage, version);
        }

        var shipments = await context.ExternalShipments.Where(s => s.RequestItemId == itemId && s.StageNo == stageNo).OrderBy(s => s.Id).ToListAsync(cancellationToken);
        var added = change(request, shipments);
        if (added is not null)
        {
            context.ExternalShipments.Add(added);
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

    public async Task<IReadOnlyList<ExternalShipment>> ListAsync(IReadOnlyCollection<long> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if (itemIds.Count == 0)
        {
            return [];
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ExternalShipments.AsNoTracking().Where(s => itemIds.Contains(s.RequestItemId)).OrderBy(s => s.Id).ToListAsync(cancellationToken);
    }
}
