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

public sealed record TemplateRow(Stored<ShiftTemplate> Stored, string CodeText, string NameText, string TimesText, string WorkingText, string StatusText);

public sealed record WeekdayRow(Stored<WeeklyWorkday> Stored, string DayText, string StateText);

public sealed record ExceptionRow(Stored<CalendarException> Stored, string DateText, string KindText, string ScopeText, string ReasonText, string StatusText);

public sealed record UnitShiftRow(Stored<UnitShift> Stored, string UnitText, string ShiftText, string CapacityText, string OverlapText, string StatusText, string DaysText = "");

public sealed record ScheduleDisplayRow(ScheduleRowView View, string DateText, string OperatorText, string UnitText, string ShiftText, string MachineText, string StatusText, string FlagText);

public sealed record OffDayRow(Stored<OperatorOffDay> Stored, string OperatorText, string RangeText, string KindText, string StatusText);

/// <summary>
/// State and actions of the Planning calendar screen (Decision Log #13, #100): shift templates, the regular week, holidays and extra working days, the shifts each unit runs with capacity, and the
/// worker schedule with operator off-days. Dates are typed as Jalali text and times as HH:mm. Every rule (D5 overlaps, capacity, absence, machine exclusivity) and every permission is the
/// Application's; the screen shows what it reports and the Application refuses again.
/// </summary>
public sealed class CalendarViewModel(
    CalendarService calendar,
    WorkerScheduleService schedule,
    PlanningMasterService planning,
    OperatorService operators,
    IJalaliDateService dates,
    DigitStyle digitStyle,
    Func<DateTime> utcNow,
    Func<Principal> currentUser) : ObservableObject
{
    public const string Heading = "تقویم کاری، شیفت‌ها و برنامهٔ اپراتورها";

    public const string Note =
        "قالب شیفت‌ها و الگوی هفته داده هستند و برنامه‌ریزی آن‌ها را تعریف می‌کند؛ هیچ ساعت یا روزی ثابت نیست. هم‌پوشانی شیفت‌های یک واحد فقط با علامت صریح برنامه‌ریزی و با ظرفیت جدا ممکن است. "
        + "یک اپراتور هرگز در دو شیفت هم‌پوشان یک روز نیست و یک ماشین در یک زمان یک اپراتور دارد (مگر تحویل صریح). تعطیل‌کردن یک روز یا ثبت مرخصی، ردیف‌های برنامه‌ریزی‌شدهٔ ناسازگار را لغو نمی‌کند؛ آن‌ها علامت می‌خورند.";

    private static readonly string[] DayNames = ["یکشنبه", "دوشنبه", "سه‌شنبه", "چهارشنبه", "پنجشنبه", "جمعه", "شنبه"];

    private readonly ObservableCollection<TemplateRow> _templates = [];
    private readonly ObservableCollection<WeekdayRow> _weekdays = [];
    private readonly ObservableCollection<ExceptionRow> _exceptions = [];
    private readonly ObservableCollection<UnitShiftRow> _unitShifts = [];
    private readonly ObservableCollection<ScheduleDisplayRow> _schedule = [];
    private readonly ObservableCollection<OffDayRow> _offDays = [];
    private readonly ObservableCollection<Stored<ProductionUnit>> _units = [];
    private readonly ObservableCollection<Stored<Machine>> _machines = [];
    private readonly ObservableCollection<Stored<Operator>> _operators = [];
    private CalendarCapabilities _capabilities = CalendarCapabilities.None;
    private CalendarConfiguration? _config;
    private DateOnly _from;
    private DateOnly _to;
    private string _message = string.Empty;
    private bool _isError;
    private bool _isBusy;
    private bool _denied;

    public ObservableCollection<TemplateRow> Templates => _templates;

    public ObservableCollection<WeekdayRow> Weekdays => _weekdays;

    public ObservableCollection<ExceptionRow> Exceptions => _exceptions;

    public ObservableCollection<UnitShiftRow> UnitShifts => _unitShifts;

    public ObservableCollection<ScheduleDisplayRow> Schedule => _schedule;

    public ObservableCollection<OffDayRow> OffDays => _offDays;

    public ObservableCollection<Stored<ProductionUnit>> Units => _units;

    public ObservableCollection<Stored<Machine>> Machines => _machines;

    public ObservableCollection<Stored<Operator>> OperatorChoices => _operators;

    public IReadOnlyList<Choice<CalendarExceptionKind>> ExceptionKinds { get; } = [new(CalendarExceptionKind.Off, "تعطیل / نبود کار"), new(CalendarExceptionKind.Working, "روز کاری اضافه (با تأیید)")];

    public IReadOnlyList<Choice<OffDayKind>> OffDayKinds { get; } = [new(OffDayKind.Leave, "مرخصی"), new(OffDayKind.Sick, "استعلاجی"), new(OffDayKind.Mission, "مأموریت"), new(OffDayKind.Other, "سایر")];

    public CalendarCapabilities Capabilities
    {
        get => _capabilities;
        private set
        {
            if (SetProperty(ref _capabilities, value))
            {
                OnPropertyChanged(nameof(CanMaintain));
                OnPropertyChanged(nameof(CanScheduleWorkers));
            }
        }
    }

    public bool CanMaintain => _capabilities.CanMaintain;

    public bool CanScheduleWorkers => _capabilities.CanScheduleWorkers;

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

    // ---- loading --------------------------------------------------------------------------------------------------------------------------------------------------

    public Task LoadAsync(CancellationToken cancellationToken = default) => RunAsync(() => ReloadAsync(cancellationToken));

    public Task SetRangeAsync(string? fromText, string? toText) => RunAsync(async () =>
    {
        if (!TryDate(fromText, out var from) || !TryDate(toText, out var to))
        {
            Fail(Messages.ForIssue(ValidationIssue.DateInvalid, JalaliDateError.None));
            return;
        }

        _from = from;
        _to = to;
        await ReloadAsync(CancellationToken.None);
    });

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        if (_to == default)
        {
            var (today, _) = dates.UtcToLocal(utcNow());
            _from = today;
            _to = today.AddDays(13);
        }

        var view = await calendar.LoadAsync(currentUser(), cancellationToken);
        ClearAll();
        if (!view.Succeeded)
        {
            Capabilities = CalendarCapabilities.None;
            IsDenied = view.Status == OperationStatus.NotAuthorized;
            Fail(IsDenied ? "شما اجازهٔ مشاهدهٔ تقویم و شیفت‌ها را ندارید." : Messages.For(view));
            return;
        }

        IsDenied = false;
        Capabilities = view.Value!.Capabilities;
        _config = view.Value.Configuration;
        var unitData = await planning.LoadAsync(currentUser(), cancellationToken);
        var units = unitData.Succeeded ? unitData.Value!.Units : [];
        foreach (var unit in units)
        {
            _units.Add(unit);
        }

        foreach (var machine in unitData.Succeeded ? unitData.Value!.Machines : [])
        {
            _machines.Add(machine);
        }

        var operatorData = await operators.LoadAsync(currentUser(), cancellationToken);
        var people = operatorData.Succeeded ? operatorData.Value!.Operators : [];
        foreach (var person in people.Where(o => o.Value.IsActive))
        {
            _operators.Add(person);
        }

        var templates = _config.Templates.ToDictionary(t => t.Value.Id);
        var unitNames = units.ToDictionary(u => u.Value.Id, u => u.Value.Name);
        var machineNames = _machines.ToDictionary(m => m.Value.Id, m => m.Value.Code);
        var peopleNames = people.ToDictionary(o => o.Value.Id, o => o.Value.FullName);

        foreach (var t in _config.Templates)
        {
            _templates.Add(new TemplateRow(
                t, t.Value.Code, t.Value.Name,
                $"{Time(t.Value.StartTime)} تا {Time(t.Value.EndTime)}" + (t.Value.CrossesMidnight ? " (تا فردا)" : string.Empty) + (t.Value.BreakStart is { } bs && t.Value.BreakEnd is { } be ? $"؛ استراحت {Time(bs)} تا {Time(be)}" : string.Empty),
                Digits(t.Value.WorkingMinutes) + " دقیقه", t.Value.IsActive ? "فعال" : "بازنشسته"));
        }

        foreach (var w in _config.Weekly.OrderBy(w => ((int)w.Value.Day + 1) % 7))
        {
            _weekdays.Add(new WeekdayRow(w, DayNames[(int)w.Value.Day], w.Value.IsWorkday ? "روز کاری" : "تعطیل"));
        }

        foreach (var e in _config.Exceptions)
        {
            var scope = (e.Value.ProductionUnitId is { } u ? unitNames.GetValueOrDefault(u, "?") : "همهٔ واحدها") + " / " + (e.Value.ShiftTemplateId is { } s ? templates.GetValueOrDefault(s)?.Value.Code ?? "?" : "همهٔ شیفت‌ها");
            _exceptions.Add(new ExceptionRow(e, dates.FormatDate(e.Value.Date), e.Value.Kind == CalendarExceptionKind.Off ? "تعطیل" : "روز کاری اضافه", scope, e.Value.Reason, e.Value.IsActive ? "فعال" : "لغوشده"));
        }

        foreach (var u in _config.UnitShifts)
        {
            _unitShifts.Add(new UnitShiftRow(
                u, unitNames.GetValueOrDefault(u.Value.ProductionUnitId, "?"), templates.GetValueOrDefault(u.Value.ShiftTemplateId)?.Value.Code ?? "?",
                $"{Digits(u.Value.MinOrZero())} تا {Digits(u.Value.MaxOperators)}", u.Value.OverlapAllowed ? "هم‌پوشانی علامت‌خورده" : string.Empty, u.Value.IsActive ? "فعال" : "بازنشسته", DaysText(u.Value.RegularDays)));
        }

        var range = await schedule.LoadAsync(currentUser(), _from, _to, cancellationToken);
        if (range.Succeeded)
        {
            foreach (var row in range.Value!.Rows)
            {
                var r = row.Row.Value;
                _schedule.Add(new ScheduleDisplayRow(
                    row, dates.FormatDate(r.Date), row.OperatorName.Length > 0 ? row.OperatorName : peopleNames.GetValueOrDefault(r.OperatorId, "?"), unitNames.GetValueOrDefault(r.ProductionUnitId, "?"),
                    templates.GetValueOrDefault(r.ShiftTemplateId)?.Value.Code ?? "?", r.MachineId is { } m ? machineNames.GetValueOrDefault(m, "?") + (r.HandoverMinutes is { } h ? $" (تحویل {Digits(h)} دقیقه)" : string.Empty) : "—",
                    r.IsPlanned ? "برنامه‌ریزی‌شده" : "لغوشده: " + r.CancelReason, FlagText(row.Flags)));
            }

            foreach (var o in range.Value.OffDays)
            {
                _offDays.Add(new OffDayRow(o, peopleNames.GetValueOrDefault(o.Value.OperatorId, "?"), dates.FormatDate(o.Value.FromDate) + " تا " + dates.FormatDate(o.Value.ToDate), OffDayKinds.First(k => k.Value == o.Value.Kind).Text, o.Value.IsActive ? "فعال" : "لغوشده"));
            }
        }

        OnPropertyChanged(nameof(RangeText));
    }

    // ---- maintenance ----------------------------------------------------------------------------------------------------------------------------------------------

    public Task AddTemplateAsync(string? code, string? name, string? start, string? end, string? breakStart, string? breakEnd) => CommandAsync("قالب شیفت ثبت شد.", async token =>
    {
        if (!TryTime(start, out var s) || !TryTime(end, out var e) || !TryOptionalTime(breakStart, out var bs) || !TryOptionalTime(breakEnd, out var be))
        {
            return Outcome<int>.Invalid(ValidationIssue.ShiftTimesInvalid);
        }

        return Wrap(await calendar.AddTemplateAsync(currentUser(), code, name, s, e, bs, be, token));
    });

    public Task SetTemplateActiveAsync(TemplateRow row, bool active) => CommandAsync(active ? "قالب فعال شد." : "قالب بازنشسته شد.", async token =>
        Wrap(await calendar.SetTemplateActiveAsync(currentUser(), row.Stored.Value.Id, row.Stored.Version, active, token)));

    public Task SetWeekdayAsync(WeekdayRow row, bool isWorkday) => CommandAsync("الگوی هفته تغییر کرد.", async token =>
        Wrap(await calendar.SetWeekdayAsync(currentUser(), row.Stored.Value.Day, row.Stored.Version, isWorkday, token)));

    public Task AddExceptionAsync(string? dateText, CalendarExceptionKind kind, long? unitId, long? templateId, string? reason) => CommandAsync("استثنای تقویم ثبت شد.", async token =>
    {
        if (!TryDate(dateText, out var date))
        {
            return Outcome<int>.Invalid(ValidationIssue.DateInvalid);
        }

        return await calendar.AddExceptionAsync(currentUser(), date, kind, unitId, templateId, reason, token);
    });

    public Task CancelExceptionAsync(ExceptionRow row) => CommandAsync("استثنا لغو شد.", token => calendar.CancelExceptionAsync(currentUser(), row.Stored.Value.Id, row.Stored.Version, token));

    public Task AddUnitShiftAsync(long unitId, long templateId, string? max, string? min, bool overlapAllowed, int? regularDays = null) => CommandAsync("شیفت واحد ثبت شد.", async token =>
    {
        if (!TryInt(max, out var maxValue) || !TryInt(min, out var minValue))
        {
            return Outcome<int>.Invalid(ValidationIssue.CapacityInvalid);
        }

        return Wrap(await calendar.AddUnitShiftAsync(currentUser(), unitId, templateId, maxValue, minValue, overlapAllowed, token, regularDays));
    });

    /// <summary>Sets the weekdays of the selected unit-shift (a recurring unit schedule); null returns it to the company pattern.</summary>
    public Task SetUnitShiftDaysAsync(UnitShiftRow row, int? regularDays) => CommandAsync("روزهای شیفت واحد تغییر کرد.", async token =>
        Wrap(await calendar.SetUnitShiftDaysAsync(currentUser(), row.Stored.Value.Id, row.Stored.Version, regularDays, token)));

    /// <summary>A date on which the unit works only the given hours (Decision Log #123 Q17).</summary>
    public Task AddUnitDateHoursAsync(long unitId, string? dateText, string? start, string? end, string? max, string? reason) => CommandAsync("ساعت ویژهٔ تاریخ ثبت شد.", async token =>
    {
        if (!TryDate(dateText, out var date))
        {
            return Outcome<int>.Invalid(ValidationIssue.DateInvalid);
        }

        if (!TryTime(start, out var s) || !TryTime(end, out var e))
        {
            return Outcome<int>.Invalid(ValidationIssue.ShiftTimesInvalid);
        }

        if (!TryInt(max, out var maxValue))
        {
            return Outcome<int>.Invalid(ValidationIssue.CapacityInvalid);
        }

        return Wrap(await calendar.AddUnitDateHoursAsync(currentUser(), unitId, date, s, e, maxValue, reason, token));
    });

    private string DaysText(int? mask) =>
        mask is not { } m ? "الگوی شرکت" : m == 0 ? "فقط با استثنای تاریخی" : string.Join("، ", Enumerable.Range(0, 7).Where(d => (m >> d & 1) == 1).Select(d => DayNames[d]));

    public Task SetUnitShiftActiveAsync(UnitShiftRow row, bool active) => CommandAsync(active ? "شیفت واحد فعال شد." : "شیفت واحد بازنشسته شد.", async token =>
        Wrap(await calendar.SetUnitShiftActiveAsync(currentUser(), row.Stored.Value.Id, row.Stored.Version, active, token)));

    // ---- scheduling -----------------------------------------------------------------------------------------------------------------------------------------------

    public Task ScheduleAsync(long operatorId, string? dateText, long unitId, long templateId, long? machineId, string? handoverMinutes, string? handoverNote) => CommandAsync("اپراتور برنامه‌ریزی شد.", async token =>
    {
        if (!TryDate(dateText, out var date))
        {
            return Outcome<int>.Invalid(ValidationIssue.DateInvalid);
        }

        int? minutes = null;
        if (!string.IsNullOrWhiteSpace(handoverMinutes))
        {
            if (!TryInt(handoverMinutes, out var parsed))
            {
                return Outcome<int>.Invalid(ValidationIssue.HandoverInvalid);
            }

            minutes = parsed;
        }

        return Wrap(await schedule.ScheduleAsync(currentUser(), operatorId, date, unitId, templateId, machineId, minutes, handoverNote, token));
    });

    public Task CancelScheduleAsync(ScheduleDisplayRow row, string? reason) => CommandAsync("ردیف برنامه لغو شد.", async token =>
        Wrap(await schedule.CancelScheduleAsync(currentUser(), row.View.Row.Value.Id, row.View.Row.Version, reason, token)));

    public Task AddOffDayAsync(long operatorId, string? fromText, string? toText, OffDayKind kind, string? note) => CommandAsync("مرخصی/غیبت ثبت شد.", async token =>
    {
        if (!TryDate(fromText, out var from) || !TryDate(toText, out var to))
        {
            return Outcome<int>.Invalid(ValidationIssue.DateInvalid);
        }

        return await schedule.AddOffDayAsync(currentUser(), operatorId, from, to, kind, note, token);
    });

    public Task CancelOffDayAsync(OffDayRow row) => CommandAsync("مرخصی/غیبت لغو شد.", async token =>
        Wrap(await schedule.CancelOffDayAsync(currentUser(), row.Stored.Value.Id, row.Stored.Version, token)));

    // ---- helpers --------------------------------------------------------------------------------------------------------------------------------------------------

    private static Outcome<int> Wrap<T>(Outcome<T> outcome) => outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);

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
                Succeed(outcome.Value > 0 ? $"{success} هشدار: {Digits(outcome.Value)} ردیف برنامهٔ اپراتور از قبل ثبت‌شده با این تغییر ناسازگار شد؛ لغو نشده و علامت خورده است." : success);
            }
            else
            {
                Fail(outcome.Status == OperationStatus.ConcurrencyConflict ? "این ردیف را کاربر دیگری تغییر داده است؛ کاری انجام نشد. اطلاعات دوباره بارگذاری شد." : Messages.For(outcome));
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

    private void ClearAll()
    {
        _templates.Clear();
        _weekdays.Clear();
        _exceptions.Clear();
        _unitShifts.Clear();
        _schedule.Clear();
        _offDays.Clear();
        _units.Clear();
        _machines.Clear();
        _operators.Clear();
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

    private bool TryDate(string? text, out DateOnly date)
    {
        date = default;
        if (!dates.TryParseDate(text, out var jalali, out _))
        {
            return false;
        }

        date = dates.ToGregorian(jalali);
        return true;
    }

    private static string AsciiDigits(string? text) =>
        new((text ?? string.Empty).Trim().Select(c => c switch { >= '۰' and <= '۹' => (char)('0' + (c - '۰')), >= '٠' and <= '٩' => (char)('0' + (c - '٠')), _ => c }).ToArray());

    private static bool TryTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(AsciiDigits(text), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static bool TryOptionalTime(string? text, out TimeOnly? time)
    {
        time = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!TryTime(text, out var parsed))
        {
            return false;
        }

        time = parsed;
        return true;
    }

    private static bool TryInt(string? text, out int value) => int.TryParse(AsciiDigits(text), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private string Time(TimeOnly time) => Digits(time.ToString("HH:mm", CultureInfo.InvariantCulture));

    private string Digits(int value) => Digits(value.ToString(CultureInfo.InvariantCulture));

    private string Digits(string text) => digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : text;

    private static string FlagText(ScheduleFlags flags)
    {
        var parts = new List<string>();
        if (flags.HasFlag(ScheduleFlags.DayNowOff))
        {
            parts.Add("روز اکنون تعطیل است");
        }

        if (flags.HasFlag(ScheduleFlags.OperatorAbsent))
        {
            parts.Add("اپراتور غایب است");
        }

        if (flags.HasFlag(ScheduleFlags.ShiftNoLongerRun))
        {
            parts.Add("واحد دیگر این شیفت را اجرا نمی‌کند");
        }

        return string.Join("؛ ", parts);
    }
}

internal static class UnitShiftDisplay
{
    public static int MinOrZero(this UnitShift value) => value.MinOperators;
}
