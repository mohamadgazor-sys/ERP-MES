using System.Collections.ObjectModel;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>One item on the stage-work screen: the display text, the entry it was read from (with its row versions) and the actions the Application says are possible now.</summary>
public sealed record StageWorkRow(QueueRow Display, ProductionQueueEntry Entry, AvailableStageActions Actions);

/// <summary>
/// State and actions of the demonstration stage-work screen: release the first stage, start it, submit it for QC and record whole-quantity QC approval. It talks only to
/// <see cref="StageWorkService"/>, which checks the user's CURRENT permission, the expected row versions and the Domain rules on every action; the buttons only mirror what the
/// Application reported and are enforced again there. A stale view is reported as a conflict and the list is reloaded; nothing is changed by a failed action. It is a snapshot (not
/// live) and it is not the authoritative per-unit operational queue.
/// </summary>
public sealed class StageWorkViewModel(StageWorkService service, DigitStyle digitStyle, Func<Principal> currentUser) : ObservableObject
{
    public const string Heading = "مراحل تولید و کنترل کیفیت (نسخهٔ نمایشی)";

    public const string Note =
        "فقط گذارهای نمایشی: آزادسازی مرحلهٔ اول، شروع، ارسال به کنترل کیفیت و تأیید کنترل کیفیت برای کل تعداد. رد کنترل کیفیت، بازکاری، ضایعات، تأمین بیرونی و ویرایش مسیر وجود ندارد. "
        + "مرحلهٔ بعد فقط پس از تأیید کنترل کیفیت مرحلهٔ قبل در دسترس می‌شود. این صفحه صف عملیاتی رسمی واحدهای تولید نیست و زنده نیست؛ پس از هر کار فهرست دوباره بارگذاری می‌شود. "
        + "شرط‌های مجوز آزادسازی در تولید (وضعیت درخواست، قرارداد و تأیید مدیریت) هنوز تصویب نشده‌اند و در این نسخهٔ نمایشی بررسی نمی‌شوند.";

    public const string LoadingText = "در حال بارگذاری مراحل…";
    public const string EmptyText = "در حال حاضر آیتمی برای نمایش وجود ندارد.";
    public const string DeniedText = "شما اجازهٔ مشاهدهٔ مراحل تولید را ندارید.";
    public const string ConflictText = "این آیتم یا مرحله را کاربر دیگری تغییر داده است؛ کاری انجام نشد. فهرست دوباره بارگذاری شد؛ وضعیت فعلی را ببینید و در صورت نیاز دوباره تلاش کنید.";
    public const string InvalidStateText = "در وضعیت فعلی این مرحله، این کار مجاز نیست؛ کاری انجام نشد. فهرست دوباره بارگذاری شد.";
    public const string NotFoundText = "آیتم یا مرحله دیگر وجود ندارد؛ کاری انجام نشد. فهرست دوباره بارگذاری شد.";
    public const string StaleViewText = "اطلاعات این ردیف کامل نیست؛ کاری انجام نشد. فهرست دوباره بارگذاری شد.";
    public const string NothingSelectedText = "ابتدا یک آیتم را از فهرست انتخاب کنید.";
    public const string ReleasedText = "مرحلهٔ اول آزاد شد و آمادهٔ اجرا است.";
    public const string StartedText = "مرحله شروع شد.";
    public const string SubmittedText = "مرحله برای کنترل کیفیت ارسال شد.";
    public const string ApprovedAllText = "تأیید کنترل کیفیت ثبت شد؛ همهٔ مراحل این آیتم تأیید شده است.";

    private readonly QueueRowFormatter _formatter = new(digitStyle);
    private readonly ObservableCollection<StageWorkRow> _rows = [];
    private StageWorkRow? _selected;
    private string _message = string.Empty;
    private bool _isError;
    private bool _isLoading;
    private bool _isBusy;
    private bool _isDenied;
    private bool _hasError;
    private bool _hasLoaded;

    public ObservableCollection<StageWorkRow> Rows => _rows;

    public StageWorkRow? SelectedRow
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                RaiseActionChanges();
            }
        }
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>True when <see cref="Message"/> reports a refusal or a failure (shown in red); false for a success.</summary>
    public bool IsErrorMessage
    {
        get => _isError;
        private set => SetProperty(ref _isError, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseActionChanges();
            }
        }
    }

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

    public bool IsEmpty => _hasLoaded && !_isDenied && !_hasError && _rows.Count == 0;

    public bool ShowRows => _hasLoaded && !_isDenied && !_hasError && _rows.Count > 0;

    // The buttons mirror the Application's answer for the selected row (permission AND stage state) and are off while an action runs. Every action is checked again by the Application.
    public bool CanRelease => !_isBusy && _selected?.Actions.CanRelease == true;

    public bool CanStart => !_isBusy && _selected?.Actions.CanStart == true;

    public bool CanSubmitForQc => !_isBusy && _selected?.Actions.CanSubmitForQc == true;

    public bool CanApprove => !_isBusy && _selected?.Actions.CanApprove == true;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsLoading = true;
        SetMessage(string.Empty, false);
        try
        {
            await ReloadAsync(cancellationToken);
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

    public Task<bool> ReleaseAsync(CancellationToken cancellationToken = default) => PerformAsync(StageAction.Release, cancellationToken);

    public Task<bool> StartAsync(CancellationToken cancellationToken = default) => PerformAsync(StageAction.Start, cancellationToken);

    public Task<bool> SubmitForQcAsync(CancellationToken cancellationToken = default) => PerformAsync(StageAction.SubmitForQc, cancellationToken);

    public Task<bool> ApproveAsync(CancellationToken cancellationToken = default) => PerformAsync(StageAction.ApproveWholeQuantity, cancellationToken);

    private async Task<bool> PerformAsync(StageAction action, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return false;
        }

        var succeeded = false;
        IsBusy = true;
        try
        {
            var row = _selected;
            if (row?.Entry.CurrentStage is null)
            {
                SetMessage(NothingSelectedText, true);
                return false;
            }

            // The versions are those the row was READ with. If anyone changed the request, the item or a stage since, the Application reports a conflict and changes nothing.
            var outcome = await service.PerformAsync(currentUser(), action, StageTarget.From(row.Entry), cancellationToken);
            if (outcome.Succeeded)
            {
                succeeded = true;
                await ReloadAsync(cancellationToken);
                SetMessage(SuccessText(outcome.Value!), false);
            }
            else
            {
                var text = FailureText(outcome);
                if (outcome.Status != OperationStatus.Failed)
                {
                    // What is true now is shown: another user's change, a changed permission or a vanished record.
                    await ReloadAsync(cancellationToken);
                }

                SetMessage(text, true);
            }
        }
        catch (OperationCanceledException)
        {
            // The screen was closed; nothing to report.
        }
        catch (Exception)
        {
            SetMessage(Messages.Unexpected, true);
        }
        finally
        {
            IsBusy = false;
        }

        return succeeded;
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var keep = _selected?.Entry.ItemId;
        var outcome = await service.LoadAsync(currentUser(), cancellationToken);
        switch (outcome.Status)
        {
            case OperationStatus.Succeeded:
                var position = 0;
                _rows.Clear();
                foreach (var entry in outcome.Value!.Entries)
                {
                    _rows.Add(new StageWorkRow(_formatter.Format(++position, entry), entry, StageWorkService.AvailableFor(outcome.Value.Capabilities, entry)));
                }

                _hasLoaded = true;
                IsDenied = false;
                HasError = false;
                SelectedRow = keep is { } id ? _rows.FirstOrDefault(r => r.Entry.ItemId == id) : null;
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(ShowRows));
                break;

            case OperationStatus.NotAuthorized:
                _rows.Clear();
                _hasLoaded = true;
                HasError = false;
                IsDenied = true;
                SelectedRow = null;
                SetMessage(DeniedText, true);
                break;

            default:
                ShowError(outcome.Status == OperationStatus.ConcurrencyConflict ? ConflictText : outcome.Status == OperationStatus.NotFound ? NotFoundText : Messages.Failed);
                break;
        }
    }

    private void ShowError(string text)
    {
        _rows.Clear();
        _hasLoaded = true;
        IsDenied = false;
        HasError = true;
        SelectedRow = null;
        SetMessage(text, true);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowRows));
    }

    private static string SuccessText(StageActionResult result) => result.Action switch
    {
        StageAction.Release => ReleasedText,
        StageAction.Start => StartedText,
        StageAction.SubmitForQc => SubmittedText,
        _ => result.NextStageNo is { } next ? $"تأیید کنترل کیفیت ثبت شد؛ مرحلهٔ {PersianDigits(next)} اکنون آمادهٔ اجرا است." : ApprovedAllText,
    };

    private static string FailureText(Outcome<StageActionResult> outcome) => outcome.Status switch
    {
        OperationStatus.NotAuthorized => Messages.NotAuthorized,
        OperationStatus.ConcurrencyConflict => ConflictText,
        OperationStatus.InvalidState => InvalidStateText,
        OperationStatus.NotFound => NotFoundText,
        OperationStatus.ValidationFailed => StaleViewText,
        _ => Messages.Failed,
    };

    private static string PersianDigits(int value) => new(value.ToString(System.Globalization.CultureInfo.InvariantCulture).Select(c => (char)('۰' + (c - '0'))).ToArray());

    private void SetMessage(string text, bool isError)
    {
        Message = text;
        IsErrorMessage = isError;
    }

    private void RaiseActionChanges()
    {
        OnPropertyChanged(nameof(CanRelease));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanSubmitForQc));
        OnPropertyChanged(nameof(CanApprove));
    }
}
