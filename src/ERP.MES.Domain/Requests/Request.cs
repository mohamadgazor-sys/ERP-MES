using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Documents;

namespace ERP.MES.Domain.Requests;

/// <summary>
/// The customer request: the central business container (Domain and Database Specification sections 2.1 and 4). It
/// holds the customer-supplied <see cref="RequestNo"/>, the Jalali request year derived from the request date at
/// creation, the status and its items. A request has exactly one authoritative <see cref="WorkType"/> (MAKE or REPAIR); every item inherits it and mixed-type
/// requests do not exist (Decision Log #84, which supersedes the per-item work type of #2).
///
/// The status changes offered are the ones the owner's lifecycle states explicitly (Decision Log #29, #75, #82): submit to Management, Management approves or rejects, a rejected
/// request is reopened, Planning forwards to Drawing, Drawing returns its result, Planning may send it back. Each creates one <see cref="RequestWorkflowEntry"/>. Cancellation,
/// closing and everything after the Drawing result (contract, item definition, production) are not modelled yet. Persistence is outside this type.
/// </summary>
public sealed class Request
{
    private readonly List<RequestItem> _items = [];
    private readonly List<RequestDocumentLink> _documentLinks = [];
    private readonly List<RequestWorkflowEntry> _workflow = [];

    private Request(
        long id,
        long customerId,
        RequestNo requestNo,
        WorkType workType,
        DateOnly requestDate,
        int jalaliRequestYear,
        string? title,
        string? description,
        long createdByUserId,
        DateTime createdAtUtc)
    {
        Id = id;
        CustomerId = customerId;
        RequestNo = requestNo;
        WorkType = workType;
        RequestDate = requestDate;
        JalaliRequestYear = jalaliRequestYear;
        Title = title;
        Description = description;
        Status = RequestStatus.Created;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public long Id { get; }

    public long CustomerId { get; }

    public RequestNo RequestNo { get; }

    /// <summary>The request's one work type, MAKE or REPAIR, chosen at creation. All items inherit it (Decision Log #84).</summary>
    public WorkType WorkType { get; }

    /// <summary>The customer's request date; the source of <see cref="JalaliRequestYear"/>.</summary>
    public DateOnly RequestDate { get; }

    /// <summary>Jalali year of <see cref="RequestDate"/>, fixed at creation (Decision Log #31). A change would be an audited correction, which is not modelled here.</summary>
    public int JalaliRequestYear { get; }

    public string? Title { get; }

    public string? Description { get; }

    public RequestStatus Status { get; private set; }

    public long CreatedByUserId { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Customer + RequestNo + Jalali Request Year, compared exactly (case-sensitive).</summary>
    public RequestKey Key => new(CustomerId, RequestNo.Value, JalaliRequestYear);

    public IReadOnlyList<RequestItem> Items => _items;

    /// <summary>All document links, oldest first, including superseded ones (the history is kept).</summary>
    public IReadOnlyList<RequestDocumentLink> DocumentLinks => _documentLinks;

    /// <summary>The current request PDF, or null when none is attached (Decision Log #87).</summary>
    public RequestDocumentLink? CurrentRequestPdf =>
        _documentLinks.LastOrDefault(l => l.Type == DocumentType.RequestPdf && l.IsCurrent);

    public bool HasRequestPdf => CurrentRequestPdf is not null;

    /// <summary>The request-level workflow history, oldest first. Append-only: every status-changing action adds one entry.</summary>
    public IReadOnlyList<RequestWorkflowEntry> WorkflowHistory => _workflow;

    public static Request Create(
        long id,
        Customer customer,
        RequestNo requestNo,
        WorkType workType,
        DateOnly requestDate,
        string? title,
        string? description,
        long createdByUserId,
        DateTime createdAtUtc,
        IJalaliDateService dates)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(requestNo);
        ArgumentNullException.ThrowIfNull(dates);
        if (!Enum.IsDefined(workType))
        {
            throw new ArgumentOutOfRangeException(nameof(workType), workType, "Unknown work type.");
        }

        var utc = Customer.RequireUtc(createdAtUtc, nameof(createdAtUtc));
        var year = dates.GetJalaliYear(requestDate);
        return new Request(id, customer.Id, requestNo, workType, requestDate, year, title, description, createdByUserId, utc);
    }

    /// <summary>
    /// Central submits the request to Management: <c>CREATED</c> to <c>SENT_TO_MANAGEMENT</c> (Decision Log #75, #87). Allowed from <c>CREATED</c> only, with or without items, and only
    /// when the request PDF is attached. The caller (the Application use case) has already checked the permission.
    /// </summary>
    public void SubmitToManagement(WorkflowActor actor, DateTime changedAtUtc)
    {
        var utc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
        RequireStatus(RequestStatus.Created, "submitted to Management");

        // Central must attach the request PDF before submitting (Decision Log #87).
        if (!HasRequestPdf)
        {
            throw new InvalidOperationException("The request PDF must be attached before the request is submitted to Management.");
        }

        Transition(RequestAction.Submit, RequestStatus.SentToManagement, actor, utc, outcome: null, note: null);
    }

    /// <summary>Management (Manager role) approves: <c>SENT_TO_MANAGEMENT</c> to <c>MGMT_APPROVED_AT_PLANNING</c>.</summary>
    public void Approve(WorkflowActor actor, DateTime changedAtUtc)
    {
        var utc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
        RequireStatus(RequestStatus.SentToManagement, "approved");
        Transition(RequestAction.Approve, RequestStatus.MgmtApprovedAtPlanning, actor, utc, outcome: null, note: null);
    }

    /// <summary>Management (Manager role) rejects: <c>SENT_TO_MANAGEMENT</c> to <c>MGMT_REJECTED_AT_PLANNING</c>. The reason is mandatory (Decision Log #29).</summary>
    public void Reject(WorkflowActor actor, string reason, DateTime changedAtUtc)
    {
        var utc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
        var text = RequireNote(reason, nameof(reason));
        RequireStatus(RequestStatus.SentToManagement, "rejected");
        Transition(RequestAction.Reject, RequestStatus.MgmtRejectedAtPlanning, actor, utc, outcome: null, text);
    }

    /// <summary>Planning or Management reopens a rejected request for a new Management decision: <c>MGMT_REJECTED_AT_PLANNING</c> to <c>SENT_TO_MANAGEMENT</c>. The reason is mandatory and audited (Decision Log #29).</summary>
    public void Reopen(WorkflowActor actor, string reason, DateTime changedAtUtc)
    {
        var utc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
        var text = RequireNote(reason, nameof(reason));
        RequireStatus(RequestStatus.MgmtRejectedAtPlanning, "reopened");
        Transition(RequestAction.Reopen, RequestStatus.SentToManagement, actor, utc, outcome: null, text);
    }

    /// <summary>Planning sends an approved request to Drawing: <c>MGMT_APPROVED_AT_PLANNING</c> to <c>UNDER_DRAWING_REVIEW</c>.</summary>
    public void ForwardToDrawing(WorkflowActor actor, DateTime changedAtUtc)
    {
        var utc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
        RequireStatus(RequestStatus.MgmtApprovedAtPlanning, "forwarded to Drawing");
        Transition(RequestAction.ForwardToDrawing, RequestStatus.UnderDrawingReview, actor, utc, outcome: null, note: null);
    }

    /// <summary>
    /// Drawing returns its technical result to Planning: <c>UNDER_DRAWING_REVIEW</c> to <c>DRAWING_RETURNED_TO_PLANNING</c>. The outcome is data on the workflow entry. A technical note
    /// is required for every outcome except a full acceptance (docs/WORKFLOW_REDESIGN.md, a labelled default). The result never depends on material availability (Decision Log #82).
    /// </summary>
    public void RecordDrawingResult(WorkflowActor actor, DrawingOutcome outcome, string? note, DateTime changedAtUtc)
    {
        var utc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown Drawing outcome.");
        }

        var text = outcome == DrawingOutcome.FullyAccepted ? OptionalNote(note, nameof(note)) : RequireNote(note, nameof(note));
        RequireStatus(RequestStatus.UnderDrawingReview, "given a Drawing result");
        Transition(RequestAction.RecordDrawingResult, RequestStatus.DrawingReturnedToPlanning, actor, utc, outcome, text);
    }

    /// <summary>Planning sends a returned request back to Drawing (more information or correction): <c>DRAWING_RETURNED_TO_PLANNING</c> to <c>UNDER_DRAWING_REVIEW</c>. The reason is mandatory (a labelled default, docs/REQUEST_STATUS_TRANSITIONS_DRAFT.md row 9).</summary>
    public void SendBackToDrawing(WorkflowActor actor, string reason, DateTime changedAtUtc)
    {
        var utc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
        var text = RequireNote(reason, nameof(reason));
        RequireStatus(RequestStatus.DrawingReturnedToPlanning, "sent back to Drawing");
        Transition(RequestAction.SendBackToDrawing, RequestStatus.UnderDrawingReview, actor, utc, outcome: null, text);
    }

    private void RequireStatus(RequestStatus expected, string what)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException("Only a " + expected.Code() + " request can be " + what + "; this one is " + Status.Code() + ".");
        }
    }

    private static string RequireNote(string? note, string parameterName)
    {
        var text = OptionalNote(note, parameterName);
        return text ?? throw new ArgumentException("A reason or note is required.", parameterName);
    }

    private static string? OptionalNote(string? note, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var text = note.Trim();
        return text.Length <= RequestWorkflowEntry.MaxNoteLength
            ? text
            : throw new ArgumentException("A reason or note is limited to " + RequestWorkflowEntry.MaxNoteLength + " characters.", parameterName);
    }

    // The one place where a status changes through a workflow action: the status, the "updated" time and the history entry change together.
    private void Transition(RequestAction action, RequestStatus to, WorkflowActor actor, DateTime atUtc, DrawingOutcome? outcome, string? note)
    {
        _workflow.Add(new RequestWorkflowEntry(action, Status, to, outcome, note, actor, atUtc));
        Status = to;
        UpdatedAtUtc = atUtc;
    }

    /// <summary>
    /// Attaches the request PDF (Decision Log #87). Allowed while the request is still <c>CREATED</c> (before it is submitted to Management). A PDF that already
    /// exists is superseded, not deleted: its link stays with the time it was replaced. The document is stored once and linked here (Decision Log #35).
    /// </summary>
    public RequestDocumentLink AttachRequestPdf(StoredDocument document, long linkedByUserId, DateTime attachedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Type != DocumentType.RequestPdf)
        {
            throw new ArgumentException("Only a request PDF can be attached as the request PDF.", nameof(document));
        }

        var utc = Customer.RequireUtc(attachedAtUtc, nameof(attachedAtUtc));
        if (Status != RequestStatus.Created)
        {
            throw new InvalidOperationException("The request PDF can be attached only while the request is " + RequestStatus.Created.Code() + "; this one is " + Status.Code() + ".");
        }

        CurrentRequestPdf?.Supersede(utc);
        var link = new RequestDocumentLink(document, linkedByUserId, utc);
        _documentLinks.Add(link);
        UpdatedAtUtc = utc;
        return link;
    }

    /// <summary>
    /// Sets the status without checking a transition. Request status transitions are not approved yet (the table is a
    /// draft), so no public transition method exists; this internal member serves tests and the later workflow work.
    /// </summary>
    internal void SetStatus(RequestStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown request status.");
        }

        Status = status;
    }

    /// <summary>
    /// DEMONSTRATION-ONLY (Decision Log #57, T1): places a request directly in one of the approved statuses without checking a
    /// transition, so fictional sample data can show requests at different points. It is NOT a status transition and does not approve the
    /// transition table, which stays an unapproved draft (Decision Log #60). It is deliberately <c>internal</c>: only the Infrastructure assembly (the demonstration seed) and the Domain tests can see
    /// it, and a repository test allows exactly one production call site.
    /// </summary>
    internal void PlaceInStatusForDemonstration(RequestStatus status, DateTime changedAtUtc)
    {
        var utc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
        SetStatus(status);
        UpdatedAtUtc = utc;
    }

    /// <summary>True when an item with exactly this number already exists in the request. The comparison is exact (ordinal, no trimming); the spec does not say whether item numbers are case-sensitive, so the exact comparison is used until it is decided.</summary>
    public bool HasItemNo(string itemNo) =>
        _items.Any(i => string.Equals(i.ItemNo, itemNo, StringComparison.Ordinal));

    /// <summary>
    /// Planning defines an item with its specification (Decision Log #86, #95). A MAKE item needs a quantity type, a material and a weight; a REPAIR item may omit them. Values that are
    /// given must be valid (positive, within the scale, not rounded). The gates before definition (Management, Drawing, contract, inquiries) are checked by the Application, which can see
    /// the other aggregates; this method enforces what the request and the specification alone decide.
    /// </summary>
    public RequestItem DefineItem(
        long itemId, string itemNo, string partName, decimal requestedQuantity, ItemSpecification specification, long createdByUserId, DateTime createdAtUtc, Priority priority = Priority.Normal)
    {
        ArgumentNullException.ThrowIfNull(specification);
        var problem = specification.Check(WorkType);
        if (problem != SpecificationProblem.None)
        {
            throw new ItemSpecificationException(problem);
        }

        if (Status is RequestStatus.Cancelled or RequestStatus.Closed)
        {
            throw new InvalidOperationException("A " + Status.Code() + " request accepts no new items.");
        }

        if (itemNo is not null && HasItemNo(itemNo))
        {
            throw new InvalidOperationException("The request already contains an item with number '" + itemNo + "'.");
        }

        var item = new RequestItem(itemId, itemNo!, partName, requestedQuantity, priority, createdByUserId, createdAtUtc, specification);
        _items.Add(item);
        UpdatedAtUtc = createdAtUtc;
        return item;
    }

    /// <summary>
    /// Revises an existing item's definition (Excel import "update", Decision Log #97) under the same specification rules as <see cref="DefineItem"/>. Returns false when nothing changed.
    /// An item that already has stages or a QC approval cannot be revised this way (<see cref="InvalidOperationException"/>).
    /// </summary>
    public bool ReviseItem(string itemNo, string partName, decimal requestedQuantity, Priority priority, ItemSpecification specification, DateTime changedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(specification);
        var problem = specification.Check(WorkType);
        if (problem != SpecificationProblem.None)
        {
            throw new ItemSpecificationException(problem);
        }

        if (Status is RequestStatus.Cancelled or RequestStatus.Closed)
        {
            throw new InvalidOperationException("A " + Status.Code() + " request accepts no item changes.");
        }

        var item = _items.FirstOrDefault(i => string.Equals(i.ItemNo, itemNo, StringComparison.Ordinal)) ?? throw new InvalidOperationException("The request has no item with number '" + itemNo + "'.");
        var changed = item.ReviseDefinition(partName, requestedQuantity, priority, specification, changedAtUtc);
        if (changed)
        {
            UpdatedAtUtc = changedAtUtc;
        }

        return changed;
    }

    /// <summary>
    /// Adds an item. (RequestID, ItemNo) must be unique (section 5.2). The item inherits the request's work type (Decision Log #84). New
    /// items default to <see cref="Priority.Normal"/> (Decision Log #58).
    ///
    /// Cancelled and closed requests accept no new items. Ordinary item definition belongs before production release,
    /// and later changes follow the controlled Planning process (Decision Log #34); that limit is NOT enforced here
    /// because no production-release state exists yet, and the status transitions and permissions that would govern
    /// it are deferred to the workflow and authorization work. Callers must not treat this method as a rule that items
    /// may be added in every other state.
    /// </summary>
    public RequestItem AddItem(
        long itemId,
        string itemNo,
        string partName,
        decimal requestedQuantity,
        long createdByUserId,
        DateTime createdAtUtc,
        Priority priority = Priority.Normal)
    {
        if (Status is RequestStatus.Cancelled or RequestStatus.Closed)
        {
            throw new InvalidOperationException("A " + Status.Code() + " request accepts no new items.");
        }

        if (itemNo is not null && HasItemNo(itemNo))
        {
            throw new InvalidOperationException("The request already contains an item with number '" + itemNo + "'.");
        }

        var item = new RequestItem(itemId, itemNo!, partName, requestedQuantity, priority, createdByUserId, createdAtUtc);
        _items.Add(item);
        UpdatedAtUtc = createdAtUtc;
        return item;
    }
}

/// <summary>Thrown when an item specification is incomplete or invalid for the request's work type.</summary>
public sealed class ItemSpecificationException(SpecificationProblem problem) : ArgumentException("The item specification is not valid: " + problem)
{
    public SpecificationProblem Problem { get; } = problem;
}
