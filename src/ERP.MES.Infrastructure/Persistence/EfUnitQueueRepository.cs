using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>
/// The unit-queue persistence on <see cref="ErpMesDbContext"/> (provider-neutral). A queue command is ONE unit of work: the requests it changes, the override rows of the units it touches, the
/// append-only history and the audit entries are saved by a single SaveChanges (one transaction) after every named row version was compared.
/// </summary>
public sealed class EfUnitQueueRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IUnitQueueRepository
{
    public async Task<IReadOnlyList<Stored<QueueOverride>>> ListOverridesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await context.QueueOverrides.OrderBy(o => o.Id).ToListAsync(cancellationToken);
        return list.Select(o => new Stored<QueueOverride>(o, context.GetRowVersion(o))).ToList();
    }

    public async Task<IReadOnlyList<Stored<UserUnitDesignation>>> ListDesignationsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await context.UserUnitDesignations.OrderBy(d => d.Id).ToListAsync(cancellationToken);
        return list.Select(d => new Stored<UserUnitDesignation>(d, context.GetRowVersion(d))).ToList();
    }

    public async Task<IReadOnlyList<QueueOverrideHistoryEntry>> ListOverrideHistoryAsync(long unitId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.QueueOverrideHistory.Where(e => e.ProductionUnitId == unitId).OrderBy(e => e.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductionUserInfo>> ListProductionUsersAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.Users
            .Where(u => u.Department.Code == DepartmentCodes.Production)
            .OrderBy(u => u.Id)
            .Select(u => new { u.Id, Username = u.Username.Value, u.DisplayName, u.IsActive })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new ProductionUserInfo(r.Id, r.Username, r.DisplayName, r.IsActive)).ToList();
    }

    public async Task CommitAsync(QueueCommit commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Overrides of the touched units first: their owners' requests must be loaded too, so renumbered rows can be described.
        var unitIds = commit.UnitIds.ToList();
        var overrides = unitIds.Count == 0
            ? new List<QueueOverride>()
            : await context.QueueOverrides.Where(o => unitIds.Contains(o.ProductionUnitId)).ToListAsync(cancellationToken);
        var requestIds = commit.RequestIds.ToHashSet();
        var ownerItemIds = overrides.Select(o => o.RequestItemId).Distinct().ToList();
        if (ownerItemIds.Count > 0)
        {
            requestIds.UnionWith(await context.RequestItems.Where(i => ownerItemIds.Contains(i.Id)).Select(i => EF.Property<long>(i, "RequestId")).Distinct().ToListAsync(cancellationToken));
        }

        var loaded = new Dictionary<long, Request>();
        foreach (var requestId in requestIds.OrderBy(i => i))
        {
            loaded[requestId] = await EfRequestRepository.LoadAggregate(context).SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
                ?? throw new RecordNotFoundException("request", requestId);
        }

        // Overrides of the named stages (a stage outside the touched units may carry one, for example when only the item's priority changes).
        var itemIds = loaded.Values.SelectMany(r => r.Items).Select(i => i.Id).ToList();
        var more = await context.QueueOverrides.Where(o => itemIds.Contains(o.RequestItemId) && !unitIds.Contains(o.ProductionUnitId)).ToListAsync(cancellationToken);
        overrides.AddRange(more);

        // Every named row is compared BEFORE the change runs.
        foreach (var ((requestId, stageRef), expected) in commit.StageVersions)
        {
            var request = loaded.GetValueOrDefault(requestId) ?? throw new RecordNotFoundException("request", requestId);
            var stage = request.Items.FirstOrDefault(i => i.Id == stageRef.ItemId)?.Stages.FirstOrDefault(s => s.StageNo == stageRef.StageNo)
                ?? throw new RecordNotFoundException("stage " + stageRef.StageNo + " of item", stageRef.ItemId);
            PersistenceSave.CheckExpectedVersion(context, stage, expected);
        }

        foreach (var ((requestId, itemId), expected) in commit.ItemVersions)
        {
            var request = loaded.GetValueOrDefault(requestId) ?? throw new RecordNotFoundException("request", requestId);
            var item = request.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new RecordNotFoundException("request item", itemId);
            PersistenceSave.CheckExpectedVersion(context, item, expected);
        }

        foreach (var (stageRef, expected) in commit.OverrideVersions)
        {
            var existing = overrides.FirstOrDefault(o => o.RequestItemId == stageRef.ItemId && o.StageNo == stageRef.StageNo);
            if (expected == 0)
            {
                if (existing is not null)
                {
                    throw new ConcurrencyConflictException("The stage got an override after it was loaded. Nothing was saved; reload it and try again.");
                }
            }
            else
            {
                if (existing is null)
                {
                    throw new RecordNotFoundException("queue override of item", stageRef.ItemId);
                }

                PersistenceSave.CheckExpectedVersion(context, existing, expected);
            }
        }

        var workspace = new Workspace(context, loaded, overrides);
        commit.Apply(workspace);
        await PersistenceSave.SaveAsync(context, cancellationToken);
    }

    public async Task<Stored<UserUnitDesignation>> AssignDesignationAsync(
        long userId, long productionUnitId, long assignedByUserId, DateTime assignedAtUtc, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.UserUnitDesignations.SingleOrDefaultAsync(d => d.UserId == userId && d.ProductionUnitId == productionUnitId, cancellationToken);
        if (existing is { IsActive: true })
        {
            return new Stored<UserUnitDesignation>(existing, context.GetRowVersion(existing));
        }

        UserUnitDesignation row;
        if (existing is null)
        {
            row = new UserUnitDesignation(userId, productionUnitId, assignedByUserId, assignedAtUtc);
            context.UserUnitDesignations.Add(row);
        }
        else
        {
            row = existing;
            row.Reinstate(assignedByUserId, assignedAtUtc);
        }

        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<UserUnitDesignation>(row, context.GetRowVersion(row));
    }

    public async Task<Stored<UserUnitDesignation>> RevokeDesignationAsync(
        long designationId, long expectedVersion, long revokedByUserId, DateTime revokedAtUtc, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await context.UserUnitDesignations.SingleOrDefaultAsync(d => d.Id == designationId, cancellationToken)
            ?? throw new RecordNotFoundException("unit designation", designationId);
        PersistenceSave.CheckExpectedVersion(context, row, expectedVersion);
        row.Revoke(revokedByUserId, revokedAtUtc);
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<UserUnitDesignation>(row, context.GetRowVersion(row));
    }

    private sealed class Workspace(ErpMesDbContext context, Dictionary<long, Request> requests, List<QueueOverride> overrides) : IQueueWorkspace
    {
        public Request Request(long requestId) => requests.GetValueOrDefault(requestId) ?? throw new RecordNotFoundException("request", requestId);

        public (Request Request, RequestItem Item)? FindItem(long itemId)
        {
            foreach (var request in requests.Values)
            {
                if (request.Items.FirstOrDefault(i => i.Id == itemId) is { } item)
                {
                    return (request, item);
                }
            }

            return null;
        }

        public QueueOverride? FindOverride(long itemId, int stageNo) => overrides.FirstOrDefault(o => o.RequestItemId == itemId && o.StageNo == stageNo);

        public IReadOnlyList<QueueOverride> OverridesOfUnit(long unitId) => overrides.Where(o => o.ProductionUnitId == unitId).ToList();

        public void AddOverride(QueueOverride value)
        {
            overrides.Add(value);
            context.QueueOverrides.Add(value);
        }

        public void AddHistory(QueueOverrideHistoryEntry entry) => context.QueueOverrideHistory.Add(entry);

        public void AddAudit(AuditLogEntry entry) => context.AuditLog.Add(entry);
    }
}
