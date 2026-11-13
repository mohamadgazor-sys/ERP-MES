using ERP.MES.Application.Requests;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The home page tiles are counts of the persisted production queue (Master Specification 12.4), not placeholders.
public class DashboardTilesTests
{
    private static ProductionQueueEntry Entry(long requestId, string itemNo, RequestStatus status, Priority priority, QueueRouteState route, ItemStageStatus? stage) =>
        new(requestId, "R" + requestId, 1, "مشتری", status, new DateOnly(2026, 11, 1), requestId * 100 + itemNo.Length, itemNo, "قطعه", WorkType.Make, 1m, priority, route, 2, route == QueueRouteState.Completed ? 2 : 0,
            stage is { } s ? new QueueStage(1, "فرزکاری", s, 1m, 1m, 1) : null, null, 1);

    [Fact]
    public void The_tiles_count_open_requests_items_in_production_items_waiting_for_QC_and_unfinished_urgent_items()
    {
        var entries = new[]
        {
            Entry(1, "1-1", RequestStatus.MgmtApprovedAtPlanning, Priority.Normal, QueueRouteState.Open, ItemStageStatus.Planned),
            Entry(1, "1-22", RequestStatus.MgmtApprovedAtPlanning, Priority.VeryUrgent, QueueRouteState.Open, ItemStageStatus.InProgress),
            Entry(2, "2-1", RequestStatus.MgmtApprovedAtPlanning, Priority.Urgent, QueueRouteState.Open, ItemStageStatus.WaitingQc),
            Entry(2, "2-22", RequestStatus.MgmtApprovedAtPlanning, Priority.Urgent, QueueRouteState.Completed, null),
            Entry(3, "3-1", RequestStatus.Cancelled, Priority.Urgent, QueueRouteState.Open, ItemStageStatus.InProgress),
        };

        var tiles = DashboardTiles.From(entries, DigitStyle.Persian).ToDictionary(t => t.Title, t => t.ValueText);
        Assert.Equal("۲", tiles["درخواست‌های باز"]);               // requests 1 and 2; 3 is cancelled
        Assert.Equal("۲", tiles["آیتم‌های در تولید"]);              // in progress and waiting QC; the planned one and the completed one are not
        Assert.Equal("۱", tiles["در انتظار تأیید کنترل کیفیت"]);
        Assert.Equal("۲", tiles["آیتم‌های فوری"]);                  // very urgent in progress, urgent waiting QC; the completed one and the cancelled request's do not count
    }

    [Fact]
    public async Task The_shell_shows_the_loaded_counts_and_keeps_the_placeholders_when_the_user_may_not_read_the_queue_or_the_load_fails()
    {
        var shell = new ShellViewModel(new JalaliDateService(DigitStyle.Persian, TimeZoneInfo.Utc), () => DateTime.UtcNow);
        Assert.All(shell.DashboardTiles, t => Assert.Equal("—", t.ValueText));

        shell.DashboardLoader = _ => Task.FromResult<IReadOnlyList<DashboardTile>?>(null);
        await shell.RefreshDashboardAsync();
        Assert.All(shell.DashboardTiles, t => Assert.Equal("—", t.ValueText));

        shell.DashboardLoader = _ => throw new InvalidOperationException("boom");
        await shell.RefreshDashboardAsync();
        Assert.All(shell.DashboardTiles, t => Assert.Equal("—", t.ValueText));

        shell.DashboardLoader = _ => Task.FromResult<IReadOnlyList<DashboardTile>?>(DashboardTiles.From([Entry(1, "1-1", RequestStatus.MgmtApprovedAtPlanning, Priority.Normal, QueueRouteState.Open, ItemStageStatus.InProgress)], DigitStyle.Persian));
        await shell.RefreshDashboardAsync();
        Assert.Equal("۱", shell.DashboardTiles.Single(t => t.Title == "آیتم‌های در تولید").ValueText);
    }
}
