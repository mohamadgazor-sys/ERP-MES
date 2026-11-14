using System.Collections.ObjectModel;
using System.Globalization;
using ERP.MES.Application.Costing;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Warehouse;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>One choice of record the Warehouse can propose to reverse for the selected item.</summary>
public sealed record ReversalTarget(InvalidatedRecordKind Kind, long Id, string Text);

/// <summary>One reversal proposal of any item: what, why, who proposed it and when, and how Management decided.</summary>
public sealed record ReversalRow(WarehouseReversal Reversal, string ItemText, string TargetText, string NatureText, string ReasonText, string ProposedText, string StatusText, string DecisionText);

public sealed record CompletionRow(CompletionItem Item, string RequestText, string ItemText, string QuantityText, string ReceiptText, string PriceText, string FinishedText, string DispatchText);

/// <summary>
/// State and actions of the Warehouse completion screen (Master Specification 17.2; Decision Log #95 D7, #105). Three separate records: the final receipt after the last QC, the finished/sent mark (only after
/// the receipt and Management's price approval) and optional customer dispatches. The rules, duplicates and who may do what are the Application's; the screen shows the state and relays typed values.
/// </summary>
public sealed class CompletionViewModel(
    WarehouseCompletionService completion,
    IJalaliDateService dates,
    DigitStyle digitStyle,
    Func<Principal> currentUser,
    UserDirectoryService? users = null) : ObservableObject
{
    public const string Heading = "دریافت انبار و تکمیل";

    public const string Note =
        "سه ثبت جدا و غیرقابل ویرایش: ۱) دریافت نهایی انبار پس از آخرین کنترل کیفیت؛ ۲) تکمیل/ارسال که فقط پس از دریافت و پس از تأیید قیمت توسط مدیریت مجاز است؛ ۳) ارسال به مشتری (تاریخ و شمارهٔ سند) که اختیاری است و برای بسته‌شدن آیتم لازم نیست. "
        + "هیچ ثبتی جای ثبت دیگر را نمی‌گیرد. بازگشت قطعه از تأمین بیرونی دریافت نهایی نیست. فقط انبار ثبت می‌کند؛ برنامه‌ریزی و مدیریت می‌بینند.";

    private readonly ObservableCollection<CompletionRow> _rows = [];
    private readonly ObservableCollection<ReversalRow> _reversals = [];
    private CompletionCapabilities _capabilities = new(false, false, false, false);
    private string _message = string.Empty;
    private bool _isError;
    private bool _isBusy;
    private bool _denied;

    public ObservableCollection<CompletionRow> Rows => _rows;

    public ObservableCollection<ReversalRow> Reversals => _reversals;

    public bool HasReversals => _reversals.Count > 0;

    public bool CanProposeReversal => _capabilities.CanProposeReversal;

    public bool CanDecideReversal => _capabilities.CanDecideReversal;

    public static IReadOnlyList<(ReversalNature Nature, string Text)> Natures { get; } =
    [
        (ReversalNature.RecordedInError, "رکورد اشتباه ثبت شده است"),
        (ReversalNature.PhysicalMovementCorrection, "حرکت فیزیکی واقعاً رخ داده و با تراکنش جبرانی اصلاح می‌شود"),
    ];

    /// <summary>The records of the item that can still be proposed for reversal (current ones only); the original records stay in the list as history.</summary>
    public static IReadOnlyList<ReversalTarget> TargetsOf(CompletionItem item)
    {
        var targets = new List<ReversalTarget>();
        if (item.Receipt is { } receipt && item.ReceiptInvalidation is null)
        {
            targets.Add(new ReversalTarget(InvalidatedRecordKind.WarehouseReceipt, receipt.Id, "رسید انبار"));
        }

        if (item.FinishedSent is { } mark && item.FinishedSentInvalidation is null)
        {
            targets.Add(new ReversalTarget(InvalidatedRecordKind.FinishedSent, mark.Id, "تکمیل/ارسال شد"));
        }

        foreach (var dispatch in item.Dispatches.Where(d => !item.ReversedDispatchIds.Contains(d.Id)))
        {
            targets.Add(new ReversalTarget(InvalidatedRecordKind.CustomerDispatch, dispatch.Id, "ارسال به مشتری (" + dispatch.DocumentReference + ")"));
        }

        return targets;
    }

    public bool CanReceive => _capabilities.CanReceive;

    public bool CanMarkFinished => _capabilities.CanMarkFinished;

    public bool CanDispatch => _capabilities.CanDispatch;

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

    public Task ReceiveAsync(CompletionRow row, string? note) => CommandAsync("دریافت نهایی ثبت شد.", async () =>
        Relay(await completion.ReceiveAsync(currentUser(), row.Item.ItemId, note)));

    public Task MarkFinishedAsync(CompletionRow row) => CommandAsync("آیتم تکمیل/ارسال‌شده ثبت شد.", async () =>
        Relay(await completion.MarkFinishedSentAsync(currentUser(), row.Item.ItemId)));

    public Task DispatchAsync(CompletionRow row, string? dateText, string? reference, string? note) => CommandAsync("ارسال به مشتری ثبت شد.", async () =>
        Relay(await completion.RecordDispatchAsync(currentUser(), row.Item.ItemId, dateText, reference, note)));

    /// <summary>The Warehouse proposes the reversal of the chosen record of the item; nothing changes until Management approves.</summary>
    public Task ProposeReversalAsync(CompletionRow row, ReversalTarget? target, ReversalNature nature, string? reason) => CommandAsync("پیشنهاد برگشت ثبت شد و منتظر تصمیم مدیریت است؛ رکورد اصلی تغییری نکرده است.", async () =>
        target is null
            ? Outcome<int>.Invalid(ValidationIssue.ReversalTargetMissing)
            : Relay(await completion.ProposeReversalAsync(currentUser(), row.Item.ItemId, target.Kind, target.Id, nature, reason)));

    /// <summary>Management approves (the original is marked reversed, never deleted) or rejects (a reason is required) the proposal.</summary>
    public Task DecideReversalAsync(ReversalRow row, bool approve, string? reason) => CommandAsync(approve ? "برگشت تأیید شد؛ رکورد اصلی به‌عنوان برگشت‌خورده علامت خورد و در سابقه می‌ماند." : "پیشنهاد برگشت رد شد؛ رکورد اصلی جاری می‌ماند.", async () =>
        Relay(await completion.DecideReversalAsync(currentUser(), row.Reversal.Id, approve, reason)));

    private static Outcome<int> Relay<T>(Outcome<T> outcome) => outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var outcome = await completion.LoadAsync(currentUser(), cancellationToken);
        _rows.Clear();
        if (!outcome.Succeeded)
        {
            _capabilities = new CompletionCapabilities(false, false, false, false);
            _reversals.Clear();
            OnPropertyChanged(nameof(HasReversals));
            IsDenied = outcome.Status == OperationStatus.NotAuthorized;
            Fail(IsDenied ? "شما اجازهٔ مشاهدهٔ دریافت و تکمیل انبار را ندارید." : Messages.For(outcome));
            NotifyCaps();
            return;
        }

        IsDenied = false;
        _capabilities = outcome.Value!.Capabilities;
        NotifyCaps();
        foreach (var item in outcome.Value.Items)
        {
            _rows.Add(new CompletionRow(
                item,
                item.RequestNo,
                $"{item.ItemNo} — {item.PartName}",
                Digits(item.AcceptedBaseUnits.ToString(CultureInfo.InvariantCulture)),
                item.Receipt is { } receipt ? "دریافت‌شده " + dates.FormatDateTime(receipt.ReceivedAtUtc) + StateSuffix(item.ReceiptInvalidation) : "دریافت نشده",
                item.LatestDecisionStatus switch
                {
                    PriceDecisionStatus.Approved => $"قیمت تأییدشده: {Rial(item.ApprovedPriceRial ?? 0)} ریال",
                    PriceDecisionStatus.Submitted => "قیمت در انتظار مدیریت",
                    PriceDecisionStatus.Draft => "قیمت ارسال نشده",
                    _ => "هزینه محاسبه نشده",
                },
                item.FinishedSent is { } finished ? "تکمیل/ارسال‌شده " + dates.FormatDateTime(finished.MarkedAtUtc) + StateSuffix(item.FinishedSentInvalidation) : string.Empty,
                item.Dispatches.Count == 0 ? string.Empty : string.Join("؛ ", item.Dispatches.Select(d => $"{dates.FormatDate(d.DispatchDate)} ({d.DocumentReference})" + (item.ReversedDispatchIds.Contains(d.Id) ? " — برگشت‌خورده" : item.FlaggedDispatchIds.Contains(d.Id) ? " — پس از ارسال، اصلاح کنترل کیفیت رخ داد (سابقه حفظ شده)" : string.Empty)))));
        }

        _reversals.Clear();
        var ids = outcome.Value.Items.SelectMany(i => i.Reversals).SelectMany(r => new[] { r.ProposedByUserId, r.DecidedByUserId ?? 0 }).Where(id => id != 0).Distinct();
        var names = await ActorLookup.DescribeAsync(users, currentUser(), ids, cancellationToken);
        foreach (var item in outcome.Value.Items)
        {
            foreach (var reversal in item.Reversals)
            {
                _reversals.Add(new ReversalRow(
                    reversal,
                    $"{item.RequestNo} / {item.ItemNo}",
                    reversal.TargetKind switch
                    {
                        InvalidatedRecordKind.WarehouseReceipt => "رسید انبار",
                        InvalidatedRecordKind.FinishedSent => "تکمیل/ارسال شد",
                        _ => "ارسال به مشتری (" + (item.Dispatches.FirstOrDefault(d => d.Id == reversal.TargetId)?.DocumentReference ?? "؟") + ")",
                    },
                    Natures.First(n => n.Nature == reversal.Nature).Text,
                    reversal.ProposalReason,
                    ActorLookup.Text(names, reversal.ProposedByUserId) + " — " + dates.FormatDateTime(reversal.ProposedAtUtc),
                    reversal.Status switch
                    {
                        ReversalStatus.Proposed => "منتظر تصمیم مدیریت",
                        ReversalStatus.Approved => "تأیید شد (رکورد اصلی برگشت‌خورده است)",
                        _ => "رد شد (رکورد اصلی جاری است)",
                    },
                    reversal.DecidedAtUtc is { } decidedAt
                        ? ActorLookup.Text(names, reversal.DecidedByUserId) + " — " + dates.FormatDateTime(decidedAt) + (reversal.DecisionReason is { } why ? " — " + why : string.Empty)
                        : string.Empty));
            }
        }

        OnPropertyChanged(nameof(HasReversals));
    }

    private static string StateSuffix(RecordInvalidation? marker) => marker switch
    {
        null => string.Empty,
        { Effect: InvalidationEffect.Reversed } => " — برگشت‌خورده (سابقه حفظ شده)",
        _ => " — غیرجاری (اصلاح کنترل کیفیت)",
    };

    private void NotifyCaps()
    {
        OnPropertyChanged(nameof(CanProposeReversal));
        OnPropertyChanged(nameof(CanDecideReversal));
        OnPropertyChanged(nameof(CanReceive));
        OnPropertyChanged(nameof(CanMarkFinished));
        OnPropertyChanged(nameof(CanDispatch));
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
                Fail(Messages.For(outcome));
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

    private string Rial(decimal amount) => Digits(amount.ToString("N0", CultureInfo.InvariantCulture)).Replace(",", "٬", StringComparison.Ordinal);

    private string Digits(string text) => digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : text;
}
