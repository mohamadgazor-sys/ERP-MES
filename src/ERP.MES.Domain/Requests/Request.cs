using ERP.MES.Domain.Calendar;

namespace ERP.MES.Domain.Requests;

/// <summary>
/// The customer request: the central business container (Domain and Database Specification sections 2.1 and 4). It
/// holds the customer-supplied <see cref="RequestNo"/>, the Jalali request year derived from the request date at
/// creation, the status and its items, which may mix MAKE and REPAIR work.
///
/// Status transitions are intentionally absent: the transition table is an unapproved draft. Contract, workflow
/// history, documents and persistence are outside this type.
/// </summary>
public sealed class Request
{
    private readonly List<RequestItem> _items = [];

    private Request(
        long id,
        long customerId,
        RequestNo requestNo,
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

    public static Request Create(
        long id,
        Customer customer,
        RequestNo requestNo,
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

        var utc = Customer.RequireUtc(createdAtUtc, nameof(createdAtUtc));
        var year = dates.GetJalaliYear(requestDate);
        return new Request(id, customer.Id, requestNo, requestDate, year, title, description, createdByUserId, utc);
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

    /// <summary>True when an item with exactly this number already exists in the request. The comparison is exact (ordinal, no trimming); the spec does not say whether item numbers are case-sensitive, so the exact comparison is used until it is decided.</summary>
    public bool HasItemNo(string itemNo) =>
        _items.Any(i => string.Equals(i.ItemNo, itemNo, StringComparison.Ordinal));

    /// <summary>
    /// Adds an item. (RequestID, ItemNo) must be unique (section 5.2). A request may mix MAKE and REPAIR items. New
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
        WorkType workType,
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

        var item = new RequestItem(itemId, itemNo!, partName, workType, requestedQuantity, priority, createdByUserId, createdAtUtc);
        _items.Add(item);
        UpdatedAtUtc = createdAtUtc;
        return item;
    }
}
