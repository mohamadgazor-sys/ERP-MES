using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Production;

/// <summary>Operational category of a released stage in a unit queue (Decision Log #78): IN_PROGRESS first, then STOPPED / problem, then WAITING.</summary>
public enum QueueCategory
{
    InProgress = 1,
    Stopped = 2,
    Waiting = 3,
}

/// <summary>Why a stage in a unit queue (or its pending list) is not actionable now. <see cref="None"/> means it is actionable (for the role that acts on its status).</summary>
public enum QueueBlock
{
    None = 0,

    /// <summary>Production stopped the stage because of a problem; the stop reason is shown. It stays out of the actionable work until it is resumed.</summary>
    StageStopped,

    /// <summary>The request is cancelled or closed.</summary>
    RequestClosed,

    /// <summary>A production-release gate no longer holds (Management / Drawing / contract / inquiry / clarification); nothing may start until it holds again.</summary>
    GateNotSatisfied,

    /// <summary>The first stage has not been released by Planning yet.</summary>
    NotReleased,

    /// <summary>QC has not approved the immediately preceding stage.</summary>
    WaitingPreviousQc,

    /// <summary>The stage was submitted and waits for QC.</summary>
    WaitingQc,

    /// <summary>Scrap left a shortfall that Management must accept or Planning must answer before the stage can be QC approved.</summary>
    WaitingShortfallDecision,
}

/// <summary>
/// One stage in a production unit's operational queue or pending list. It carries stable identifiers (request, item, stage number), the facts the order is derived from, why it is not
/// actionable, and every row version the entry was read with (for the expected-version checks of the actions). Planned end is null: it needs the working calendar and capacity
/// (phase 6a) and is never invented.
/// </summary>
public sealed record UnitQueueEntry(
    long RequestId,
    string RequestNo,
    string CustomerName,
    DateOnly RequestDate,
    RequestStatus RequestStatus,
    long ItemId,
    string ItemNo,
    string PartName,
    WorkType WorkType,
    int StageNo,
    int StageCount,
    string StageTitle,
    ItemStageStatus Status,
    decimal PlannedQuantity,
    decimal EstimatedMinutes,
    Priority Priority,
    QueueCategory? Category,
    QueueBlock Block,
    string? StopReason,
    int? PlanningPosition,
    DateTime? AvailableSinceUtc,
    int? OverrideRank,
    bool OverrideNeedsReview,
    string? OverrideReason,
    string? OverrideReviewNote)
{
    /// <summary>Position in the unit queue (1 = first); 0 for entries of the pending list, which have no queue position.</summary>
    public int Order { get; init; }

    public DateTime? PlannedEndUtc { get; init; }

    /// <summary>Base units QC sent to rework that production has to redo and resubmit (0 when none).</summary>
    public long ReworkBaseUnits { get; init; }

    public bool IsActionable => Block == QueueBlock.None;

    /// <summary>The item's production gate state when it does not hold (Management, Drawing, the item's contract decision, inquiries, clarifications), so the screen can name the exact reason (Decision Log #115); null when the gates hold.</summary>
    public ProductionGateStatus? Gate { get; init; }

    public bool IsOverridden => OverrideRank is not null;

    public long RequestVersion { get; init; }

    public long ItemVersion { get; init; }

    public long StageVersion { get; init; }

    /// <summary>Row version of the stage overrides row, if one exists (active or not); the expected version of an override command.</summary>
    public long? OverrideVersion { get; init; }

    public IReadOnlyDictionary<int, long> StageVersions { get; init; } = new Dictionary<int, long>();

    public StageTarget ToStageTarget() => new(RequestId, ItemId, StageNo, RequestVersion, ItemVersion, StageVersions);

    public QueueRowRef ToRowRef() => new(RequestId, ItemId, StageNo, StageVersion, OverrideVersion);
}

/// <summary>Names a stage of a queue and the versions the caller read it with; <see cref="OverrideVersion"/> is null when no override row existed.</summary>
public sealed record QueueRowRef(long RequestId, long ItemId, int StageNo, long ExpectedStageVersion, long? ExpectedOverrideVersion = null);

/// <summary>A production unit as shown in queue choosers.</summary>
public sealed record UnitInfo(long UnitId, string Code, string Name, bool IsActive);

/// <summary>The queue of one production unit as read at one moment. <see cref="Queue"/> is in the operational order; <see cref="Pending"/> lists stages that are not released or are waiting for QC, each with its reason.</summary>
public sealed record UnitQueue(UnitInfo Unit, IReadOnlyList<UnitQueueEntry> Queue, IReadOnlyList<UnitQueueEntry> Pending, int CompletedStageCount, int OverridesNeedingReview);

/// <summary>What the signed-in user may do on the unit queue now (from the database). A convenience for enabling controls; every command checks again.</summary>
public sealed record UnitQueueCapabilities(
    bool CanView, bool CanOrder, bool CanChangePriority, bool CanOverride, bool CanStopOrResume, bool CanRun, bool CanRelease, bool CanApproveQc, bool CanManageDesignations)
{
    public static UnitQueueCapabilities None { get; } = new(false, false, false, false, false, false, false, false, false);
}

/// <summary>The units the user can see and what they may do. A Production user sees only the units they are designated to.</summary>
public sealed record UnitQueueOverview(IReadOnlyList<UnitInfo> Units, UnitQueueCapabilities Capabilities);

/// <summary>The pure ordering of a unit queue (Decision Log #78, #99). Deterministic: every key is total and the last keys are the stable identifiers.</summary>
public static class UnitQueueOrdering
{
    /// <summary>
    /// Order: (1) operational category IN_PROGRESS, STOPPED, WAITING; (2) an active Management override first, by rank; (3) Planning priority (خیلی فوری, فوری, عادی); (4) Planning position
    /// (entries without one last); (5) first come, first served by when the stage became available (stages without that timestamp use their request date); (6) planned end (none yet);
    /// (7) request date, request id, item id, stage number.
    /// </summary>
    public static IReadOnlyList<UnitQueueEntry> Sort(IEnumerable<UnitQueueEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries
            .OrderBy(e => (int)(e.Category ?? QueueCategory.Waiting))
            .ThenBy(e => e.OverrideRank is null ? 1 : 0)
            .ThenBy(e => e.OverrideRank ?? int.MaxValue)
            .ThenBy(e => e.Priority.SortOrder())
            .ThenBy(e => e.PlanningPosition ?? int.MaxValue)
            .ThenBy(e => e.AvailableSinceUtc ?? e.RequestDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
            .ThenBy(e => e.PlannedEndUtc ?? DateTime.MaxValue)
            .ThenBy(e => e.RequestDate)
            .ThenBy(e => e.RequestId)
            .ThenBy(e => e.ItemId)
            .ThenBy(e => e.StageNo)
            .Select((e, i) => e with { Order = i + 1 })
            .ToList();
    }
}
