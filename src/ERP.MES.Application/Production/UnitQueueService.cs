using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Production;

/// <summary>A queue command named rows that are not valid for it (not in the unit, not released, listed twice, no active override...). Nothing was saved.</summary>
internal sealed class QueueRowException(string message) : InvalidOperationException(message);

/// <summary>
/// The operational per-unit production queues (Decision Log #78, #99) built on the same request data as the overview (<see cref="ProductionQueueService"/>): the queue of a unit is derived
/// from persisted stages, Planning positions, stage holds and Management overrides, never from screen state. Every command (1) reloads the user from the database and checks the
/// permission AND, for Production users, the unit designation; (2) names the row versions it was based on, which are compared before anything changes; (3) lets the Domain validate; (4) saves
/// the change, its history and its audit entries in ONE transaction. A stale version, an invalid row or a failure saves nothing.
///
/// ORDER of a unit queue: <see cref="UnitQueueOrdering"/>. Management overrides sit above Planning's order inside their category, never replace Planning's priority or position, and stay until
/// Management clears them; a priority change by Planning while an override is active flags the override for Management review instead of dropping or ignoring it.
/// </summary>
public sealed class UnitQueueService(
    IRequestRepository requests,
    IUnitQueueRepository store,
    IPlanningMasterRepository planning,
    RequestGateService gates,
    CurrentAuthorization authorization,
    IClock clock,
    IAuditContext auditContext)
{
    private static readonly Permission[] AllPermissions =
    [
        Permission.ViewUnitQueue, Permission.OrderUnitQueue, Permission.ChangePlanningPriority, Permission.OverrideQueueOrder, Permission.ManageStageHold,
        Permission.RunStage, Permission.ReleaseStage, Permission.InspectOrCorrectQc, Permission.ManageUnitDesignations,
    ];

    public async Task<UnitQueueCapabilities> GetCapabilitiesAsync(Principal? principal, CancellationToken cancellationToken = default) =>
        CapabilitiesOf(await authorization.EvaluateAsync(principal, AllPermissions, cancellationToken));

    /// <summary>The units the user may see (all for Management and Planning; the designated ones for Production) and what they may do.</summary>
    public async Task<Outcome<UnitQueueOverview>> GetOverviewAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, AllPermissions, cancellationToken);
        if (!set.Has(Permission.ViewUnitQueue) || set.Current is null)
        {
            return Outcome<UnitQueueOverview>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var visible = await VisibleUnitsAsync(set.Current, cancellationToken);
            return Outcome<UnitQueueOverview>.Ok(new UnitQueueOverview(visible, CapabilitiesOf(set)));
        });
    }

    public async Task<Outcome<UnitQueue>> LoadAsync(Principal? principal, long unitId, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, AllPermissions, cancellationToken);
        if (!set.Has(Permission.ViewUnitQueue) || set.Current is null)
        {
            return Outcome<UnitQueue>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var visible = await VisibleUnitsAsync(set.Current, cancellationToken);
            if (visible.All(u => u.UnitId != unitId))
            {
                // A Production user is told nothing about units they are not designated to; Management and Planning see every unit, so a missing one is simply not found.
                return Outcome<UnitQueue>.Fail(set.Current.DepartmentCode == DepartmentCodes.Production ? OperationStatus.NotAuthorized : OperationStatus.NotFound);
            }

            var built = await BuildAsync(unitId, cancellationToken);
            return built is null ? Outcome<UnitQueue>.Fail(OperationStatus.NotFound) : Outcome<UnitQueue>.Ok(built.Queue);
        });
    }

    /// <summary>Planning gives the listed released stages their positions 1..n in the order listed (Decision Log #78). Other stages keep theirs.</summary>
    public async Task<Outcome<int>> SetPlanningOrderAsync(Principal? principal, long unitId, IReadOnlyList<QueueRowRef> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var decision = await authorization.CheckAsync(principal, Permission.OrderUnitQueue, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<int>.Fail(OperationStatus.NotAuthorized);
        }

        if (RowsProblem(rows) is { } problem)
        {
            return Outcome<int>.Invalid(problem);
        }

        return await RunAsync<int>(async () =>
        {
            var built = await BuildAsync(unitId, cancellationToken);
            if (built is null)
            {
                return Outcome<int>.Invalid(ValidationIssue.QueueUnitInvalid);
            }

            var user = decision.Current!;
            var now = clock.UtcNow;
            var commit = new QueueCommit(
                rows.Select(r => r.RequestId).Distinct().ToList(),
                [],
                rows.ToDictionary(r => (r.RequestId, new StageRef(r.ItemId, r.StageNo)), r => r.ExpectedStageVersion),
                new Dictionary<(long, long), long>(),
                new Dictionary<StageRef, long>(),
                workspace =>
                {
                    var position = 0;
                    foreach (var row in rows)
                    {
                        position++;
                        var (request, item, stage) = Resolve(workspace, row, unitId);
                        var before = stage.PlanningPosition;
                        if (item.SetPlanningPosition(row.StageNo, position))
                        {
                            workspace.AddAudit(Audit(user, now, AuditActions.QueuePositionsSet, "ItemStage", row.ItemId + "/" + row.StageNo,
                                $"Request {request.RequestNo.Value}, item {item.ItemNo}, stage {row.StageNo}: Planning position {Show(before)} -> {position} in unit {built.Queue.Unit.Code}"));
                        }
                    }
                });
            await store.CommitAsync(commit, cancellationToken);
            return Outcome<int>.Ok(rows.Count);
        });
    }

    /// <summary>Changes an item's Planning priority. If a Planning user changes it while a Management override covers one of the item's stages, the override is flagged for Management review (never dropped).</summary>
    public async Task<Outcome<int>> ChangePriorityAsync(
        Principal? principal, long requestId, long itemId, Priority priority, long expectedItemVersion, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ChangePlanningPriority, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<int>.Fail(OperationStatus.NotAuthorized);
        }

        if (!Enum.IsDefined(priority))
        {
            return Outcome<int>.Invalid(ValidationIssue.PriorityInvalid);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        var flagged = 0;
        return await RunAsync<int>(async () =>
        {
            var commit = new QueueCommit(
                [requestId],
                [],
                new Dictionary<(long, StageRef), long>(),
                new Dictionary<(long, long), long> { [(requestId, itemId)] = expectedItemVersion },
                new Dictionary<StageRef, long>(),
                workspace =>
                {
                    var request = workspace.Request(requestId);
                    var item = request.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new QueueRowException("The request has no such item.");
                    var old = item.Priority;
                    if (!item.ChangePriority(priority, now))
                    {
                        return;
                    }

                    workspace.AddAudit(Audit(user, now, AuditActions.ItemPriorityChanged, "RequestItem", itemId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        $"Request {request.RequestNo.Value}, item {item.ItemNo}: Planning priority {old.PersianName()} -> {priority.PersianName()}"));

                    // Management's own change of priority is an informed Management decision; a Planning change must not silently defeat or ignore a Management override.
                    if (user.DepartmentCode != DepartmentCodes.Planning)
                    {
                        return;
                    }

                    foreach (var stage in item.Stages)
                    {
                        if (workspace.FindOverride(itemId, stage.StageNo) is { IsActive: true } existing)
                        {
                            var note = $"Planning ({user.DisplayName}) changed the item's priority from {old.PersianName()} to {priority.PersianName()}";
                            existing.FlagForReview(note, now);
                            flagged++;
                            workspace.AddHistory(new QueueOverrideHistoryEntry(
                                NewOperationId(), itemId, stage.StageNo, existing.ProductionUnitId, QueueOverrideAction.FlaggedForReview, user.UserId, now, note, existing.Rank, existing.Rank, null, null));
                            workspace.AddAudit(Audit(user, now, AuditActions.QueueOverrideFlaggedForReview, "ItemStage", itemId + "/" + stage.StageNo,
                                $"Request {request.RequestNo.Value}, item {item.ItemNo}, stage {stage.StageNo}: Management override (rank {existing.Rank}) flagged for review after a Planning priority change"));
                        }
                    }
                });
            await store.CommitAsync(commit, cancellationToken);
            return Outcome<int>.Ok(flagged);
        });
    }

    /// <summary>
    /// Management places the listed released stages at the head of the unit's override order, in the order listed (Decision Log #78). Stages that were already overridden and are not listed keep their
    /// relative order after them. Planning's priority and positions are untouched. Every affected row gets a history entry with the manager, time, reason and old/new rank and queue position.
    /// </summary>
    public async Task<Outcome<int>> OverrideAsync(Principal? principal, long unitId, string? reason, IReadOnlyList<QueueRowRef> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var decision = await authorization.CheckAsync(principal, Permission.OverrideQueueOrder, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<int>.Fail(OperationStatus.NotAuthorized);
        }

        if (RowsProblem(rows) is { } problem)
        {
            return Outcome<int>.Invalid(problem);
        }

        if (!ReasonOk(reason))
        {
            return Outcome<int>.Invalid(ValidationIssue.QueueReasonRequired);
        }

        return await RunAsync<int>(async () =>
        {
            var built = await BuildAsync(unitId, cancellationToken);
            if (built is null)
            {
                return Outcome<int>.Invalid(ValidationIssue.QueueUnitInvalid);
            }

            var text = reason!.Trim();
            var user = decision.Current!;
            var now = clock.UtcNow;
            var operation = NewOperationId();
            var unit = built.Queue.Unit;

            // The ranks the operation produces, computed from the queue as read, so the history can show old and new positions. The same assignment runs again inside the transaction on the freshly loaded rows.
            var listed = rows.Select(r => new StageRef(r.ItemId, r.StageNo)).ToList();
            var simulated = built.Queue.Queue;
            var remaining = built.Queue.Queue.Where(e => e.OverrideRank is not null && !listed.Contains(new StageRef(e.ItemId, e.StageNo))).OrderBy(e => e.OverrideRank).ToList();
            var newRanks = new Dictionary<StageRef, int>();
            for (var i = 0; i < listed.Count; i++)
            {
                newRanks[listed[i]] = i + 1;
            }

            for (var i = 0; i < remaining.Count; i++)
            {
                newRanks[new StageRef(remaining[i].ItemId, remaining[i].StageNo)] = listed.Count + i + 1;
            }

            var afterOrder = UnitQueueOrdering.Sort(simulated.Select(e => newRanks.TryGetValue(new StageRef(e.ItemId, e.StageNo), out var rank) ? e with { OverrideRank = rank } : e))
                .ToDictionary(e => new StageRef(e.ItemId, e.StageNo), e => e.Order);
            var beforeOrder = built.Queue.Queue.ToDictionary(e => new StageRef(e.ItemId, e.StageNo), e => e.Order);

            var commit = new QueueCommit(
                rows.Select(r => r.RequestId).Distinct().ToList(),
                [unitId],
                rows.ToDictionary(r => (r.RequestId, new StageRef(r.ItemId, r.StageNo)), r => r.ExpectedStageVersion),
                new Dictionary<(long, long), long>(),
                rows.ToDictionary(r => new StageRef(r.ItemId, r.StageNo), r => r.ExpectedOverrideVersion ?? 0L),
                workspace =>
                {
                    for (var i = 0; i < rows.Count; i++)
                    {
                        var row = rows[i];
                        var (request, item, _) = Resolve(workspace, row, unitId);
                        var key = new StageRef(row.ItemId, row.StageNo);
                        var existing = workspace.FindOverride(row.ItemId, row.StageNo);
                        var oldRank = existing is { IsActive: true } ? existing.Rank : (int?)null;
                        var action = oldRank is null ? QueueOverrideAction.Set : QueueOverrideAction.Moved;
                        if (existing is null)
                        {
                            workspace.AddOverride(new QueueOverride(row.ItemId, row.StageNo, unitId, i + 1, text, user.UserId, now));
                        }
                        else
                        {
                            existing.Activate(unitId, i + 1, text, user.UserId, now);
                        }

                        Record(workspace, user, now, operation, request, item, row.StageNo, unitId, action, text, oldRank, i + 1, beforeOrder.GetValueOrDefault(key), afterOrder.GetValueOrDefault(key));
                    }

                    var rank = rows.Count;
                    foreach (var other in workspace.OverridesOfUnit(unitId).Where(o => o.IsActive && !listed.Contains(new StageRef(o.RequestItemId, o.StageNo))).OrderBy(o => o.Rank).ThenBy(o => o.RequestItemId).ThenBy(o => o.StageNo).ToList())
                    {
                        rank++;
                        if (other.Rank == rank)
                        {
                            continue;
                        }

                        var oldRank = other.Rank;
                        other.Activate(unitId, rank, other.Reason, other.SetByUserId, other.SetAtUtc);
                        var owner = workspace.FindItem(other.RequestItemId);
                        if (owner is { } found)
                        {
                            var key = new StageRef(other.RequestItemId, other.StageNo);
                            Record(workspace, user, now, operation, found.Request, found.Item, other.StageNo, unitId, QueueOverrideAction.Moved, "Renumbered by: " + text, oldRank, rank, beforeOrder.GetValueOrDefault(key), afterOrder.GetValueOrDefault(key));
                        }
                    }
                });
            await store.CommitAsync(commit, cancellationToken);
            return Outcome<int>.Ok(rows.Count);
        });
    }

    /// <summary>Management clears the overrides of the listed stages; they return to Planning's ordinary order.</summary>
    public async Task<Outcome<int>> ClearOverridesAsync(Principal? principal, long unitId, string? reason, IReadOnlyList<QueueRowRef> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var decision = await authorization.CheckAsync(principal, Permission.OverrideQueueOrder, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<int>.Fail(OperationStatus.NotAuthorized);
        }

        if (RowsProblem(rows) is { } problem || rows.Any(r => r.ExpectedOverrideVersion is null))
        {
            return Outcome<int>.Invalid(ValidationIssue.QueueRowsInvalid);
        }

        if (!ReasonOk(reason))
        {
            return Outcome<int>.Invalid(ValidationIssue.QueueReasonRequired);
        }

        return await RunAsync<int>(async () =>
        {
            var built = await BuildAsync(unitId, cancellationToken);
            if (built is null)
            {
                return Outcome<int>.Invalid(ValidationIssue.QueueUnitInvalid);
            }

            var text = reason!.Trim();
            var user = decision.Current!;
            var now = clock.UtcNow;
            var operation = NewOperationId();
            var cleared = rows.Select(r => new StageRef(r.ItemId, r.StageNo)).ToHashSet();
            var beforeOrder = built.Queue.Queue.ToDictionary(e => new StageRef(e.ItemId, e.StageNo), e => e.Order);
            var afterOrder = UnitQueueOrdering.Sort(built.Queue.Queue.Select(e => cleared.Contains(new StageRef(e.ItemId, e.StageNo)) ? e with { OverrideRank = null, OverrideNeedsReview = false } : e))
                .ToDictionary(e => new StageRef(e.ItemId, e.StageNo), e => e.Order);
            var commit = new QueueCommit(
                rows.Select(r => r.RequestId).Distinct().ToList(),
                [unitId],
                new Dictionary<(long, StageRef), long>(),
                new Dictionary<(long, long), long>(),
                rows.ToDictionary(r => new StageRef(r.ItemId, r.StageNo), r => r.ExpectedOverrideVersion!.Value),
                workspace =>
                {
                    foreach (var row in rows)
                    {
                        var request = workspace.Request(row.RequestId);
                        var item = request.Items.FirstOrDefault(i => i.Id == row.ItemId) ?? throw new QueueRowException("The request has no such item.");
                        var existing = workspace.FindOverride(row.ItemId, row.StageNo);
                        if (existing is not { IsActive: true } || existing.ProductionUnitId != unitId)
                        {
                            throw new QueueRowException("The stage has no active override in this unit.");
                        }

                        var oldRank = existing.Rank;
                        existing.Clear();
                        var key = new StageRef(row.ItemId, row.StageNo);
                        Record(workspace, user, now, operation, request, item, row.StageNo, unitId, QueueOverrideAction.Cleared, text, oldRank, null, beforeOrder.GetValueOrDefault(key), afterOrder.GetValueOrDefault(key));
                    }
                });
            await store.CommitAsync(commit, cancellationToken);
            return Outcome<int>.Ok(rows.Count);
        });
    }

    /// <summary>Management confirms that an override flagged for review stays as it is.</summary>
    public async Task<Outcome<int>> ConfirmOverrideReviewAsync(Principal? principal, long unitId, IReadOnlyList<QueueRowRef> rows, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var decision = await authorization.CheckAsync(principal, Permission.OverrideQueueOrder, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<int>.Fail(OperationStatus.NotAuthorized);
        }

        if (RowsProblem(rows) is { } problem || rows.Any(r => r.ExpectedOverrideVersion is null))
        {
            return Outcome<int>.Invalid(ValidationIssue.QueueRowsInvalid);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        var operation = NewOperationId();
        return await RunAsync<int>(async () =>
        {
            var commit = new QueueCommit(
                rows.Select(r => r.RequestId).Distinct().ToList(),
                [unitId],
                new Dictionary<(long, StageRef), long>(),
                new Dictionary<(long, long), long>(),
                rows.ToDictionary(r => new StageRef(r.ItemId, r.StageNo), r => r.ExpectedOverrideVersion!.Value),
                workspace =>
                {
                    foreach (var row in rows)
                    {
                        var request = workspace.Request(row.RequestId);
                        var item = request.Items.FirstOrDefault(i => i.Id == row.ItemId) ?? throw new QueueRowException("The request has no such item.");
                        var existing = workspace.FindOverride(row.ItemId, row.StageNo);
                        if (existing is null || existing.ProductionUnitId != unitId)
                        {
                            throw new QueueRowException("The stage has no override in this unit.");
                        }

                        existing.ConfirmReview();
                        Record(workspace, user, now, operation, request, item, row.StageNo, unitId, QueueOverrideAction.ReviewConfirmed, "Review confirmed", existing.Rank, existing.Rank, null, null);
                    }
                });
            await store.CommitAsync(commit, cancellationToken);
            return Outcome<int>.Ok(rows.Count);
        });
    }

    /// <summary>Stops a released stage because of a problem (reason required). Planning, or a Production user of the stage's unit.</summary>
    public Task<Outcome<int>> StopStageAsync(Principal? principal, QueueRowRef row, string? reason, CancellationToken cancellationToken = default) =>
        HoldAsync(principal, row, reason, stop: true, cancellationToken);

    /// <summary>Resumes a stopped stage. Planning, or a Production user of the stage's unit.</summary>
    public Task<Outcome<int>> ResumeStageAsync(Principal? principal, QueueRowRef row, CancellationToken cancellationToken = default) =>
        HoldAsync(principal, row, null, stop: false, cancellationToken);

    private async Task<Outcome<int>> HoldAsync(Principal? principal, QueueRowRef row, string? reason, bool stop, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        var decision = await authorization.CheckAsync(principal, Permission.ManageStageHold, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<int>.Fail(OperationStatus.NotAuthorized);
        }

        var text = reason?.Trim();
        if (stop && (string.IsNullOrEmpty(text) || text.Length > RequestItem.MaxStopReasonLength))
        {
            return Outcome<int>.Invalid(ValidationIssue.QueueReasonRequired);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        return await RunAsync<int>(async () =>
        {
            var loaded = await requests.FindAsync(row.RequestId, cancellationToken);
            var stageUnit = loaded?.Value.Items.FirstOrDefault(i => i.Id == row.ItemId)?.Stages.FirstOrDefault(s => s.StageNo == row.StageNo)?.ProductionUnitId;
            if (loaded is null || stageUnit is null)
            {
                return Outcome<int>.Invalid(ValidationIssue.QueueRowsInvalid);
            }

            if (!await CanAccessUnitAsync(user, stageUnit.Value, cancellationToken))
            {
                return Outcome<int>.Fail(OperationStatus.NotAuthorized);
            }

            var commit = new QueueCommit(
                [row.RequestId],
                [],
                new Dictionary<(long, StageRef), long> { [(row.RequestId, new StageRef(row.ItemId, row.StageNo))] = row.ExpectedStageVersion },
                new Dictionary<(long, long), long>(),
                new Dictionary<StageRef, long>(),
                workspace =>
                {
                    var request = workspace.Request(row.RequestId);
                    var item = request.Items.FirstOrDefault(i => i.Id == row.ItemId) ?? throw new QueueRowException("The request has no such item.");
                    if (stop)
                    {
                        item.StopStage(row.StageNo, text, user.UserId, now);
                    }
                    else
                    {
                        item.ResumeStage(row.StageNo);
                    }

                    workspace.AddAudit(Audit(user, now, stop ? AuditActions.StageStopped : AuditActions.StageResumed, "ItemStage", row.ItemId + "/" + row.StageNo,
                        $"Request {request.RequestNo.Value}, item {item.ItemNo}, stage {row.StageNo}: {(stop ? "stopped: " + text : "resumed")}"));
                });
            await store.CommitAsync(commit, cancellationToken);
            return Outcome<int>.Ok(1);
        });
    }

    // ---- reading -------------------------------------------------------------------------------------------------------------------------------------------------

    private sealed record Built(UnitQueue Queue);

    private async Task<Built?> BuildAsync(long unitId, CancellationToken cancellationToken)
    {
        var units = await planning.ListUnitsAsync(cancellationToken);
        var unitRow = units.FirstOrDefault(u => u.Value.Id == unitId);
        if (unitRow is null)
        {
            return null;
        }

        var unit = new UnitInfo(unitRow.Value.Id, unitRow.Value.Code, unitRow.Value.Name, unitRow.Value.IsActive);
        var overrides = (await store.ListOverridesAsync(cancellationToken)).ToDictionary(o => new StageRef(o.Value.RequestItemId, o.Value.StageNo));
        var summaries = await requests.ListAsync(customerId: null, cancellationToken);
        var queue = new List<UnitQueueEntry>();
        var pending = new List<UnitQueueEntry>();
        var completed = 0;
        foreach (var summary in summaries)
        {
            var loaded = await requests.FindWithVersionsAsync(summary.RequestId, cancellationToken);
            if (loaded is null || !loaded.Value.Items.Any(i => i.Stages.Any(s => s.ProductionUnitId == unitId)))
            {
                continue;
            }

            var closed = loaded.Value.Status is RequestStatus.Cancelled or RequestStatus.Closed;
            var gate = await gates.EvaluateAsync(summary.RequestId, cancellationToken);
            var gateOk = gate is { Satisfied: true };
            foreach (var item in loaded.Value.Items)
            {
                var stages = item.Stages.OrderBy(s => s.StageNo).ToList();
                var versions = stages.ToDictionary(s => s.StageNo, s => loaded.Versions.StageVersion(item.Id, s.StageNo));
                foreach (var stage in stages.Where(s => s.ProductionUnitId == unitId))
                {
                    if (stage.Status == ItemStageStatus.QcApproved)
                    {
                        completed++;
                        continue;
                    }

                    var released = stage.Status is ItemStageStatus.Waiting or ItemStageStatus.InProgress;
                    overrides.TryGetValue(new StageRef(item.Id, stage.StageNo), out var stored);
                    var active = stored is { Value.IsActive: true } && released && stored.Value.ProductionUnitId == unitId ? stored.Value : null;
                    var block = closed ? QueueBlock.RequestClosed
                        : !gateOk ? QueueBlock.GateNotSatisfied
                        : stage.Status switch
                        {
                            ItemStageStatus.Planned => QueueBlock.NotReleased,
                            ItemStageStatus.WaitingPreviousQc => QueueBlock.WaitingPreviousQc,
                            ItemStageStatus.WaitingQc => QueueBlock.WaitingQc,
                            _ => stage.IsStopped ? QueueBlock.StageStopped : QueueBlock.None,
                        };
                    QueueCategory? category = !released ? null : stage.IsStopped ? QueueCategory.Stopped : stage.Status == ItemStageStatus.InProgress ? QueueCategory.InProgress : QueueCategory.Waiting;
                    var entry = new UnitQueueEntry(
                        summary.RequestId, summary.RequestNo.Value, summary.CustomerName, loaded.Value.RequestDate, loaded.Value.Status,
                        item.Id, item.ItemNo, item.PartName, loaded.Value.WorkType, stage.StageNo, stages.Count, stage.StageTitle, stage.Status,
                        stage.PlannedQuantity, stage.PlannedTotalMinutes, item.Priority, category, block, stage.StopReason,
                        released ? stage.PlanningPosition : null, stage.AvailableSinceUtc,
                        active?.Rank, active?.NeedsReview ?? false, active?.Reason, active?.ReviewNote)
                    {
                        RequestVersion = loaded.Versions.Request,
                        ItemVersion = loaded.Versions.ItemVersion(item.Id),
                        StageVersion = versions[stage.StageNo],
                        OverrideVersion = stored?.Version,
                        StageVersions = versions,
                    };
                    (released ? queue : pending).Add(entry);
                }
            }
        }

        var ordered = UnitQueueOrdering.Sort(queue);
        var pendingOrdered = pending
            .OrderBy(e => e.Priority.SortOrder()).ThenBy(e => e.RequestDate).ThenBy(e => e.RequestId).ThenBy(e => e.ItemId).ThenBy(e => e.StageNo).ToList();
        return new Built(new UnitQueue(unit, ordered, pendingOrdered, completed, ordered.Count(e => e.OverrideNeedsReview)));
    }

    private async Task<IReadOnlyList<UnitInfo>> VisibleUnitsAsync(Principal user, CancellationToken cancellationToken)
    {
        var units = (await planning.ListUnitsAsync(cancellationToken)).Select(u => new UnitInfo(u.Value.Id, u.Value.Code, u.Value.Name, u.Value.IsActive)).OrderBy(u => u.Code, StringComparer.Ordinal).ToList();
        if (user.DepartmentCode != DepartmentCodes.Production)
        {
            return units;
        }

        var designated = (await store.ListDesignationsAsync(cancellationToken)).Where(d => d.Value.UserId == user.UserId && d.Value.IsActive).Select(d => d.Value.ProductionUnitId).ToHashSet();
        return units.Where(u => designated.Contains(u.UnitId)).ToList();
    }

    /// <summary>Planning and Management may act on every unit; a Production user only on a unit they are designated to; everyone else on none.</summary>
    internal async Task<bool> CanAccessUnitAsync(Principal user, long unitId, CancellationToken cancellationToken)
    {
        if (user.DepartmentCode is DepartmentCodes.Planning or DepartmentCodes.Management)
        {
            return true;
        }

        if (user.DepartmentCode != DepartmentCodes.Production)
        {
            return false;
        }

        return (await store.ListDesignationsAsync(cancellationToken)).Any(d => d.Value.UserId == user.UserId && d.Value.ProductionUnitId == unitId && d.Value.IsActive);
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------------------------------------------

    private static async Task<Outcome<T>> RunAsync<T>(Func<Task<Outcome<T>>> action)
    {
        try
        {
            return await CustomerService.GuardAsync(action);
        }
        catch (QueueRowException)
        {
            return Outcome<T>.Invalid(ValidationIssue.QueueRowsInvalid);
        }
        catch (ArgumentException)
        {
            return Outcome<T>.Invalid(ValidationIssue.QueueReasonRequired);
        }
        catch (InvalidOperationException)
        {
            return Outcome<T>.Fail(OperationStatus.InvalidState);
        }
    }

    private static ValidationIssue? RowsProblem(IReadOnlyList<QueueRowRef> rows)
    {
        if (rows.Count == 0 || rows.Count > 5000 || rows.Any(r => r is null))
        {
            return ValidationIssue.QueueRowsInvalid;
        }

        return rows.Select(r => (r.ItemId, r.StageNo)).Distinct().Count() != rows.Count ? ValidationIssue.QueueRowsInvalid : null;
    }

    private static bool ReasonOk(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= QueueOverride.MaxReasonLength;

    private static (Request Request, RequestItem Item, ItemStage Stage) Resolve(IQueueWorkspace workspace, QueueRowRef row, long unitId)
    {
        var request = workspace.Request(row.RequestId);
        var item = request.Items.FirstOrDefault(i => i.Id == row.ItemId) ?? throw new QueueRowException("The request has no such item.");
        var stage = item.Stages.FirstOrDefault(s => s.StageNo == row.StageNo) ?? throw new QueueRowException("The item has no such stage.");
        if (stage.ProductionUnitId != unitId || stage.Status is not (ItemStageStatus.Waiting or ItemStageStatus.InProgress))
        {
            throw new QueueRowException("The stage is not a released stage of this unit.");
        }

        return (request, item, stage);
    }

    private void Record(
        IQueueWorkspace workspace, Principal user, DateTime now, string operation, Request request, RequestItem item, int stageNo, long unitId,
        QueueOverrideAction action, string reason, int? oldRank, int? newRank, int? oldOrder, int? newOrder)
    {
        workspace.AddHistory(new QueueOverrideHistoryEntry(operation, item.Id, stageNo, unitId, action, user.UserId, now, reason, oldRank, newRank, oldOrder, newOrder));
        var code = action switch
        {
            QueueOverrideAction.Set => AuditActions.QueueOverrideSet,
            QueueOverrideAction.Moved => AuditActions.QueueOverrideMoved,
            QueueOverrideAction.Cleared => AuditActions.QueueOverrideCleared,
            QueueOverrideAction.ReviewConfirmed => AuditActions.QueueOverrideReviewConfirmed,
            _ => AuditActions.QueueOverrideFlaggedForReview,
        };
        workspace.AddAudit(Audit(user, now, code, "ItemStage", item.Id + "/" + stageNo,
            $"Request {request.RequestNo.Value}, item {item.ItemNo}, stage {stageNo}: {action}; rank {Show(oldRank)} -> {Show(newRank)}; queue position {Show(oldOrder)} -> {Show(newOrder)}; reason: {reason}"));
    }

    private AuditLogEntry Audit(Principal user, DateTime now, string code, string targetType, string targetId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, targetType, targetId, summary);

    private static string Show(int? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-";

    private static string NewOperationId() => Guid.NewGuid().ToString("N");

    private static UnitQueueCapabilities CapabilitiesOf(PermissionSet set) =>
        set.Current is null
            ? UnitQueueCapabilities.None
            : new UnitQueueCapabilities(
                set.Has(Permission.ViewUnitQueue), set.Has(Permission.OrderUnitQueue), set.Has(Permission.ChangePlanningPriority), set.Has(Permission.OverrideQueueOrder),
                set.Has(Permission.ManageStageHold), set.Has(Permission.RunStage), set.Has(Permission.ReleaseStage), set.Has(Permission.InspectOrCorrectQc), set.Has(Permission.ManageUnitDesignations));
}
