using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Documents;

namespace ERP.MES.Domain.Requests;

/// <summary>
/// The customer request: the central business container (Domain and Database Specification sections 2.1 and 4). It
/// holds the customer-supplied <see cref="RequestNo"/>, the Jalali request year derived from the request date at
/// creation, the status and its items. A request has exactly one authoritative <see cref="WorkType"/> (MAKE or REPAIR); every item inherits it and mixed-type
/// requests do not exist (Decision Log #84, which supersedes the per-item work type of #2).
///
/// The only status change offered is the one named by the approved permission "create a request and submit it to
/// Management" (Decision Log #34, #75): <see cref="SubmitToManagement"/>. The rest of the transition table is an unapproved
/// draft and is not modelled. Contract, workflow history, documents and persistence are outside this type.
/// </summary>
public sealed class Request
{
    private readonly List<RequestItem> _items = [];
    private readonly List<RequestDocumentLink> _documentLinks = [];

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
    /// The one request transition of this milestone (Decision Log #75): <c>CREATED</c> to <c>SENT_TO_MANAGEMENT</c>, the "submit to Management" of the approved Central
    /// action. It is allowed from <c>CREATED</c> only, with or without items (items are defined afterwards by Drawing and Planning). It does not approve the draft
    /// transition table: approval, rejection, reopening and cancellation are not modelled. The caller (the Application use case) has already checked the permission.
    /// </summary>
    public void SubmitToManagement(DateTime changedAtUtc)
    {
        var utc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
        if (Status != RequestStatus.Created)
        {
            throw new InvalidOperationException("Only a " + RequestStatus.Created.Code() + " request can be submitted to Management; this one is " + Status.Code() + ".");
        }

        // Central must attach the request PDF before submitting (Decision Log #87).
        if (!HasRequestPdf)
        {
            throw new InvalidOperationException("The request PDF must be attached before the request is submitted to Management.");
        }

        Status = RequestStatus.SentToManagement;
        UpdatedAtUtc = utc;
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
