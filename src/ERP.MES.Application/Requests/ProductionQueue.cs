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
    long ItemVersion)
{
    /// <summary>The request row's version as read (Decision Log #77). A stage action names it as an expected version.</summary>
    public long RequestVersion { get; init; }

    /// <summary>The row version of every stage of the item as read, by stage number. A stage action names the versions of the stages it changes or relies on.</summary>
    public IReadOnlyDictionary<int, long> StageVersions { get; init; } = new Dictionary<int, long>();
}

/// <summary>The queue as read at one moment, already in the documented display order (<see cref="ProductionQueueService.DisplayOrder"/>).</summary>
public sealed record ProductionQueue(IReadOnlyList<ProductionQueueEntry> Entries);

/// <summary>
/// The read-only production OVERVIEW for Management and Planning (docs/PRODUCTION_QUEUE.md, Decision Log #78). It lists every item of every request with its request, priority,
/// current stage and QC position, using the existing request repository; it changes nothing. Access is checked against the user as freshly loaded from the database
/// (<see cref="Permission.ViewProductionQueue"/>), never against a screen's snapshot.
///
/// DISPLAY ORDER (provisional, deterministic, NOT the authoritative operational queue order): Planning priority (خیلی فوری, فوری, عادی), then request date (older first), then
/// request id, then item id. The approved target order of a production unit's queue (operational category IN_PROGRESS, then STOPPED/problem states, then WAITING; then Planning
/// priority and Planning position; first come first served for equal positions; then planned end and stable identifiers) and the Management override policy are recorded in
/// Decision Log #78 but need per-unit released-stage queues and Planning-position data that do not exist yet, so none of it is applied here.
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

        return await CustomerService.GuardAsync(async () => Outcome<ProductionQueue>.Ok(new ProductionQueue(await ReadEntriesAsync(requests, cancellationToken))));
    }

    /// <summary>Reads every item of every request in the display order. The caller has already checked the permission. Also used by the stage-work screen (<see cref="StageWorkService"/>).</summary>
    internal static async Task<IReadOnlyList<ProductionQueueEntry>> ReadEntriesAsync(IRequestRepository requests, CancellationToken cancellationToken)
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

        return DisplayOrder(entries);
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
            loaded.Value.WorkType,
            item.RequestedQuantity,
            item.Priority,
            state,
            stages.Count,
            approved,
            queueStage,
            stages.Count == 0 ? null : stages.Sum(s => s.PlannedTotalMinutes),
            loaded.Versions.ItemVersion(item.Id))
        {
            RequestVersion = loaded.Versions.Request,
            StageVersions = stages.ToDictionary(s => s.StageNo, s => loaded.Versions.StageVersion(item.Id, s.StageNo)),
        };
    }
}
