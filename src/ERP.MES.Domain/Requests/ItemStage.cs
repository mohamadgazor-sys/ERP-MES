namespace ERP.MES.Domain.Requests;

/// <summary>
/// The subset of the stage status list (Domain and Database Specification section 7) that the current model uses.
/// The full list is not final ("to be finalized before schema freeze"), so values are added with the work that needs
/// them; rejection, rework and external-supply states are deliberately not present.
/// </summary>
public enum ItemStageStatus
{
    /// <summary>Created, not yet released by Planning (first stage only).</summary>
    Planned = 1,

    /// <summary>Not executable until QC approves the immediately preceding stage (section 9.2).</summary>
    WaitingPreviousQc = 2,

    /// <summary>Released and executable, not started.</summary>
    Waiting = 3,

    InProgress = 4,

    WaitingQc = 5,

    QcApproved = 6,
}

public static class ItemStageStatusInfo
{
    public static string Code(this ItemStageStatus status) => status switch
    {
        ItemStageStatus.Planned => "PLANNED",
        ItemStageStatus.WaitingPreviousQc => "WAITING_PREVIOUS_QC",
        ItemStageStatus.Waiting => "WAITING",
        ItemStageStatus.InProgress => "IN_PROGRESS",
        ItemStageStatus.WaitingQc => "WAITING_QC",
        ItemStageStatus.QcApproved => "QC_APPROVED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown stage status."),
    };
}

/// <summary>
/// One stage of an item's route (section 6.1). Stages are item-specific and ordered by <see cref="StageNo"/>.
/// Only the planning inputs and the status are modelled here; status changes go through <see cref="RequestItem"/>,
/// which owns the route and the QC gate.
/// </summary>
public sealed class ItemStage
{
    internal ItemStage(int stageNo, string stageTitle, decimal plannedMinutesPerPiece, decimal plannedQuantity, ItemStageStatus status, long? productionUnitId = null, long? machineId = null)
    {
        ProductionUnitId = productionUnitId;
        MachineId = machineId;
        StageNo = stageNo;
        StageTitle = stageTitle;
        PlannedMinutesPerPiece = plannedMinutesPerPiece;
        PlannedQuantity = plannedQuantity;
        Status = status;
    }

    public int StageNo { get; }

    public string StageTitle { get; }

    /// <summary>Planning input: machining minutes per piece.</summary>
    public decimal PlannedMinutesPerPiece { get; }

    /// <summary>Quantity used for planning.</summary>
    public decimal PlannedQuantity { get; }

    /// <summary>Planned Total Processing Minutes = planned minutes per piece x planned quantity (section 19). No rounding is applied; the scale is undecided.</summary>
    public decimal PlannedTotalMinutes => PlannedMinutesPerPiece * PlannedQuantity;

    public ItemStageStatus Status { get; internal set; }

    /// <summary>The production unit Planning planned the stage in (Decision Log #89); null for a stage of the simplified demonstration.</summary>
    public long? ProductionUnitId { get; }

    /// <summary>The machine Planning planned the stage on; it belongs to <see cref="ProductionUnitId"/>. Null when only the unit is planned.</summary>
    public long? MachineId { get; }

    /// <summary>Planning's explicit position of this released stage within its unit's queue (Decision Log #78, #99); null = no position given. Positions may repeat; equal positions are ordered first come, first served.</summary>
    public int? PlanningPosition { get; internal set; }

    /// <summary>When the stage became available to run (released by Planning, or the previous stage was approved by QC): the "first come" timestamp of the queue tie-break. Null for stages that never became available or were created before this was recorded.</summary>
    public DateTime? AvailableSinceUtc { get; internal set; }

    /// <summary>When the stage was stopped (a problem); null while it is not stopped. A stopped stage keeps its status and cannot be started, submitted or approved until it is resumed.</summary>
    public DateTime? StoppedAtUtc { get; internal set; }

    /// <summary>Why the stage was stopped (required); null while it is not stopped.</summary>
    public string? StopReason { get; internal set; }

    /// <summary>The user who stopped the stage; null while it is not stopped.</summary>
    public long? StoppedByUserId { get; internal set; }

    public bool IsStopped => StoppedAtUtc is not null;
}

/// <summary>
/// DEMONSTRATION-ONLY record of a QC approval of a stage (append-only history, section 2.5). It holds a single approved
/// quantity, which the demonstration always sets to the stage's whole planned quantity. It does NOT represent the
/// separate accepted, rework and scrap quantities that section 9.3 requires; modelling those dispositions (and the
/// QC rejection and rework flow) is future production work and will need a richer QC inspection record.
/// </summary>
public sealed record QcApproval(int StageNo, decimal ApprovedQuantity, long ApprovedByUserId, DateTime ApprovedAtUtc);
