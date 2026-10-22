using System.Collections.ObjectModel;
using System.Globalization;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>One row of the production queue as shown: every value is already text in the screen's digit shape. Nothing here is a completion date.</summary>
public sealed record QueueRow(
    string Position,
    string RequestNo,
    string CustomerName,
    string RequestStatusText,
    string ItemNo,
    string PartName,
    string WorkTypeText,
    string QuantityText,
    string PriorityText,
    string CurrentStageText,
    string StageStatusText,
    string QcStatusText,
    string StageEstimateText,
    string TotalEstimateText);

/// <summary>
/// State of the read-only production overview (Management and Planning). It asks the Application (<see cref="ProductionQueueService"/>), which checks the user's CURRENT permission; this
/// class decides nothing about access. Loading, empty, permission-denied, conflict and database-error states are explicit, in Persian. The screen has no write action:
/// no stage change, QC action, priority change or queue-order override exists here (those need decisions that are still open).
/// </summary>
public sealed class ProductionQueueViewModel(ProductionQueueService queue, IJalaliDateService dates, DigitStyle digitStyle, Func<DateTime> utcNow, Func<Principal> currentUser) : ObservableObject
{
    public const string Heading = "نمای کلی تولید (فقط‌خواندنی)";

    public const string OverviewNote =
        "این صفحه یک نمای کلی فقط‌خواندنی از آیتم‌ها است و صف عملیاتی رسمی واحدهای تولید نیست. هیچ‌چیز در این صفحه تغییر نمی‌کند: تغییر مرحله، کنترل کیفیت، تغییر اولویت و بازنویسی ترتیب در این نسخه وجود ندارد. "
        + "اطلاعات در لحظهٔ بارگذاری خوانده می‌شود و زنده نیست؛ برای دیدن تغییرات دیگران «بارگذاری دوباره» را بزنید.";

    public const string OrderNote =
        "ترتیب نمایش موقت است و ترتیب عملیاتی رسمی صف نیست: اولویت برنامه‌ریزی (خیلی فوری، فوری، عادی)، سپس تاریخ درخواست (قدیمی‌تر اول)، سپس شناسهٔ درخواست و شناسهٔ آیتم. "
        + "ترتیب نهایی صف هر واحد تولید (در حال اجرا، سپس توقف/مشکل‌دار، سپس در انتظار؛ بعد اولویت و موقعیت برنامه‌ریزی، سپس پایان برنامه‌ریزی‌شده) و قاعدهٔ بازنویسی مدیریت تصویب شده‌اند ولی هنوز پیاده‌سازی نشده‌اند و در این فهرست اعمال نمی‌شوند.";

    public const string EstimateNote =
        "«مدت پردازش تخمینی» برابر است با دقیقه به ازای هر قطعه × تعداد برنامه‌ریزی‌شده و فقط حجم کار را نشان می‌دهد؛ تاریخ تکمیل نیست و تقویم کاری، صف، ظرفیت ماشین‌ها و توقف‌ها در آن لحاظ نشده است.";

    public const string EmptyText = "در حال حاضر آیتمی برای نمایش در صف وجود ندارد.";
    public const string LoadingText = "در حال بارگذاری نمای کلی تولید…";
    public const string DeniedText = "شما اجازهٔ مشاهدهٔ نمای کلی تولید را ندارید. این صفحه فقط برای مدیریت و برنامه‌ریزی است.";
    public const string ConflictText = "اطلاعات هنگام خواندن توسط کاربر دیگری تغییر کرد. «بارگذاری دوباره» را بزنید.";
    public const string NotFoundText = "بخشی از اطلاعات صف دیگر وجود ندارد. «بارگذاری دوباره» را بزنید.";
    public const string NotAvailableText = "—";

    private readonly ObservableCollection<QueueRow> _rows = [];
    private string _message = string.Empty;
    private bool _isLoading;
    private bool _isBusy;
    private bool _isDenied;
    private bool _hasLoaded;
    private bool _hasError;
    private string _loadedAtText = string.Empty;

    public ObservableCollection<QueueRow> Rows => _rows;

    /// <summary>The text of the failure, denial or conflict; empty when the last load worked.</summary>
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>The Application refused the current user. The list is empty and stays hidden.</summary>
    public bool IsDenied
    {
        get => _isDenied;
        private set
        {
            if (SetProperty(ref _isDenied, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(ShowRows));
            }
        }
    }

    /// <summary>The last load failed for a reason other than permission (database error, conflict, vanished record).</summary>
    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (SetProperty(ref _hasError, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(ShowRows));
            }
        }
    }

    /// <summary>The queue was read successfully and contains no item.</summary>
    public bool IsEmpty => _hasLoaded && !_isDenied && !_hasError && _rows.Count == 0;

    public bool ShowRows => _hasLoaded && !_isDenied && !_hasError && _rows.Count > 0;

    /// <summary>When the queue was read (Jalali date and time); empty until a successful load. The data is a snapshot.</summary>
    public string LoadedAtText
    {
        get => _loadedAtText;
        private set => SetProperty(ref _loadedAtText, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsLoading = true;
        Message = string.Empty;
        try
        {
            var outcome = await queue.LoadAsync(currentUser(), cancellationToken);
            Apply(outcome);
        }
        catch (OperationCanceledException)
        {
            // The screen was closed; nothing to report.
        }
        catch (Exception)
        {
            ShowError(Messages.Unexpected);
        }
        finally
        {
            IsLoading = false;
            IsBusy = false;
        }
    }

    private void Apply(Outcome<ProductionQueue> outcome)
    {
        switch (outcome.Status)
        {
            case OperationStatus.Succeeded:
                _rows.Clear();
                var position = 0;
                foreach (var e in outcome.Value!.Entries)
                {
                    _rows.Add(ToRow(++position, e));
                }

                _hasLoaded = true;
                IsDenied = false;
                HasError = false;
                Message = string.Empty;
                LoadedAtText = "زمان بارگذاری: " + dates.FormatDateTime(utcNow());
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(ShowRows));
                break;

            case OperationStatus.NotAuthorized:
                _rows.Clear();
                _hasLoaded = true;
                HasError = false;
                IsDenied = true;
                Message = DeniedText;
                LoadedAtText = string.Empty;
                break;

            case OperationStatus.ConcurrencyConflict:
                ShowError(ConflictText);
                break;

            case OperationStatus.NotFound:
                ShowError(NotFoundText);
                break;

            default:
                ShowError(Messages.Failed);
                break;
        }
    }

    private void ShowError(string text)
    {
        _rows.Clear();
        _hasLoaded = true;
        IsDenied = false;
        HasError = true;
        Message = text;
        LoadedAtText = string.Empty;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowRows));
    }

    private QueueRow ToRow(int position, ProductionQueueEntry e) => new(
        Digits(position.ToString(CultureInfo.InvariantCulture)),
        e.RequestNo,
        e.CustomerName,
        Messages.StatusText(e.RequestStatus),
        e.ItemNo,
        e.PartName,
        Messages.WorkTypeText(e.WorkType),
        Number(e.RequestedQuantity),
        e.Priority.PersianName(),
        CurrentStageText(e),
        e.CurrentStage is null ? NotAvailableText : StageStatusText(e.CurrentStage.Status),
        QcStatusText(e),
        e.CurrentStage is null ? NotAvailableText : Number(e.CurrentStage.EstimatedMinutes),
        e.TotalEstimatedMinutes is null ? NotAvailableText : Number(e.TotalEstimatedMinutes.Value));

    private string CurrentStageText(ProductionQueueEntry e) => e.RouteState switch
    {
        QueueRouteState.NoRoute => "مسیر تعریف نشده",
        QueueRouteState.Completed => "همهٔ مراحل تکمیل شد",
        _ => $"مرحلهٔ {Digits(e.CurrentStage!.StageNo.ToString(CultureInfo.InvariantCulture))} از {Digits(e.StageCount.ToString(CultureInfo.InvariantCulture))}: {e.CurrentStage.Title}",
    };

    /// <summary>The QC position of the item: a stage waiting for QC, the whole route approved, or how many stages QC has approved so far.</summary>
    private string QcStatusText(ProductionQueueEntry e)
    {
        if (e.RouteState == QueueRouteState.NoRoute)
        {
            return NotAvailableText;
        }

        if (e.RouteState == QueueRouteState.Completed)
        {
            return "همهٔ مراحل توسط کنترل کیفیت تأیید شده است";
        }

        var approved = $"{Digits(e.ApprovedStageCount.ToString(CultureInfo.InvariantCulture))} از {Digits(e.StageCount.ToString(CultureInfo.InvariantCulture))} مرحله تأیید شده";
        return e.CurrentStage!.Status == ItemStageStatus.WaitingQc ? "در انتظار تأیید کنترل کیفیت؛ " + approved : approved;
    }

    public static string StageStatusText(ItemStageStatus status) => status switch
    {
        ItemStageStatus.Planned => "برنامه‌ریزی‌شده (هنوز آزاد نشده)",
        ItemStageStatus.WaitingPreviousQc => "در انتظار تأیید کنترل کیفیت مرحلهٔ قبل",
        ItemStageStatus.Waiting => "آمادهٔ اجرا",
        ItemStageStatus.InProgress => "در حال اجرا",
        ItemStageStatus.WaitingQc => "در انتظار کنترل کیفیت",
        ItemStageStatus.QcApproved => "تأییدشده توسط کنترل کیفیت",
        _ => string.Empty,
    };

    // Exact decimals as stored (no rounding), trailing zeros not shown, in the digit shape of the screen.
    private string Number(decimal value) => Digits(value.ToString("0.############################", CultureInfo.InvariantCulture));

    private string Digits(string text) =>
        digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : text;
}
