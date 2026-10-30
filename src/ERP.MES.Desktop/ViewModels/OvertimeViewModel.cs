using System.Collections.ObjectModel;
using System.Globalization;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Planning;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Scheduling;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Desktop.ViewModels;

public sealed record OvertimeRow(OvertimeItem Item, string DateText, string OperatorText, string MachineText, string TimeText, string StatusText, string ReasonText);

/// <summary>
/// State and actions of the overtime screen (Decision Log #89, #95 D5, #103). The unit supervisor proposes overtime, Management or the Production Manager approves it within the limits Management configured, and only Management decides what is
/// outside them (Decision Log #122). The Application checks who may do what and every rule; the screen relays typed values (Jalali date, HH:mm times, minutes) and shows what the Application reports.
/// </summary>
public sealed class OvertimeViewModel(
    OvertimeService overtime,
    OperatorService operators,
    PlanningMasterService planning,
    IJalaliDateService dates,
    DigitStyle digitStyle,
    Func<DateTime> utcNow,
    Func<Principal> currentUser) : ObservableObject
{
    public const string Heading = "اضافه‌کاری";

    public const string Note =
        "سرپرست واحد برای اپراتور و ماشین واحد خودش اضافه‌کاری پیشنهاد می‌دهد (نه برای خودش)؛ مدیریت یا مدیر تولید آن را تأیید یا (با دلیل) رد می‌کند و اگر از سقف روزانه یا هفتگی بگذرد، فقط مدیریت تصمیم می‌گیرد؛ برنامه‌ریزی اضافه‌کاری را تأیید نمی‌کند. "
        + "تا مدیریت سقف‌ها را تعریف نکند هیچ اضافه‌کاری تأیید نمی‌شود (عددی از پیش فرض نشده است). اضافه‌کاری بیرون از شیفت عادی است و با شیفت، مرخصی یا کار دیگر اپراتور و ماشین هم‌پوشانی ندارد. "
        + "تأیید اضافه‌کاری فقط مجوز ساعت است و کار انجام‌شده را ثابت نمی‌کند؛ ساعت کار فقط از گزارش‌های تأییدشده می‌آید. هفته از شنبه شروع می‌شود و نرخ ماشین همان نرخ عادی است.";

    private readonly ObservableCollection<OvertimeRow> _rows = [];
    private readonly ObservableCollection<Stored<Operator>> _operators = [];
    private readonly ObservableCollection<Stored<ProductionUnit>> _units = [];
    private readonly ObservableCollection<Stored<Machine>> _machines = [];
    private OvertimeView? _view;
    private DateOnly _from;
    private DateOnly _to;
    private string _message = string.Empty;
    private bool _isError;
    private bool _isBusy;
    private bool _denied;
    private string _limitsText = string.Empty;

    public ObservableCollection<OvertimeRow> Rows => _rows;

    public ObservableCollection<Stored<Operator>> Operators => _operators;

    public ObservableCollection<Stored<ProductionUnit>> Units => _units;

    public ObservableCollection<Stored<Machine>> Machines => _machines;

    public bool CanPropose => _view?.Capabilities.CanPropose ?? false;

    public bool CanDecide => _view?.Capabilities.CanDecide ?? false;

    public bool CanManagementDecide => _view?.Capabilities.CanManagementDecide ?? false;

    public bool CanConfigureLimits => _view?.Capabilities.CanConfigureLimits ?? false;

    public string LimitsText
    {
        get => _limitsText;
        private set => SetProperty(ref _limitsText, value);
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

    public string RangeText => dates.FormatDate(_from) + " تا " + dates.FormatDate(_to);

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

    public Task ProposeAsync(Stored<Operator>? person, Stored<ProductionUnit>? unit, Stored<Machine>? machine, string? dateText, string? startText, string? endText, string? reason) =>
        CommandAsync("اضافه‌کاری پیشنهاد شد.", async token =>
        {
            if (person is null || unit is null || machine is null || !dates.TryParseDate(dateText, out var jalali, out _) || !TryTime(startText, out var start) || !TryTime(endText, out var end))
            {
                return Outcome<int>.Invalid(ValidationIssue.OvertimeInvalid);
            }

            var date = dates.ToGregorian(jalali);
            var outcome = await overtime.ProposeAsync(currentUser(), person.Value.Id, unit.Value.Id, machine.Value.Id, date.ToDateTime(start), (end > start ? date : date.AddDays(1)).ToDateTime(end), reason, token);
            return Relay(outcome);
        });

    public Task ApproveAsync(OvertimeRow row, string? note) => CommandAsync("تأیید شد (اگر از سقف بیشتر باشد، برای تصمیم مدیریت ارسال می‌شود).", async token =>
        Relay(await overtime.DecideAsync(currentUser(), row.Item.Assignment.Value.Id, row.Item.Assignment.Version, true, note, token)));

    public Task RejectAsync(OvertimeRow row, string? reason) => CommandAsync("رد شد.", async token =>
        Relay(await overtime.DecideAsync(currentUser(), row.Item.Assignment.Value.Id, row.Item.Assignment.Version, false, reason, token)));

    public Task ManagementApproveAsync(OvertimeRow row, string? note) => CommandAsync("تأیید شد.", async token =>
        Relay(await overtime.ManagementDecideAsync(currentUser(), row.Item.Assignment.Value.Id, row.Item.Assignment.Version, true, note, token)));

    public Task ManagementRejectAsync(OvertimeRow row, string? reason) => CommandAsync("رد شد.", async token =>
        Relay(await overtime.ManagementDecideAsync(currentUser(), row.Item.Assignment.Value.Id, row.Item.Assignment.Version, false, reason, token)));

    public Task CancelAsync(OvertimeRow row, string? reason) => CommandAsync("لغو شد.", async token =>
        Relay(await overtime.CancelAsync(currentUser(), row.Item.Assignment.Value.Id, row.Item.Assignment.Version, reason, token)));

    /// <summary>Management sets the daily and weekly limits in minutes (the version of the current limits, or none).</summary>
    public Task SaveLimitsAsync(string? dayMinutes, string? weekMinutes) => CommandAsync("سقف اضافه‌کاری ذخیره شد.", async token =>
    {
        if (!TryInt(dayMinutes, out var day) || !TryInt(weekMinutes, out var week))
        {
            return Outcome<int>.Invalid(ValidationIssue.OvertimeLimitsInvalid);
        }

        return Relay(await overtime.SetLimitsAsync(currentUser(), _view?.Limits?.Version ?? 0, day, week, token));
    });

    private static Outcome<int> Relay<T>(Outcome<T> outcome) => outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        if (_to == default)
        {
            var (today, _) = dates.UtcToLocal(utcNow());
            _from = today.AddDays(-7);
            _to = today.AddDays(30);
        }

        var outcome = await overtime.LoadAsync(currentUser(), _from, _to, cancellationToken);
        _rows.Clear();
        if (!outcome.Succeeded)
        {
            _view = null;
            IsDenied = outcome.Status == OperationStatus.NotAuthorized;
            Fail(IsDenied ? "شما اجازهٔ مشاهدهٔ اضافه‌کاری را ندارید." : Messages.For(outcome));
            NotifyCaps();
            return;
        }

        IsDenied = false;
        _view = outcome.Value!;
        var limits = _view.Limits?.Value;
        LimitsText = limits is null
            ? "سقف اضافه‌کاری هنوز تعریف نشده است؛ تا تعریف آن، اضافه‌کاری تأیید نمی‌شود."
            : $"سقف هر اپراتور: روزانه {Digits(limits.MaxMinutesPerDay.ToString(CultureInfo.InvariantCulture))} دقیقه، هفتگی {Digits(limits.MaxMinutesPerWeek.ToString(CultureInfo.InvariantCulture))} دقیقه.";
        NotifyCaps();

        _operators.Clear();
        var people = await operators.LoadAsync(currentUser(), cancellationToken);
        foreach (var person in people.Succeeded ? people.Value!.Operators.Where(o => o.Value.IsActive) : [])
        {
            _operators.Add(person);
        }

        _units.Clear();
        _machines.Clear();
        var master = await planning.LoadAsync(currentUser(), cancellationToken);
        if (master.Succeeded)
        {
            foreach (var unit in master.Value!.Units.Where(u => u.Value.IsActive && _view.SupervisedUnitIds.Contains(u.Value.Id)))
            {
                _units.Add(unit);
            }

            foreach (var machine in master.Value.Machines.Where(m => m.Value.IsActive && _view.SupervisedUnitIds.Contains(m.Value.ProductionUnitId)))
            {
                _machines.Add(machine);
            }
        }

        foreach (var item in _view.Items)
        {
            var a = item.Assignment.Value;
            _rows.Add(new OvertimeRow(
                item,
                dates.FormatDate(a.WorkDate),
                item.OperatorName,
                item.MachineCode,
                $"{Digits(a.StartLocal.ToString("HH:mm", CultureInfo.InvariantCulture))} تا {Digits(a.EndLocal.ToString("HH:mm", CultureInfo.InvariantCulture))} ({Digits(a.Minutes.ToString(CultureInfo.InvariantCulture))} دقیقه)",
                StatusText(a),
                a.Reason));
        }

        OnPropertyChanged(nameof(RangeText));
    }

    private static string StatusText(OvertimeAssignment a) => a.Status switch
    {
        OvertimeStatus.Proposed => "پیشنهادشده (در انتظار تأیید مدیریت یا مدیر تولید)",
        OvertimeStatus.AwaitingManagement => "خارج از سقف؛ در انتظار مدیریت",
        OvertimeStatus.Approved => a.ExceedsLimits ? "تأییدشده (خارج از سقف، با تأیید مدیریت)" : "تأییدشده",
        OvertimeStatus.Rejected => "ردشده: " + a.DecisionNote,
        _ => "لغوشده: " + a.DecisionNote,
    };

    private void NotifyCaps()
    {
        OnPropertyChanged(nameof(CanPropose));
        OnPropertyChanged(nameof(CanDecide));
        OnPropertyChanged(nameof(CanManagementDecide));
        OnPropertyChanged(nameof(CanConfigureLimits));
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

    private static bool TryInt(string? text, out int value) => int.TryParse(Ascii(text ?? string.Empty), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private string Digits(string text) => digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : text;
}
