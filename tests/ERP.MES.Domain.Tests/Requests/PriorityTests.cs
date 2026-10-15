using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Tests.Requests;

public class PriorityTests
{
    [Fact]
    public void Labels_are_exactly_the_three_specified_persian_texts()
    {
        // Spelled with code points so a wrong letter form (for example Arabic yeh U+064A) cannot hide in the source.
        Assert.Equal("خیلی فوری", Priority.VeryUrgent.PersianName());
        Assert.Equal("فوری", Priority.Urgent.PersianName());
        Assert.Equal("عادی", Priority.Normal.PersianName());
    }

    [Fact]
    public void Codes_and_sort_orders_match_the_specification()
    {
        Assert.Equal(("VERY_URGENT", 1), (Priority.VeryUrgent.Code(), Priority.VeryUrgent.SortOrder()));
        Assert.Equal(("URGENT", 2), (Priority.Urgent.Code(), Priority.Urgent.SortOrder()));
        Assert.Equal(("NORMAL", 3), (Priority.Normal.Code(), Priority.Normal.SortOrder()));
        Assert.Equal(3, PriorityInfo.All.Count);
    }

    [Fact]
    public void Urgency_comparison_puts_the_highest_urgency_first_and_allows_ties()
    {
        Assert.True(PriorityInfo.CompareUrgency(Priority.VeryUrgent, Priority.Urgent) < 0);
        Assert.True(PriorityInfo.CompareUrgency(Priority.Normal, Priority.Urgent) > 0);
        Assert.Equal(0, PriorityInfo.CompareUrgency(Priority.Urgent, Priority.Urgent));

        var sorted = new[] { Priority.Normal, Priority.VeryUrgent, Priority.Urgent, Priority.VeryUrgent }
            .OrderBy(p => p.SortOrder()).ToArray();
        Assert.Equal(new[] { Priority.VeryUrgent, Priority.VeryUrgent, Priority.Urgent, Priority.Normal }, sorted);
    }

    [Fact]
    public void Unknown_values_are_rejected()
    {
        var unknown = (Priority)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => unknown.Code());
        Assert.Throws<ArgumentOutOfRangeException>(() => unknown.PersianName());
        Assert.Throws<ArgumentOutOfRangeException>(() => unknown.SortOrder());
    }

    [Fact]
    public void Several_items_may_share_a_priority_and_planning_can_change_it()
    {
        var request = RequestFixtures.NewRequest();
        var a = request.AddMake("1.10", priority: Priority.Urgent);
        var b = request.AddRepair("2.10", priority: Priority.Urgent);

        Assert.Equal(a.Priority, b.Priority);

        var later = RequestFixtures.Now.AddHours(1);
        Assert.True(a.ChangePriority(Priority.VeryUrgent, later));
        Assert.Equal(Priority.VeryUrgent, a.Priority);
        Assert.Equal(later, a.UpdatedAtUtc);
        Assert.Equal(Priority.Urgent, b.Priority);

        Assert.False(a.ChangePriority(Priority.VeryUrgent, later.AddHours(1)));
        Assert.Equal(later, a.UpdatedAtUtc);
    }

    [Fact]
    public void Changing_the_priority_rejects_unknown_values_and_non_utc_times()
    {
        var item = RequestFixtures.NewRequest().AddMake();

        Assert.Throws<ArgumentOutOfRangeException>(() => item.ChangePriority((Priority)0, RequestFixtures.Now));
        Assert.Throws<ArgumentException>(() => item.ChangePriority(Priority.Urgent, new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Local)));
        Assert.Equal(Priority.Normal, item.Priority);
    }
}
