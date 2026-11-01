using System.Collections.ObjectModel;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.ViewModels;

public sealed record RequestRow(long RequestId, string RequestNo, string CustomerName, string WorkTypeText, string DateText, string YearText, string Title, string StatusText, int ItemCount);

public sealed record ItemRow(string ItemNo, string PartName, string WorkTypeText, string QuantityText, string PriorityText, long ItemId = 0) : IHasItemId;

public sealed record HistoryRow(string DateText, string ActionText, string StatusChangeText, string DepartmentText, string OutcomeText, string NoteText, string ActorText = "");

public sealed record CustomerChoice(long Id, string Name);

public sealed record Choice<T>(T Value, string Text);

/// <summary>
/// State and actions of the request screen: the list, one request with its items, the entry form of a new request and the entry form of a new item. It talks only to
/// the Application use cases and makes no authorization decision: <see cref="Capabilities"/> (the Application's answer) only enables buttons, and each command is checked
/// again by the Application when it runs. Failures become Persian messages; nothing here throws to the window. Fixed seeded routes and the stage workflow are not part of
/// this screen (demonstration build: items are listed, their routes are not edited).
/// </summary>
public sealed class RequestsViewModel(
    RequestService requests, RequestDocumentService documents, RequestWorkflowService lifecycle, CustomerService customers, IJalaliDateService dates, DigitStyle digitStyle, Func<DateTime> utcNow, Func<Principal> currentUser, UserDirectoryService? users = null) : ObservableObject
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
    private RequestActionCapabilities _actions = RequestActionCapabilities.None;
    private readonly ObservableCollection<HistoryRow> _history = [];

    public ObservableCollection<RequestRow> Requests => _rows;

    public ObservableCollection<ItemRow> Items => _items;

    /// <summary>The workflow history of the selected request, oldest first (Domain and Database Specification 4.4).</summary>
    public ObservableCollection<HistoryRow> History => _history;

    public bool HasHistory => _history.Count > 0;

    public bool HasNoHistory => !HasHistory;

    /// <summary>The controlled Drawing outcomes offered when Drawing returns its result.</summary>
    public IReadOnlyList<Choice<DrawingOutcome>> DrawingOutcomes { get; } =
    [
        new(DrawingOutcome.FullyAccepted, Messages.OutcomeText(DrawingOutcome.FullyAccepted)),
        new(DrawingOutcome.PartiallyAccepted, Messages.OutcomeText(DrawingOutcome.PartiallyAccepted)),
        new(DrawingOutcome.Rejected, Messages.OutcomeText(DrawingOutcome.Rejected)),
        new(DrawingOutcome.MoreInformationNeeded, Messages.OutcomeText(DrawingOutcome.MoreInformationNeeded)),
        new(DrawingOutcome.ReturnedForCorrection, Messages.OutcomeText(DrawingOutcome.ReturnedForCorrection)),
    ];

    // Which lifecycle buttons are offered: the Application's answer about the user (from the database) AND the request being in the status the action needs.
    // The Application checks both again when the action runs.
    public bool CanApprove => _actions.CanDecide && Status == RequestStatus.SentToManagement;

    public bool CanReject => CanApprove;

    public bool CanReopen => _actions.CanReopen && Status == RequestStatus.MgmtRejectedAtPlanning;

    public bool CanForwardToDrawing => _actions.CanForwardToDrawing && Status == RequestStatus.MgmtApprovedAtPlanning;

    public bool CanRecordDrawingResult => _actions.CanRecordDrawingResult && Status == RequestStatus.UnderDrawingReview;

    public bool CanSendBackToDrawing => _actions.CanForwardToDrawing && Status == RequestStatus.DrawingReturnedToPlanning;

    /// <summary>True when any lifecycle action is offered for the selected request (shows the action panel).</summary>
    public bool HasWorkflowActions => CanApprove || CanReopen || CanForwardToDrawing || CanRecordDrawingResult || CanSendBackToDrawing;

    private RequestStatus? Status => _detail?.Value.Status;

    /// <summary>Active customers a new request can be entered for.</summary>
    public ObservableCollection<CustomerChoice> Customers => _customers;

    public IReadOnlyList<Choice<WorkType>> WorkTypes { get; } =
    [
        new(WorkType.Make, Messages.WorkTypeText(WorkType.Make)),
        new(WorkType.Repair, Messages.WorkTypeText(WorkType.Repair)),
    ];

    /// <summary>The three exact priority labels, most urgent first (Decision Log #58).</summary>

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
        : $"درخواست {Messages.Ltr(_detail.Value.RequestNo.Value)} — {_detailCustomer}";

    public string DetailLine => _detail is null
        ? string.Empty
        : $"تاریخ: {dates.FormatDate(_detail.Value.RequestDate)}   |   سال شمسی: {YearText(_detail.Value.JalaliRequestYear)}   |   وضعیت: {Messages.StatusText(_detail.Value.Status)}   |   نوع کار: {Messages.WorkTypeText(_detail.Value.WorkType)}";

    public string DetailTitle => _detail?.Value.Title ?? string.Empty;

    public string DetailDescription => _detail?.Value.Description ?? string.Empty;

    public bool HasNoItems => _detail is not null && _items.Count == 0;

    public bool CanCreateRequest => _capabilities.CanCreateRequest;

    /// <summary>Submitting needs the permission and a request still in CREATED; the Application checks both again.</summary>
    public bool CanSubmit => _capabilities.CanSubmitRequest && _detail?.Value.Status == RequestStatus.Created && HasPdf;

    /// <summary>True when the selected request has its request PDF (Decision Log #87).</summary>
    public bool HasPdf => _detail?.Value.HasRequestPdf == true;

    /// <summary>The PDF of the selected request in Persian, or the reminder that it is missing.</summary>
    public string PdfText => _detail?.Value.CurrentRequestPdf is { } pdf
        ? $"فایل PDF درخواست: {Messages.Ltr(pdf.FileName)} ({SizeText(pdf.ContentLength)})"
        : "فایل PDF درخواست پیوست نشده است؛ پیش از ارسال به مدیریت باید پیوست شود.";

    /// <summary>Attaching (or replacing) the PDF needs the Central permission and a request that has not been submitted yet; the Application checks both again.</summary>
    public bool CanAttachPdf => _capabilities.CanAttachRequestPdf && _detail?.Value.Status == RequestStatus.Created;

    /// <summary>Adding an item needs the permission for the request's one work type (Decision Log #84); the Application checks again.</summary>
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

    /// <summary>Attaches the chosen file as the request PDF. The file is read by the window; the Application validates the name, size and content.</summary>
    public async Task<bool> AttachPdfAsync(string? fileName, byte[]? content, CancellationToken cancellationToken = default)
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
            var outcome = await documents.AttachRequestPdfAsync(currentUser(), id, _detail.Version, fileName, content, cancellationToken);
            if (!outcome.Succeeded)
            {
                await ShowFailureAsync(outcome, cancellationToken, reopen: id);
                return;
            }

            saved = true;
            await ReloadAsync(cancellationToken, keepMessage: true);
            await LoadDetailAsync(id, cancellationToken, keepMessage: true);
            Message = "فایل PDF درخواست پیوست شد.";
        });
        return saved;
    }

    /// <summary>The chosen file is too large even to read; the same message as the Application's size check.</summary>
    public void ReportPdfTooLarge() => Message = Messages.ForIssue(ValidationIssue.PdfTooLarge, JalaliDateError.None);

    /// <summary>The current PDF of the selected request for viewing, or null with a message.</summary>
    public async Task<RequestPdfFile?> LoadPdfAsync(CancellationToken cancellationToken = default)
    {
        RequestPdfFile? file = null;
        await RunAsync(async () =>
        {
            if (_detail is null)
            {
                Message = Messages.NothingSelected;
                return;
            }

            var outcome = await documents.GetRequestPdfAsync(currentUser(), _detail.Value.Id, cancellationToken);
            if (!outcome.Succeeded)
            {
                Message = Messages.For(outcome);
                return;
            }

            file = outcome.Value;
        });
        return file;
    }

    public Task<bool> ApproveAsync(CancellationToken cancellationToken = default) =>
        RunLifecycleAsync((id, version) => lifecycle.ApproveAsync(currentUser(), id, version, cancellationToken), "درخواست تأیید شد و به برنامه‌ریزی می‌رسد.", cancellationToken);

    public Task<bool> RejectAsync(string? reason, CancellationToken cancellationToken = default) =>
        RunLifecycleAsync((id, version) => lifecycle.RejectAsync(currentUser(), id, version, reason, cancellationToken), "درخواست رد شد.", cancellationToken);

    public Task<bool> ReopenAsync(string? reason, CancellationToken cancellationToken = default) =>
        RunLifecycleAsync((id, version) => lifecycle.ReopenAsync(currentUser(), id, version, reason, cancellationToken), "درخواست برای تصمیم جدید مدیریت بازگشایی شد.", cancellationToken);

    public Task<bool> ForwardToDrawingAsync(CancellationToken cancellationToken = default) =>
        RunLifecycleAsync((id, version) => lifecycle.ForwardToDrawingAsync(currentUser(), id, version, cancellationToken), "درخواست به نقشه‌کشی ارسال شد.", cancellationToken);

    public Task<bool> RecordDrawingResultAsync(DrawingOutcome outcome, string? note, CancellationToken cancellationToken = default) =>
        RunLifecycleAsync((id, version) => lifecycle.RecordDrawingResultAsync(currentUser(), id, version, outcome, note, cancellationToken), "نتیجهٔ نقشه‌کشی ثبت شد و به برنامه‌ریزی بازگشت.", cancellationToken);

    public Task<bool> SendBackToDrawingAsync(string? reason, CancellationToken cancellationToken = default) =>
        RunLifecycleAsync((id, version) => lifecycle.SendBackToDrawingAsync(currentUser(), id, version, reason, cancellationToken), "درخواست به نقشه‌کشی بازگردانده شد.", cancellationToken);

    // One path for every lifecycle action: the selected request at the version this screen loaded it at; a failure reloads what is true now.
    private async Task<bool> RunLifecycleAsync(Func<long, long, Task<Outcome<Stored<Request>>>> call, string success, CancellationToken cancellationToken)
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
            var outcome = await call(id, _detail.Version);
            if (!outcome.Succeeded)
            {
                await ShowFailureAsync(outcome, cancellationToken, reopen: id);
                return;
            }

            saved = true;
            await ReloadAsync(cancellationToken, keepMessage: true);
            await LoadDetailAsync(id, cancellationToken, keepMessage: true);
            Message = success;
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
        _actions = await lifecycle.GetCapabilitiesAsync(currentUser(), cancellationToken);
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
        _history.Clear();
        var actors = await ActorLookup.DescribeAsync(users, currentUser(), _detail.Value.WorkflowHistory.Select(e => e.ActionByUserId));
        foreach (var entry in _detail.Value.WorkflowHistory)
        {
            _history.Add(new HistoryRow(
                dates.FormatDateTime(entry.ActionAtUtc),
                Messages.ActionText(entry.Action),
                Messages.StatusText(entry.FromStatus) + " ← " + Messages.StatusText(entry.ToStatus),
                Messages.DepartmentText(entry.ActionByDepartmentCode),
                entry.Outcome is { } drawingOutcome ? Messages.OutcomeText(drawingOutcome) : string.Empty,
                entry.Note ?? string.Empty,
                ActorLookup.Text(actors, entry.ActionByUserId)));
        }

        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HasNoHistory));
        foreach (var item in _detail.Value.Items)
        {
            _items.Add(new ItemRow(item.ItemNo, item.PartName, Messages.WorkTypeText(_detail.Value.WorkType), QuantityDisplay(item.RequestedQuantity), item.Priority.PersianName(), item.Id));
        }

        SelectedRequest = _rows.FirstOrDefault(r => r.RequestId == requestId) ?? _selected;
        RaiseDetailChanges();
    }

    private void ClearDetail()
    {
        _detail = null;
        _detailCustomer = string.Empty;
        _items.Clear();
        _history.Clear();
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HasNoHistory));
        RaiseDetailChanges();
    }

    private void RaiseDetailChanges()
    {
        foreach (var name in new[]
                 {
                     nameof(HasDetail), nameof(DetailHeading), nameof(DetailLine), nameof(DetailTitle), nameof(DetailDescription), nameof(HasNoItems), nameof(CanSubmit), nameof(HasPdf), nameof(PdfText), nameof(CanAttachPdf), nameof(CanApprove), nameof(CanReject), nameof(CanReopen), nameof(CanForwardToDrawing), nameof(CanRecordDrawingResult), nameof(CanSendBackToDrawing), nameof(HasWorkflowActions),
                 })
        {
            OnPropertyChanged(name);
        }
    }

    private void RaiseCapabilityChanges()
    {
        foreach (var name in new[] { nameof(CanCreateRequest), nameof(CanSubmit), nameof(CanSetPriority), nameof(CanAttachPdf), nameof(CanApprove), nameof(CanReject), nameof(CanReopen), nameof(CanForwardToDrawing), nameof(CanRecordDrawingResult), nameof(CanSendBackToDrawing), nameof(HasWorkflowActions) })
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

    private static string SizeText(long bytes) =>
        bytes >= 1024 * 1024
            ? (bytes / (1024.0 * 1024.0)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " مگابایت"
            : Math.Max(1, (bytes + 1023) / 1024).ToString(System.Globalization.CultureInfo.InvariantCulture) + " کیلوبایت";
}
