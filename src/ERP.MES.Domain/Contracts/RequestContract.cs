using ERP.MES.Domain.Documents;
using ERP.MES.Domain.Quality;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Contracts;

/// <summary>
/// The request-level contract gate (Domain and Database Specification section 11; Decision Log #85). One per MAKE request. Planning decides whether a contract is required; when it is,
/// Planning prepares a version (number, date, title, PDF) and submits it; Management approves or rejects. A rejection returns the contract to Planning (provisional default routing):
/// Planning starts a NEW version, so every submitted version, its PDF link and Management's decision stay as they were. The request status is not changed by this type; the contract has its
/// own status and a request's readiness is derived from it. Every method needs the request's <see cref="ContractEligibility"/> (read in the same transaction) and refuses a request that
/// is not eligible. Callers check permissions first; persistence is outside this type.
/// </summary>
public sealed class RequestContract
{
    public const int MaxNoteLength = 2000;

    private readonly List<ContractVersion> _versions = [];
    private readonly List<ContractHistoryEntry> _history = [];

    private RequestContract()
    {
    }

    public long Id { get; }

    public long RequestId { get; private set; }

    public ContractRequirement Requirement { get; private set; }

    public ContractStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>All versions, oldest first, including submitted and withdrawn ones.</summary>
    public IReadOnlyList<ContractVersion> Versions => _versions;

    /// <summary>The append-only history, oldest first.</summary>
    public IReadOnlyList<ContractHistoryEntry> History => _history;

    public ContractVersion? CurrentVersion => _versions.LastOrDefault(v => v.State != ContractVersionState.Withdrawn);

    /// <summary>True when the contract gate no longer blocks the request: no contract required, or the required contract is approved.</summary>
    public bool IsSatisfied => Status is ContractStatus.NotRequired or ContractStatus.Approved;

    /// <summary>Planning's decision on a MAKE request that has no contract record yet.</summary>
    public static RequestContract Decide(long requestId, ContractEligibility facts, ContractRequirement requirement, string? note, WorkflowActor actor, DateTime atUtc)
    {
        var contract = new RequestContract { RequestId = requestId };
        contract.RequireEligible(facts);
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        var text = CleanNote(note, required: false);
        if (!Enum.IsDefined(requirement))
        {
            throw new ArgumentOutOfRangeException(nameof(requirement), requirement, "Unknown contract requirement.");
        }

        contract.CreatedAtUtc = utc;
        contract.UpdatedAtUtc = utc;
        contract.Requirement = requirement;
        if (requirement == ContractRequirement.NotRequired)
        {
            contract.Add(ContractAction.DecidedNotRequired, null, ContractStatus.NotRequired, null, text, actor, utc);
        }
        else
        {
            contract._versions.Add(new ContractVersion(1, null, null, null, null, actor, utc));
            contract.Add(ContractAction.DecidedRequired, null, ContractStatus.Draft, 1, text, actor, utc);
        }

        return contract;
    }

    /// <summary>
    /// Planning changes its decision with a reason. Allowed from "not required" to "required" (a new draft starts) and, while no version was ever submitted, from "required" back to
    /// "not required" (the unsubmitted draft is withdrawn, not deleted). Once a version was submitted the decision is final.
    /// </summary>
    public void ChangeRequirement(ContractEligibility facts, ContractRequirement requirement, string? reason, WorkflowActor actor, DateTime atUtc)
    {
        RequireEligible(facts);
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        var text = CleanNote(reason, required: true);
        if (!Enum.IsDefined(requirement) || requirement == Requirement)
        {
            throw new InvalidOperationException("The requirement is already as requested.");
        }

        var from = Status;
        if (requirement == ContractRequirement.Required)
        {
            Requirement = ContractRequirement.Required;
            var no = _versions.Count + 1;
            _versions.Add(new ContractVersion(no, null, null, null, null, actor, utc));
            Add(ContractAction.RequirementChanged, from, ContractStatus.Draft, no, text, actor, utc);
        }
        else
        {
            if (Status != ContractStatus.Draft || _versions.Any(v => v.State == ContractVersionState.Submitted))
            {
                throw new InvalidOperationException("A contract that was submitted can no longer be declared not required.");
            }

            Requirement = ContractRequirement.NotRequired;
            foreach (var draft in _versions.Where(v => v.State == ContractVersionState.Draft))
            {
                draft.Withdraw();
            }

            Add(ContractAction.RequirementChanged, from, ContractStatus.NotRequired, null, text, actor, utc);
        }
    }

    /// <summary>Planning edits the draft version's fields. Only a draft can be edited; a submitted version never changes.</summary>
    public void SaveDraft(ContractEligibility facts, string? contractNo, DateOnly? contractDate, string? title, string? note, WorkflowActor actor, DateTime atUtc)
    {
        RequireEligible(facts);
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        var draft = RequireDraft();
        draft.SetFields(contractNo, contractDate, title, note);
        Add(ContractAction.DraftSaved, Status, Status, draft.VersionNo, null, actor, utc);
    }

    /// <summary>Planning attaches the contract PDF to the draft. A replaced PDF's link stays as history (the binary is stored once).</summary>
    public void AttachPdf(ContractEligibility facts, StoredDocument document, WorkflowActor actor, DateTime atUtc)
    {
        ArgumentNullException.ThrowIfNull(document);
        RequireEligible(facts);
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        var draft = RequireDraft();
        draft.AttachPdf(document, actor.UserId, utc);
        Add(ContractAction.PdfAttached, Status, Status, draft.VersionNo, $"{document.FileName} ({document.ContentLength} bytes, SHA-256 {document.ContentHash})", actor, utc);
    }

    /// <summary>Planning submits the draft to Management. Needs a contract number, a contract date and the PDF.</summary>
    public void Submit(ContractEligibility facts, WorkflowActor actor, DateTime atUtc)
    {
        RequireEligible(facts);
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        var draft = RequireDraft();
        var problem = draft.CheckSubmittable();
        if (problem != ContractSubmissionProblem.None)
        {
            throw new ContractSubmissionException(problem);
        }

        draft.MarkSubmitted(actor.UserId, utc);
        var from = Status;
        Status = ContractStatus.PendingManagement;
        Add(ContractAction.Submitted, from, Status, draft.VersionNo, null, actor, utc);
    }

    /// <summary>Management approves the submitted version; the explanation is optional.</summary>
    public void Approve(ContractEligibility facts, string? note, WorkflowActor actor, DateTime atUtc)
    {
        RequireEligible(facts);
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        var text = CleanNote(note, required: false);
        var version = RequirePending();
        version.Decide(ContractDecision.Approved, text, actor.UserId, utc);
        var from = Status;
        Status = ContractStatus.Approved;
        Add(ContractAction.Approved, from, Status, version.VersionNo, text, actor, utc);
    }

    /// <summary>Management rejects the submitted version; a reason is mandatory. The contract returns to Planning (provisional default routing).</summary>
    public void Reject(ContractEligibility facts, string? reason, WorkflowActor actor, DateTime atUtc)
    {
        RequireEligible(facts);
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        var text = CleanNote(reason, required: true);
        var version = RequirePending();
        version.Decide(ContractDecision.Rejected, text, actor.UserId, utc);
        var from = Status;
        Status = ContractStatus.RejectedAtPlanning;
        Add(ContractAction.Rejected, from, Status, version.VersionNo, text, actor, utc);
    }

    /// <summary>Planning starts a new draft version from a rejected one. The rejected version, its PDF link and Management's decision are untouched; the new draft starts with the same fields and a new link to the same stored PDF.</summary>
    public void StartRevision(ContractEligibility facts, WorkflowActor actor, DateTime atUtc)
    {
        RequireEligible(facts);
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        if (Status != ContractStatus.RejectedAtPlanning)
        {
            throw new InvalidOperationException("Only a rejected contract can be revised.");
        }

        var previous = _versions.Last(v => v.State == ContractVersionState.Submitted);
        var next = new ContractVersion(_versions.Count + 1, previous.ContractNo, previous.ContractDate, previous.Title, previous.Note, actor, utc);
        if (previous.CurrentPdf is { } carried)
        {
            next.CarryOverPdf(carried, actor.UserId, utc);
        }

        _versions.Add(next);
        var from = Status;
        Status = ContractStatus.Draft;
        Add(ContractAction.RevisionStarted, from, Status, next.VersionNo, null, actor, utc);
    }

    private ContractVersion RequireDraft()
    {
        if (Status != ContractStatus.Draft)
        {
            throw new InvalidOperationException("The contract is not a draft.");
        }

        return _versions.Last(v => v.State == ContractVersionState.Draft);
    }

    private ContractVersion RequirePending()
    {
        if (Status != ContractStatus.PendingManagement)
        {
            throw new InvalidOperationException("The contract is not waiting for Management.");
        }

        return _versions.Last(v => v.State == ContractVersionState.Submitted);
    }

    private void RequireEligible(ContractEligibility facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var reason = facts.Check();
        if (reason != ContractIneligibility.None)
        {
            throw new ContractGateException(reason);
        }
    }

    private void Add(ContractAction action, ContractStatus? from, ContractStatus to, int? versionNo, string? note, WorkflowActor actor, DateTime atUtc)
    {
        UpdatedAtUtc = atUtc;
        Status = to;
        _history.Add(new ContractHistoryEntry(action, from, to, versionNo, note, actor, atUtc));
    }

    internal static string? CleanNote(string? note, bool required)
    {
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (text is null && required)
        {
            throw new ArgumentException("A reason is required.", nameof(note));
        }

        return text is { Length: > MaxNoteLength } ? throw new ArgumentException("The text is limited to " + MaxNoteLength + " characters.", nameof(note)) : text;
    }
}

/// <summary>One version of a request's contract. Mutable only while it is a draft; once submitted it is never changed except for Management's single decision.</summary>
public sealed class ContractVersion
{
    public const int MaxContractNoLength = 50;
    public const int MaxTitleLength = 200;

    private readonly List<ContractDocumentLink> _documentLinks = [];

    private ContractVersion()
    {
    }

    internal ContractVersion(int versionNo, string? contractNo, DateOnly? contractDate, string? title, string? note, WorkflowActor actor, DateTime createdAtUtc)
    {
        VersionNo = versionNo;
        State = ContractVersionState.Draft;
        CreatedByUserId = actor.UserId;
        CreatedAtUtc = createdAtUtc;
        SetFields(contractNo, contractDate, title, note);
    }

    public long Id { get; }

    public int VersionNo { get; private set; }

    public string? ContractNo { get; private set; }

    public DateOnly? ContractDate { get; private set; }

    public string? Title { get; private set; }

    public string? Note { get; private set; }

    public ContractVersionState State { get; private set; }

    public long CreatedByUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public long? SubmittedByUserId { get; private set; }

    public DateTime? SubmittedAtUtc { get; private set; }

    public ContractDecision? Decision { get; private set; }

    public string? DecisionNote { get; private set; }

    public long? DecidedByUserId { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>All PDF links of this version, oldest first, including superseded ones.</summary>
    public IReadOnlyList<ContractDocumentLink> DocumentLinks => _documentLinks;

    public ContractDocumentLink? CurrentPdf => _documentLinks.LastOrDefault(l => l.IsCurrent);

    public ContractSubmissionProblem CheckSubmittable() =>
        string.IsNullOrWhiteSpace(ContractNo) ? ContractSubmissionProblem.ContractNoMissing
        : ContractDate is null ? ContractSubmissionProblem.ContractDateMissing
        : CurrentPdf is null ? ContractSubmissionProblem.PdfMissing
        : ContractSubmissionProblem.None;

    internal void SetFields(string? contractNo, DateOnly? contractDate, string? title, string? note)
    {
        var no = string.IsNullOrWhiteSpace(contractNo) ? null : contractNo.Trim();
        var heading = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        if (no is { Length: > MaxContractNoLength } || heading is { Length: > MaxTitleLength })
        {
            throw new ArgumentException("The contract number or title is too long.");
        }

        RequireDraftState();
        ContractNo = no;
        ContractDate = contractDate;
        Title = heading;
        Note = RequestContract.CleanNote(note, required: false);
    }

    internal void AttachPdf(StoredDocument document, long userId, DateTime atUtc)
    {
        RequireDraftState();
        if (document.Type != DocumentType.ContractPdf)
        {
            throw new ArgumentException("The document is not a contract PDF.", nameof(document));
        }

        foreach (var link in _documentLinks.Where(l => l.IsCurrent))
        {
            link.Supersede(atUtc);
        }

        _documentLinks.Add(new ContractDocumentLink(document, userId, atUtc));
    }

    /// <summary>Links the previous version's stored PDF to this draft with a new link row (the binary stays stored once; the document need not be loaded).</summary>
    internal void CarryOverPdf(ContractDocumentLink from, long userId, DateTime atUtc)
    {
        RequireDraftState();
        _documentLinks.Add(new ContractDocumentLink(from, userId, atUtc));
    }

    internal void MarkSubmitted(long userId, DateTime atUtc)
    {
        RequireDraftState();
        State = ContractVersionState.Submitted;
        SubmittedByUserId = userId;
        SubmittedAtUtc = atUtc;
    }

    internal void Decide(ContractDecision decision, string? note, long userId, DateTime atUtc)
    {
        if (State != ContractVersionState.Submitted || Decision is not null)
        {
            throw new InvalidOperationException("The version has no pending Management decision.");
        }

        Decision = decision;
        DecisionNote = note;
        DecidedByUserId = userId;
        DecidedAtUtc = atUtc;
    }

    internal void Withdraw()
    {
        RequireDraftState();
        State = ContractVersionState.Withdrawn;
    }

    private void RequireDraftState()
    {
        if (State != ContractVersionState.Draft)
        {
            throw new InvalidOperationException("Only a draft version can be changed.");
        }
    }
}

/// <summary>The association of a stored PDF with a contract version (a link row, Decision Log #35). Never deleted; a replaced PDF is marked superseded.</summary>
public sealed class ContractDocumentLink
{
    private ContractDocumentLink()
    {
        FileName = string.Empty;
        ContentHash = string.Empty;
    }

    internal ContractDocumentLink(StoredDocument document, long linkedByUserId, DateTime linkedAtUtc)
    {
        Document = document;
        Type = document.Type;
        FileName = document.FileName;
        ContentLength = document.ContentLength;
        ContentHash = document.ContentHash;
        LinkedByUserId = linkedByUserId;
        LinkedAtUtc = linkedAtUtc;
    }

    internal ContractDocumentLink(ContractDocumentLink from, long linkedByUserId, DateTime linkedAtUtc)
    {
        DocumentId = from.DocumentId;
        Type = from.Type;
        FileName = from.FileName;
        ContentLength = from.ContentLength;
        ContentHash = from.ContentHash;
        LinkedByUserId = linkedByUserId;
        LinkedAtUtc = linkedAtUtc;
    }

    public long Id { get; }

    public DocumentType Type { get; }

    /// <summary>The stored binary; not loaded with the contract (it can be large) - use the snapshot fields.</summary>
    public StoredDocument? Document { get; }

    public long DocumentId { get; }

    public string FileName { get; }

    public long ContentLength { get; }

    public string ContentHash { get; }

    public long LinkedByUserId { get; }

    public DateTime LinkedAtUtc { get; }

    public DateTime? SupersededAtUtc { get; private set; }

    public bool IsCurrent => SupersededAtUtc is null;

    internal void Supersede(DateTime atUtc) => SupersededAtUtc = atUtc;
}

/// <summary>One row of the contract history. Append-only: created with the change it records, never changed or deleted.</summary>
public sealed class ContractHistoryEntry
{
    private ContractHistoryEntry()
    {
        ActionByDepartmentCode = string.Empty;
    }

    internal ContractHistoryEntry(ContractAction action, ContractStatus? from, ContractStatus to, int? versionNo, string? note, WorkflowActor actor, DateTime atUtc)
    {
        Action = action;
        FromStatus = from;
        ToStatus = to;
        VersionNo = versionNo;
        Note = note;
        ActionByUserId = actor.UserId;
        ActionByDepartmentCode = actor.DepartmentCode;
        ActionAtUtc = atUtc;
    }

    public long Id { get; }

    public ContractAction Action { get; }

    public ContractStatus? FromStatus { get; }

    public ContractStatus ToStatus { get; }

    public int? VersionNo { get; }

    public string? Note { get; }

    public long ActionByUserId { get; }

    public string ActionByDepartmentCode { get; }

    public DateTime ActionAtUtc { get; }
}
