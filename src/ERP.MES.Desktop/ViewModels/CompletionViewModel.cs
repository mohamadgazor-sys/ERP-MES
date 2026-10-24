using System.Collections.ObjectModel;
using System.Globalization;
using ERP.MES.Application.Costing;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Costing;

namespace ERP.MES.Desktop.ViewModels;

public sealed record CompletionRow(CompletionItem Item, string RequestText, string ItemText, string QuantityText, string ReceiptText, string PriceText, string FinishedText, string DispatchText);

/// <summary>
/// State and actions of the Warehouse completion screen (Master Specification 17.2; Decision Log #95 D7, #105). Three separate records: the final receipt after the last QC, the finished/sent mark (only after
/// the receipt and Management's price approval) and optional customer dispatches. The rules, duplicates and who may do what are the Application's; the screen shows the state and relays typed values.
/// </summary>
public sealed class CompletionViewModel(
    WarehouseCompletionService completion,
    IJalaliDateService dates,
    DigitStyle digitStyle,
    Func<Principal> currentUser) : ObservableObject
{
    public const string Heading = "دریافت انبار و تکمیل";

    public const string Note =
        "سه ثبت جدا و غیرقابل ویرایش: ۱) دریافت نهایی انبار پس از آخرین کنترل کیفیت؛ ۲) تکمیل/ارسال که فقط پس از دریافت و پس از تأیید قیمت توسط مدیریت مجاز است؛ ۳) ارسال به مشتری (تاریخ و شمارهٔ سند) که اختیاری است و برای بسته‌شدن آیتم لازم نیست. "
        + "هیچ ثبتی جای ثبت دیگر را نمی‌گیرد. بازگشت قطعه از تأمین بیرونی دریافت نهایی نیست. فقط انبار ثبت می‌کند؛ برنامه‌ریزی و مدیریت می‌بینند.";

    private readonly ObservableCollection<CompletionRow> _rows = [];
    private CompletionCapabilities _capabilities = new(false, false, false, false);
    private string _message = string.Empty;
    private bool _isError;
    private bool _isBusy;
    private bool _denied;

    public ObservableCollection<CompletionRow> Rows => _rows;

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

    private static Outcome<int> Relay<T>(Outcome<T> outcome) => outcome.Succeeded ? Outcome<int>.Ok(0) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var outcome = await completion.LoadAsync(currentUser(), cancellationToken);
        _rows.Clear();
        if (!outcome.Succeeded)
        {
            _capabilities = new CompletionCapabilities(false, false, false, false);
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
                item.Receipt is { } receipt ? "دریافت‌شده " + dates.FormatDateTime(receipt.ReceivedAtUtc) : "دریافت نشده",
                item.LatestDecisionStatus switch
                {
                    PriceDecisionStatus.Approved => $"قیمت تأییدشده: {Rial(item.ApprovedPriceRial ?? 0)} ریال",
                    PriceDecisionStatus.Submitted => "قیمت در انتظار مدیریت",
                    PriceDecisionStatus.Draft => "قیمت ارسال نشده",
                    _ => "هزینه محاسبه نشده",
                },
                item.FinishedSent is { } finished ? "تکمیل/ارسال‌شده " + dates.FormatDateTime(finished.MarkedAtUtc) : string.Empty,
                item.Dispatches.Count == 0 ? string.Empty : string.Join("؛ ", item.Dispatches.Select(d => $"{dates.FormatDate(d.DispatchDate)} ({d.DocumentReference})"))));
        }
    }

    private void NotifyCaps()
    {
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
