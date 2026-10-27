using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>
/// Work session persistence on <see cref="ErpMesDbContext"/> (provider-neutral, Decision Log #112). A command runs in ONE explicit transaction on one context; the open-interval uniqueness
/// of session, machine and operator is enforced by the database as well as by the Application, so a race between two supervisors cannot double-book a resource.
/// </summary>
public sealed class EfWorkSessionRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IWorkSessionRepository
{
    public async Task<IReadOnlyList<Stored<PauseReason>>> ListReasonsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await context.PauseReasons.OrderBy(r => r.Id).ToListAsync(cancellationToken);
        return rows.Select(r => new Stored<PauseReason>(r, context.GetRowVersion(r))).ToList();
    }

    public async Task<Stored<PauseReason>> AddReasonAsync(PauseReason reason, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.PauseReasons.Add(reason);
        context.AuditLog.Add(audit);
        try
        {
            await PersistenceSave.SaveAsync(context, cancellationToken);
        }
        catch (PersistenceException exception) when (exception is not ConcurrencyConflictException)
        {
            throw new PersistenceException("The pause reason code already exists.", exception);
        }

        return new Stored<PauseReason>(reason, context.GetRowVersion(reason));
    }

    public async Task<Stored<PauseReason>> UpdateReasonAsync(long id, long expectedVersion, Action<PauseReason> change, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var reason = await context.PauseReasons.SingleOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new RecordNotFoundException("pause reason", id);
        PersistenceSave.CheckExpectedVersion(context, reason, expectedVersion);
        change(reason);
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<PauseReason>(reason, context.GetRowVersion(reason));
    }

    public async Task<IReadOnlyList<SessionRow>> ListForUnitAsync(long unitId, DateTime finishedSince, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var sessions = await Load(context).Where(s => s.ProductionUnitId == unitId && (s.Status == WorkSessionStatus.Assigned || s.Status == WorkSessionStatus.Active || s.Status == WorkSessionStatus.Paused
            || (s.FinishedAtUtc != null && s.FinishedAtUtc >= finishedSince))).OrderBy(s => s.Id).ToListAsync(cancellationToken);
        return await ToRowsAsync(context, sessions, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionRow>> ListForStageAsync(long itemId, int stageNo, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var sessions = await Load(context).Where(s => s.RequestItemId == itemId && s.StageNo == stageNo).OrderBy(s => s.Id).ToListAsync(cancellationToken);
        return await ToRowsAsync(context, sessions, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionRow>> ListForOperatorAsync(long operatorId, DateTime finishedSince, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var sessions = await Load(context).Where(s => s.OperatorId == operatorId && (s.Status == WorkSessionStatus.Assigned || s.Status == WorkSessionStatus.Active || s.Status == WorkSessionStatus.Paused
            || (s.FinishedAtUtc != null && s.FinishedAtUtc >= finishedSince))).OrderBy(s => s.Id).ToListAsync(cancellationToken);
        return await ToRowsAsync(context, sessions, cancellationToken);
    }

    public async Task<SessionRow?> FindAsync(long sessionId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var session = await Load(context).SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        return session is null ? null : (await ToRowsAsync(context, [session], cancellationToken)).Single();
    }

    public async Task<IReadOnlyList<OutageEvent>> ListOpenOutagesAsync(long? unitId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.OutageEvents.AsNoTracking().Where(o => o.EndedAtUtc == null && (unitId == null || o.ProductionUnitId == null || o.ProductionUnitId == unitId)).OrderBy(o => o.Id).ToListAsync(cancellationToken);
    }

    public async Task<long?> FindStageUnitAsync(long itemId, int stageNo, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ItemStages.AsNoTracking().Where(s => EF.Property<long>(s, "RequestItemId") == itemId && s.StageNo == stageNo).Select(s => s.ProductionUnitId).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> StageHasSessionsAsync(long itemId, int stageNo, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.WorkSessions.AnyAsync(s => s.RequestItemId == itemId && s.StageNo == stageNo && s.Status != WorkSessionStatus.Cancelled, cancellationToken);
    }

    public async Task<SessionCommitResult> CommitAsync(Func<ISessionWorkspace, Task<SessionCommitOutput>> apply, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(apply);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var workspace = new Workspace(context);
        var output = await apply(workspace);
        await workspace.CheckpointAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        var stored = output.Sessions.Select(s => new Stored<WorkSession>(s, context.GetRowVersion(s))).ToList();
        return new SessionCommitResult(stored, output.Outage);
    }

    private static IQueryable<WorkSession> Load(ErpMesDbContext context) =>
        context.WorkSessions
            .Include(s => s.Intervals.OrderBy(i => i.Id))
            .Include(s => s.Pauses.OrderBy(p => p.Id))
            .Include(s => s.Events.OrderBy(e => e.Id))
            .Include(s => s.Corrections.OrderBy(c => c.Id))
            .AsSplitQuery();

    private static async Task<IReadOnlyList<SessionRow>> ToRowsAsync(ErpMesDbContext context, IReadOnlyList<WorkSession> sessions, CancellationToken cancellationToken)
    {
        if (sessions.Count == 0)
        {
            return [];
        }

        var itemIds = sessions.Select(s => s.RequestItemId).Distinct().ToList();
        var items = await (from i in context.RequestItems.AsNoTracking()
                           join r in context.Requests.AsNoTracking() on EF.Property<long>(i, "RequestId") equals r.Id
                           where itemIds.Contains(i.Id)
                           select new { i.Id, i.ItemNo, i.PartName, RequestNo = r.RequestNo.Value }).ToDictionaryAsync(x => x.Id, cancellationToken);
        var stageTitles = (await context.ItemStages.AsNoTracking().Where(s => itemIds.Contains(EF.Property<long>(s, "RequestItemId"))).Select(s => new { ItemId = EF.Property<long>(s, "RequestItemId"), s.StageNo, s.StageTitle }).ToListAsync(cancellationToken))
            .ToDictionary(x => (x.ItemId, x.StageNo), x => x.StageTitle);
        var machineIds = sessions.Select(s => s.MachineId).Distinct().ToList();
        var machines = await context.Machines.AsNoTracking().Where(m => machineIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.Code, cancellationToken);
        var operatorIds = sessions.Select(s => s.OperatorId).Distinct().ToList();
        var operators = await context.Operators.AsNoTracking().Where(o => operatorIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, cancellationToken);
        var unitIds = sessions.Select(s => s.ProductionUnitId).Distinct().ToList();
        var units = await context.ProductionUnits.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Code, cancellationToken);
        return sessions.Select(s =>
        {
            var item = items[s.RequestItemId];
            var op = operators[s.OperatorId];
            return new SessionRow(
                new Stored<WorkSession>(s, context.GetRowVersion(s)), item.RequestNo, item.ItemNo, item.PartName, stageTitles.GetValueOrDefault((s.RequestItemId, s.StageNo), string.Empty),
                units[s.ProductionUnitId], machines[s.MachineId], op.FullName, op.Code);
        }).ToList();
    }

    private sealed class Workspace(ErpMesDbContext context) : ISessionWorkspace
    {
        public Task<WorkSession?> FindSessionAsync(long sessionId, CancellationToken cancellationToken = default) =>
            Load(context).SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken)!;

        public void ExpectVersion(WorkSession session, long expectedVersion) => PersistenceSave.CheckExpectedVersion(context, session, expectedVersion);

        public async Task<IReadOnlyList<WorkSession>> StageSessionsAsync(long itemId, int stageNo, CancellationToken cancellationToken = default) =>
            await Load(context).Where(s => s.RequestItemId == itemId && s.StageNo == stageNo).OrderBy(s => s.Id).ToListAsync(cancellationToken);

        public async Task<(Request Request, RequestItem Item, ItemStage Stage)?> FindStageAsync(long itemId, int stageNo, CancellationToken cancellationToken = default)
        {
            var request = await EfRequestRepository.LoadAggregate(context).FirstOrDefaultAsync(r => r.Items.Any(i => i.Id == itemId), cancellationToken);
            var item = request?.Items.FirstOrDefault(i => i.Id == itemId);
            var stage = item?.Stages.FirstOrDefault(s => s.StageNo == stageNo);
            return request is null || item is null || stage is null ? null : (request, item, stage);
        }

        public void ExpectAndFenceStage(ItemStage stage, long? expectedVersion)
        {
            PersistenceSave.CheckExpectedVersion(context, stage, expectedVersion);
            var version = context.Entry(stage).Property(ErpMesDbContext.RowVersionPropertyName);
            version.IsModified = true;
        }

        public Task<bool> IsMachineBusyAsync(long machineId, long exceptSessionId, CancellationToken cancellationToken = default) =>
            context.WorkSessionIntervals.AnyAsync(i => i.MachineId == machineId && i.EndedAtUtc == null && EF.Property<long>(i, "SessionId") != exceptSessionId, cancellationToken);

        public Task<bool> IsOperatorBusyAsync(long operatorId, long exceptSessionId, CancellationToken cancellationToken = default) =>
            context.WorkSessionIntervals.AnyAsync(i => i.OperatorId == operatorId && i.EndedAtUtc == null && EF.Property<long>(i, "SessionId") != exceptSessionId, cancellationToken);

        public async Task<IReadOnlyList<WorkSession>> ActiveSessionsAsync(long? unitId, IReadOnlyCollection<long> machineIds, CancellationToken cancellationToken = default) =>
            await Load(context).Where(s => s.Status == WorkSessionStatus.Active && (unitId == null || s.ProductionUnitId == unitId) && (machineIds.Count == 0 || machineIds.Contains(s.MachineId)))
                .OrderBy(s => s.Id).ToListAsync(cancellationToken);

        public Task<Machine?> FindMachineAsync(long machineId, CancellationToken cancellationToken = default) => context.Machines.AsNoTracking().SingleOrDefaultAsync(m => m.Id == machineId, cancellationToken)!;

        public Task<PauseReason?> FindReasonAsync(long reasonId, CancellationToken cancellationToken = default) => context.PauseReasons.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reasonId, cancellationToken)!;

        public Task<OutageEvent?> FindOutageAsync(long outageId, CancellationToken cancellationToken = default) => context.OutageEvents.SingleOrDefaultAsync(o => o.Id == outageId, cancellationToken)!;

        public async Task<IReadOnlyList<WorkSession>> SessionsPausedByOutageAsync(long outageId, CancellationToken cancellationToken = default) =>
            await Load(context).Where(s => s.Status == WorkSessionStatus.Paused && s.Pauses.Any(p => p.OutageEventId == outageId && p.ResumedAtUtc == null)).OrderBy(s => s.Id).ToListAsync(cancellationToken);

        public Task<bool> StageHasReportLinesAsync(long itemId, int stageNo, CancellationToken cancellationToken = default) =>
            context.JobReportLines.AnyAsync(l => l.RequestItemId == itemId && l.StageNo == stageNo, cancellationToken);

        public async Task<bool> OverlapsOtherSessionsAsync(WorkSession session, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            foreach (var interval in session.Intervals)
            {
                var end = interval.EndedAtUtc ?? nowUtc;
                var start = interval.StartedAtUtc;
                if (await context.WorkSessionIntervals.AnyAsync(
                        i => EF.Property<long>(i, "SessionId") != session.Id && (i.MachineId == interval.MachineId || i.OperatorId == interval.OperatorId)
                             && i.StartedAtUtc < end && (i.EndedAtUtc == null || i.EndedAtUtc > start), cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        public void Add(WorkSession session) => context.WorkSessions.Add(session);

        public void Add(OutageEvent outage) => context.OutageEvents.Add(outage);

        public void AddAudit(AuditLogEntry entry) => context.AuditLog.Add(entry);

        public async Task CheckpointAsync(CancellationToken cancellationToken = default)
        {
            context.ChangeTracker.DetectChanges();

            // A change to a child row (a new interval or event) raises the session's own version, so two commands that read the same session conflict.
            foreach (var entry in context.ChangeTracker.Entries<WorkSession>().Where(e => e.State == EntityState.Unchanged).ToList())
            {
                var session = entry.Entity;
                var changed = session.Intervals.Cast<object>().Concat(session.Pauses).Concat(session.Events).Concat(session.Corrections)
                    .Any(child => context.Entry(child).State is EntityState.Added or EntityState.Modified);
                if (changed)
                {
                    entry.Property(ErpMesDbContext.RowVersionPropertyName).IsModified = true;
                }
            }

            try
            {
                await PersistenceSave.SaveAsync(context, cancellationToken);
            }
            catch (PersistenceException exception) when (exception is not ConcurrencyConflictException)
            {
                // The only expected database refusal of a session command is a second open interval for a session, machine or operator: another command got there first.
                throw new ConcurrencyConflictException("The machine, operator or session was taken by another command a moment ago. Nothing was saved; reload and try again.", exception);
            }
        }
    }
}
