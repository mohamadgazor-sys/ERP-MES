using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Domain.Tests.Scheduling;

// Unavailable intervals (machine downtime) in the working-calendar forecast (Decision Log #128).
public class DowntimeCalendarTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    private const long Unit = 9;

    private static DateTime T(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0);

    // Saturday 24 to Thursday 29 work 08:00-17:00 with a 12:00-13:00 break; Friday is off.
    private static WorkingCalendar Calendar() => new(
        Enum.GetValues<DayOfWeek>().Select(d => new WeeklyWorkday(d, d != DayOfWeek.Friday, Now)).ToList(),
        [],
        [new UnitShift(1, Unit, 1, 3, 1, false, 1, Now)],
        [new ShiftTemplate(1, "D", "روزکار", new TimeOnly(8, 0), new TimeOnly(17, 0), new TimeOnly(12, 0), new TimeOnly(13, 0), 1, Now)]);

    [Fact]
    public void Merge_joins_overlapping_and_touching_intervals_and_drops_empty_ones()
    {
        var merged = WorkingCalendar.Merge([(T(24, 10), T(24, 11)), (T(24, 8), T(24, 9)), (T(24, 9), T(24, 9, 30)), (T(24, 10, 30), T(24, 12)), (T(24, 14), T(24, 14))]);
        Assert.Equal([(T(24, 8), T(24, 9, 30)), (T(24, 10), T(24, 12))], merged);
    }

    [Fact]
    public void Subtract_cuts_holes_and_ends_out_of_intervals()
    {
        IReadOnlyList<(DateTime, DateTime)> working = [(T(24, 8), T(24, 12)), (T(24, 13), T(24, 17))];
        Assert.Equal(working, WorkingCalendar.Subtract(working, []));
        Assert.Equal(
            [(T(24, 8), T(24, 9)), (T(24, 10), T(24, 11)), (T(24, 15), T(24, 17))],
            WorkingCalendar.Subtract(working, [(T(24, 9), T(24, 10)), (T(24, 11), T(24, 15)), (T(25, 8), T(25, 9))]));
        Assert.Empty(WorkingCalendar.Subtract(working, [(T(24, 7), T(24, 18))]));
    }

    [Fact]
    public void Intersect_keeps_only_the_time_in_which_every_list_is_unavailable()
    {
        IReadOnlyList<(DateTime, DateTime)> first = [(T(24, 8), T(24, 9)), (T(24, 14), T(24, 16))];
        IReadOnlyList<(DateTime, DateTime)> second = [(T(24, 8), T(24, 10)), (T(24, 15), T(24, 17))];
        Assert.Equal([(T(24, 8), T(24, 9)), (T(24, 15), T(24, 16))], WorkingCalendar.Intersect([first, second]));
        Assert.Empty(WorkingCalendar.Intersect([first, [(T(24, 10), T(24, 11))]]));
        Assert.Empty(WorkingCalendar.Intersect([]));
        Assert.Equal(first, WorkingCalendar.Intersect([first]));
    }

    [Fact]
    public void Unavailable_time_is_skipped_by_the_forecast()
    {
        var calendar = Calendar();
        Assert.Equal(T(24, 9), calendar.AddWorkingMinutes(Unit, T(24, 8), 60));

        // Down 08:00-09:30: the hour of work ends at 10:30.
        Assert.Equal(T(24, 10, 30), calendar.AddWorkingMinutes(Unit, T(24, 8), 60, unavailable: [(T(24, 8), T(24, 9, 30))]));

        // Down for the rest of Thursday 29 from 16:00 and the whole of Saturday 31: the remaining work moves to Sunday 1 November (Friday is off anyway).
        Assert.Equal(new DateTime(2026, 11, 1, 8, 30, 0), calendar.AddWorkingMinutes(Unit, T(29, 15), 90, unavailable: [(T(29, 16), new DateTime(2026, 11, 1, 0, 0, 0))]));

        // Zero minutes still end at the start, and an empty list changes nothing.
        Assert.Equal(T(24, 8), calendar.AddWorkingMinutes(Unit, T(24, 8), 0, unavailable: [(T(24, 8), T(24, 9))]));
        Assert.Equal(T(24, 9), calendar.AddWorkingMinutes(Unit, T(24, 8), 60, unavailable: []));
    }
}
