using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Contracts;
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

    // ---- Approved-contract notice for Planning and Warehouse (Master Specification 18.5, Decision Log #4) ----

    private static ContractListItem Contract(long id, string requestNo, RequestStatus request, ContractStatus? contract) =>
        new(id, requestNo, "مشتری", request, contract, contract is null ? null : "CN-" + id, null, contract is null ? null : 1, null);

    [Fact]
    public void The_contract_notice_counts_approved_contracts_of_open_requests_and_lists_their_request_numbers_without_storing_anything()
    {
        var contracts = new[]
        {
            Contract(1, "R-2", RequestStatus.MgmtApprovedAtPlanning, ContractStatus.Approved),
            Contract(2, "R-1", RequestStatus.MgmtApprovedAtPlanning, ContractStatus.Approved),
            Contract(3, "R-3", RequestStatus.MgmtApprovedAtPlanning, ContractStatus.PendingManagement),   // not approved yet
            Contract(4, "R-4", RequestStatus.Closed, ContractStatus.Approved),                              // closed request
            Contract(5, "R-5", RequestStatus.Cancelled, ContractStatus.Approved),                           // cancelled request
            Contract(6, "R-6", RequestStatus.MgmtApprovedAtPlanning, null),                                 // no decision
        };

        var tile = DashboardTiles.ContractNotice(contracts, DigitStyle.Persian);
        Assert.Equal(("قراردادهای تأییدشده", "۲"), (tile.Title, tile.ValueText));
        Assert.Contains("R-1، R-2", tile.Note, StringComparison.Ordinal);
        Assert.DoesNotContain("R-3", tile.Note, StringComparison.Ordinal);
        Assert.Equal("۰", DashboardTiles.ContractNotice([], DigitStyle.Persian).ValueText);

        var many = Enumerable.Range(1, 8).Select(i => Contract(i, "R-" + i, RequestStatus.MgmtApprovedAtPlanning, ContractStatus.Approved)).ToArray();
        var big = DashboardTiles.ContractNotice(many, DigitStyle.Persian);
        Assert.Equal("۸", big.ValueText);
        Assert.Contains("و ۳ مورد دیگر", big.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_home_page_composes_the_production_counts_and_the_contract_notice_by_what_the_user_may_read()
    {
        var entries = new[] { Entry(1, "1-1", RequestStatus.MgmtApprovedAtPlanning, Priority.Normal, QueueRouteState.Open, ItemStageStatus.Planned) };
        var contracts = new[] { Contract(1, "R-1", RequestStatus.MgmtApprovedAtPlanning, ContractStatus.Approved) };

        Assert.Equal(5, DashboardTiles.Compose(entries, contracts, DigitStyle.Persian)!.Count);   // Planning: counts and notice
        Assert.Equal(["قراردادهای تأییدشده"], DashboardTiles.Compose(null, contracts, DigitStyle.Persian)!.Select(t => t.Title));   // Warehouse: the notice only
        Assert.Equal(4, DashboardTiles.Compose(entries, null, DigitStyle.Persian)!.Count);        // a user who reads no contracts
        Assert.Null(DashboardTiles.Compose(null, null, DigitStyle.Persian));                      // nothing readable: placeholders stay
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
