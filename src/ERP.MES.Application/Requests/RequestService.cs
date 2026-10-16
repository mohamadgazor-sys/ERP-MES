using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

/// <summary>What a person typed to create a request: the customer, the customer's own request number, the request date as Jalali text, and optional texts.</summary>
public sealed record CreateRequestCommand(long CustomerId, string? RequestNo, string? RequestDateText, string? Title, string? Description);

/// <summary>What a person typed to add an item. <see cref="Quantity"/> is already a number; see <see cref="QuantityText"/>.</summary>
public sealed record AddItemCommand(string? ItemNo, string? PartName, WorkType WorkType, decimal Quantity, Priority Priority = Priority.Normal);

/// <summary>A request with its items and the name of its customer, loaded at <see cref="Stored{T}.Version"/>; <see cref="Versions"/> holds the row version of the request and of every item and stage, for the item- and stage-level expected versions (Decision Log #77).</summary>
public sealed record RequestDetail(Stored<Request> Request, string CustomerName, RequestVersions Versions);

/// <summary>
/// Which actions the current user may perform right now, for enabling buttons. It is an answer of the Application (from the database, via
/// <see cref="CurrentAuthorization"/>), not a decision of the screen - and only a convenience: every command checks again when it runs.
/// </summary>
public sealed record Capabilities(
    bool CanView,
    bool CanMaintainCustomers,
    bool CanCreateRequest,
    bool CanSubmitRequest,
    bool CanAddMakeItem,
    bool CanAddRepairItem,
    bool CanSetPriority)
{
    public static Capabilities None { get; } = new(false, false, false, false, false, false, false);
}

/// <summary>
/// Request entry and viewing (Decision Log #34, #75). A mixed MAKE/REPAIR request is built in turn by the departments that own the parts: Central creates the request
/// and submits it; Drawing adds MAKE items; Planning adds REPAIR items and may add any item. Nobody is allowed more than the approved permission map says, and every
/// command reloads the user from the database and checks the permission at the time of the operation (<see cref="CurrentAuthorization"/>). Only the one transition
/// CREATED to SENT_TO_MANAGEMENT exists; the rest of the status table is an unapproved draft. Updates pass the version the request was loaded at.
/// </summary>
public sealed class RequestService(
    IRequestRepository requests, ICustomerRepository customers, CurrentAuthorization authorization, IClock clock, IJalaliDateService dates)
{
    private static readonly Permission[] AllPermissions =
    [
        Permission.ViewCustomersAndRequests,
        Permission.MaintainCustomers,
        Permission.CreateAndSubmitRequest,
        Permission.DefineMakeItems,
        Permission.DefineRepairItemsAndEditAnyItem,
        Permission.ChangePlanningPriority,
    ];

    public async Task<Capabilities> GetCapabilitiesAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, AllPermissions, cancellationToken);
        if (set.Current is null)
        {
            return Capabilities.None;
        }

        // Planning "may create/edit any item" (Decision Log #34), so Planning's permission also covers MAKE items.
        var repair = set.Has(Permission.DefineRepairItemsAndEditAnyItem);
        return new Capabilities(
            set.Has(Permission.ViewCustomersAndRequests),
            set.Has(Permission.MaintainCustomers),
            set.Has(Permission.CreateAndSubmitRequest),
            set.Has(Permission.CreateAndSubmitRequest),
            set.Has(Permission.DefineMakeItems) || repair,
            repair,
            set.Has(Permission.ChangePlanningPriority));
    }

    public async Task<Outcome<IReadOnlyList<RequestSummary>>> ListAsync(Principal? principal, long? customerId, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ViewCustomersAndRequests, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<IReadOnlyList<RequestSummary>>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<IReadOnlyList<RequestSummary>>.Ok(await requests.ListAsync(customerId, cancellationToken)));
    }

    public async Task<Outcome<RequestDetail>> GetAsync(Principal? principal, long requestId, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ViewCustomersAndRequests, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<RequestDetail>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var versioned = await requests.FindWithVersionsAsync(requestId, cancellationToken);
            if (versioned is null)
            {
                return Outcome<RequestDetail>.Fail(OperationStatus.NotFound);
            }

            var customer = await customers.FindAsync(versioned.Value.CustomerId, cancellationToken);
            return Outcome<RequestDetail>.Ok(new RequestDetail(
                new Stored<Request>(versioned.Value, versioned.Versions.Request), customer?.Value.Name ?? string.Empty, versioned.Versions));
        });
    }

    /// <summary>
    /// Creates a request in status CREATED (Central), only for an ACTIVE customer that exists at this moment (Decision Log #76). Requests that already exist keep working when their customer is deactivated later. The number is stored exactly as typed; the Jalali year comes from the request date (Decision Log #31). A number that
    /// differs from an existing one of the same customer and year only by letter case is accepted and reported in <see cref="Outcome{T}.Warnings"/> (non-blocking warning).
    /// </summary>
    public async Task<Outcome<Stored<Request>>> CreateAsync(Principal? principal, CreateRequestCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var decision = await authorization.CheckAsync(principal, Permission.CreateAndSubmitRequest, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<Request>>.Fail(OperationStatus.NotAuthorized);
        }

        if (command.CustomerId <= 0)
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.CustomerRequired);
        }

        if (!RequestNo.TryCreate(command.RequestNo, out var requestNo, out var requestNoError))
        {
            return Outcome<Stored<Request>>.Invalid(requestNoError == RequestNoError.Empty ? ValidationIssue.RequestNoRequired : ValidationIssue.RequestNoWhitespace);
        }

        if (string.IsNullOrWhiteSpace(command.RequestDateText))
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.RequestDateRequired);
        }

        if (!dates.TryParseDate(command.RequestDateText, out var jalaliDate, out var dateError))
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.RequestDateInvalid, dateError);
        }

        var requestDate = dates.ToGregorian(jalaliDate);
        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            // The customer is read from the database NOW: a list the screen showed earlier, or any cached state, decides nothing (Decision Log #76). Only an active
            // customer may get a new request; a missing or inactive one is refused, and the customer is neither changed nor replaced by another.
            var customer = await customers.FindAsync(command.CustomerId, cancellationToken);
            if (customer is null)
            {
                return Outcome<Stored<Request>>.Invalid(ValidationIssue.CustomerNotFound);
            }

            if (!customer.Value.IsActive)
            {
                return Outcome<Stored<Request>>.Invalid(ValidationIssue.CustomerInactive);
            }

            var request = Request.Create(
                0, customer.Value, requestNo!, requestDate, NullIfBlank(command.Title), NullIfBlank(command.Description), decision.Current!.UserId, now, dates);
            var near = await requests.FindCaseInsensitiveNearDuplicatesAsync(request.CustomerId, request.JalaliRequestYear, requestNo!, cancellationToken);
            var stored = await requests.AddAsync(request, cancellationToken);
            return Outcome<Stored<Request>>.Ok(stored, near.Select(n => n.Value).ToList());
        });
    }

    /// <summary>The one transition of this milestone, CREATED to SENT_TO_MANAGEMENT (Central). Allowed with or without items.</summary>
    public async Task<Outcome<Stored<Request>>> SubmitToManagementAsync(Principal? principal, long requestId, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.CreateAndSubmitRequest, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<Request>>.Fail(OperationStatus.NotAuthorized);
        }

        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () => Outcome<Stored<Request>>.Ok(
                await requests.UpdateAsync(requestId, expectedVersion, r => r.SubmitToManagement(now), cancellationToken)));
        }
        catch (InvalidOperationException)
        {
            return Outcome<Stored<Request>>.Fail(OperationStatus.InvalidState);
        }
    }

    /// <summary>
    /// Adds an item. A MAKE item needs the Drawing permission to define MAKE items or Planning's permission over any item; a REPAIR item needs Planning's. A priority other
    /// than the default "عادی" additionally needs the permission to change the Planning priority (Planning or Management), so Drawing's items start as Normal.
    /// </summary>
    public async Task<Outcome<Stored<Request>>> AddItemAsync(Principal? principal, long requestId, long expectedVersion, AddItemCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var set = await authorization.EvaluateAsync(
            principal,
            [Permission.DefineMakeItems, Permission.DefineRepairItemsAndEditAnyItem, Permission.ChangePlanningPriority],
            cancellationToken);
        if (set.Current is null || !Enum.IsDefined(command.WorkType))
        {
            return set.Current is null
                ? Outcome<Stored<Request>>.Fail(OperationStatus.NotAuthorized)
                : Outcome<Stored<Request>>.Invalid(ValidationIssue.WorkTypeInvalid);
        }

        var mayDefine = command.WorkType == WorkType.Make
            ? set.Has(Permission.DefineMakeItems) || set.Has(Permission.DefineRepairItemsAndEditAnyItem)
            : set.Has(Permission.DefineRepairItemsAndEditAnyItem);
        if (!mayDefine)
        {
            return Outcome<Stored<Request>>.Fail(OperationStatus.NotAuthorized);
        }

        if (!Enum.IsDefined(command.Priority))
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.PriorityInvalid);
        }

        if (command.Priority != Priority.Normal && !set.Has(Permission.ChangePlanningPriority))
        {
            return Outcome<Stored<Request>>.Fail(OperationStatus.NotAuthorized);
        }

        if (string.IsNullOrWhiteSpace(command.ItemNo))
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.ItemNoRequired);
        }

        if (string.IsNullOrWhiteSpace(command.PartName))
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.PartNameRequired);
        }

        if (command.Quantity <= 0)
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.QuantityInvalid);
        }

        var userId = set.Current.UserId;
        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var current = await requests.FindAsync(requestId, cancellationToken);
            if (current is null)
            {
                return Outcome<Stored<Request>>.Fail(OperationStatus.NotFound);
            }

            if (current.Value.Status is RequestStatus.Cancelled or RequestStatus.Closed)
            {
                return Outcome<Stored<Request>>.Invalid(ValidationIssue.RequestClosedForItems);
            }

            if (current.Value.HasItemNo(command.ItemNo))
            {
                return Outcome<Stored<Request>>.Invalid(ValidationIssue.ItemNoDuplicate);
            }

            // The Domain rules run again inside the update (a concurrent change is reported as a conflict by the repository). The item number is stored exactly as typed.
            return Outcome<Stored<Request>>.Ok(await requests.UpdateAsync(
                requestId,
                expectedVersion,
                r => r.AddItem(0, command.ItemNo, command.PartName, command.WorkType, command.Quantity, userId, now, command.Priority),
                cancellationToken));
        });
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
