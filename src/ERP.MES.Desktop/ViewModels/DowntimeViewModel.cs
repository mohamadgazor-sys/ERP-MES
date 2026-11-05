using System.Collections.ObjectModel;
using System.Globalization;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Scheduling;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Desktop.ViewModels;

public sealed record DowntimeRow(
    DowntimeItem Item, string MachineText, string UnitText, string ReasonText, string StartText, string EndText, string DurationText, string StatusText, string NoteText, string ReporterText);

/// <summary>A stop reason in the reason combo box.</summary>
public sealed record DowntimeReasonOption(DowntimeReason Reason, string Text);

/// <summary>
/// State and actions of the machine-downtime screen (Master Specification section 21, Decision Log #14, #128). Maintenance/PM reports that a machine cannot work, corrects the end or cancels the
/// record; Planning accepts or rejects it; Management and the Production Manager see the register. The Application checks who may do what and every rule; the screen relays typed values
/// (Jalali dates, HH:mm times) and shows what the Application reports. Reported and accepted downtime is taken out of the planned-end forecast.
/// </summary>
public sealed class DowntimeViewModel(
    MachineDowntimeService downtime,
    IJalaliDateService dates,
    DigitStyle digitStyle,
    Func<DateTime> utcNow,
    Func<Principal> currentUser,
    UserDirectoryService? users = null) : ObservableObject
{
    public const string Heading = "توقف ماشین‌ها";

    public const string Note =
        "نگهداری و تعمیرات توقف هر ماشین را با علت، شروع و پایان ثبت می‌کند و در صورت لزوم پایان را اصلاح یا ثبت را با دلیل لغو می‌کند؛ برنامه‌ریزی توقف را بررسی و تأیید یا (با دلیل) رد می‌کند. "
        + "توقف ثبت‌شده یا تأییدشده از زمان در دسترس ماشین کم می‌شود و پایان پیش‌بینی‌شدهٔ کارهای صف را عقب می‌برد: کار مرحله‌ای که ماشین برنامه‌ریزی‌شده دارد منتظر همان ماشین می‌ماند و کار بدون ماشین فقط وقتی همهٔ ماشین‌های فعال واحد متوقف‌اند. "
        + "توقف ردشده یا لغوشده اثری ندارد و هیچ ثبتی پاک نمی‌شود. این قواعد پیش‌فرض‌اند و منتظر تأیید مالک (ثبت تصمیم ۱۲۸).";

    private readonly ObservableCollection<DowntimeRow> _rows = [];
    private readonly ObservableCollection<DowntimeMachine> _machines = [];
    private DowntimeView? _view;
    private DateOnly _from;
    private DateOnly _to;
    private string _message = string.Empty;
    private bool _isError;
    private bool _isBusy;
    private bool _denied;

    public static IReadOnlyList<DowntimeReasonOption> Reasons { get; } = Enum.GetValues<DowntimeReason>().Select(r => new DowntimeReasonOption(r, ReasonText(r))).ToList();

    public ObservableCollection<DowntimeRow> Rows => _rows;

    public ObservableCollection<DowntimeMachine> Machines => _machines;

    public bool CanReport => _view?.Capabilities.CanReport ?? false;

    public bool CanDecide => _view?.Capabilities.CanDecide ?? false;

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

    public string RangeText => dates.FormatDate(_from) + " تا " + dates.FormatDate(_to);

    public static string ReasonText(DowntimeReason reason) => reason switch
    {
        DowntimeReason.PowerOutage => "قطع برق",
        DowntimeReason.PreventiveMaintenance => "نگهداری پیشگیرانه (PM)",
        DowntimeReason.Breakdown => "خرابی",
        DowntimeReason.NoOperator => "نبود اپراتور",
        DowntimeReason.NoMaterial => "نبود مواد",
        DowntimeReason.NoDrawing => "نبود نقشه",
        DowntimeReason.NoQcResult => "نبود نتیجهٔ کنترل کیفیت",
        DowntimeReason.SetupChangeover => "راه‌اندازی / تعویض قالب",
        _ => "سایر",
    };

    public Task LoadAsync(CancellationToken cancellationToken = default) => RunAsync(() => ReloadAsync(cancellationToken));

    public Task SetRangeAsync(string? fromText, string? toText) => RunAsync(async () =>
    {
        if (!dates.TryParseDate(fromText, out var from, out _) || !dates.TryParseDate(toText, out var to, out _))
        {
            Fail(Messages.ForIssue(ValidationIssue.DateInvalid, JalaliDateError.None));
            return;
        }

        _from = dates.ToGregorian(from);
        _to = dates.ToGregorian(to);
        await ReloadAsync(CancellationToken.None);
    });

    /// <summary>
    /// Maintenance/PM reports downtime. The end date may be left empty when the downtime ends on the start date; an end time not after the start time on the same date means the next day.
    /// </summary>
    public Task ReportAsync(DowntimeMachine? machine, DowntimeReasonOption? reason, string? startDateText, string? startTimeText, string? endDateText, string? endTimeText, string? note) =>
        CommandAsync("توقف ثبت شد و در پیش‌بینی پایان کارها اثر دارد.", async token =>
        {
            if (machine is null || reason is null || !TryInstant(startDateText, startTimeText, null, out var start) || !TryInstant(endDateText, endTimeText, start, out var end))
            {
                return Outcome<int>.Invalid(ValidationIssue.DowntimeInvalid);
            }

            return Relay(await downtime.ReportAsync(currentUser(), machine.MachineId, reason.Reason, start, end, note, token));
        });

    public Task AcceptAsync(DowntimeRow row, string? note) => CommandAsync("توقف تأیید شد.", async token =>
        Relay(await downtime.DecideAsync(currentUser(), row.Item.Downtime.Value.Id, row.Item.Downtime.Version, true, note, token)));

    public Task RejectAsync(DowntimeRow row, string? reason) => CommandAsync("توقف رد شد و دیگر در پیش‌بینی اثر ندارد.", async token =>
        Relay(await downtime.DecideAsync(currentUser(), row.Item.Downtime.Value.Id, row.Item.Downtime.Version, false, reason, token)));

    /// <summary>Maintenance/PM changes the end; the date may be left empty to keep the end on the start date.</summary>
    public Task ChangeEndAsync(DowntimeRow row, string? endDateText, string? endTimeText, string? reason) => CommandAsync("پایان توقف اصلاح شد؛ برنامه‌ریزی باید آن را دوباره بررسی کند.", async token =>
    {
        var start = row.Item.Downtime.Value.StartLocal;
        if (!TryInstant(endDateText, endTimeText, start, out var end))
        {
            return Outcome<int>.Invalid(ValidationIssue.DowntimeInvalid);
        }

        return Relay(await downtime.ChangeEndAsync(currentUser(), row.Item.Downtime.Value.Id, row.Item.Downtime.Version, end, reason, token));
    });

    public Task CancelAsync(DowntimeRow row, string? reason) => CommandAsync("توقف لغو شد و دیگر در پیش‌بینی اثر ندارد.", async token =>
        Relay(await downtime.CancelAsync(currentUser(), row.Item.Downtime.Value.Id, row.Item.Downtime.Version, reason, token)));

    private static Outcome<int> Relay<T>(Outcome<T> outcome) => outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);

    /// <summary>
    /// A local instant from a Jalali date and an HH:mm time. For an end (<paramref name="start"/> given) an empty date means the start date, and then a time not after the start time means the next day.
    /// </summary>
    private bool TryInstant(string? dateText, string? timeText, DateTime? start, out DateTime value)
    {
        value = default;
        if (!TryTime(timeText, out var time))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(dateText) && start is { } from)
        {
            var sameDay = DateOnly.FromDateTime(from).ToDateTime(time);
            value = sameDay > from ? sameDay : sameDay.AddDays(1);
            return true;
        }

        if (!dates.TryParseDate(dateText, out var jalali, out _))
        {
            return false;
        }

        value = dates.ToGregorian(jalali).ToDateTime(time);
        return true;
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        if (_to == default)
        {
            var (today, _) = dates.UtcToLocal(utcNow());
            _from = today.AddDays(-30);
            _to = today.AddDays(30);
        }

        var outcome = await downtime.LoadAsync(currentUser(), _from, _to, cancellationToken);
        _rows.Clear();
        _machines.Clear();
        if (!outcome.Succeeded)
        {
            _view = null;
            IsDenied = outcome.Status == OperationStatus.NotAuthorized;
            Fail(IsDenied ? "شما اجازهٔ مشاهدهٔ توقف ماشین‌ها را ندارید." : Messages.For(outcome));
            NotifyCaps();
            return;
        }

        IsDenied = false;
        _view = outcome.Value!;
        NotifyCaps();
        foreach (var machine in _view.Machines)
        {
            _machines.Add(machine);
        }

        var actors = await ActorsAsync(cancellationToken);
        foreach (var item in _view.Items)
        {
            var d = item.Downtime.Value;
            _rows.Add(new DowntimeRow(
                item,
                item.MachineCode,
                item.UnitName,
                ReasonText(d.Reason),
                Instant(d.StartLocal),
                Instant(d.EndLocal),
                Digits(d.DurationMinutes.ToString(CultureInfo.InvariantCulture)) + " دقیقه",
                StatusText(d, actors),
                d.Note ?? string.Empty,
                UserDirectoryService.Describe(actors.GetValueOrDefault(d.ReportedByUserId))));
        }

        OnPropertyChanged(nameof(RangeText));
    }

    private async Task<IReadOnlyDictionary<long, UserDirectoryEntry>> ActorsAsync(CancellationToken cancellationToken)
    {
        if (users is null || _view is null)
        {
            return new Dictionary<long, UserDirectoryEntry>();
        }

        var ids = _view.Items.SelectMany(i => new[] { i.Downtime.Value.ReportedByUserId, i.Downtime.Value.DecidedByUserId, i.Downtime.Value.CancelledByUserId })
            .Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        var lookup = await users.LookupAsync(currentUser(), ids, cancellationToken);
        return lookup.Succeeded ? lookup.Value! : new Dictionary<long, UserDirectoryEntry>();
    }

    private static string StatusText(MachineDowntime d, IReadOnlyDictionary<long, UserDirectoryEntry> actors)
    {
        string By(long? id) => id is { } user && actors.GetValueOrDefault(user) is { } entry ? " — " + UserDirectoryService.Describe(entry) : string.Empty;
        return d.Status switch
        {
            DowntimeStatus.Reported => "ثبت‌شده (در انتظار بررسی برنامه‌ریزی؛ در پیش‌بینی اثر دارد)",
            DowntimeStatus.Accepted => "تأییدشده" + By(d.DecidedByUserId) + (string.IsNullOrEmpty(d.DecisionNote) ? string.Empty : ": " + d.DecisionNote),
            DowntimeStatus.Rejected => "ردشده" + By(d.DecidedByUserId) + ": " + d.DecisionNote,
            _ => "لغوشده" + By(d.CancelledByUserId) + ": " + d.DecisionNote,
        };
    }

    private string Instant(DateTime local) =>
        dates.FormatDate(DateOnly.FromDateTime(local)) + " " + Digits(local.ToString("HH:mm", CultureInfo.InvariantCulture));

    private void NotifyCaps()
    {
        OnPropertyChanged(nameof(CanReport));
        OnPropertyChanged(nameof(CanDecide));
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

    private static string Ascii(string text) =>
        new(text.Trim().Select(c => c switch { >= '۰' and <= '۹' => (char)('0' + (c - '۰')), >= '٠' and <= '٩' => (char)('0' + (c - '٠')), _ => c }).ToArray());

    private static bool TryTime(string? text, out TimeOnly time) => TimeOnly.TryParseExact(Ascii(text ?? string.Empty), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private string Digits(string text) => digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : text;
}
