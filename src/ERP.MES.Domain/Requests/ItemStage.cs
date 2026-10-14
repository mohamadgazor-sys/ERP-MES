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
    internal ItemStage(int stageNo, string stageTitle, decimal plannedMinutesPerPiece, decimal plannedQuantity, ItemStageStatus status)
    {
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
}

/// <summary>
/// A recorded QC approval of a stage (append-only history, section 2.5). It carries the approved quantity so the
/// production model can later record accepted, rework and scrap quantities (section 9.3). In the demonstration the
/// approved quantity is always the stage's whole planned quantity; that is demo-only, not a production rule.
/// </summary>
public sealed record QcApproval(int StageNo, decimal ApprovedQuantity, long ApprovedByUserId, DateTime ApprovedAtUtc);
