namespace ERP.MES.Domain.Requests;

/// <summary>
/// One distinguishable item of a request (section 5): item number, part name, work type (MAKE or REPAIR), requested
/// quantity, priority and its ordered stages.
///
/// The route operations (<see cref="ReleaseFirstStage"/>, <see cref="StartStage"/>, <see cref="SubmitStageForQc"/>,
/// <see cref="ApproveStage"/>) enforce the decided QC gate (section 9.2): stage N + 1 cannot become executable until QC has
/// approved stage N. The in-between transitions (release, start, submit for QC) are provisional: the specification
/// names the states but does not define these transitions, so they are the minimum needed to walk an item through
/// its stages and will be refined with the work-session and QC milestones. QC rejection, rework, scrap, partial
/// quantities, external-supply stages and controlled route revision are not modelled.
/// </summary>
public sealed class RequestItem
{
    private readonly List<ItemStage> _stages = [];
    private readonly List<QcApproval> _qcApprovals = [];

    internal RequestItem(
        long id,
        string itemNo,
        string partName,
        WorkType workType,
        decimal requestedQuantity,
        Priority priority,
        long createdByUserId,
        DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(itemNo))
        {
            throw new ArgumentException("An item number is required.", nameof(itemNo));
        }

        if (string.IsNullOrWhiteSpace(partName))
        {
            throw new ArgumentException("A part name is required.", nameof(partName));
        }

        if (!Enum.IsDefined(workType))
        {
            throw new ArgumentOutOfRangeException(nameof(workType), workType, "Unknown work type.");
        }

        if (requestedQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedQuantity), requestedQuantity, "The requested quantity must be greater than zero.");
        }

        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown priority.");
        }

        Id = id;
        ItemNo = itemNo;
        PartName = partName;
        WorkType = workType;
        RequestedQuantity = requestedQuantity;
        Priority = priority;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Customer.RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
    }

    public long Id { get; }

    /// <summary>Customer/request item identifier, short text such as "1.10" or "A-01". Stored and compared exactly as given.</summary>
    public string ItemNo { get; }

    public string PartName { get; }

    public WorkType WorkType { get; }

    public decimal RequestedQuantity { get; }

    /// <summary>Planning-controlled priority. Its storage location (item or stage) is still pending confirmation (section 18); the item field of section 5.1 is used and stages carry none.</summary>
    public Priority Priority { get; private set; }

    public long CreatedByUserId { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyList<ItemStage> Stages => _stages;

    public IReadOnlyList<QcApproval> QcApprovals => _qcApprovals;

    /// <summary>True when every stage of a non-empty route has been approved by QC.</summary>
    public bool IsRouteCompleted => _stages.Count > 0 && _stages.All(s => s.Status == ItemStageStatus.QcApproved);

    /// <summary>Changes the Planning priority. Several items may share a priority. Returns false when nothing changed.</summary>
    public bool ChangePriority(Priority priority, DateTime changedAtUtc)
    {
        if (!Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown priority.");
        }

        var utc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
        if (priority == Priority)
        {
            return false;
        }

        Priority = priority;
        UpdatedAtUtc = utc;
        return true;
    }

    /// <summary>
    /// Appends an internal stage to the route. Allowed only while no stage has been released, because the handling of a
    /// route change after production has started (controlled route revision) is still an open decision.
    /// </summary>
    public ItemStage AddStage(string stageTitle, decimal plannedMinutesPerPiece, decimal? plannedQuantity = null)
    {
        if (string.IsNullOrWhiteSpace(stageTitle))
        {
            throw new ArgumentException("A stage title is required.", nameof(stageTitle));
        }

        if (plannedMinutesPerPiece <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plannedMinutesPerPiece), plannedMinutesPerPiece, "Planned minutes per piece must be greater than zero.");
        }

        var quantity = plannedQuantity ?? RequestedQuantity;
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plannedQuantity), quantity, "The planned quantity must be greater than zero.");
        }

        if (_stages.Any(s => s.Status is not (ItemStageStatus.Planned or ItemStageStatus.WaitingPreviousQc)))
        {
            throw new InvalidOperationException("The route cannot be extended after a stage has been released: controlled route revision is not implemented.");
        }

        var status = _stages.Count == 0 ? ItemStageStatus.Planned : ItemStageStatus.WaitingPreviousQc;
        var stage = new ItemStage(_stages.Count + 1, stageTitle, plannedMinutesPerPiece, quantity, status);
        _stages.Add(stage);
        return stage;
    }

    /// <summary>Planning releases the first (internal) stage, which makes it executable (section 9.2).</summary>
    public void ReleaseFirstStage()
    {
        var first = _stages.FirstOrDefault() ?? throw new InvalidOperationException("The item has no stages.");
        Require(first, ItemStageStatus.Planned, "released");
        first.Status = ItemStageStatus.Waiting;
    }

    public void StartStage(int stageNo)
    {
        var stage = GetStage(stageNo);
        Require(stage, ItemStageStatus.Waiting, "started");
        RequirePreviousApproved(stage);
        stage.Status = ItemStageStatus.InProgress;
    }

    public void SubmitStageForQc(int stageNo)
    {
        var stage = GetStage(stageNo);
        Require(stage, ItemStageStatus.InProgress, "submitted for QC");
        stage.Status = ItemStageStatus.WaitingQc;
    }

    /// <summary>
    /// QC approves a stage for its whole planned quantity and the next stage becomes executable. The caller (the
    /// application layer) is responsible for checking that the actor belongs to QC.
    /// </summary>
    public QcApproval ApproveStage(int stageNo, long approvedByUserId, DateTime approvedAtUtc)
    {
        var stage = GetStage(stageNo);
        var utc = Customer.RequireUtc(approvedAtUtc, nameof(approvedAtUtc));
        Require(stage, ItemStageStatus.WaitingQc, "approved by QC");
        RequirePreviousApproved(stage);

        stage.Status = ItemStageStatus.QcApproved;
        var approval = new QcApproval(stage.StageNo, stage.PlannedQuantity, approvedByUserId, utc);
        _qcApprovals.Add(approval);
        UpdatedAtUtc = utc;

        var next = _stages.FirstOrDefault(s => s.StageNo == stage.StageNo + 1);
        if (next is { Status: ItemStageStatus.WaitingPreviousQc })
        {
            next.Status = ItemStageStatus.Waiting;
        }

        return approval;
    }

    private ItemStage GetStage(int stageNo) =>
        _stages.FirstOrDefault(s => s.StageNo == stageNo)
        ?? throw new ArgumentOutOfRangeException(nameof(stageNo), stageNo, "The item has no such stage.");

    private static void Require(ItemStage stage, ItemStageStatus expected, string action)
    {
        if (stage.Status != expected)
        {
            throw new InvalidOperationException(
                "Stage " + stage.StageNo + " cannot be " + action + " while it is " + stage.Status.Code() + "; it must be " + expected.Code() + ".");
        }
    }

    // Defence in depth for the QC gate: even if a status were set wrongly, a stage after the first never proceeds
    // unless the immediately preceding stage is QC approved.
    private void RequirePreviousApproved(ItemStage stage)
    {
        if (stage.StageNo > 1 && _stages[stage.StageNo - 2].Status != ItemStageStatus.QcApproved)
        {
            throw new InvalidOperationException("Stage " + stage.StageNo + " cannot proceed before QC has approved stage " + (stage.StageNo - 1) + ".");
        }
    }
}
