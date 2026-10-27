using ERP.MES.Application.Production;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Application.Tests;

// The availability rule at shift, break and midnight boundaries (Decision Log #112). Local time is a plain wall clock here.
public class OperatorAvailabilityTests
{
    private static readonly DateTime Created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Saturday = new(2026, 10, 24);

    private static readonly ShiftTemplate Day = new(1, "D", "D", new TimeOnly(8, 0), new TimeOnly(17, 0), new TimeOnly(12, 0), new TimeOnly(13, 0), 1, Created);
    private static readonly ShiftTemplate Night = new(2, "N", "N", new TimeOnly(22, 0), new TimeOnly(6, 0), null, null, 1, Created);

    private static WorkingCalendar Calendar() =>
        new(
            Enum.GetValues<DayOfWeek>().Select(d => new WeeklyWorkday(d, d != DayOfWeek.Friday, Created)),
            [],
            [new UnitShift(1, 7, 1, 3, 1, false, 1, Created), new UnitShift(2, 7, 2, 3, 1, false, 1, Created)],
            [Day, Night]);

    private static Operator Worker(bool active = true)
    {
        var op = new Operator(5, "OP", "x", null, 1, Created);
        op.SetActive(active, Created);
        return op;
    }

    private static WorkerSchedule Row(DateOnly date, long template) => new(0, 5, date, 7, template, null, null, null, 1, Created);

    private static OperatorAvailability At(DateTime local, IReadOnlyList<WorkerSchedule> rows, IReadOnlyList<OperatorOffDay>? off = null, IReadOnlyList<OvertimeAssignment>? overtime = null, bool active = true) =>
        OperatorAvailabilityRules.Evaluate(local, Worker(active), 7, off ?? [], rows, new Dictionary<long, ShiftTemplate> { [1] = Day, [2] = Night }, Calendar(), overtime ?? []);

    [Theory]
    [InlineData(7, 59, OperatorAvailability.OutsideShift)]
    [InlineData(8, 0, OperatorAvailability.Available)]
    [InlineData(12, 30, OperatorAvailability.Available)]
    [InlineData(16, 59, OperatorAvailability.Available)]
    [InlineData(17, 0, OperatorAvailability.OutsideShift)]
    public void The_day_shift_is_inclusive_at_the_start_and_exclusive_at_the_end(int hour, int minute, OperatorAvailability expected) =>
        Assert.Equal(expected, At(Saturday.ToDateTime(new TimeOnly(hour, minute)), [Row(Saturday, 1)]));

    [Fact]
    public void A_night_shift_belongs_to_the_date_it_started_and_covers_the_hours_after_midnight()
    {
        var rows = new[] { Row(Saturday, 2) };

        Assert.Equal(OperatorAvailability.Available, At(Saturday.ToDateTime(new TimeOnly(23, 30)), rows));
        Assert.Equal(OperatorAvailability.Available, At(Saturday.AddDays(1).ToDateTime(new TimeOnly(0, 30)), rows));
        Assert.Equal(OperatorAvailability.Available, At(Saturday.AddDays(1).ToDateTime(new TimeOnly(5, 59)), rows));
        Assert.Equal(OperatorAvailability.OutsideShift, At(Saturday.AddDays(1).ToDateTime(new TimeOnly(6, 0)), rows));
        Assert.Equal(OperatorAvailability.OutsideShift, At(Saturday.ToDateTime(new TimeOnly(21, 59)), rows));
    }

    [Fact]
    public void No_schedule_row_or_a_cancelled_one_means_outside_the_shift()
    {
        var cancelled = Row(Saturday, 1);
        cancelled.Cancel("x", 1, Created);

        Assert.Equal(OperatorAvailability.OutsideShift, At(Saturday.ToDateTime(new TimeOnly(10, 0)), []));
        Assert.Equal(OperatorAvailability.OutsideShift, At(Saturday.ToDateTime(new TimeOnly(10, 0)), [cancelled]));
    }

    [Fact]
    public void A_day_the_calendar_marks_off_does_not_make_the_shift_available()
    {
        var friday = new DateOnly(2026, 10, 23);

        Assert.Equal(OperatorAvailability.OutsideShift, At(friday.ToDateTime(new TimeOnly(10, 0)), [Row(friday, 1)]));
    }

    [Fact]
    public void An_off_day_and_an_inactive_operator_are_refused_outright()
    {
        var off = new[] { new OperatorOffDay(1, 5, Saturday, Saturday, OffDayKind.Sick, null, 1, Created) };

        Assert.Equal(OperatorAvailability.Absent, At(Saturday.ToDateTime(new TimeOnly(10, 0)), [Row(Saturday, 1)], off));
        Assert.Equal(OperatorAvailability.Inactive, At(Saturday.ToDateTime(new TimeOnly(10, 0)), [Row(Saturday, 1)], active: false));
    }

    [Fact]
    public void An_off_day_that_ended_yesterday_still_covers_the_tail_of_the_night_shift_that_began_yesterday()
    {
        var off = new[] { new OperatorOffDay(1, 5, Saturday, Saturday, OffDayKind.Leave, null, 1, Created) };
        var rows = new[] { Row(Saturday, 2) };

        Assert.Equal(OperatorAvailability.Absent, At(Saturday.AddDays(1).ToDateTime(new TimeOnly(1, 0)), rows, off));
        Assert.Equal(OperatorAvailability.OutsideShift, At(Saturday.AddDays(1).ToDateTime(new TimeOnly(7, 0)), rows, off));
    }

    [Fact]
    public void Approved_overtime_covers_its_window_only_and_a_proposed_one_does_not()
    {
        var start = Saturday.ToDateTime(new TimeOnly(18, 0));
        var approved = new OvertimeAssignment(1, 5, 7, 3, start, start.AddHours(2), "x", 1, Created);
        approved.PlanningApprove(false, null, 2, Created);
        var proposed = new OvertimeAssignment(2, 5, 7, 3, start, start.AddHours(2), "x", 1, Created);

        Assert.Equal(OperatorAvailability.Available, At(start.AddMinutes(30), [], overtime: [approved]));
        Assert.Equal(OperatorAvailability.OutsideShift, At(start.AddHours(2), [], overtime: [approved]));
        Assert.Equal(OperatorAvailability.OutsideShift, At(start.AddMinutes(30), [], overtime: [proposed]));
    }
}
