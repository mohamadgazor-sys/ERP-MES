using System.Collections.ObjectModel;
using System.Globalization;
using ERP.MES.Application.Costing;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Planning;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.ViewModels;

public sealed record CostingRow(CostingItemSummary Summary, string RequestText, string ItemText, string QuantityText, string StatusText, string CostText, string PriceText, string ActorText = "");

/// <summary>
/// State and actions of the cost and price screen (docs/COSTING_DESIGN.md; Decision Log #91, #95 D3/D4, #104). Warehouse calculates and submits, Management approves or changes the price, Planning reads.
/// The formula, the gaps and who may do what are the Application's; the screen shows the amounts in Iranian Rial (never converted to Toman) and relays the typed price, reason and contract reference.
/// </summary>
public sealed class CostingViewModel(
    CostingService costing,
    PlanningMasterService planning,
    IJalaliDateService dates,
    DigitStyle digitStyle,
    Func<Principal> currentUser,
    UserDirectoryService? users = null) : ObservableObject
{
    public const string Heading = "هزینه و قیمت آیتم‌ها";

    public const string Note =
        "فقط آیتم‌هایی که همهٔ مرحله‌هایشان تأیید کنترل کیفیت شده است فهرست می‌شوند. هزینه = ساعت‌های تأییدشدهٔ گزارش کار × نرخ ماشین در روز کار + مادهٔ اولیهٔ واحدهای واردشده به تولید؛ ضایعات و بازکاری روی واحدهای پذیرفته‌شده تقسیم می‌شود. "
        + "انبار محاسبه و ارسال می‌کند؛ فقط مدیریت قیمت را تأیید یا (با دلیل) تغییر می‌دهد. برای آیتم قراردادِ تأییدشده، قیمت همان مبلغ قرارداد است و مدیریت آن را با شمارهٔ قرارداد ثبت می‌کند "
        + "(برنامه مبلغ قرارداد را ذخیره نمی‌کند)؛ هزینهٔ محاسبه‌شده فقط اطلاع است. همهٔ مبلغ‌ها ریال هستند. نرخ یا قیمت ناموجود، محاسبه را متوقف می‌کند و جاافتادگی نام برده می‌شود.";

    private readonly ObservableCollection<CostingRow> _rows = [];
    private readonly ObservableCollection<string> _detail = [];
    private Dictionary<long, string> _machines = [];
    private CostingCapabilities _capabilities = new(false, false, false);
    private string _message = string.Empty;
    private bool _isError;
    private bool _isBusy;
    private bool _denied;

    public ObservableCollection<CostingRow> Rows => _rows;

    public ObservableCollection<string> Detail => _detail;

    public bool CanCalculate => _capabilities.CanCalculate;

    public bool CanApprove => _capabilities.CanApprove;

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

    public Task CalculateAsync(CostingRow row) => CommandAsync("هزینه محاسبه شد؛ برای تصمیم قیمت، آن را ارسال کنید.", async () =>
    {
        var outcome = await costing.CalculateAsync(currentUser(), row.Summary.ItemId);
        return outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError, outcome.Warnings);
    });

    public Task SubmitAsync(CostingRow row) => CommandAsync("قیمت برای مدیریت ارسال شد.", async () =>
    {
        if (row.Summary.LatestDecision is not { } decision)
        {
            return Outcome<int>.Fail(OperationStatus.InvalidState);
        }

        var outcome = await costing.SubmitAsync(currentUser(), decision.Value.Id, decision.Version);
        return outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
    });

    /// <summary>Approves with the typed price (blank accepts the calculated proposal), reason and contract reference.</summary>
    public Task ApproveAsync(CostingRow row, string? priceText, string? reason, string? contractReference) => CommandAsync("قیمت تأیید شد.", async () =>
    {
        if (row.Summary.LatestDecision is not { } decision)
        {
            return Outcome<int>.Fail(OperationStatus.InvalidState);
        }

        decimal? price = null;
        if (!string.IsNullOrWhiteSpace(priceText))
        {
            if (!decimal.TryParse(Ascii(priceText), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                return Outcome<int>.Invalid(ValidationIssue.ItemPriceInvalid);
            }

            price = parsed;
        }

        var outcome = await costing.ApproveAsync(currentUser(), decision.Value.Id, decision.Version, price, reason, contractReference);
        return outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
    });

    /// <summary>Shows the lines of the latest snapshot of the item: every approved report line with its rate, and the material.</summary>
    public Task ShowDetailAsync(CostingRow row) => RunAsync(async () =>
    {
        _detail.Clear();
        var history = await costing.HistoryAsync(currentUser(), row.Summary.ItemId);
        if (!history.Succeeded || history.Value!.Calculations.Count == 0)
        {
            return;
        }

        var calc = history.Value.Calculations[^1].Value;
        foreach (var line in calc.Lines)
        {
            _detail.Add(line.Kind == CostLineKind.Machining
                ? $"ماشین {_machines.GetValueOrDefault(line.MachineId ?? 0, "?")}، {dates.FormatDate(line.WorkDate!.Value)}، {Digits(line.Minutes!.Value.ToString(CultureInfo.InvariantCulture))} دقیقه × {Rial(line.UnitPriceRial)} ریال در ساعت = {Rial(line.AmountRial)} ریال"
                : $"ماده، {Digits(line.WeightKg!.Value.ToString("0.####", CultureInfo.InvariantCulture))} کیلوگرم × {Rial(line.UnitPriceRial)} ریال در کیلوگرم (قیمت روز {dates.FormatDate(line.WorkDate!.Value)}) = {Rial(line.AmountRial)} ریال");
        }

        _detail.Add($"جمع: {Rial(calc.TotalRial)} ریال؛ سهم هر واحد پایه: {Rial(calc.UnitCostRial)} ریال (روی {Digits(calc.AcceptedBaseUnits.ToString(CultureInfo.InvariantCulture))} واحد پذیرفته‌شده)");
    });

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var outcome = await costing.LoadAsync(currentUser(), cancellationToken);
        _rows.Clear();
        _detail.Clear();
        if (!outcome.Succeeded)
        {
            _capabilities = new CostingCapabilities(false, false, false);
            IsDenied = outcome.Status == OperationStatus.NotAuthorized;
            Fail(IsDenied ? "شما اجازهٔ مشاهدهٔ هزینه و قیمت را ندارید." : Messages.For(outcome));
            NotifyCaps();
            return;
        }

        IsDenied = false;
        _capabilities = outcome.Value!.Capabilities;
        NotifyCaps();
        var master = await planning.LoadAsync(currentUser(), cancellationToken);
        _machines = master.Succeeded ? master.Value!.Machines.ToDictionary(m => m.Value.Id, m => m.Value.Code) : [];
        var actors = await ActorLookup.DescribeAsync(
            users, currentUser(),
            outcome.Value.Items.SelectMany(i => new long?[] { i.LatestCalculation?.Value.CalculatedByUserId, i.LatestDecision?.Value.SubmittedByUserId, i.LatestDecision?.Value.ApprovedByUserId }).Where(id => id is not null).Select(id => id!.Value),
            cancellationToken);
        foreach (var item in outcome.Value.Items)
        {
            var calc = item.LatestCalculation?.Value;
            var decision = item.LatestDecision?.Value;
            _rows.Add(new CostingRow(
                item,
                item.RequestNo,
                $"{item.ItemNo} — {item.PartName}" + (item.ContractApproved && item.WorkType == WorkType.Make ? " (قرارداد)" : string.Empty),
                Digits(item.AcceptedBaseUnits.ToString(CultureInfo.InvariantCulture)),
                decision is null ? "محاسبه نشده" : decision.Status switch { PriceDecisionStatus.Draft => "محاسبه‌شده (ارسال نشده)", PriceDecisionStatus.Submitted => "ارسال‌شده به مدیریت", _ => "قیمت تأییدشده" },
                calc is null ? string.Empty : $"{Rial(calc.TotalRial)} ریال (هر واحد {Rial(calc.UnitCostRial)})",
                decision?.ApprovedPriceRial is { } approved ? $"{Rial(approved)} ریال" : decision?.ProposedPriceRial is { } proposed ? $"پیشنهاد {Rial(proposed)} ریال" : string.Empty,
                ActorsOf(actors, calc?.CalculatedByUserId, decision?.SubmittedByUserId, decision?.ApprovedByUserId)));
        }
    }

    private static string ActorsOf(IReadOnlyDictionary<long, string> names, long? calculatedBy, long? submittedBy, long? approvedBy)
    {
        var parts = new List<string>();
        if (calculatedBy is not null)
        {
            parts.Add("محاسبه: " + ActorLookup.Text(names, calculatedBy));
        }

        if (submittedBy is not null)
        {
            parts.Add("ارسال: " + ActorLookup.Text(names, submittedBy));
        }

        if (approvedBy is not null)
        {
            parts.Add("تأیید: " + ActorLookup.Text(names, approvedBy));
        }

        return string.Join("؛ ", parts);
    }

    private void NotifyCaps()
    {
        OnPropertyChanged(nameof(CanCalculate));
        OnPropertyChanged(nameof(CanApprove));
    }

    private async Task CommandAsync(string success, Func<Task<Outcome<int>>> command)
    {
        if (IsBusy)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var outcome = await command();
            if (outcome.Succeeded)
            {
                Succeed(success);
            }
            else
            {
                var text = outcome.Status == OperationStatus.ConcurrencyConflict ? "این مورد را کاربر دیگری تغییر داده است؛ کاری انجام نشد. فهرست دوباره بارگذاری شد." : Messages.For(outcome);
                Fail(outcome.WarningList.Count == 0 ? text : text + " " + string.Join(" ", outcome.WarningList.Select(Gap)));
            }

            var message = Message;
            var error = IsErrorMessage;
            await ReloadAsync(CancellationToken.None);
            Message = message;
            IsErrorMessage = error;
        });
    }

    private string Gap(string token)
    {
        var parts = token.Split('|');
        var date = parts.Length > 2 && DateOnly.TryParseExact(parts[2], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? dates.FormatDate(d) : parts.ElementAtOrDefault(2) ?? "?";
        return parts[0] == "RATE" ? $"نرخ ماشین {parts[1]} در تاریخ {date} تعریف نشده است." : $"قیمت ماده {parts[1]} در تاریخ {date} تعریف نشده است.";
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
        new(text.Trim().Replace(",", string.Empty, StringComparison.Ordinal).Replace("٬", string.Empty, StringComparison.Ordinal).Select(c => c switch { >= '۰' and <= '۹' => (char)('0' + (c - '۰')), >= '٠' and <= '٩' => (char)('0' + (c - '٠')), _ => c }).ToArray());

    private string Rial(decimal amount) => Digits(amount.ToString("N0", CultureInfo.InvariantCulture)).Replace(",", "٬", StringComparison.Ordinal);

    private string Digits(string text) => digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : text;
}
