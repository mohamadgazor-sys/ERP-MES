using System.Collections.ObjectModel;
using System.Globalization;
using ERP.MES.Application.Reporting;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;

namespace ERP.MES.Desktop.ViewModels;

public sealed record KpiUnitRow(
    string UnitText, string HoursText, string OvertimeText, string QuantityText, string RatesText, string PlanText, string UtilizationText, string StatusText);

public sealed record KpiOperatorRow(string RankText, string OperatorText, string HoursText, string DaysText);

/// <summary>
/// State and actions of the unit metrics screen (Decision Log #95 D6/D8, #106). Metrics are shown side by side and labelled - there is no composite score and no unit ranking. The figures use approved
/// reports and QC decisions only; units with reports still waiting for approval say so and are not authoritative. The operator list is an hour ranking only. Everything is computed by the Application.
/// </summary>
public sealed class KpiViewModel(KpiService kpi, IJalaliDateService dates, DigitStyle digitStyle, Func<DateTime> utcNow, Func<Principal> currentUser) : ObservableObject
{
    public const string Heading = "شاخص‌های واحدهای تولید";

    public const string Note =
        "هر شاخص جدا و با برچسب نوع آن نشان داده می‌شود؛ امتیاز واحد یا رتبهٔ «بهترین واحد» وجود ندارد. فقط گزارش‌های تأییدشده و تصمیم‌های کنترل کیفیت شمرده می‌شوند. ساعت مفید = ساعت گزارش‌شده منهای توقف؛ "
        + "پذیرفته/بازکاری/ضایعات به واحدی که مرحله را انجام داده نسبت داده می‌شود (واحد پایه)؛ نسبت‌ها سهم تصمیم‌های بازهٔ انتخاب‌شده هستند؛ برنامه در برابر واقعی برای مرحله‌های تکمیل‌شدهٔ بازه است؛ "
        + "بهره‌وری = ساعت مفید ÷ ساعت شیفت برنامه‌ریزی‌شده (با اضافه‌کاری می‌تواند از ۱۰۰٪ بیشتر شود). سهم اضافه‌کاری برآوردی است (هم‌پوشانی زمانی با اضافه‌کاری تأییدشده). "
        + "تا وقتی گزارش‌های واحد در انتظار تأیید باشند، اعداد آن قطعی نیست و تعداد آن‌ها نشان داده می‌شود. رتبهٔ اپراتورها فقط بر پایهٔ ساعت مفید است. نتیجه تا لحظهٔ اجرا معتبر است.";

    private readonly ObservableCollection<KpiUnitRow> _units = [];
    private readonly ObservableCollection<KpiOperatorRow> _operators = [];
    private DateOnly _from;
    private DateOnly _to;
    private string _message = string.Empty;
    private string _runText = string.Empty;
    private bool _isError;
    private bool _isBusy;
    private bool _denied;

    public ObservableCollection<KpiUnitRow> Units => _units;

    public ObservableCollection<KpiOperatorRow> Operators => _operators;

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

    public string RunText
    {
        get => _runText;
        private set => SetProperty(ref _runText, value);
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

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        if (_to == default)
        {
            var (today, _) = dates.UtcToLocal(utcNow());
            _to = today;
            _from = today.AddDays(-29);
        }

        var outcome = await kpi.ReportAsync(currentUser(), _from, _to, cancellationToken);
        _units.Clear();
        _operators.Clear();
        if (!outcome.Succeeded)
        {
            IsDenied = outcome.Status == OperationStatus.NotAuthorized;
            RunText = string.Empty;
            Fail(IsDenied ? "شما اجازهٔ مشاهدهٔ شاخص‌ها را ندارید (فقط برنامه‌ریزی، مدیریت و سرپرست واحد)." : Messages.For(outcome));
            return;
        }

        IsDenied = false;
        Succeed(string.Empty);
        var report = outcome.Value!;
        RunText = $"اجرا: {dates.FormatDateTime(report.RunAtUtc)}؛ بازه: {RangeText}؛ گزارش در انتظار تأیید: {Digits(report.TotalPendingReports.ToString(CultureInfo.InvariantCulture))}";
        foreach (var unit in report.Units)
        {
            _units.Add(new KpiUnitRow(
                $"{unit.Name} ({unit.Code})",
                Hours(unit.ProductiveMinutes),
                Hours(unit.EstimatedOvertimeMinutes) + " (برآورد)",
                $"پذیرفته {N(unit.AcceptedBaseUnits)} / بازکاری {N(unit.ReworkBaseUnits)} / ضایعات {N(unit.ScrapBaseUnits)}",
                $"پذیرش {Percent(unit.AcceptanceRate)} / بازکاری {Percent(unit.ReworkRate)} / ضایعات {Percent(unit.ScrapRate)}",
                unit.CompletedStages == 0 ? "مرحلهٔ تکمیل‌شده‌ای نیست" : $"{N(unit.CompletedStages)} مرحله؛ برنامه {Hours((int)decimal.Round(unit.PlannedMinutes))} در برابر واقعی {Hours(unit.ActualMinutes)}",
                unit.Utilization is null ? "شیفت برنامه‌ریزی‌شده‌ای نیست" : $"{Percent(unit.Utilization)} از {Hours(unit.ScheduledMinutes)}",
                unit.IsAuthoritative ? "قطعی" : $"قطعی نیست: {N(unit.PendingReports)} گزارش در انتظار تأیید"));
        }

        foreach (var person in report.Operators)
        {
            _operators.Add(new KpiOperatorRow(N(person.Rank), $"{person.Name} ({person.Code})", Hours(person.ProductiveMinutes), N(person.WorkedDays) + " روز"));
        }

        OnPropertyChanged(nameof(RangeText));
    }

    private string Hours(int minutes) => Digits((minutes / 60m).ToString("0.##", CultureInfo.InvariantCulture)) + " ساعت";

    private string Percent(decimal? ratio) => ratio is null ? "—" : Digits((ratio.Value * 100m).ToString("0.#", CultureInfo.InvariantCulture)) + "٪";

    private string N(long value) => Digits(value.ToString(CultureInfo.InvariantCulture));

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

    private string Digits(string text) => digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : text;
}
