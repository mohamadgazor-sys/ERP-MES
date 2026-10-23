using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Domain.Tests.Scheduling;

// Shift templates as data, the working calendar and the worker/off-day/capacity rules (Decision Log #13, #95 D5, #100).
public class SchedulingTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    private static ShiftTemplate Day(long id = 1) => new(id, "D", "روزکار", new TimeOnly(8, 0), new TimeOnly(17, 0), new TimeOnly(12, 0), new TimeOnly(13, 0), 1, Now);

    private static ShiftTemplate Night(long id = 3) => new(id, "C", "شب‌کار", new TimeOnly(22, 0), new TimeOnly(6, 0), null, null, 1, Now);

    private static IReadOnlyList<WeeklyWorkday> SaturdayToThursday() =>
        Enum.GetValues<DayOfWeek>().Select(d => new WeeklyWorkday(d, d != DayOfWeek.Friday, Now)).ToList();

    // 2026-10-24 is a Saturday, 2026-10-23 a Friday.
    private static readonly DateOnly Saturday = new(2026, 10, 24);
    private static readonly DateOnly Friday = new(2026, 10, 23);

    [Fact]
    public void A_template_is_data_with_a_break_a_midnight_crossing_and_validation()
    {
        var day = Day();
        Assert.Equal(540, day.TotalMinutes);
        Assert.Equal(60, day.BreakMinutes);
        Assert.Equal(480, day.WorkingMinutes);
        Assert.False(day.CrossesMidnight);
        Assert.Equal(new[] { (0, 240), (300, 540) }, day.WorkingPieces().ToArray());

        var night = Night();
        Assert.True(night.CrossesMidnight);
        Assert.Equal(480, night.TotalMinutes);
        Assert.Equal(480, night.WorkingMinutes);
        Assert.Equal(new[] { (0, 480) }, night.WorkingPieces().ToArray());

        var nightWithBreak = new ShiftTemplate(4, "C2", "شب", new TimeOnly(22, 0), new TimeOnly(6, 0), new TimeOnly(1, 0), new TimeOnly(1, 30), 1, Now);
        Assert.Equal(450, nightWithBreak.WorkingMinutes);
        Assert.Equal(new[] { (0, 180), (210, 480) }, nightWithBreak.WorkingPieces().ToArray());

        Assert.Throws<ArgumentException>(() => new ShiftTemplate(5, "X", "x", new TimeOnly(8, 0), new TimeOnly(8, 0), null, null, 1, Now));
        Assert.Throws<ArgumentException>(() => new ShiftTemplate(5, "X", "x", new TimeOnly(8, 0), new TimeOnly(16, 0), new TimeOnly(12, 0), null, 1, Now));
        Assert.Throws<ArgumentException>(() => new ShiftTemplate(5, "X", "x", new TimeOnly(8, 0), new TimeOnly(16, 0), new TimeOnly(7, 0), new TimeOnly(9, 0), 1, Now));
        Assert.Throws<ArgumentException>(() => new ShiftTemplate(5, "X", "x", new TimeOnly(8, 0), new TimeOnly(16, 0), new TimeOnly(15, 0), new TimeOnly(16, 0), 1, Now));
    }

    [Fact]
    public void The_regular_week_is_configuration_so_friday_is_off_only_because_the_pattern_says_so()
    {
        var calendar = new WorkingCalendar(SaturdayToThursday(), [], [new UnitShift(1, 9, 1, 3, 1, false, 1, Now)], [Day()]);

        Assert.True(calendar.IsWorking(9, 1, Saturday));
        Assert.False(calendar.IsWorking(9, 1, Friday));
        Assert.Single(calendar.Occurrences(9, Saturday));
        Assert.Empty(calendar.Occurrences(9, Friday));

        var everyDay = new WorkingCalendar(Enum.GetValues<DayOfWeek>().Select(d => new WeeklyWorkday(d, true, Now)), [], [new UnitShift(1, 9, 1, 3, 1, false, 1, Now)], [Day()]);
        Assert.True(everyDay.IsWorking(9, 1, Friday));
    }

    [Fact]
    public void Exceptions_apply_by_scope_the_most_specific_wins_and_off_wins_at_equal_scope()
    {
        var holiday = new CalendarException(1, Saturday, CalendarExceptionKind.Off, null, null, "تعطیل رسمی", 1, Now);
        var extraFriday = new CalendarException(2, Friday, CalendarExceptionKind.Working, null, null, "تأیید سرپرست", 1, Now);
        var unitWorksAnyway = new CalendarException(3, Saturday, CalendarExceptionKind.Working, 9, null, "تولید فوری", 1, Now);
        var unitShiftOff = new CalendarException(4, Saturday.AddDays(1), CalendarExceptionKind.Off, 9, 1, "تعمیرات", 1, Now);
        var calendar = new WorkingCalendar(
            SaturdayToThursday(), [holiday, extraFriday, unitWorksAnyway, unitShiftOff], [new UnitShift(1, 9, 1, 3, 1, false, 1, Now), new UnitShift(2, 10, 1, 3, 1, false, 1, Now)], [Day()]);

        Assert.True(calendar.IsWorking(9, 1, Friday));
        Assert.False(calendar.IsWorking(10, 1, Saturday)); // company holiday, no unit exception for unit 10
        Assert.True(calendar.IsWorking(9, 1, Saturday)); // the unit exception overrides the company holiday
        Assert.False(calendar.IsWorking(9, 1, Saturday.AddDays(1)));
        Assert.True(calendar.IsWorking(10, 1, Saturday.AddDays(1)));

        var conflicting = new WorkingCalendar(
            SaturdayToThursday(),
            [new CalendarException(5, Saturday, CalendarExceptionKind.Off, null, null, "a", 1, Now), new CalendarException(6, Saturday, CalendarExceptionKind.Working, null, null, "b", 1, Now)],
            [new UnitShift(1, 9, 1, 3, 1, false, 1, Now)],
            [Day()]);
        Assert.False(conflicting.IsWorking(9, 1, Saturday));

        var cancelled = new CalendarException(7, Saturday, CalendarExceptionKind.Off, null, null, "x", 1, Now);
        cancelled.Cancel();
        Assert.True(new WorkingCalendar(SaturdayToThursday(), [cancelled], [new UnitShift(1, 9, 1, 3, 1, false, 1, Now)], [Day()]).IsWorking(9, 1, Saturday));
        Assert.Throws<ArgumentException>(() => new CalendarException(8, Saturday, CalendarExceptionKind.Off, null, null, " ", 1, Now));
    }

    [Fact]
    public void Work_minutes_advance_through_working_intervals_skipping_breaks_off_days_and_crossing_midnight()
    {
        var calendar = new WorkingCalendar(SaturdayToThursday(), [], [new UnitShift(1, 9, 1, 3, 1, false, 1, Now)], [Day()]);

        // Saturday 07:00: work starts at 08:00; 4h before the break end at 12:00, the rest after 13:00.
        Assert.Equal(new DateTime(2026, 10, 24, 12, 0, 0), calendar.AddWorkingMinutes(9, new DateTime(2026, 10, 24, 7, 0, 0), 240));
        Assert.Equal(new DateTime(2026, 10, 24, 13, 30, 0), calendar.AddWorkingMinutes(9, new DateTime(2026, 10, 24, 7, 0, 0), 270));
        Assert.Equal(new DateTime(2026, 10, 24, 8, 30, 0), calendar.AddWorkingMinutes(9, new DateTime(2026, 10, 24, 8, 0, 0), 30));

        // 600 minutes from Saturday 08:00 = 480 on Saturday, 120 on Sunday (08:00 + 2h = 10:00).
        Assert.Equal(new DateTime(2026, 10, 25, 10, 0, 0), calendar.AddWorkingMinutes(9, new DateTime(2026, 10, 24, 8, 0, 0), 600));

        // Starting on Thursday afternoon, Friday is skipped: the rest is done on Saturday.
        Assert.Equal(new DateTime(2026, 10, 24, 9, 0, 0), calendar.AddWorkingMinutes(9, new DateTime(2026, 10, 22, 16, 0, 0), 120));

        // No shifts for the unit, or work that does not fit the horizon: no forecast.
        Assert.Null(calendar.AddWorkingMinutes(77, new DateTime(2026, 10, 24, 8, 0, 0), 10));
        Assert.Null(calendar.AddWorkingMinutes(9, new DateTime(2026, 10, 24, 8, 0, 0), 480 * 400, horizonDays: 30));
        Assert.Equal(new DateTime(2026, 10, 24, 8, 0, 0), calendar.AddWorkingMinutes(9, new DateTime(2026, 10, 24, 8, 0, 0), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.AddWorkingMinutes(9, new DateTime(2026, 10, 24, 8, 0, 0), -1));

        var night = new WorkingCalendar(SaturdayToThursday(), [], [new UnitShift(1, 9, 3, 2, 1, false, 1, Now)], [Night()]);
        // A shift that started Saturday 22:00 runs into Sunday: 3h from Sunday 01:00 end at 04:00; 6h end at 06:00 and the next 1h on Sunday 22:00-23:00.
        Assert.Equal(new DateTime(2026, 10, 25, 4, 0, 0), night.AddWorkingMinutes(9, new DateTime(2026, 10, 25, 1, 0, 0), 180));
        Assert.Equal(new DateTime(2026, 10, 25, 23, 0, 0), night.AddWorkingMinutes(9, new DateTime(2026, 10, 25, 1, 0, 0), 5 * 60 + 60));
    }

    [Fact]
    public void Overlapping_shifts_of_one_unit_are_merged_so_the_overlap_is_not_counted_twice_and_the_overlap_is_detected()
    {
        var a = new ShiftTemplate(10, "A", "A", new TimeOnly(6, 0), new TimeOnly(14, 0), null, null, 1, Now);
        var day = Day();
        var calendar = new WorkingCalendar(SaturdayToThursday(), [], [new UnitShift(1, 9, 10, 2, 1, false, 1, Now), new UnitShift(2, 9, 1, 2, 1, true, 1, Now)], [a, day]);

        var occurrences = calendar.Occurrences(9, Saturday);
        Assert.Equal(2, occurrences.Count);
        Assert.True(occurrences[0].Overlaps(occurrences[1]));

        // One lane: A covers 06:00-14:00 and D works 08:00-12:00 and 13:00-17:00, so the union is 06:00-17:00 (the overlapped hours are not counted twice).
        var intervals = calendar.WorkingIntervals(9, new DateTime(2026, 10, 24, 0, 0, 0), 1).Where(i => i.Start.Date == new DateTime(2026, 10, 24)).ToList();
        Assert.Equal((new DateTime(2026, 10, 24, 6, 0, 0), new DateTime(2026, 10, 24, 17, 0, 0)), intervals.Single());
    }

    [Fact]
    public void Capacity_off_days_and_schedule_rows_are_validated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnitShift(1, 1, 1, 0, 0, false, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnitShift(1, 1, 1, 2, 3, false, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnitShift(1, 1, 1, UnitShift.MaxCapacity + 1, 0, false, 1, Now));
        var shift = new UnitShift(1, 1, 1, 4, 2, false, 1, Now);
        shift.SetCapacity(5, 0, true, Now);
        Assert.True(shift.OverlapAllowed);

        Assert.Throws<ArgumentException>(() => new OperatorOffDay(1, 5, Saturday, Saturday.AddDays(-1), OffDayKind.Leave, null, 1, Now));
        var off = new OperatorOffDay(1, 5, Saturday, Saturday.AddDays(2), OffDayKind.Sick, "  گواهی  ", 1, Now);
        Assert.True(off.Covers(Saturday.AddDays(1)));
        Assert.False(off.Covers(Saturday.AddDays(3)));
        off.Cancel();
        Assert.False(off.Covers(Saturday));

        var row = new WorkerSchedule(1, 5, Saturday, 9, 1, 3, null, null, 1, Now);
        Assert.True(row.IsPlanned);
        Assert.Throws<ArgumentException>(() => new WorkerSchedule(2, 5, Saturday, 9, 1, 3, 30, " ", 1, Now));
        Assert.Throws<ArgumentException>(() => new WorkerSchedule(2, 5, Saturday, 9, 1, null, 30, "handover", 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkerSchedule(2, 5, Saturday, 9, 1, 3, 0, "x", 1, Now));
        Assert.Throws<ArgumentException>(() => row.Cancel(" ", 1, Now));
        row.Cancel("غیبت", 2, Now);
        Assert.Equal(WorkerScheduleStatus.Cancelled, row.Status);
        Assert.Throws<InvalidOperationException>(() => row.Cancel("again", 2, Now));
    }
}
