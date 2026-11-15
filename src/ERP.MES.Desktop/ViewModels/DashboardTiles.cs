using System.Globalization;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>
/// The four tiles of the home page, computed from the persisted production queue (Master Specification 12.4): open requests, items in production, items waiting for QC and urgent items that are not complete.
/// Counts only; nothing is estimated. An item is "in production" when its route is open and its current stage is not merely planned.
/// </summary>
public static class DashboardTiles
{
    public static IReadOnlyList<DashboardTile> From(IReadOnlyList<ProductionQueueEntry> entries, DigitStyle digits)
    {
        ArgumentNullException.ThrowIfNull(entries);
        string N(int value) => digits == DigitStyle.Persian
            ? new string(value.ToString(CultureInfo.InvariantCulture).Select(c => (char)('۰' + (c - '0'))).ToArray())
            : value.ToString(CultureInfo.InvariantCulture);

        var open = entries.Where(e => e.RequestStatus is not (RequestStatus.Cancelled or RequestStatus.Closed)).ToList();
        var openItems = open.Where(e => e.RouteState != QueueRouteState.Completed).ToList();
        var requests = open.Select(e => e.RequestId).Distinct().Count();
        var inProduction = openItems.Count(e => e.RouteState == QueueRouteState.Open && e.CurrentStage is { Status: not ItemStageStatus.Planned });
        var waitingQc = openItems.Count(e => e.CurrentStage is { Status: ItemStageStatus.WaitingQc });
        var urgent = openItems.Count(e => e.Priority != Priority.Normal);
        return
        [
            new DashboardTile("درخواست‌های باز", N(requests), "درخواست‌های دارای آیتم که لغو یا بسته نشده‌اند"),
            new DashboardTile("آیتم‌های در تولید", N(inProduction), "مرحلهٔ جاری آزاد شده است (در صف، در حال اجرا، بازرسی یا بیرونی)"),
            new DashboardTile("در انتظار تأیید کنترل کیفیت", N(waitingQc), "مرحله به کنترل کیفیت تحویل شده و منتظر تصمیم است"),
            new DashboardTile("آیتم‌های فوری", N(urgent), "آیتم‌های نیمه‌کاره با اولویت «فوری» یا «خیلی فوری»"),
        ];
    }

    /// <summary>
    /// The approved-contract notice for Planning and Warehouse (Master Specification 18.5, Decision Log #4): when Management approves a contract the dashboard says so. It is derived directly from the contract
    /// state (an approved contract of a request that is neither cancelled nor closed), never a separate stored object; the numbers of the requests are listed (at most five).
    /// </summary>
    public static DashboardTile ContractNotice(IReadOnlyList<ContractListItem> contracts, DigitStyle digits)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        string N(int value) => digits == DigitStyle.Persian
            ? new string(value.ToString(CultureInfo.InvariantCulture).Select(c => (char)('۰' + (c - '0'))).ToArray())
            : value.ToString(CultureInfo.InvariantCulture);

        var approved = contracts.Where(c => c.ContractStatus == ContractStatus.Approved && c.RequestStatus is not (RequestStatus.Cancelled or RequestStatus.Closed))
            .OrderBy(c => c.RequestNo, StringComparer.Ordinal).ToList();
        if (approved.Count == 0)
        {
            return new DashboardTile("قراردادهای تأییدشده", N(0), "قرارداد تأییدشده‌ای برای درخواست‌های باز وجود ندارد");
        }

        var names = string.Join("، ", approved.Take(5).Select(c => c.RequestNo));
        var more = approved.Count > 5 ? $" (و {N(approved.Count - 5)} مورد دیگر)" : string.Empty;
        return new DashboardTile("قراردادهای تأییدشده", N(approved.Count), "مدیریت قرارداد را تأیید کرده و برای این درخواست‌ها در دسترس است: " + names + more);
    }

    /// <summary>
    /// The home page tiles for one user: the production counts when the user may read the production queue, and the approved-contract notice when the user may read contracts (Planning, Warehouse, Management).
    /// Null when the user may read neither, so the placeholder tiles stay.
    /// </summary>
    public static IReadOnlyList<DashboardTile>? Compose(IReadOnlyList<ProductionQueueEntry>? queue, IReadOnlyList<ContractListItem>? contracts, DigitStyle digits)
    {
        var tiles = new List<DashboardTile>();
        if (queue is not null)
        {
            tiles.AddRange(From(queue, digits));
        }

        if (contracts is not null)
        {
            tiles.Add(ContractNotice(contracts, digits));
        }

        return tiles.Count == 0 ? null : tiles;
    }
}
