namespace ERP.MES.Domain.Requests;

/// <summary>
/// One distinguishable item of a request (section 5): item number, part name, requested
/// quantity, priority and its ordered stages.
///
/// The route operations enforce the decided QC gate (section 9.2): stage N + 1 cannot become executable until QC has
/// approved stage N. The planning priority is stored here and inherited by every stage and queue entry of the item
/// (Decision Log #58); stages have no priority of their own.
///
/// DEMONSTRATION-ONLY (Decision Log #58): <see cref="ReleaseFirstStage"/>, <see cref="StartStage"/>,
/// <see cref="SubmitStageForQc"/> and <see cref="ApproveStageWholeQuantity"/> are the minimal provisional path
/// PLANNED -> WAITING -> IN_PROGRESS -> WAITING_QC -> QC_APPROVED. They do not approve the production stage state
/// machine. QC rejection, rework, scrap and partial quantities (section 9.3, a production requirement), external-supply
/// stages and the audited controlled route revision are not modelled here and must remain possible to add.
/// </summary>
public sealed class RequestItem
{
    private readonly List<ItemStage> _stages = [];
    private readonly List<QcApproval> _qcApprovals = [];

    internal RequestItem(
        long id,
        string itemNo,
        string partName,
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
    /// INITIAL ROUTE CONSTRUCTION: appends an internal stage while the item's route is first being defined (seeded
    /// routes, Planning's initial route). Allowed only while no stage has been released. This is deliberately NOT route
    /// revision and must not be used as one: the approved controlled revision of a route after production has started
    /// (section 6.2) needs an authorized Planning actor, a reason, a timestamp, the before/after sequence and an audit
    /// record, plus rules for downstream stages that are still undecided. That operation does not exist yet; it is
    /// outside the demonstration (fixed seeded routes, no route editor) and is neither implemented nor prohibited by
    /// this method. Do not add a post-release change by relaxing the check below.
    /// </summary>
    public ItemStage DefineInitialStage(string stageTitle, decimal plannedMinutesPerPiece, decimal? plannedQuantity = null)
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
            throw new InvalidOperationException(
                "The initial route can only be defined before the first stage is released. Changing a route after work has started needs the controlled, audited route revision, which is not implemented yet.");
        }

        var status = _stages.Count == 0 ? ItemStageStatus.Planned : ItemStageStatus.WaitingPreviousQc;
        var stage = new ItemStage(_stages.Count + 1, stageTitle, plannedMinutesPerPiece, quantity, status);
        _stages.Add(stage);
        return stage;
    }

    /// <summary>Planning releases the first (internal) stage, which makes it executable (section 9.2). Demonstration-only transition PLANNED -> WAITING.</summary>
    public void ReleaseFirstStage()
    {
        var first = _stages.FirstOrDefault() ?? throw new InvalidOperationException("The item has no stages.");
        Require(first, ItemStageStatus.Planned, "released");
        first.Status = ItemStageStatus.Waiting;
    }

    /// <summary>Demonstration-only transition WAITING -> IN_PROGRESS.</summary>
    public void StartStage(int stageNo)
    {
        var stage = GetStage(stageNo);
        Require(stage, ItemStageStatus.Waiting, "started");
        RequirePreviousApproved(stage);
        stage.Status = ItemStageStatus.InProgress;
    }

    /// <summary>Demonstration-only transition IN_PROGRESS -> WAITING_QC.</summary>
    public void SubmitStageForQc(int stageNo)
    {
        var stage = GetStage(stageNo);
        Require(stage, ItemStageStatus.InProgress, "submitted for QC");
        stage.Status = ItemStageStatus.WaitingQc;
    }

    /// <summary>
    /// DEMONSTRATION-ONLY: QC approves a stage for its whole planned quantity and the next stage becomes executable.
    /// This is not the production rule: the approved requirements (section 9.3) split a QC result into accepted,
    /// rework and scrap quantities, which this method and <see cref="QcApproval"/> do not represent (future work). The caller (the application layer) must check
    /// that the actor belongs to QC.
    /// </summary>
    public QcApproval ApproveStageWholeQuantity(int stageNo, long approvedByUserId, DateTime approvedAtUtc)
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
