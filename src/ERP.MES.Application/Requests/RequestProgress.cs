using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

/// <summary>How an item is classified for the request-level progress counts (Master Specification 12.5; one class per item, so the counts add up).</summary>
public enum ItemProgressKind
{
    /// <summary>Every stage of the route is QC approved.</summary>
    Completed = 1,

    /// <summary>The item's current stage has units QC sent to rework that are not resubmitted yet.</summary>
    Rework,

    /// <summary>Work is under way: a stage is in progress, or an external stage is at the contractor.</summary>
    Active,

    /// <summary>Not under way: no route yet, not released, waiting for the previous stage's QC, queued but not started, waiting for QC, a shortfall decision or the Warehouse's external dispatch, or stopped.</summary>
    WaitingOrBlocked,
}

/// <summary>One item's stage progress: completed stages of the route and the stage it is at.</summary>
public sealed record ItemProgress(long ItemId, string ItemNo, int StagesCompleted, int StageCount, ItemProgressKind Kind, int? CurrentStageNo, string? CurrentStageTitle, ItemStageStatus? CurrentStatus);

/// <summary>
/// Request-level and item-level progress derived from the persisted stage state, never typed (Master Specification 12.5, 12.5.1): the counts of completed, active, waiting/blocked and
/// rework items and each item's "completed stages / stages". The headline percentage formula is the owner-approved completed-items / total-items x 100 (R1, Decision Log #142).
/// The classification of each stage status into active versus waiting/blocked is a labelled default (Decision Log #141).
/// </summary>
public sealed record RequestProgress(int TotalItems, int Completed, int Active, int WaitingOrBlocked, int Rework, IReadOnlyList<ItemProgress> Items)
{
    /// <summary>Completed items divided by all items (0 for a request without items).</summary>
    public double CompletedItemRatio => TotalItems == 0 ? 0d : (double)Completed / TotalItems;

    /// <summary>
    /// The request's headline completion percentage, owner-approved formula (Decision Log #142 R1): completed items / total items x 100, shown as a whole number rounded DOWN so that 100 appears only
    /// when every item is complete. Null for a request without items (nothing is divided and no percentage is shown). Informational only: it never replaces a workflow gate, a QC requirement, an assembly dependency or a Management approval.
    /// </summary>
    public int? CompletedItemPercent => TotalItems == 0 ? null : (int)Math.Floor(100d * Completed / TotalItems);

    public static RequestProgress From(Request request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rows = new List<ItemProgress>();
        foreach (var item in request.Items.OrderBy(i => i.ItemNo, StringComparer.Ordinal))
        {
            var stages = item.Stages;
            var completed = stages.Count(s => s.Status == ItemStageStatus.QcApproved);
            var current = stages.FirstOrDefault(s => s.Status != ItemStageStatus.QcApproved);
            var kind = Classify(stages.Count, current);
            rows.Add(new ItemProgress(item.Id, item.ItemNo, completed, stages.Count, kind, current?.StageNo, current?.StageTitle, current?.Status));
        }

        return new RequestProgress(
            rows.Count,
            rows.Count(r => r.Kind == ItemProgressKind.Completed),
            rows.Count(r => r.Kind == ItemProgressKind.Active),
            rows.Count(r => r.Kind == ItemProgressKind.WaitingOrBlocked),
            rows.Count(r => r.Kind == ItemProgressKind.Rework),
            rows);
    }

    private static ItemProgressKind Classify(int stageCount, ItemStage? current)
    {
        if (stageCount == 0 || current is null)
        {
            return stageCount == 0 ? ItemProgressKind.WaitingOrBlocked : ItemProgressKind.Completed;
        }

        if (current.ReworkBaseUnits > 0)
        {
            return ItemProgressKind.Rework;
        }

        if (current.IsStopped)
        {
            return ItemProgressKind.WaitingOrBlocked;
        }

        return current.Status is ItemStageStatus.InProgress or ItemStageStatus.ExternalSent ? ItemProgressKind.Active : ItemProgressKind.WaitingOrBlocked;
    }
}
