using System.Collections.ObjectModel;
using System.Globalization;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Planning;
using ERP.MES.Application.Production;
using ERP.MES.Application.Reporting;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Scheduling;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Reporting;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Desktop.ViewModels;

public sealed record StageChoice(UnitQueueEntry Entry, string Text);

public sealed record JobReportRow(JobReportItem Item, string DateText, string OperatorText, string UnitText, string ShiftText, string StatusText, string LinesText, string FlagsText, string ReviewText);

public sealed record DraftLineRow(JobLineInput Input, string StageText, string MachineText, string TimeText, string UnitsText);

/// <summary>
/// State and actions of the daily job report screen (Decision Log #82 R5, #95 D8, #102). The operator builds a draft from the released stages of their unit, saves it and submits it; the supervisor of
/// the unit (Planning when it has none, flagged) approves or returns it. Who may do what, the overlap checks and the flags are the Application's; the screen only relays typed values (Jalali date,
/// HH:mm times) and shows what it reports.
/// </summary>
public sealed class JobReportsViewModel(
    JobReportService reports,
    UnitQueueService queue,
    PlanningMasterService planning,
    CalendarService calendar,
    IJalaliDateService dates,
    DigitStyle digitStyle,
    Func<DateTime> utcNow,
    Func<Principal> currentUser) : ObservableObject
{
    public const string Heading = "گزارش کار روزانهٔ اپراتورها";

    public const string Note =
        "هر اپراتور فقط گزارش خودش را می‌سازد و ارسال می‌کند؛ هیچ‌کس به‌جای اپراتور دیگر گزارش نمی‌دهد. گزارش پذیرفته/بازکاری/ضایعات ندارد (آن‌ها را کنترل کیفیت تعیین می‌کند). "
        + "ردیف‌های کار خارج از شیفت، بدون برنامه، در روز مرخصی، روی ماشین غیر از برنامه یا هم‌پوشان با اپراتور دیگر رد نمی‌شوند؛ برای بررسی‌کننده علامت می‌خورند. سرپرست واحد گزارش را تأیید یا (با دلیل) برمی‌گرداند؛ "
        + "اگر واحد سرپرست ندارد، برنامه‌ریزی بررسی می‌کند و این موضوع علامت‌گذاری می‌شود. فقط گزارش‌های تأییدشده بعداً در هزینه و شاخص‌ها می‌آیند.";

    private readonly ObservableCollection<JobReportRow> _rows = [];
    private readonly ObservableCollection<DraftLineRow> _draft = [];
    private readonly ObservableCollection<UnitInfo> _units = [];
    private readonly ObservableCollection<StageChoice> _stages = [];
    private readonly ObservableCollection<Stored<Machine>> _machines = [];
    private readonly ObservableCollection<Stored<ShiftTemplate>> _templates = [];
    private JobReportCapabilities _capabilities = JobReportCapabilities.None;
    private UnitInfo? _unit;
    private Stored<DailyJobReport>? _editing;
    private DateOnly _from;
    private DateOnly _to;
    private string _message = string.Empty;
    private bool _isError;
    private bool _isBusy;
    private bool _denied;

    public ObservableCollection<JobReportRow> Rows => _rows;

    public ObservableCollection<DraftLineRow> Draft => _draft;

    public ObservableCollection<UnitInfo> Units => _units;

    public ObservableCollection<StageChoice> Stages => _stages;

    public ObservableCollection<Stored<Machine>> Machines => _machines;

    public ObservableCollection<Stored<ShiftTemplate>> Templates => _templates;

    public bool CanSubmit => _capabilities.CanSubmit;

    public bool CanReview => _capabilities.CanReview;

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

    /// <summary>Chooses the unit the draft is for and loads that unit's released stages (the operator is designated to it, or the call is refused).</summary>
    public Task SelectUnitAsync(long unitId) => RunAsync(async () =>
    {
        _unit = _units.FirstOrDefault(u => u.UnitId == unitId);
        await LoadStagesAsync(CancellationToken.None);
    });

    /// <summary>Adds one line to the draft from typed local times on the report date.</summary>
    public void AddLine(StageChoice? stage, Stored<Machine>? machine, string? dateText, string? startText, string? endText, string? downtime, string? downtimeReason, string? units, string? note)
    {
        if (stage is null || machine is null || !dates.TryParseDate(dateText, out var jalali, out _) || !TryTime(startText, out var start) || !TryTime(endText, out var end)
            || !TryInt(downtime ?? "0", out var downtimeMinutes) || !long.TryParse(Ascii(units ?? "0"), NumberStyles.None, CultureInfo.InvariantCulture, out var processed))
        {
            Fail(Messages.ForIssue(ValidationIssue.JobReportLineInvalid, JalaliDateError.None));
            return;
        }

        try
        {
            var date = dates.ToGregorian(jalali);
            var startUtc = dates.LocalToUtc(date, start);
            var endUtc = dates.LocalToUtc(end > start ? date : date.AddDays(1), end);
            var input = new JobLineInput(stage.Entry.RequestId, stage.Entry.ItemId, stage.Entry.StageNo, machine.Value.Id, startUtc, endUtc, downtimeMinutes, downtimeReason, processed, note);
            _draft.Add(new DraftLineRow(input, stage.Text, machine.Value.Code, $"{Digits(start.ToString("HH:mm", CultureInfo.InvariantCulture))} تا {Digits(end.ToString("HH:mm", CultureInfo.InvariantCulture))}", Digits(processed.ToString(CultureInfo.InvariantCulture))));
            Succeed("ردیف کار به پیش‌نویس اضافه شد؛ برای ثبت، «ذخیرهٔ پیش‌نویس» را بزنید.");
        }
        catch (ArgumentException)
        {
            Fail(Messages.ForIssue(ValidationIssue.JobReportLineInvalid, JalaliDateError.None));
        }
    }

    public void RemoveLine(DraftLineRow row) => _draft.Remove(row);

    public Task SaveDraftAsync(string? dateText, Stored<ShiftTemplate>? template) => CommandAsync("پیش‌نویس ذخیره شد.", async token =>
    {
        if (_unit is null || template is null || !dates.TryParseDate(dateText, out var jalali, out _))
        {
            return Outcome<int>.Invalid(ValidationIssue.JobReportDateInvalid);
        }

        var outcome = await reports.SaveDraftAsync(currentUser(), _editing?.Value.Id, _editing?.Version, dates.ToGregorian(jalali), _unit.UnitId, template.Value.Id, _draft.Select(d => d.Input).ToList(), token);
        if (outcome.Succeeded)
        {
            _editing = outcome.Value;
        }

        return outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
    });

    public Task SubmitAsync() => CommandAsync("گزارش ارسال شد.", async token =>
    {
        if (_editing is null)
        {
            return Outcome<int>.Invalid(ValidationIssue.JobReportDateInvalid);
        }

        var outcome = await reports.SubmitAsync(currentUser(), _editing.Value.Id, _editing.Version, token);
        if (outcome.Succeeded)
        {
            _editing = null;
            _draft.Clear();
        }

        return outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
    });

    /// <summary>Loads an own draft or returned report into the editor.</summary>
    public void Edit(JobReportRow row)
    {
        if (!row.Item.CanEdit)
        {
            return;
        }

        _editing = row.Item.Report;
        _draft.Clear();
        foreach (var line in row.Item.Report.Value.Lines)
        {
            var (_, start) = dates.UtcToLocal(line.StartUtc);
            var (_, end) = dates.UtcToLocal(line.EndUtc);
            _draft.Add(new DraftLineRow(
                new JobLineInput(line.RequestId, line.RequestItemId, line.StageNo, line.MachineId, line.StartUtc, line.EndUtc, line.DowntimeMinutes, line.DowntimeReason, line.ProcessedBaseUnits, line.Note),
                $"آیتم {line.RequestItemId} مرحلهٔ {Digits(line.StageNo.ToString(CultureInfo.InvariantCulture))}", _machines.FirstOrDefault(m => m.Value.Id == line.MachineId)?.Value.Code ?? "?",
                $"{Digits(start.ToString("HH:mm", CultureInfo.InvariantCulture))} تا {Digits(end.ToString("HH:mm", CultureInfo.InvariantCulture))}", Digits(line.ProcessedBaseUnits.ToString(CultureInfo.InvariantCulture))));
        }
    }

    public Task ApproveAsync(JobReportRow row) => CommandAsync("گزارش تأیید شد.", async token =>
    {
        var outcome = await reports.ReviewAsync(currentUser(), row.Item.Report.Value.Id, row.Item.Report.Version, true, null, token);
        return outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
    });

    public Task ReturnAsync(JobReportRow row, string? reason) => CommandAsync("گزارش با دلیل بازگردانده شد.", async token =>
    {
        var outcome = await reports.ReviewAsync(currentUser(), row.Item.Report.Value.Id, row.Item.Report.Version, false, reason, token);
        return outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
    });

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        if (_to == default)
        {
            var (today, _) = dates.UtcToLocal(utcNow());
            _from = today.AddDays(-13);
            _to = today;
        }

        var outcome = await reports.LoadAsync(currentUser(), _from, _to, cancellationToken);
        _rows.Clear();
        if (!outcome.Succeeded)
        {
            _capabilities = JobReportCapabilities.None;
            IsDenied = outcome.Status == OperationStatus.NotAuthorized;
            Fail(IsDenied ? "شما اجازهٔ مشاهدهٔ گزارش‌های کار را ندارید." : Messages.For(outcome));
            NotifyCaps();
            return;
        }

        IsDenied = false;
        _capabilities = outcome.Value!.Capabilities;
        NotifyCaps();
        var overview = await queue.GetOverviewAsync(currentUser(), cancellationToken);
        _units.Clear();
        foreach (var unit in overview.Succeeded ? overview.Value!.Units : [])
        {
            _units.Add(unit);
        }

        var calendarView = await calendar.LoadAsync(currentUser(), cancellationToken);
        _templates.Clear();
        foreach (var template in calendarView.Succeeded ? calendarView.Value!.Configuration.Templates.Where(t => t.Value.IsActive) : [])
        {
            _templates.Add(template);
        }

        var master = await planning.LoadAsync(currentUser(), cancellationToken);
        _machines.Clear();
        foreach (var machine in master.Succeeded ? master.Value!.Machines.Where(m => m.Value.IsActive) : [])
        {
            _machines.Add(machine);
        }

        var unitNames = _units.ToDictionary(u => u.UnitId, u => u.Name);
        var shiftCodes = _templates.ToDictionary(t => t.Value.Id, t => t.Value.Code);
        foreach (var item in outcome.Value.Items)
        {
            var r = item.Report.Value;
            _rows.Add(new JobReportRow(
                item, dates.FormatDate(r.WorkDate), item.OperatorName, unitNames.GetValueOrDefault(r.ProductionUnitId, "?"), shiftCodes.GetValueOrDefault(r.ShiftTemplateId, "?"),
                r.Status switch { JobReportStatus.Draft => "پیش‌نویس", JobReportStatus.Submitted => "ارسال‌شده", JobReportStatus.Returned => "بازگردانده‌شده: " + r.ReturnReason, _ => "تأییدشده" + (r.ReviewedByPlanningFallback ? " (بررسی توسط برنامه‌ریزی چون واحد سرپرست ندارد)" : string.Empty) },
                Digits(r.Lines.Count.ToString(CultureInfo.InvariantCulture)) + " ردیف کار، " + Digits(r.Lines.Sum(l => l.ProductiveMinutes).ToString(CultureInfo.InvariantCulture)) + " دقیقهٔ مفید",
                r.HasFlaggedLines ? $"{Digits(r.Lines.Count(l => l.Flags != JobLineFlags.None).ToString(CultureInfo.InvariantCulture))} ردیف کار علامت‌دار" : string.Empty,
                item.CanReview ? (item.ReviewWouldBePlanningFallback ? "قابل بررسی (به‌عنوان جایگزین سرپرست)" : "قابل بررسی") : string.Empty));
        }

        await LoadStagesAsync(cancellationToken);
        OnPropertyChanged(nameof(RangeText));
    }

    private async Task LoadStagesAsync(CancellationToken cancellationToken)
    {
        _stages.Clear();
        if (_unit is null || !_capabilities.CanSubmit)
        {
            return;
        }

        var loaded = await queue.LoadAsync(currentUser(), _unit.UnitId, cancellationToken);
        foreach (var entry in loaded.Succeeded ? loaded.Value!.Queue : [])
        {
            _stages.Add(new StageChoice(entry, $"{Messages.Ltr(entry.RequestNo)} / {Messages.Ltr(entry.ItemNo)} — مرحلهٔ {Digits(entry.StageNo.ToString(CultureInfo.InvariantCulture))}: {entry.StageTitle}"));
        }
    }

    private void NotifyCaps()
    {
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(CanReview));
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
                Fail(outcome.Status == OperationStatus.ConcurrencyConflict ? "این گزارش را کاربر دیگری تغییر داده است؛ کاری انجام نشد. فهرست دوباره بارگذاری شد." : Messages.For(outcome));
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

    private static bool TryInt(string text, out int value) => int.TryParse(Ascii(text), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private string Digits(string text) => digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : text;
}
