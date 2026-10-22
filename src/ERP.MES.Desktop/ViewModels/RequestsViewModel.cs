using System.Collections.ObjectModel;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.ViewModels;

public sealed record RequestRow(long RequestId, string RequestNo, string CustomerName, string WorkTypeText, string DateText, string YearText, string Title, string StatusText, int ItemCount);

public sealed record ItemRow(string ItemNo, string PartName, string WorkTypeText, string QuantityText, string PriorityText);

public sealed record CustomerChoice(long Id, string Name);

public sealed record Choice<T>(T Value, string Text);

/// <summary>
/// State and actions of the request screen: the list, one request with its items, the entry form of a new request and the entry form of a new item. It talks only to
/// the Application use cases and makes no authorization decision: <see cref="Capabilities"/> (the Application's answer) only enables buttons, and each command is checked
/// again by the Application when it runs. Failures become Persian messages; nothing here throws to the window. Fixed seeded routes and the stage workflow are not part of
/// this screen (demonstration build: items are listed, their routes are not edited).
/// </summary>
public sealed class RequestsViewModel(
    RequestService requests, CustomerService customers, IJalaliDateService dates, DigitStyle digitStyle, Func<DateTime> utcNow, Func<Principal> currentUser) : ObservableObject
{
    private readonly ObservableCollection<RequestRow> _rows = [];
    private readonly ObservableCollection<ItemRow> _items = [];
    private readonly ObservableCollection<CustomerChoice> _customers = [];
    private RequestRow? _selected;
    private Stored<Request>? _detail;
    private string _detailCustomer = string.Empty;
    private string _message = string.Empty;
    private bool _isBusy;
    private bool _isLoading;
    private bool _hasLoaded;
    private Capabilities _capabilities = Capabilities.None;

    public ObservableCollection<RequestRow> Requests => _rows;

    public ObservableCollection<ItemRow> Items => _items;

    /// <summary>Active customers a new request can be entered for.</summary>
    public ObservableCollection<CustomerChoice> Customers => _customers;

    public IReadOnlyList<Choice<WorkType>> WorkTypes { get; } =
    [
        new(WorkType.Make, Messages.WorkTypeText(WorkType.Make)),
        new(WorkType.Repair, Messages.WorkTypeText(WorkType.Repair)),
    ];

    /// <summary>The three exact priority labels, most urgent first (Decision Log #58).</summary>
    public IReadOnlyList<Choice<Priority>> Priorities { get; } = PriorityInfo.All.Select(p => new Choice<Priority>(p, p.PersianName())).ToList();

    public RequestRow? SelectedRequest
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsEmpty => _hasLoaded && _rows.Count == 0;

    public bool HasDetail => _detail is not null;

    public string DetailHeading => _detail is null
        ? string.Empty
        : $"درخواست {_detail.Value.RequestNo.Value} — {_detailCustomer}";

    public string DetailLine => _detail is null
        ? string.Empty
        : $"تاریخ: {dates.FormatDate(_detail.Value.RequestDate)}   |   سال شمسی: {YearText(_detail.Value.JalaliRequestYear)}   |   وضعیت: {Messages.StatusText(_detail.Value.Status)}   |   نوع کار: {Messages.WorkTypeText(_detail.Value.WorkType)}";

    public string DetailTitle => _detail?.Value.Title ?? string.Empty;

    public string DetailDescription => _detail?.Value.Description ?? string.Empty;

    public bool HasNoItems => _detail is not null && _items.Count == 0;

    public bool CanCreateRequest => _capabilities.CanCreateRequest;

    /// <summary>Submitting needs the permission and a request still in CREATED; the Application checks both again.</summary>
    public bool CanSubmit => _capabilities.CanSubmitRequest && _detail?.Value.Status == RequestStatus.Created;

    /// <summary>Adding an item needs the permission for the request's one work type (Decision Log #84); the Application checks again.</summary>
    public bool CanAddItem => _detail is not null
        && (_detail.Value.WorkType == WorkType.Make ? _capabilities.CanAddMakeItem : _capabilities.CanAddRepairItem);

    public bool CanSetPriority => _capabilities.CanSetPriority;

    /// <summary>Today's date as Jalali text in the screen's digit shape, to prefill the request date.</summary>
    public string TodayText => dates.FormatDateTime(utcNow()).Split(' ')[0];

    public async Task LoadAsync(CancellationToken cancellationToken = default) =>
        await RunAsync(() => ReloadAsync(cancellationToken), showLoading: true);

    /// <summary>Opens a request from the list (null clears the detail).</summary>
    public async Task SelectAsync(RequestRow? row, CancellationToken cancellationToken = default)
    {
        if (row is null)
        {
            ClearDetail();
            return;
        }

        if (_detail is not null && _detail.Value.Id == row.RequestId)
        {
            return;
        }

        await RunAsync(() => LoadDetailAsync(row.RequestId, cancellationToken));
    }

    public async Task<bool> CreateAsync(long customerId, string? requestNo, WorkType workType, string? dateText, string? title, string? description, CancellationToken cancellationToken = default)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            var outcome = await requests.CreateAsync(currentUser(), new CreateRequestCommand(customerId, requestNo, workType, dateText, title, description), cancellationToken);
            if (!outcome.Succeeded)
            {
                await ShowFailureAsync(outcome, cancellationToken);
                return;
            }

            saved = true;
            var created = outcome.Value!.Value;
            var text = "درخواست ثبت شد.";
            if (outcome.WarningList.Count > 0)
            {
                text += " هشدار: برای همین مشتری و سال، شمارهٔ مشابهی وجود دارد که فقط در بزرگی/کوچکی حروف فرق دارد: " + string.Join("، ", outcome.WarningList);
            }

            await ReloadAsync(cancellationToken, keepMessage: true);
            await LoadDetailAsync(created.Id, cancellationToken, keepMessage: true);
            Message = text;
        });
        return saved;
    }

    public async Task<bool> SubmitAsync(CancellationToken cancellationToken = default)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            if (_detail is null)
            {
                Message = Messages.NothingSelected;
                return;
            }

            var id = _detail.Value.Id;
            var outcome = await requests.SubmitToManagementAsync(currentUser(), id, _detail.Version, cancellationToken);
            if (!outcome.Succeeded)
            {
                await ShowFailureAsync(outcome, cancellationToken, reopen: id);
                return;
            }

            saved = true;
            await ReloadAsync(cancellationToken, keepMessage: true);
            await LoadDetailAsync(id, cancellationToken, keepMessage: true);
            Message = "درخواست به مدیریت ارسال شد.";
        });
        return saved;
    }

    public async Task<bool> AddItemAsync(string? itemNo, string? partName, string? quantityText, Priority priority, CancellationToken cancellationToken = default)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            if (_detail is null)
            {
                Message = Messages.NothingSelected;
                return;
            }

            if (!QuantityText.TryParse(quantityText, out var quantity))
            {
                Message = Messages.ForIssue(ValidationIssue.QuantityInvalid, JalaliDateError.None);
                return;
            }

            var id = _detail.Value.Id;
            var outcome = await requests.AddItemAsync(
                currentUser(), id, _detail.Version, new AddItemCommand(itemNo, partName, quantity, priority), cancellationToken);
            if (!outcome.Succeeded)
            {
                await ShowFailureAsync(outcome, cancellationToken, reopen: id);
                return;
            }

            saved = true;
            await ReloadAsync(cancellationToken, keepMessage: true);
            await LoadDetailAsync(id, cancellationToken, keepMessage: true);
            Message = "آیتم ثبت شد.";
        });
        return saved;
    }

    private async Task ShowFailureAsync<T>(Outcome<T> outcome, CancellationToken cancellationToken, long? reopen = null)
    {
        var text = Messages.For(outcome);
        var customerChanged = outcome.Issue is ValidationIssue.CustomerInactive or ValidationIssue.CustomerNotFound;
        if (customerChanged || outcome.Status is OperationStatus.ConcurrencyConflict or OperationStatus.NotFound)
        {
            // After a conflict, a vanished record or a customer that is no longer usable the lists are reloaded, so the screen shows what is true now.
            await ReloadAsync(cancellationToken, keepMessage: true);
            if (reopen is { } id && outcome.Status == OperationStatus.ConcurrencyConflict)
            {
                await LoadDetailAsync(id, cancellationToken, keepMessage: true);
            }
        }

        Message = text;
    }

    private async Task ReloadAsync(CancellationToken cancellationToken, bool keepMessage = false)
    {
        if (!keepMessage)
        {
            Message = string.Empty;
        }

        _capabilities = await requests.GetCapabilitiesAsync(currentUser(), cancellationToken);
        RaiseCapabilityChanges();

        var list = await requests.ListAsync(currentUser(), customerId: null, cancellationToken);
        if (!list.Succeeded)
        {
            _rows.Clear();
            _hasLoaded = false;
            OnPropertyChanged(nameof(IsEmpty));
            Message = Messages.For(list);
            return;
        }

        var keep = _detail?.Value.Id;
        _rows.Clear();
        foreach (var r in list.Value!)
        {
            _rows.Add(new RequestRow(
                r.RequestId, r.RequestNo.Value, r.CustomerName, Messages.WorkTypeText(r.WorkType), dates.FormatDate(r.RequestDate), YearText(r.JalaliRequestYear), r.Title ?? string.Empty, Messages.StatusText(r.Status), r.ItemCount));
        }

        SelectedRequest = _rows.FirstOrDefault(r => r.RequestId == keep);
        _hasLoaded = true;
        OnPropertyChanged(nameof(IsEmpty));

        var customerList = await customers.ListAsync(currentUser(), activeOnly: true, cancellationToken);
        if (customerList.Succeeded)
        {
            _customers.Clear();
            foreach (var c in customerList.Value!)
            {
                _customers.Add(new CustomerChoice(c.Value.Id, c.Value.Name));
            }
        }
    }

    private async Task LoadDetailAsync(long requestId, CancellationToken cancellationToken, bool keepMessage = false)
    {
        if (!keepMessage)
        {
            Message = string.Empty;
        }

        var outcome = await requests.GetAsync(currentUser(), requestId, cancellationToken);
        if (!outcome.Succeeded)
        {
            ClearDetail();
            Message = Messages.For(outcome);
            return;
        }

        _detail = outcome.Value!.Request;
        _detailCustomer = outcome.Value.CustomerName;
        _items.Clear();
        foreach (var item in _detail.Value.Items)
        {
            _items.Add(new ItemRow(item.ItemNo, item.PartName, Messages.WorkTypeText(_detail.Value.WorkType), QuantityDisplay(item.RequestedQuantity), item.Priority.PersianName()));
        }

        SelectedRequest = _rows.FirstOrDefault(r => r.RequestId == requestId) ?? _selected;
        RaiseDetailChanges();
    }

    private void ClearDetail()
    {
        _detail = null;
        _detailCustomer = string.Empty;
        _items.Clear();
        RaiseDetailChanges();
    }

    private void RaiseDetailChanges()
    {
        foreach (var name in new[]
                 {
                     nameof(HasDetail), nameof(DetailHeading), nameof(DetailLine), nameof(DetailTitle), nameof(DetailDescription), nameof(HasNoItems), nameof(CanSubmit), nameof(CanAddItem),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    private void RaiseCapabilityChanges()
    {
        foreach (var name in new[] { nameof(CanCreateRequest), nameof(CanSubmit), nameof(CanAddItem), nameof(CanSetPriority) })
        {
            OnPropertyChanged(name);
        }
    }

    // Quantities are exact decimals; they are shown as stored (no rounding), in the digit shape of the screen.
    // Display only: the stored text can carry trailing zeros ("10.0"); they are not shown. The value itself is never changed or rounded.
    private string QuantityDisplay(decimal value) => Digits(value.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture));

    private string YearText(int year) => Digits(year.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private string Digits(string text) =>
        digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('\u06F0' + (c - '0')) : c).ToArray()) : text;

    private async Task RunAsync(Func<Task> action, bool showLoading = false)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsLoading = showLoading;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // The screen was closed; nothing to report.
        }
        catch (Exception)
        {
            Message = Messages.Unexpected;
        }
        finally
        {
            IsLoading = false;
            IsBusy = false;
        }
    }
}
