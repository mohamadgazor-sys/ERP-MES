using System.Collections.ObjectModel;
using System.Globalization;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>One stage waiting for QC (or a shortfall decision) as shown, with the entry that carries the ids and row versions.</summary>
public sealed record InspectionRow(
    InspectionEntry2 Entry, string RequestNo, string ItemNo, string PartName, string StageText, string StatusText, string UnitsText, string InputText, string AwaitingText, string AcceptedText, string ReworkText, string ScrapText) : IHasItemId
{
    public long ItemId => Entry.ItemId;

    public string CustomerName => Entry.CustomerName;
}

public sealed record InspectionHistoryRow(string TimeText, string KindText, string UnitsText, string ReasonText, string ActorText = "");

/// <summary>
/// State and actions of the QC inspection screen (Decision Log #92, #95 D2/D3, #101): QC decides base units of a submitted stage as accepted, rework or scrap; Management accepts a shortfall and Planning
/// records a replacement order. Rules, permissions, row versions and the audit are the Application's; the screen shows what it reports and relays the typed counts.
/// </summary>
public sealed class QcInspectionViewModel(QcInspectionService service, IJalaliDateService dates, DigitStyle digitStyle, Func<Principal> currentUser, UserDirectoryService? users = null) : ObservableObject
{
    public const string Heading = "بازرسی کنترل کیفیت — پذیرفته / بازکاری / ضایعات";

    public const string Note =
        "تصمیم‌ها بر پایهٔ واحد پایه‌اند (جفت = ۲ قطعه) و شناسهٔ تک‌تک قطعه‌ها وجود ندارد. بیش از واحدهای در انتظار نمی‌توان تصمیم گرفت؛ بازکاری فقط همان واحدها را دوباره به کنترل می‌فرستد؛ "
        + "مرحلهٔ بعد فقط واحدهای پذیرفته‌شده را می‌گیرد. اگر ضایعات کسری ایجاد کند، مرحله تا تصمیم مدیریت (پذیرش کسری) یا ثبت سفارش جایگزین توسط برنامه‌ریزی تکمیل نمی‌شود؛ سفارش جایگزین خودکار ساخته نمی‌شود.";

    private readonly ObservableCollection<InspectionRow> _rows = [];
    private readonly ObservableCollection<InspectionHistoryRow> _history = [];
    private InspectionCapabilities _capabilities = InspectionCapabilities.None;
    private InspectionRow? _selected;
    private string _message = string.Empty;
    private bool _isError;
    private bool _isBusy;
    private bool _denied;

    public ObservableCollection<InspectionRow> Rows => _rows;

    public ObservableCollection<InspectionHistoryRow> History => _history;

    public InspectionCapabilities Capabilities
    {
        get => _capabilities;
        private set
        {
            if (SetProperty(ref _capabilities, value))
            {
                OnPropertyChanged(nameof(CanInspect));
                OnPropertyChanged(nameof(CanAcceptShortfall));
                OnPropertyChanged(nameof(CanOrderReplacement));
            }
        }
    }

    public bool CanInspect => _capabilities.CanInspect;

    public bool CanAcceptShortfall => _capabilities.CanAcceptShortfall;

    public bool CanOrderReplacement => _capabilities.CanOrderReplacement;

    public InspectionRow? SelectedRow => _selected;

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

    public Task LoadAsync(CancellationToken cancellationToken = default) => RunAsync(() => ReloadAsync(cancellationToken));

    public Task SelectAsync(InspectionRow? row) => RunAsync(async () =>
    {
        _selected = row;
        OnPropertyChanged(nameof(SelectedRow));
        await LoadHistoryAsync(CancellationToken.None);
    });

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var keep = _selected is null ? (long?)null : _selected.Entry.ItemId * 1000 + _selected.Entry.StageNo;
        var outcome = await service.LoadAsync(currentUser(), cancellationToken);
        _rows.Clear();
        _history.Clear();
        if (!outcome.Succeeded)
        {
            Capabilities = InspectionCapabilities.None;
            IsDenied = outcome.Status == OperationStatus.NotAuthorized;
            Fail(IsDenied ? "شما اجازهٔ مشاهدهٔ بازرسی‌های کنترل کیفیت را ندارید." : Messages.For(outcome));
            return;
        }

        IsDenied = false;
        Capabilities = outcome.Value!.Capabilities;
        foreach (var entry in outcome.Value.Entries)
        {
            _rows.Add(Format(entry));
        }

        _selected = _rows.FirstOrDefault(r => r.Entry.ItemId * 1000 + r.Entry.StageNo == keep);
        OnPropertyChanged(nameof(SelectedRow));
        await LoadHistoryAsync(cancellationToken);
    }

    private async Task LoadHistoryAsync(CancellationToken cancellationToken)
    {
        _history.Clear();
        if (_selected is not { } row)
        {
            return;
        }

        var history = await service.HistoryAsync(currentUser(), row.Entry.ItemId, row.Entry.StageNo, cancellationToken);
        if (!history.Succeeded)
        {
            return;
        }

        var actors = await ActorLookup.DescribeAsync(users, currentUser(), history.Value!.Lines.Select(l => l.DecidedByUserId).Concat(history.Value.Decisions.Select(d => d.DecidedByUserId)), cancellationToken);
        foreach (var line in history.Value!.Lines)
        {
            _history.Add(new InspectionHistoryRow(dates.FormatDateTime(line.DecidedAtUtc), line.Disposition switch { InspectionDisposition.Accepted => "پذیرفته", InspectionDisposition.Rework => "بازکاری", _ => "ضایعات" }, Digits(line.BaseUnits), line.Reason ?? string.Empty, ActorLookup.Text(actors, line.DecidedByUserId)));
        }

        foreach (var decision in history.Value.Decisions)
        {
            _history.Add(new InspectionHistoryRow(dates.FormatDateTime(decision.DecidedAtUtc), decision.Kind == ShortfallKind.ManagementAccepted ? "پذیرش کسری توسط مدیریت" : "سفارش جایگزین ثبت شد", Digits(decision.ShortfallBaseUnits), decision.Note, ActorLookup.Text(actors, decision.DecidedByUserId)));
        }
    }

    /// <summary>QC records the typed counts (empty or zero means none) for the selected stage.</summary>
    public Task InspectAsync(string? accepted, string? rework, string? scrap, string? reason) => CommandAsync("بازرسی ثبت شد.", async token =>
    {
        if (_selected is not { } row)
        {
            return Fail2(ValidationIssue.QueueRowsInvalid);
        }

        var entries = new List<InspectionEntry>();
        foreach (var (kind, text) in new[] { (InspectionDisposition.Accepted, accepted), (InspectionDisposition.Rework, rework), (InspectionDisposition.Scrap, scrap) })
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (!long.TryParse(Ascii(text), NumberStyles.None, CultureInfo.InvariantCulture, out var units))
            {
                return Fail2(ValidationIssue.InspectionEntriesInvalid);
            }

            if (units > 0)
            {
                entries.Add(new InspectionEntry(kind, units, kind == InspectionDisposition.Accepted ? null : reason));
            }
        }

        var outcome = await service.RecordInspectionAsync(currentUser(), row.Entry.ToStageTarget(), entries, token);
        return outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
    });

    public Task DecideShortfallAsync(ShortfallKind kind, string? note) => CommandAsync(kind == ShortfallKind.ManagementAccepted ? "کسری پذیرفته شد." : "سفارش جایگزین ثبت شد.", async token =>
    {
        if (_selected is not { } row)
        {
            return Fail2(ValidationIssue.QueueRowsInvalid);
        }

        var outcome = await service.DecideShortfallAsync(currentUser(), row.Entry.ToStageTarget(), kind, note, token);
        return outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
    });

    private static Outcome<int> Fail2(ValidationIssue issue) => Outcome<int>.Invalid(issue);

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
                Fail(outcome.Status == OperationStatus.ConcurrencyConflict ? "این مرحله را کاربر دیگری تغییر داده است؛ کاری انجام نشد. فهرست دوباره بارگذاری شد." : Messages.For(outcome));
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

    private InspectionRow Format(InspectionEntry2 e) => new(
        e,
        e.RequestNo,
        e.ItemNo,
        e.PartName,
        $"مرحلهٔ {Digits(e.StageNo)} از {Digits(e.StageCount)}: {e.StageTitle}",
        e.Status switch
        {
            ItemStageStatus.WaitingQc => "در انتظار تصمیم کنترل کیفیت",
            ItemStageStatus.WaitingShortfallDecision => "در انتظار تصمیم دربارهٔ کسری",
            _ => "در بازکاری (تولید)",
        },
        e.BaseUnitsPerUnit == 1 ? "هر واحد = ۱ قطعهٔ پایه" : $"هر واحد = {Digits(e.BaseUnitsPerUnit)} واحد پایه",
        Digits(e.InputBaseUnits),
        Digits(e.AwaitingQcBaseUnits),
        Digits(e.AcceptedBaseUnits),
        Digits(e.ReworkBaseUnits),
        Digits(e.ScrapBaseUnits));

    private static string Ascii(string text) =>
        new(text.Trim().Select(c => c switch { >= '۰' and <= '۹' => (char)('0' + (c - '۰')), >= '٠' and <= '٩' => (char)('0' + (c - '٠')), _ => c }).ToArray());

    private string Digits(long value) => digitStyle == DigitStyle.Persian ? new string(value.ToString(CultureInfo.InvariantCulture).Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : value.ToString(CultureInfo.InvariantCulture);
}
