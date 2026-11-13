using System.Globalization;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Calendar;
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
}
