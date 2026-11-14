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

    // For the persistence layer: the specification is stored as plain columns, so EF Core fills the properties itself.
    private RequestItem()
    {
        ItemNo = string.Empty;
        PartName = string.Empty;
    }

    internal RequestItem(
        long id,
        string itemNo,
        int itemSequence,
        RequestItem? parent,
        string? customerItemRef,
        bool isAssembly,
        string partName,
        decimal requestedQuantity,
        Priority priority,
        long createdByUserId,
        DateTime createdAtUtc,
        ItemSpecification? specification = null)
    {
        if (string.IsNullOrWhiteSpace(itemNo))
        {
            throw new ArgumentException("An item number is required.", nameof(itemNo));
        }

        if (itemSequence is < 1 or > Request.MaxItemSequence)
        {
            throw new ArgumentOutOfRangeException(nameof(itemSequence), itemSequence, "The item sequence must be 1 to " + Request.MaxItemSequence + ".");
        }

        if (customerItemRef is not null && (customerItemRef.Length == 0 || customerItemRef.Length > MaxCustomerItemRefLength || customerItemRef.Trim() != customerItemRef))
        {
            throw new ArgumentException("A customer item reference is 1 to " + MaxCustomerItemRefLength + " characters without leading or trailing white space.", nameof(customerItemRef));
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

        var spec = specification ?? ItemSpecification.None;
        QuantityTypeId = spec.QuantityTypeId;
        BaseUnitsPerUnit = Math.Max(1, spec.BaseUnitsPerUnit);
        MaterialId = spec.MaterialId;
        WeightKgPerBaseUnit = spec.WeightKgPerBaseUnit;
        LengthMm = spec.LengthMm;
        DiameterMm = spec.DiameterMm;
        DeliveryDate = spec.DeliveryDate;
        WarehouseItemCode = spec.WarehouseItemCode;
        Id = id;
        ItemNo = itemNo;
        ItemSequence = itemSequence;
        Parent = parent;
        CustomerItemRef = customerItemRef;
        IsAssembly = isAssembly;
        PartName = partName;
        RequestedQuantity = requestedQuantity;
        Priority = priority;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Customer.RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
    }

    /// <summary>The longest customer item reference.</summary>
    public const int MaxCustomerItemRefLength = 40;

    public long Id { get; }

    /// <summary>
    /// The system item number shown everywhere (Decision Log #121): <c>RequestNo-001</c> for an item without a parent and <c>ParentNo.001</c> for a child. It is DERIVED from the request number, the
    /// parent's number and <see cref="ItemSequence"/>, and kept in step by the request when the item is defined or moved; it is never parsed to recover the sequence. Unique within the request.
    /// </summary>
    public string ItemNo { get; private set; }

    /// <summary>The 1 to 999 position of the item among its siblings (the items without a parent, or the children of one parent). Never reused (Decision Log #121 Q3).</summary>
    public int ItemSequence { get; private set; }

    /// <summary>The assembly this item belongs to, or null. Resolved by the persistence layer to the same request's item.</summary>
    public RequestItem? Parent { get; private set; }

    /// <summary>The database id of <see cref="Parent"/> (for queries); null for an item without a parent.</summary>
    public long? ParentItemId { get; private set; }

    /// <summary>True for an assembly: an item that may have children. It must be marked before a child is attached, and cannot be turned back while children remain (Decision Log #121 Q4, Q6).</summary>
    public bool IsAssembly { get; private set; }

    /// <summary>The customer's own item number (optional, exact text): the only key used to match an existing item on a re-import (Decision Log #121 Q2).</summary>
    public string? CustomerItemRef { get; private set; }

    /// <summary>The sequence the next child of this assembly receives; only ever grows, so numbers are never reused (Decision Log #121 Q3).</summary>
    public int NextChildSequence { get; internal set; } = 1;

    /// <summary>True once a stage has been defined: the hierarchy around the item is then locked (Decision Log #121 Q6).</summary>
    public bool HasStages => _stages.Count > 0;

    internal void ApplyNumber(string itemNo)
    {
        ItemNo = itemNo;
    }

    internal void MarkAssembly() => IsAssembly = true;

    internal void UnmarkAssembly() => IsAssembly = false;

    internal void Attach(RequestItem? parent, int sequence)
    {
        Parent = parent;
        ParentItemId = parent?.Id is > 0 ? parent.Id : null;
        ItemSequence = sequence;
    }

    public string PartName { get; private set; }

    public decimal RequestedQuantity { get; private set; }

    /// <summary>The QC-owned quantity type the quantity is expressed in (Decision Log #90); null for an item without a specification (REPAIR where not applicable, or an item of the simplified demonstration).</summary>
    public long? QuantityTypeId { get; private set; }

    /// <summary>How many base units one ordered unit counts (piece 1, pair 2; Decision Log #90, #101).</summary>
    public int BaseUnitsPerUnit { get; private set; } = 1;

    /// <summary>The Warehouse material the item is made of.</summary>
    public long? MaterialId { get; private set; }

    /// <summary>Kilograms of raw material per base unit (decimal 12,4).</summary>
    public decimal? WeightKgPerBaseUnit { get; private set; }

    /// <summary>Millimetres (decimal 10,2).</summary>
    public decimal? LengthMm { get; private set; }

    /// <summary>Millimetres (decimal 10,2).</summary>
    public decimal? DiameterMm { get; private set; }

    /// <summary>The date the repaired item is to be delivered (REPAIR items; on or after the request date; Decision Log #124 B5).</summary>
    public DateOnly? DeliveryDate { get; private set; }

    /// <summary>The warehouse's own code of the item (REPAIR items; at most 40 characters). A duplicate is a warning, not an error (Decision Log #124 B5).</summary>
    public string? WarehouseItemCode { get; private set; }

    /// <summary>True when a REPAIR item still lacks its delivery date or warehouse code: it stays visible but cannot be released until completed (Decision Log #124 B5).</summary>
    public bool IsRepairIncomplete(WorkType workType) => workType == WorkType.Repair && (DeliveryDate is null || WarehouseItemCode is null);

    /// <summary>Planning-controlled priority. Its storage location (item or stage) is still pending confirmation (section 18); the item field of section 5.1 is used and stages carry none.</summary>
    public Priority Priority { get; private set; }

    public long CreatedByUserId { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyList<ItemStage> Stages => _stages;

    public IReadOnlyList<QcApproval> QcApprovals => _qcApprovals;

    /// <summary>True when every stage of a non-empty route has been approved by QC.</summary>
    public bool IsRouteCompleted => _stages.Count > 0 && _stages.All(s => s.Status == ItemStageStatus.QcApproved);

    /// <summary>True while the definition may still be revised: the item has no stages and no QC approval (a defined route is locked; its controlled revision is not built).</summary>
    public bool IsDefinitionRevisable => _stages.Count == 0 && _qcApprovals.Count == 0;

    /// <summary>
    /// Revises the item definition (Excel import "update" choice, Decision Log #16, #97): part name, quantity, priority and specification. Allowed only while
    /// <see cref="IsDefinitionRevisable"/>. The caller validated the specification for the request's work type; the item number never changes.
    /// </summary>
    internal bool ReviseDefinition(string partName, decimal requestedQuantity, Priority priority, ItemSpecification specification, DateTime changedAtUtc)
    {
        if (!IsDefinitionRevisable)
        {
            throw new InvalidOperationException("An item with a defined route or a QC approval cannot be revised by import; the controlled revision is not implemented.");
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

        var utc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
        var changed = partName != PartName || requestedQuantity != RequestedQuantity || priority != Priority || specification.QuantityTypeId != QuantityTypeId
            || specification.MaterialId != MaterialId || specification.WeightKgPerBaseUnit != WeightKgPerBaseUnit || specification.LengthMm != LengthMm || specification.DiameterMm != DiameterMm
            || specification.DeliveryDate != DeliveryDate || specification.WarehouseItemCode != WarehouseItemCode;
        PartName = partName;
        RequestedQuantity = requestedQuantity;
        Priority = priority;
        QuantityTypeId = specification.QuantityTypeId;
        BaseUnitsPerUnit = Math.Max(1, specification.BaseUnitsPerUnit);
        MaterialId = specification.MaterialId;
        WeightKgPerBaseUnit = specification.WeightKgPerBaseUnit;
        LengthMm = specification.LengthMm;
        DiameterMm = specification.DiameterMm;
        DeliveryDate = specification.DeliveryDate;
        WarehouseItemCode = specification.WarehouseItemCode;
        if (changed)
        {
            UpdatedAtUtc = utc;
        }

        return changed;
    }

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
    /// EXTERNAL STAGE (Decision Log #129): <paramref name="externalLeadTimeDays"/> is given exactly when the unit is an external route; the stage then has no machine, its minutes per piece may be
    /// zero (the contractor's time is the lead time) and its quantity must be a whole number of base units, because dispatches and returns are counted in base units.
    /// </summary>
    public ItemStage DefineInitialStage(
        string stageTitle, decimal plannedMinutesPerPiece, decimal? plannedQuantity = null, long? productionUnitId = null, long? machineId = null, int? externalLeadTimeDays = null)
    {
        if (string.IsNullOrWhiteSpace(stageTitle))
        {
            throw new ArgumentException("A stage title is required.", nameof(stageTitle));
        }

        var external = externalLeadTimeDays is not null;
        if (external ? plannedMinutesPerPiece < 0 : plannedMinutesPerPiece <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plannedMinutesPerPiece), plannedMinutesPerPiece, "Planned minutes per piece must be greater than zero (zero or more for an external stage).");
        }

        if (external && (externalLeadTimeDays is < MinExternalLeadTimeDays or > MaxExternalLeadTimeDays || machineId is not null || productionUnitId is null))
        {
            throw new ArgumentException("An external stage needs its external unit, no machine and a lead time of " + MinExternalLeadTimeDays + " to " + MaxExternalLeadTimeDays + " calendar days.", nameof(externalLeadTimeDays));
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
        var stage = new ItemStage(_stages.Count + 1, stageTitle, plannedMinutesPerPiece, quantity, status, productionUnitId, machineId, externalLeadTimeDays);
        if (external && PlannedBaseUnits(stage) is null)
        {
            throw new ArgumentException("An external stage needs a quantity that is a whole number of base units.", nameof(plannedQuantity));
        }

        _stages.Add(stage);
        return stage;
    }

    /// <summary>The shortest expected external lead time, in calendar days (Decision Log #129).</summary>
    public const int MinExternalLeadTimeDays = 1;

    /// <summary>The longest expected external lead time, in calendar days (a labelled default of Decision Log #132).</summary>
    public const int MaxExternalLeadTimeDays = 365;

    // The status a stage enters when it becomes executable: an external stage waits for the Warehouse to send it (Decision Log #129 Q4), an internal one enters its unit's queue.
    private static ItemStageStatus Executable(ItemStage stage) => stage.IsExternal ? ItemStageStatus.WaitingExternalSend : ItemStageStatus.Waiting;

    /// <summary>
    /// Planning releases the first stage, which makes it executable (section 9.2): PLANNED -> WAITING for an internal stage; PLANNED -> WAITING_EXTERNAL_SEND for an external one, which is NOT
    /// sent until the Warehouse confirms a dispatch (Decision Log #129 Q4).
    /// </summary>
    public void ReleaseFirstStage(DateTime? releasedAtUtc = null)
    {
        var first = _stages.FirstOrDefault() ?? throw new InvalidOperationException("The item has no stages.");
        Require(first, ItemStageStatus.Planned, "released");
        first.Status = Executable(first);
        first.AvailableSinceUtc = releasedAtUtc is { } at ? Customer.RequireUtc(at, nameof(releasedAtUtc)) : null;
        first.InputBaseUnits = PlannedBaseUnits(first);
    }

    /// <summary>Demonstration-only transition WAITING -> IN_PROGRESS.</summary>
    public void StartStage(int stageNo)
    {
        var stage = GetStage(stageNo);
        Require(stage, ItemStageStatus.Waiting, "started");
        RequirePreviousApproved(stage);
        RequireNotStopped(stage, "started");
        stage.Status = ItemStageStatus.InProgress;
    }

    /// <summary>Demonstration-only transition IN_PROGRESS -> WAITING_QC.</summary>
    public void SubmitStageForQc(int stageNo)
    {
        var stage = GetStage(stageNo);
        Require(stage, ItemStageStatus.InProgress, "submitted for QC");
        RequireNotStopped(stage, "submitted for QC");
        if (stage.InputBaseUnits is { } input)
        {
            // Everything still to be produced or reworked goes to QC; the reworked units come back as a new submission and are counted once (Decision Log #92, #101).
            if (input - stage.AcceptedBaseUnits - stage.ScrapBaseUnits - stage.AwaitingQcBaseUnits <= 0)
            {
                throw new InvalidOperationException("Stage " + stageNo + " has no units left to submit to QC.");
            }

            stage.AwaitingQcBaseUnits = input - stage.AcceptedBaseUnits - stage.ScrapBaseUnits;
            stage.ReworkBaseUnits = 0;
        }

        stage.Status = ItemStageStatus.WaitingQc;
        stage.PlanningPosition = null;
    }

    /// <summary>
    /// DEMONSTRATION-ONLY (stages without a production unit): QC approves a stage for its whole planned quantity and the next stage becomes executable. The production rule is
    /// <see cref="RecordInspection"/> (accepted / rework / scrap per base unit, Decision Log #101). The caller (the application layer) must check that the actor belongs to QC.
    /// </summary>
    public QcApproval ApproveStageWholeQuantity(int stageNo, long approvedByUserId, DateTime approvedAtUtc)
    {
        var stage = GetStage(stageNo);
        var utc = Customer.RequireUtc(approvedAtUtc, nameof(approvedAtUtc));
        Require(stage, ItemStageStatus.WaitingQc, "approved by QC");
        RequirePreviousApproved(stage);
        RequireNotStopped(stage, "approved by QC");
        if (stage.InputBaseUnits is not null)
        {
            if (stage.ScrapBaseUnits > 0 || stage.ReworkBaseUnits > 0)
            {
                throw new InvalidOperationException("A stage with scrap or rework cannot be approved as a whole; record the inspection.");
            }

            stage.AcceptedBaseUnits += stage.AwaitingQcBaseUnits;
            stage.AwaitingQcBaseUnits = 0;
        }

        return CompleteStage(stage, stage.PlannedQuantity, approvedByUserId, utc);
    }

    /// <summary>
    /// QC decides base units of a submitted stage (Decision Log #92, #95 D2, #101): accepted, rework or scrap, a reason for rework and scrap, never more than what is awaiting QC (so nothing is
    /// counted twice and nothing is overproduced). When nothing is awaiting any more: rework returns the stage to IN_PROGRESS for the reworked units; scrap with no rework waits for the shortfall
    /// decision; otherwise the stage is QC approved and the next stage receives the accepted base units. The caller checks that the actor belongs to QC.
    /// </summary>
    public InspectionOutcome RecordInspection(int stageNo, IReadOnlyList<InspectionEntry> entries, long decidedByUserId, DateTime decidedAtUtc, string inspectionId)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var stage = GetStage(stageNo);
        var utc = Customer.RequireUtc(decidedAtUtc, nameof(decidedAtUtc));
        Require(stage, ItemStageStatus.WaitingQc, "inspected");
        RequirePreviousApproved(stage);
        RequireNotStopped(stage, "inspected");
        if (stage.InputBaseUnits is null)
        {
            throw new StageNotCountableException("Stage " + stageNo + " has no whole number of base units; only the whole-quantity approval of the demonstration applies.");
        }

        if (entries.Count == 0)
        {
            throw new ArgumentException("At least one decision is required.", nameof(entries));
        }

        long total = 0;
        foreach (var entry in entries)
        {
            if (!Enum.IsDefined(entry.Disposition) || entry.BaseUnits <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entries), "Each decision needs a known disposition and a positive number of base units.");
            }

            var reason = entry.Reason?.Trim();
            if (entry.Disposition != InspectionDisposition.Accepted && (string.IsNullOrEmpty(reason) || reason.Length > QcInspectionLine.MaxReasonLength))
            {
                throw new ArgumentException("A reason of 1 to " + QcInspectionLine.MaxReasonLength + " characters is required for rework and scrap.", nameof(entries));
            }

            total += entry.BaseUnits;
        }

        if (total > stage.AwaitingQcBaseUnits)
        {
            throw new InspectionExceedsAwaitingException("QC cannot decide " + total + " base units: only " + stage.AwaitingQcBaseUnits + " are awaiting QC at stage " + stageNo + ".");
        }

        var lines = new List<QcInspectionLine>();
        foreach (var entry in entries)
        {
            switch (entry.Disposition)
            {
                case InspectionDisposition.Accepted:
                    stage.AcceptedBaseUnits += entry.BaseUnits;
                    break;
                case InspectionDisposition.Rework:
                    stage.ReworkBaseUnits += entry.BaseUnits;
                    break;
                default:
                    stage.ScrapBaseUnits += entry.BaseUnits;
                    break;
            }

            lines.Add(new QcInspectionLine(
                Id, stageNo, inspectionId, entry.Disposition, entry.BaseUnits, entry.Disposition == InspectionDisposition.Accepted ? null : entry.Reason!.Trim(), decidedByUserId, utc));
        }

        stage.AwaitingQcBaseUnits -= total;
        UpdatedAtUtc = utc;
        var completed = false;
        if (stage.IsExternal)
        {
            // An external stage is complete only when nothing is left to send, at the contractor or awaiting QC (Decision Log #129 (6)); rework units must be sent again.
            completed = SettleExternal(stage, decidedByUserId, utc);
        }
        else if (stage.AwaitingQcBaseUnits == 0)
        {
            if (stage.ReworkBaseUnits > 0)
            {
                stage.Status = ItemStageStatus.InProgress;
            }
            else if (stage.ScrapBaseUnits > 0)
            {
                stage.Status = ItemStageStatus.WaitingShortfallDecision;
            }
            else
            {
                CompleteStage(stage, (decimal)stage.AcceptedBaseUnits / BaseUnitsPerUnit, decidedByUserId, utc);
                completed = true;
            }
        }

        return new InspectionOutcome(lines, stage.Status, completed);
    }

    /// <summary>True when a QC correction affected any stage of the item (the visible "corrected" indicator of Master Specification 15.1).</summary>
    public bool HasQcCorrection => _stages.Any(s => s.AffectedByCorrection is not null || s.AffectedByQcCorrectionId is not null);

    /// <summary>
    /// QC corrects the final decision of a QC_APPROVED stage (Master Specification 1.10, 15; owner decisions Q1-Q3, Q6, Decision Log #142). QC names how many of the previously accepted units become
    /// rework or scrap; the accepted quantity falls by that amount and the rest stays accepted. Rework units return to the stage as executable work (IN_PROGRESS, or WAITING_EXTERNAL_SEND for an external
    /// stage) and go through production and QC again; scrap alone waits for the existing shortfall decision. Nothing is deleted or reset: the old QC lines, the approval, the work sessions and the stages
    /// downstream stay as they are, and the downstream stages that had progressed are marked as affected. The sequential gate then keeps every later stage from proceeding until this stage is QC approved
    /// again. <paramref name="sources"/> are the earlier accepted decisions superseded (their units must add up to the corrected quantity).
    /// </summary>
    public QcCorrection CorrectQcDecision(
        int stageNo, long reworkBaseUnits, long scrapBaseUnits, string? reason, IReadOnlyList<QcCorrectionSource> sources, bool afterCustomerDispatch, long userId, DateTime atUtc)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var stage = GetStage(stageNo);
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        Require(stage, ItemStageStatus.QcApproved, "corrected");
        if (stage.InputBaseUnits is null)
        {
            throw new StageNotCountableException("Stage " + stageNo + " has no whole number of base units, so a QC decision about quantities cannot be corrected.");
        }

        if (reworkBaseUnits < 0 || scrapBaseUnits < 0 || reworkBaseUnits + scrapBaseUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(reworkBaseUnits), "A correction names a positive number of units as rework or scrap.");
        }

        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > QcCorrection.MaxReasonLength)
        {
            throw new ArgumentException("A reason of 1 to " + QcCorrection.MaxReasonLength + " characters is required.", nameof(reason));
        }

        var corrected = reworkBaseUnits + scrapBaseUnits;
        if (corrected > stage.AcceptedBaseUnits)
        {
            throw new CorrectionExceedsAcceptedException("QC cannot correct " + corrected + " base units: only " + stage.AcceptedBaseUnits + " are accepted and still valid at stage " + stageNo + ".");
        }

        if (sources.Sum(s => s.SupersededBaseUnits) != corrected)
        {
            throw new ArgumentException("The superseded decisions must add up to the corrected quantity.", nameof(sources));
        }

        var acceptedBefore = stage.AcceptedBaseUnits;
        stage.AcceptedBaseUnits -= corrected;
        stage.ReworkBaseUnits += reworkBaseUnits;
        stage.ScrapBaseUnits += scrapBaseUnits;
        stage.PlanningPosition = null;
        if (stage.IsExternal)
        {
            // Rework must be sent again; scrap alone waits for the shortfall decision (the same derivation as after an inspection of an external stage).
            SettleExternal(stage, userId, utc);
        }
        else
        {
            stage.Status = reworkBaseUnits > 0 ? ItemStageStatus.InProgress : ItemStageStatus.WaitingShortfallDecision;
            if (reworkBaseUnits > 0)
            {
                stage.AvailableSinceUtc = utc;
            }
        }

        var correction = new QcCorrection(Id, stageNo, reworkBaseUnits, scrapBaseUnits, text, acceptedBefore, stage.Status, afterCustomerDispatch, sources, userId, utc);
        stage.AffectedByCorrection = correction;
        foreach (var later in _stages.Where(s => s.StageNo > stageNo && s.Status is not (ItemStageStatus.Planned or ItemStageStatus.WaitingPreviousQc)))
        {
            later.AffectedByCorrection = correction;
        }

        UpdatedAtUtc = utc;
        return correction;
    }

    /// <summary>
    /// A child of this assembly (or a descendant) was QC corrected (owner decision Q7, Decision Log #142). The first stage of the assembly returns to the waiting state (PLANNED) when it has not started
    /// (no work session exists), exactly the waiting state of an assembly whose children are unfinished (#140), until the corrected child is QC approved again. A first stage that is already in production
    /// is NOT reset: it is only marked as affected so the production staff assess the real situation. Returns true when the stage returned to waiting.
    /// </summary>
    public bool ApplyChildQcCorrection(QcCorrection correction, bool firstStageHasWorkSessions)
    {
        ArgumentNullException.ThrowIfNull(correction);
        var first = _stages.FirstOrDefault();
        if (first is null || first.Status is ItemStageStatus.Planned or ItemStageStatus.WaitingPreviousQc)
        {
            return false;
        }

        var unstarted = !firstStageHasWorkSessions
            && (first.Status == ItemStageStatus.Waiting || (first.Status == ItemStageStatus.WaitingExternalSend && first.ExternalDispatchedBaseUnits == 0))
            && first.AcceptedBaseUnits == 0 && first.AwaitingQcBaseUnits == 0 && first.ReworkBaseUnits == 0 && first.ScrapBaseUnits == 0;
        first.AffectedByCorrection = correction;
        if (!unstarted)
        {
            return false;
        }

        first.Status = ItemStageStatus.Planned;
        first.AvailableSinceUtc = null;
        first.InputBaseUnits = null;
        first.PlanningPosition = null;
        UpdatedAtUtc = correction.CorrectedAtUtc;
        return true;
    }

    /// <summary>
    /// Closes the shortfall scrap created (Decision Log #95 D3, #101): Management accepts it, or Planning records that replacement production was ordered; nothing is ordered automatically. The stage
    /// then counts as QC approved with the accepted base units, which are all the next stage receives. A note or reference is required.
    /// </summary>
    public ShortfallDecision DecideShortfall(int stageNo, ShortfallKind kind, string? note, long decidedByUserId, DateTime decidedAtUtc)
    {
        var stage = GetStage(stageNo);
        var utc = Customer.RequireUtc(decidedAtUtc, nameof(decidedAtUtc));
        Require(stage, ItemStageStatus.WaitingShortfallDecision, "decided as a shortfall");
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown shortfall decision.");
        }

        var text = note?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > QcInspectionLine.MaxReasonLength)
        {
            throw new ArgumentException("A note of 1 to " + QcInspectionLine.MaxReasonLength + " characters is required.", nameof(note));
        }

        // The shortfall is the scrap QC decided plus the units a contractor never returned (Decision Log #136); the decision keeps the two apart.
        var decision = new ShortfallDecision(Id, stageNo, kind, stage.ScrapBaseUnits + stage.ExternalMissingBaseUnits, text, decidedByUserId, utc, stage.ExternalMissingBaseUnits);
        CompleteStage(stage, (decimal)stage.AcceptedBaseUnits / BaseUnitsPerUnit, decidedByUserId, utc);
        UpdatedAtUtc = utc;
        return decision;
    }

    /// <summary>
    /// The Warehouse sends base units of an executable external stage to its external route (Decision Log #129): at most what is still to send (rework units first), the previous stage being
    /// QC approved. Returns the new dispatch row; the stage becomes EXTERNAL_SENT unless units are still awaiting QC.
    /// </summary>
    public ExternalShipment DispatchExternal(int stageNo, long baseUnits, string? pieceDescription, string? note, long userId, DateTime atUtc)
    {
        var stage = RequireExternalInProgress(stageNo, "dispatched");
        if (baseUnits <= 0 || baseUnits > stage.StillToSendBaseUnits)
        {
            throw new ExternalQuantityException("Stage " + stageNo + " has " + stage.StillToSendBaseUnits + " base units still to send; " + baseUnits + " cannot be dispatched.");
        }

        var shipment = new ExternalShipment(Id, stageNo, ExternalShipmentKind.Dispatch, null, baseUnits, pieceDescription, note, userId, atUtc);
        stage.ExternalDispatchedBaseUnits += baseUnits;
        stage.ReworkBaseUnits = Math.Max(0, stage.ReworkBaseUnits - baseUnits);
        SettleExternal(stage, userId, shipment.RecordedAtUtc);
        return shipment;
    }

    /// <summary>
    /// The Warehouse records units back from one dispatch (§16.6): never more than that dispatch still has outstanding (<paramref name="returnsOfDispatch"/> are its earlier returns). The units
    /// go to QC exactly like a production submission (#101); the time is the transaction time.
    /// </summary>
    public ExternalShipment ReturnExternal(int stageNo, ExternalShipment dispatch, IReadOnlyList<ExternalShipment> returnsOfDispatch, long baseUnits, string? note, long userId, DateTime atUtc)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(returnsOfDispatch);
        var stage = RequireExternalInProgress(stageNo, "returned");
        RequireShipmentOf(stage, dispatch, ExternalShipmentKind.Dispatch);
        var outstanding = OutstandingOf(dispatch, returnsOfDispatch);
        if (baseUnits <= 0 || baseUnits > outstanding)
        {
            throw new ExternalQuantityException("Dispatch " + dispatch.Id + " has " + outstanding + " base units outstanding; " + baseUnits + " cannot be returned.");
        }

        var shipment = new ExternalShipment(Id, stageNo, ExternalShipmentKind.Return, dispatch.Id, baseUnits, null, note, userId, atUtc);
        stage.ExternalReturnedBaseUnits += baseUnits;
        stage.AwaitingQcBaseUnits += baseUnits;
        SettleExternal(stage, userId, shipment.RecordedAtUtc);
        return shipment;
    }

    /// <summary>A mistaken dispatch is cancelled with a reason, only while nothing has been returned against it; its units are still to send again.</summary>
    public void CancelExternalDispatch(int stageNo, ExternalShipment dispatch, IReadOnlyList<ExternalShipment> returnsOfDispatch, string? reason, long userId, DateTime atUtc)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(returnsOfDispatch);
        var stage = RequireExternalInProgress(stageNo, "corrected");
        RequireShipmentOf(stage, dispatch, ExternalShipmentKind.Dispatch);
        if (returnsOfDispatch.Any(r => r.IsActive && r.Kind is ExternalShipmentKind.Return or ExternalShipmentKind.Missing && r.DispatchId == dispatch.Id))
        {
            throw new ExternalQuantityException("Dispatch " + dispatch.Id + " has returns or missing units; cancel those first.");
        }

        dispatch.MarkCancelled(reason, userId, atUtc);
        stage.ExternalDispatchedBaseUnits -= dispatch.BaseUnits;
        SettleExternal(stage, userId, dispatch.CancelledAtUtc!.Value);
    }

    /// <summary>A mistaken return is cancelled with a reason, only while QC has not decided its units yet (they are still awaiting QC); the units count as at the contractor again.</summary>
    public void CancelExternalReturn(int stageNo, ExternalShipment returned, string? reason, long userId, DateTime atUtc)
    {
        ArgumentNullException.ThrowIfNull(returned);
        var stage = RequireExternalInProgress(stageNo, "corrected");
        RequireShipmentOf(stage, returned, ExternalShipmentKind.Return);
        if (stage.AwaitingQcBaseUnits < returned.BaseUnits)
        {
            throw new ExternalQuantityException("QC has already decided units of this return; it cannot be cancelled.");
        }

        returned.MarkCancelled(reason, userId, atUtc);
        stage.ExternalReturnedBaseUnits -= returned.BaseUnits;
        stage.AwaitingQcBaseUnits -= returned.BaseUnits;
        SettleExternal(stage, userId, returned.CancelledAtUtc!.Value);
    }

    /// <summary>
    /// The Warehouse records units of one dispatch as missing - the contractor never returned them (Decision Log #136) - with a mandatory reason, never more than that dispatch has outstanding.
    /// They are not returned and not QC approved; once nothing else is away or awaiting QC the stage waits for the shortfall decision (Management accepts it, or Planning orders replacement).
    /// </summary>
    public ExternalShipment MarkExternalMissing(int stageNo, ExternalShipment dispatch, IReadOnlyList<ExternalShipment> shipmentsOfStage, long baseUnits, string? reason, long userId, DateTime atUtc)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        ArgumentNullException.ThrowIfNull(shipmentsOfStage);
        var stage = RequireExternalInProgress(stageNo, "recorded as missing");
        RequireShipmentOf(stage, dispatch, ExternalShipmentKind.Dispatch);
        var outstanding = OutstandingOf(dispatch, shipmentsOfStage);
        if (baseUnits <= 0 || baseUnits > outstanding)
        {
            throw new ExternalQuantityException("Dispatch " + dispatch.Id + " has " + outstanding + " base units outstanding; " + baseUnits + " cannot be recorded as missing.");
        }

        var shipment = new ExternalShipment(Id, stageNo, ExternalShipmentKind.Missing, dispatch.Id, baseUnits, null, reason, userId, atUtc);
        stage.ExternalMissingBaseUnits += baseUnits;
        SettleExternal(stage, userId, shipment.RecordedAtUtc);
        return shipment;
    }

    /// <summary>A mistaken missing record is cancelled with a reason while the shortfall has not been decided; its units are at the contractor again (and can be returned).</summary>
    public void CancelExternalMissing(int stageNo, ExternalShipment missing, string? reason, long userId, DateTime atUtc)
    {
        ArgumentNullException.ThrowIfNull(missing);
        var stage = RequireExternalInProgress(stageNo, "corrected");
        RequireShipmentOf(stage, missing, ExternalShipmentKind.Missing);
        missing.MarkCancelled(reason, userId, atUtc);
        stage.ExternalMissingBaseUnits -= missing.BaseUnits;
        SettleExternal(stage, userId, missing.CancelledAtUtc!.Value);
    }

    private static long OutstandingOf(ExternalShipment dispatch, IReadOnlyList<ExternalShipment> shipments) =>
        dispatch.BaseUnits - shipments.Where(r => r.IsActive && r.Kind is ExternalShipmentKind.Return or ExternalShipmentKind.Missing && r.DispatchId == dispatch.Id).Sum(r => r.BaseUnits);

    private ItemStage RequireExternalInProgress(int stageNo, string action)
    {
        var stage = GetStage(stageNo);
        if (!stage.IsExternal)
        {
            throw new InvalidOperationException("Stage " + stageNo + " is not an external stage.");
        }

        if (stage.Status is not (ItemStageStatus.WaitingExternalSend or ItemStageStatus.ExternalSent or ItemStageStatus.WaitingQc or ItemStageStatus.WaitingShortfallDecision) || stage.InputBaseUnits is null)
        {
            throw new InvalidOperationException("Stage " + stageNo + " is " + stage.Status.Code() + " and cannot be " + action + ".");
        }

        RequirePreviousApproved(stage);
        return stage;
    }

    private void RequireShipmentOf(ItemStage stage, ExternalShipment shipment, ExternalShipmentKind kind)
    {
        if (shipment.RequestItemId != Id || shipment.StageNo != stage.StageNo || shipment.Kind != kind || !shipment.IsActive)
        {
            throw new InvalidOperationException("The shipment is not an active " + kind + " of stage " + stage.StageNo + ".");
        }
    }

    /// <summary>
    /// The status of an external stage after a change (Decision Log #129): units awaiting QC -> WAITING_QC; else units at the contractor -> EXTERNAL_SENT; else units still to send (rework
    /// included) -> WAITING_EXTERNAL_SEND; else scrap -> the shortfall decision; else QC approved and the next stage unlocked. Returns true when the stage was completed.
    /// </summary>
    private bool SettleExternal(ItemStage stage, long userId, DateTime utc)
    {
        if (stage.AwaitingQcBaseUnits > 0)
        {
            stage.Status = ItemStageStatus.WaitingQc;
        }
        else if (stage.AtContractorBaseUnits > 0)
        {
            stage.Status = ItemStageStatus.ExternalSent;
        }
        else if (stage.StillToSendBaseUnits > 0)
        {
            stage.Status = ItemStageStatus.WaitingExternalSend;
        }
        else if (stage.ScrapBaseUnits > 0 || stage.ExternalMissingBaseUnits > 0)
        {
            stage.Status = ItemStageStatus.WaitingShortfallDecision;
        }
        else
        {
            CompleteStage(stage, (decimal)stage.AcceptedBaseUnits / BaseUnitsPerUnit, userId, utc);
            return true;
        }

        UpdatedAtUtc = utc;
        return false;
    }

    // The stage is QC approved: the append-only approval, and the next stage becomes executable with the accepted base units (nothing when none were accepted).
    private QcApproval CompleteStage(ItemStage stage, decimal approvedQuantity, long userId, DateTime utc)
    {
        stage.Status = ItemStageStatus.QcApproved;
        var approval = new QcApproval(stage.StageNo, approvedQuantity, userId, utc);
        _qcApprovals.Add(approval);
        UpdatedAtUtc = utc;

        var next = _stages.FirstOrDefault(s => s.StageNo == stage.StageNo + 1);
        if (next is { Status: ItemStageStatus.Waiting or ItemStageStatus.WaitingExternalSend } && stage.InputBaseUnits is not null
            && next.AcceptedBaseUnits == 0 && next.AwaitingQcBaseUnits == 0 && next.ReworkBaseUnits == 0 && next.ScrapBaseUnits == 0 && next.ExternalDispatchedBaseUnits == 0)
        {
            // Re-approval after a QC correction (Decision Log #142): the downstream stage has not started, so its input follows the quantity that is valid now (the scrapped units never arrive).
            next.InputBaseUnits = stage.AcceptedBaseUnits;
        }

        if (next is { Status: ItemStageStatus.WaitingPreviousQc })
        {
            if (stage.InputBaseUnits is not null)
            {
                next.InputBaseUnits = stage.AcceptedBaseUnits;
            }

            if (stage.InputBaseUnits is null || stage.AcceptedBaseUnits > 0)
            {
                next.Status = Executable(next);
                next.AvailableSinceUtc = utc;
            }
        }

        stage.PlanningPosition = null;
        return approval;
    }

    private long? PlannedBaseUnits(ItemStage stage)
    {
        var units = stage.PlannedQuantity * BaseUnitsPerUnit;
        return units == decimal.Truncate(units) && units > 0 ? (long)units : null;
    }

    /// <summary>
    /// Planning corrects an erroneous start (Decision Log #95 D8, #102): IN_PROGRESS back to WAITING, allowed only while QC has decided nothing yet and nothing awaits QC. The caller records the reason.
    /// </summary>
    public void UndoStart(int stageNo)
    {
        var stage = GetStage(stageNo);
        Require(stage, ItemStageStatus.InProgress, "corrected back to waiting");
        if (stage.AcceptedBaseUnits > 0 || stage.ScrapBaseUnits > 0 || stage.ReworkBaseUnits > 0 || stage.AwaitingQcBaseUnits > 0)
        {
            throw new InvalidOperationException("Stage " + stageNo + " already has QC decisions; a start cannot be undone.");
        }

        stage.Status = ItemStageStatus.Waiting;
    }

    /// <summary>
    /// Planning corrects an erroneous submission (Decision Log #95 D8, #102): WAITING_QC back to IN_PROGRESS, allowed only while QC has decided nothing at all about the stage.
    /// </summary>
    public void UndoSubmit(int stageNo)
    {
        var stage = GetStage(stageNo);
        Require(stage, ItemStageStatus.WaitingQc, "corrected back to in progress");
        if (stage.AcceptedBaseUnits > 0 || stage.ScrapBaseUnits > 0 || stage.ReworkBaseUnits > 0)
        {
            throw new InvalidOperationException("QC has already decided units of stage " + stageNo + "; a submission cannot be undone.");
        }

        stage.AwaitingQcBaseUnits = 0;
        stage.Status = ItemStageStatus.InProgress;
    }

    /// <summary>
    /// Planning's position of a released (WAITING or IN_PROGRESS) stage in its unit's queue; null removes it. Positions are not unique: equal positions are ordered first come, first served.
    /// Returns false when nothing changed.
    /// </summary>
    public bool SetPlanningPosition(int stageNo, int? position)
    {
        var stage = GetStage(stageNo);
        if (stage.Status is not (ItemStageStatus.Waiting or ItemStageStatus.InProgress))
        {
            throw new InvalidOperationException("Stage " + stageNo + " is " + stage.Status.Code() + "; only a released stage (WAITING or IN_PROGRESS) has a queue position.");
        }

        if (position is < 1 or > MaxPlanningPosition)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "A queue position is between 1 and " + MaxPlanningPosition + ".");
        }

        if (stage.PlanningPosition == position)
        {
            return false;
        }

        stage.PlanningPosition = position;
        return true;
    }

    public const int MaxPlanningPosition = 100000;

    /// <summary>Stops a released stage because of a problem (a reason is required). Allowed for a WAITING or IN_PROGRESS stage that is not already stopped.</summary>
    public void StopStage(int stageNo, string? reason, long stoppedByUserId, DateTime stoppedAtUtc)
    {
        var stage = GetStage(stageNo);
        var utc = Customer.RequireUtc(stoppedAtUtc, nameof(stoppedAtUtc));
        if (stage.Status is not (ItemStageStatus.Waiting or ItemStageStatus.InProgress))
        {
            throw new InvalidOperationException("Stage " + stageNo + " is " + stage.Status.Code() + "; only a WAITING or IN_PROGRESS stage can be stopped.");
        }

        if (stage.IsStopped)
        {
            throw new InvalidOperationException("Stage " + stageNo + " is already stopped.");
        }

        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MaxStopReasonLength)
        {
            throw new ArgumentException("A stop reason of 1 to " + MaxStopReasonLength + " characters is required.", nameof(reason));
        }

        stage.StoppedAtUtc = utc;
        stage.StopReason = text;
        stage.StoppedByUserId = stoppedByUserId;
    }

    public const int MaxStopReasonLength = 500;

    /// <summary>Resumes a stopped stage.</summary>
    public void ResumeStage(int stageNo)
    {
        var stage = GetStage(stageNo);
        if (!stage.IsStopped)
        {
            throw new InvalidOperationException("Stage " + stageNo + " is not stopped.");
        }

        stage.StoppedAtUtc = null;
        stage.StopReason = null;
        stage.StoppedByUserId = null;
    }

    private static void RequireNotStopped(ItemStage stage, string action)
    {
        if (stage.IsStopped)
        {
            throw new InvalidOperationException("Stage " + stage.StageNo + " is stopped and cannot be " + action + " until it is resumed.");
        }
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
