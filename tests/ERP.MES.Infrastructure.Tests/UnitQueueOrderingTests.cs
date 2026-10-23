using ERP.MES.Application.Production;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Infrastructure.Tests;

// The pure ordering of a unit queue (Decision Log #78, #99).
public class UnitQueueOrderingTests
{
    private static readonly DateTime T0 = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    private static UnitQueueEntry Entry(
        string itemNo, Priority priority = Priority.Normal, QueueCategory category = QueueCategory.Waiting, int? position = null, DateTime? available = null, int? overrideRank = null,
        long requestId = 1, long itemId = 1, int stageNo = 1, string requestDate = "2026-10-12") =>
        new(requestId, "R-" + requestId, "Customer", DateOnly.Parse(requestDate, System.Globalization.CultureInfo.InvariantCulture), RequestStatus.DrawingReturnedToPlanning, itemId, itemNo, "Part", WorkType.Make,
            stageNo, 1, "Stage", category == QueueCategory.InProgress ? ItemStageStatus.InProgress : ItemStageStatus.Waiting, 1m, 1m, priority, category, QueueBlock.None, null, position, available, overrideRank, false, null, null);

    private static string[] Order(params UnitQueueEntry[] entries) => UnitQueueOrdering.Sort(entries).Select(e => e.ItemNo).ToArray();

    [Fact]
    public void Category_comes_first_then_override_then_priority_then_position_then_first_come_first_served()
    {
        Assert.Equal(
            ["running", "stopped", "overridden", "veryUrgent", "urgent", "pos1", "pos2", "early", "late"],
            Order(
                Entry("late", available: T0.AddHours(2), itemId: 9),
                Entry("early", available: T0, itemId: 8),
                Entry("pos2", position: 2, available: T0.AddHours(5), itemId: 7),
                Entry("pos1", position: 1, available: T0.AddHours(6), itemId: 6),
                Entry("urgent", Priority.Urgent, itemId: 5),
                Entry("veryUrgent", Priority.VeryUrgent, itemId: 4),
                Entry("overridden", overrideRank: 1, itemId: 3),
                Entry("stopped", category: QueueCategory.Stopped, itemId: 2),
                Entry("running", category: QueueCategory.InProgress, itemId: 1)));
    }

    [Fact]
    public void Overrides_order_among_themselves_by_rank_and_do_not_pass_a_higher_category()
    {
        Assert.Equal(
            ["running", "second", "first"],
            Order(
                Entry("first", Priority.VeryUrgent, overrideRank: 2, itemId: 1),
                Entry("second", Priority.Normal, overrideRank: 1, itemId: 2),
                Entry("running", Priority.Normal, QueueCategory.InProgress, itemId: 3)));
    }

    [Fact]
    public void Equal_keys_fall_back_to_stable_identifiers_so_the_order_is_the_same_on_every_load()
    {
        var entries = new[]
        {
            Entry("c", requestId: 2, itemId: 30, stageNo: 1),
            Entry("b", requestId: 1, itemId: 20, stageNo: 2),
            Entry("a", requestId: 1, itemId: 20, stageNo: 1),
            Entry("d", requestId: 1, itemId: 10, stageNo: 1),
        };

        Assert.Equal(["d", "a", "b", "c"], Order(entries));
        Assert.Equal(["d", "a", "b", "c"], Order(entries.Reverse().ToArray()));
        Assert.Equal([1, 2, 3, 4], UnitQueueOrdering.Sort(entries).Select(e => e.Order).ToArray());
    }

    [Fact]
    public void A_stage_without_an_availability_time_uses_its_request_date_and_planned_end_is_never_invented()
    {
        var older = Entry("older", requestDate: "2026-10-01", itemId: 2);
        var newer = Entry("newer", available: T0, requestDate: "2026-10-15", itemId: 1);

        Assert.Equal(["older", "newer"], Order(newer, older));
        Assert.All(UnitQueueOrdering.Sort([older, newer]), e => Assert.Null(e.PlannedEndUtc));
    }
}
