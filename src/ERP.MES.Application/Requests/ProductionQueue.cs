using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

/// <summary>Where an item stands on its route: no stages yet, some stage still open, or every stage approved by QC.</summary>
public enum QueueRouteState
{
    NoRoute = 0,
    Open,
    Completed,
}

/// <summary>
/// The stage an item is at: the first stage, in route order, that QC has not approved. <see cref="EstimatedMinutes"/> is the stage's planned total processing
/// time (minutes per piece x planned quantity); it is an estimate of work content, not a duration on the calendar and not a completion date.
/// <see cref="Version"/> is the row version the stage was read at, for the expected-version checks of later actions (Decision Log #77).
/// </summary>
public sealed record QueueStage(int StageNo, string Title, ItemStageStatus Status, decimal PlannedQuantity, decimal EstimatedMinutes, long Version);

/// <summary>
/// One item in the production queue with the request and customer it belongs to. <see cref="TotalEstimatedMinutes"/> is the sum of the planned total processing
/// minutes of all the item's stages (null when the item has no stages). Nothing here is a completion date. <see cref="ItemVersion"/> and the stage version are the
/// row versions at read time (Decision Log #77); this milestone reads only.
/// </summary>
public sealed record ProductionQueueEntry(
    long RequestId,
    string RequestNo,
    long CustomerId,
    string CustomerName,
    RequestStatus RequestStatus,
    DateOnly RequestDate,
    long ItemId,
    string ItemNo,
    string PartName,
    WorkType WorkType,
    decimal RequestedQuantity,
    Priority Priority,
    QueueRouteState RouteState,
    int StageCount,
    int ApprovedStageCount,
    QueueStage? CurrentStage,
    decimal? TotalEstimatedMinutes,
    long ItemVersion);

/// <summary>The queue as read at one moment, already in the documented display order (<see cref="ProductionQueueService.DisplayOrder"/>).</summary>
public sealed record ProductionQueue(IReadOnlyList<ProductionQueueEntry> Entries);

/// <summary>
/// The read-only Management production queue (docs/PRODUCTION_QUEUE.md). It lists every item of every request with its request, priority, current stage and QC position,
/// using the existing request repository; it changes nothing. Access is checked against the user as freshly loaded from the database
/// (<see cref="Permission.ViewProductionQueue"/>), never against a screen's snapshot.
///
/// DISPLAY ORDER (provisional, deterministic, NOT the queue policy): Planning priority (خیلی فوری, فوری, عادی), then request date (older first), then request id, then
/// item id. Equal-priority ties and the precedence of Management overrides over later Planning changes are undecided business questions (Decision Log "Unresolved");
/// this order is only a stable way to list rows and applies no override.
/// </summary>
public sealed class ProductionQueueService(IRequestRepository requests, CurrentAuthorization authorization)
{
    public async Task<bool> CanViewAsync(Principal? principal, CancellationToken cancellationToken = default) =>
        (await authorization.CheckAsync(principal, Permission.ViewProductionQueue, cancellationToken)).IsAllowed;

    public async Task<Outcome<ProductionQueue>> LoadAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ViewProductionQueue, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<ProductionQueue>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var summaries = await requests.ListAsync(customerId: null, cancellationToken);
            var entries = new List<ProductionQueueEntry>();
            foreach (var summary in summaries)
            {
                var loaded = await requests.FindWithVersionsAsync(summary.RequestId, cancellationToken);
                if (loaded is null)
                {
                    continue;
                }

                foreach (var item in loaded.Value.Items)
                {
                    entries.Add(ToEntry(summary, loaded, item));
                }
            }

            return Outcome<ProductionQueue>.Ok(new ProductionQueue(DisplayOrder(entries)));
        });
    }

    /// <summary>The documented provisional display order; see the class remarks. Every key is a total order and the last key (item id) is unique.</summary>
    public static IReadOnlyList<ProductionQueueEntry> DisplayOrder(IEnumerable<ProductionQueueEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries
            .OrderBy(e => e.Priority.SortOrder())
            .ThenBy(e => e.RequestDate)
            .ThenBy(e => e.RequestId)
            .ThenBy(e => e.ItemId)
            .ToList();
    }

    private static ProductionQueueEntry ToEntry(RequestSummary summary, VersionedRequest loaded, RequestItem item)
    {
        var stages = item.Stages.OrderBy(s => s.StageNo).ToList();
        var approved = stages.Count(s => s.Status == ItemStageStatus.QcApproved);
        var current = stages.FirstOrDefault(s => s.Status != ItemStageStatus.QcApproved);
        var state = stages.Count == 0 ? QueueRouteState.NoRoute : current is null ? QueueRouteState.Completed : QueueRouteState.Open;
        var queueStage = current is null
            ? null
            : new QueueStage(current.StageNo, current.StageTitle, current.Status, current.PlannedQuantity, current.PlannedTotalMinutes, loaded.Versions.StageVersion(item.Id, current.StageNo));

        return new ProductionQueueEntry(
            summary.RequestId,
            summary.RequestNo.Value,
            summary.CustomerId,
            summary.CustomerName,
            loaded.Value.Status,
            loaded.Value.RequestDate,
            item.Id,
            item.ItemNo,
            item.PartName,
            item.WorkType,
            item.RequestedQuantity,
            item.Priority,
            state,
            stages.Count,
            approved,
            queueStage,
            stages.Count == 0 ? null : stages.Sum(s => s.PlannedTotalMinutes),
            loaded.Versions.ItemVersion(item.Id));
    }
}
