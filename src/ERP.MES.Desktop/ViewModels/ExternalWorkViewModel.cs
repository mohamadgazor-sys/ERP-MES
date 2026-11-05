using System.Collections.ObjectModel;
using System.Globalization;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.ViewModels;

public sealed record ExternalWorkRow(
    ExternalWorkEntry Entry, string RequestText, string ItemText, string StageText, string UnitText, string StatusText, string QuantitiesText, string OpenText, bool Overdue);

/// <summary>One row of a stage's shipment history.</summary>
public sealed record ExternalShipmentRow(ExternalShipment Shipment, string KindText, string WhenText, string QuantityText, string UserText, string NoteText, string StateText);

/// <summary>An open dispatch the Warehouse can record a return from.</summary>
public sealed record OpenDispatchChoice(long DispatchId, string Text);

/// <summary>
/// The external-work screen (Master Specification §16.7, Decision Log #129): one view of the external stages with their status and quantities (dispatched, returned, at the contractor, still
/// to send), open dispatches with the expected return date (lead time in calendar days) and an overdue flag, and the full history of each stage. The Warehouse records dispatches and returns
/// and cancels a mistaken one with a reason; Planning, Management, the Production Manager and QC read. The Application checks every rule; this screen only relays typed values.
/// </summary>
public sealed class ExternalWorkViewModel(ExternalWorkService service, IJalaliDateService dates, DigitStyle digitStyle, Func<Principal> currentUser, UserDirectoryService? users = null) : ObservableObject
{
    public const string Heading = "کار بیرونی (ارسال و برگشت)";

    public const string Note =
        "مرحله‌های بیرونی (آبکاری، عملیات حرارتی، ریخته‌گری و دیگر مسیرهای بیرونی) را انبار ارسال و دریافت می‌کند. هر ارسال و هر برگشت جداگانه با مقدار، زمان و کاربر ثبت می‌شود؛ ارسال و برگشت جزئی مجاز است و برگشت هرگز از ماندهٔ همان ارسال بیشتر نیست. "
        + "آزاد شدن مرحله به معنای ارسال نیست؛ تا انبار ارسال را ثبت نکند مرحله «در انتظار ارسال» است. واحدهای برگشتی به کنترل کیفیت می‌روند و مرحله فقط وقتی کامل می‌شود که همه برگشته و کنترل کیفیت درباره‌شان تصمیم گرفته باشد. "
        + "زمان تحویل به روز تقویمی است؛ ارسالی که از آن گذشته «دیرکرد» نشان داده می‌شود. ثبت اشتباه با دلیل لغو می‌شود و در سابقه می‌ماند.";

    private readonly ObservableCollection<ExternalWorkRow> _rows = [];
    private readonly ObservableCollection<ExternalShipmentRow> _history = [];
    private readonly ObservableCollection<OpenDispatchChoice> _openDispatches = [];
    private ExternalWorkView? _view;
    private ExternalWorkRow? _selected;
    private IReadOnlyDictionary<long, UserDirectoryEntry> _actors = new Dictionary<long, UserDirectoryEntry>();
    private string _message = string.Empty;
    private bool _isError;
    private bool _isBusy;
    private bool _denied;
    private bool _includeCompleted;

    public ObservableCollection<ExternalWorkRow> Rows => _rows;

    /// <summary>The shipment history of the selected stage, oldest first.</summary>
    public ObservableCollection<ExternalShipmentRow> History => _history;

    /// <summary>The open dispatches of the selected stage (a return names one of them).</summary>
    public ObservableCollection<OpenDispatchChoice> OpenDispatches => _openDispatches;

    public bool CanRecord => _view?.CanRecord ?? false;

    public ExternalWorkRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                RebuildSelection();
            }
        }
    }

    public bool IncludeCompleted
    {
        get => _includeCompleted;
        set => SetProperty(ref _includeCompleted, value);
    }

    public bool IsDenied
    {
        get => _denied;
        private set => SetProperty(ref _denied, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool IsErrorMessage
    {
        get => _isError;
        private set => SetProperty(ref _isError, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public static string StatusText(ItemStageStatus status) => status switch
    {
        ItemStageStatus.WaitingExternalSend => "در انتظار ارسال توسط انبار",
        ItemStageStatus.ExternalSent => "ارسال‌شده، در انتظار برگشت",
        ItemStageStatus.WaitingQc => "برگشته، در انتظار کنترل کیفیت",
        ItemStageStatus.WaitingShortfallDecision => "در انتظار تصمیم دربارهٔ کسری",
        ItemStageStatus.QcApproved => "تکمیل (تأیید کنترل کیفیت)",
        ItemStageStatus.Planned or ItemStageStatus.WaitingPreviousQc => "هنوز آزاد نشده",
        _ => ProductionQueueViewModel.StageStatusText(status),
    };

    public Task LoadAsync(CancellationToken cancellationToken = default) => RunAsync(() => ReloadAsync(cancellationToken));

    /// <summary>The Warehouse sends <paramref name="quantityText"/> base units of the selected stage.</summary>
    public Task DispatchAsync(string? quantityText, string? pieceDescription, string? note) => CommandAsync("ارسال ثبت شد.", async token =>
    {
        if (_selected is not { } row || !TryUnits(quantityText, out var units))
        {
            return Outcome<int>.Invalid(ValidationIssue.ExternalQuantityInvalid);
        }

        return Relay(await service.DispatchAsync(currentUser(), row.Entry.ToStageTarget(), units, pieceDescription, note, token));
    });

    /// <summary>The Warehouse records <paramref name="quantityText"/> base units back from the chosen open dispatch; they go to QC.</summary>
    public Task ReturnAsync(OpenDispatchChoice? dispatch, string? quantityText, string? note) => CommandAsync("برگشت ثبت شد و واحدها به کنترل کیفیت رفتند.", async token =>
    {
        if (_selected is not { } row || dispatch is null || !TryUnits(quantityText, out var units))
        {
            return Outcome<int>.Invalid(ValidationIssue.ExternalQuantityInvalid);
        }

        return Relay(await service.ReturnAsync(currentUser(), row.Entry.ToStageTarget(), dispatch.DispatchId, units, note, token));
    });

    /// <summary>The Warehouse cancels a mistaken dispatch or return of the selected stage with a reason; the row stays in the history.</summary>
    public Task CancelAsync(ExternalShipmentRow? shipment, string? reason) => CommandAsync("ثبت لغو شد و در سابقه ماند.", async token =>
    {
        if (_selected is not { } row || shipment is null)
        {
            return Outcome<int>.Invalid(ValidationIssue.ExternalQuantityInvalid);
        }

        return Relay(await service.CancelAsync(currentUser(), row.Entry.ToStageTarget(), shipment.Shipment.Id, reason, token));
    });

    private static Outcome<int> Relay<T>(Outcome<T> outcome) => outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var selectedKey = _selected is { } s ? (s.Entry.ItemId, s.Entry.StageNo) : default;
        var outcome = await service.LoadAsync(currentUser(), _includeCompleted, cancellationToken);
        _rows.Clear();
        if (!outcome.Succeeded)
        {
            _view = null;
            IsDenied = outcome.Status == OperationStatus.NotAuthorized;
            Fail(IsDenied ? "شما اجازهٔ مشاهدهٔ کار بیرونی را ندارید." : Messages.For(outcome));
            OnPropertyChanged(nameof(CanRecord));
            Selected = null;
            return;
        }

        IsDenied = false;
        _view = outcome.Value!;
        OnPropertyChanged(nameof(CanRecord));
        if (users is not null)
        {
            var ids = _view.Entries.SelectMany(e => e.History).SelectMany(h => new[] { (long?)h.RecordedByUserId, h.CancelledByUserId }).Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
            var lookup = await users.LookupAsync(currentUser(), ids, cancellationToken);
            _actors = lookup.Succeeded ? lookup.Value! : new Dictionary<long, UserDirectoryEntry>();
        }

        foreach (var e in _view.Entries)
        {
            var overdue = e.OpenDispatches.Any(d => d.Overdue);
            _rows.Add(new ExternalWorkRow(
                e,
                e.RequestNo + " — " + e.CustomerName,
                e.ItemNo + " " + e.PartName,
                $"مرحلهٔ {Digits(e.StageNo)} از {Digits(e.StageCount)}: {e.StageTitle}",
                e.UnitName + " — " + PlanningMasterViewModel.CategoryText(e.UnitCategory),
                StatusText(e.Status) + (overdue ? " — دیرکرد" : string.Empty),
                $"ورودی {Digits(e.InputBaseUnits)}، ارسال {Digits(e.DispatchedBaseUnits)}، برگشت {Digits(e.ReturnedBaseUnits)}، نزد پیمانکار {Digits(e.AtContractorBaseUnits)}، مانده برای ارسال {Digits(e.StillToSendBaseUnits)}، در انتظار کنترل کیفیت {Digits(e.AwaitingQcBaseUnits)}، پذیرفته {Digits(e.AcceptedBaseUnits)}، ضایعات {Digits(e.ScrapBaseUnits)}",
                string.Join("؛ ", e.OpenDispatches.Select(d => $"ارسال {Digits(d.Dispatch.Id)}: مانده {Digits(d.OutstandingBaseUnits)}، برگشت مورد انتظار {dates.FormatDateTime(d.ExpectedReturnUtc)}" + (d.Overdue ? " (دیرکرد)" : string.Empty))),
                overdue));
        }

        Selected = _rows.FirstOrDefault(r => (r.Entry.ItemId, r.Entry.StageNo) == selectedKey) ?? _rows.FirstOrDefault();
    }

    private void RebuildSelection()
    {
        _history.Clear();
        _openDispatches.Clear();
        if (_selected is not { } row)
        {
            return;
        }

        foreach (var h in row.Entry.History)
        {
            var who = UserDirectoryService.Describe(_actors.GetValueOrDefault(h.RecordedByUserId));
            _history.Add(new ExternalShipmentRow(
                h,
                h.Kind == ExternalShipmentKind.Dispatch ? $"ارسال {Digits(h.Id)}" : $"برگشت {Digits(h.Id)} از ارسال {Digits(h.DispatchId ?? 0)}",
                dates.FormatDateTime(h.RecordedAtUtc),
                Digits(h.BaseUnits) + " واحد پایه",
                who,
                string.Join(" — ", new[] { h.PieceDescription, h.Note }.Where(t => !string.IsNullOrEmpty(t))),
                h.IsActive ? "معتبر" : $"لغوشده ({dates.FormatDateTime(h.CancelledAtUtc!.Value)}، {UserDirectoryService.Describe(_actors.GetValueOrDefault(h.CancelledByUserId ?? 0))}): {h.CancelReason}"));
        }

        foreach (var d in row.Entry.OpenDispatches)
        {
            _openDispatches.Add(new OpenDispatchChoice(d.Dispatch.Id, $"ارسال {Digits(d.Dispatch.Id)} — مانده {Digits(d.OutstandingBaseUnits)}"));
        }
    }

    private async Task CommandAsync(string success, Func<CancellationToken, Task<Outcome<int>>> command)
    {
        if (IsBusy)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var outcome = await command(CancellationToken.None);
            if (outcome.Succeeded)
            {
                Succeed(success);
            }
            else
            {
                Fail(outcome.Status == OperationStatus.ConcurrencyConflict ? "این مورد را کاربر دیگری تغییر داده است؛ کاری انجام نشد. فهرست دوباره بارگذاری شد." : Messages.For(outcome));
            }

            var message = Message;
            var error = IsErrorMessage;
            await ReloadAsync(CancellationToken.None);
            Message = message;
            IsErrorMessage = error;
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            Fail(Messages.Unexpected);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Succeed(string text)
    {
        Message = text;
        IsErrorMessage = false;
    }

    private void Fail(string text)
    {
        Message = text;
        IsErrorMessage = true;
    }

    private static bool TryUnits(string? text, out long units)
    {
        var ascii = new string((text ?? string.Empty).Trim().Select(c => c switch { >= '۰' and <= '۹' => (char)('0' + (c - '۰')), >= '٠' and <= '٩' => (char)('0' + (c - '٠')), _ => c }).ToArray());
        return long.TryParse(ascii, NumberStyles.None, CultureInfo.InvariantCulture, out units) && units > 0;
    }

    private string Digits(long value) => Digits(value.ToString(CultureInfo.InvariantCulture));

    private string Digits(string text) => digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : text;
}
