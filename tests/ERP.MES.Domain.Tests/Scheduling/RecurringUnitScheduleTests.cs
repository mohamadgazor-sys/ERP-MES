using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Domain.Tests.Scheduling;

// Recurring unit schedules and dated exceptions in the one effective calendar (Decision Log #123 Q13, Q17, Q18).
public class RecurringUnitScheduleTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    // 2026-10-22 Thursday, 23 Friday, 24 Saturday.
    private static readonly DateOnly Thursday = new(2026, 10, 22);
    private static readonly DateOnly Friday = new(2026, 10, 23);
    private static readonly DateOnly Saturday = new(2026, 10, 24);

    private const long CncUnit = 9;
    private const long OtherUnit = 10;

    private static ShiftTemplate Day(long id = 1) => new(id, "D", "روزکار", new TimeOnly(8, 0), new TimeOnly(17, 0), new TimeOnly(12, 0), new TimeOnly(13, 0), 1, Now);

    private static ShiftTemplate FridayMorning(long id = 2) => new(id, "F", "جمعه", new TimeOnly(6, 0), new TimeOnly(12, 0), null, null, 1, Now);

    private static ShiftTemplate Night(long id = 3) => new(id, "N", "شب", new TimeOnly(22, 0), new TimeOnly(6, 0), null, null, 1, Now);

    private static IReadOnlyList<WeeklyWorkday> SaturdayToThursday() =>
        Enum.GetValues<DayOfWeek>().Select(d => new WeeklyWorkday(d, d != DayOfWeek.Friday, Now)).ToList();

    private static UnitShift Regular(long id, long unit, long template) => new(id, unit, template, 3, 1, false, 1, Now);

    private static UnitShift OnDays(long id, long unit, long template, params DayOfWeek[] days)
    {
        var shift = new UnitShift(id, unit, template, 3, 1, false, 1, Now);
        shift.SetRegularDays(UnitShift.MaskOf(days), Now);
        return shift;
    }

    private static WorkingCalendar Calendar(IEnumerable<CalendarException>? exceptions = null) => new(
        SaturdayToThursday(),
        exceptions ?? [],
        [Regular(1, CncUnit, 1), OnDays(2, CncUnit, 2, DayOfWeek.Friday), Regular(3, OtherUnit, 1)],
        [Day(), FridayMorning()]);

    [Fact]
    public void A_standing_friday_schedule_makes_only_the_selected_unit_work_on_friday_without_overtime_or_an_exception()
    {
        var calendar = Calendar();

        Assert.True(calendar.IsWorking(CncUnit, 2, Friday));
        Assert.False(calendar.IsWorking(CncUnit, 2, Saturday));
        Assert.False(calendar.IsWorking(CncUnit, 1, Friday));
        Assert.True(calendar.IsWorking(CncUnit, 1, Saturday));
        Assert.False(calendar.IsWorking(OtherUnit, 1, Friday));

        var occurrence = Assert.Single(calendar.Occurrences(CncUnit, Friday));
        Assert.Equal(new DateTime(2026, 10, 23, 6, 0, 0), occurrence.LocalStart);
        Assert.Equal(new DateTime(2026, 10, 23, 12, 0, 0), occurrence.LocalEnd);
        Assert.Empty(calendar.Occurrences(OtherUnit, Friday));
    }

    [Fact]
    public void The_forecast_counts_the_standing_friday_hours_for_the_selected_unit_only()
    {
        var calendar = Calendar();
        var thursday4pm = new DateTime(2026, 10, 22, 16, 0, 0);

        // 60 minutes remain on Thursday (16:00-17:00), the other 60 are done on Friday morning for the CNC unit.
        Assert.Equal(new DateTime(2026, 10, 23, 7, 0, 0), calendar.AddWorkingMinutes(CncUnit, thursday4pm, 120));
        // A unit without the Friday schedule continues on Saturday at 08:00 + 60 minutes.
        Assert.Equal(new DateTime(2026, 10, 24, 9, 0, 0), calendar.AddWorkingMinutes(OtherUnit, thursday4pm, 120));
    }

    [Fact]
    public void A_date_specific_company_exception_beats_the_recurring_schedule_and_a_unit_exception_beats_the_company_one()
    {
        var companyHoliday = new CalendarException(1, Friday, CalendarExceptionKind.Off, null, null, "تعطیل رسمی", 1, Now);
        Assert.False(Calendar([companyHoliday]).IsWorking(CncUnit, 2, Friday));

        // The unit is explicitly scheduled to work that holiday: the unit-and-shift exception wins over the company one.
        var unitWorks = new CalendarException(2, Friday, CalendarExceptionKind.Working, CncUnit, 2, "تولید فوری", 1, Now);
        Assert.True(Calendar([companyHoliday, unitWorks]).IsWorking(CncUnit, 2, Friday));
        Assert.False(Calendar([companyHoliday, unitWorks]).IsWorking(OtherUnit, 1, Friday));

        // At equal level Off wins.
        var unitOff = new CalendarException(3, Friday, CalendarExceptionKind.Off, CncUnit, 2, "تعمیرات", 1, Now);
        Assert.False(Calendar([companyHoliday, unitWorks, unitOff]).IsWorking(CncUnit, 2, Friday));
    }

    [Fact]
    public void A_shift_without_regular_days_runs_only_where_a_working_exception_names_the_date_and_cancelled_exceptions_do_not_count()
    {
        var onlyOnRequest = new UnitShift(5, CncUnit, 2, 2, 0, false, 1, Now);
        onlyOnRequest.SetRegularDays(0, Now);
        var withoutException = new WorkingCalendar(SaturdayToThursday(), [], [onlyOnRequest], [FridayMorning()]);
        Assert.False(withoutException.IsWorking(CncUnit, 2, Friday));
        Assert.False(withoutException.IsWorking(CncUnit, 2, Saturday));

        var working = new CalendarException(1, Saturday, CalendarExceptionKind.Working, CncUnit, 2, "ساعت ویژه", 1, Now);
        var calendar = new WorkingCalendar(SaturdayToThursday(), [working], [onlyOnRequest], [FridayMorning()]);
        Assert.True(calendar.IsWorking(CncUnit, 2, Saturday));
        Assert.False(calendar.IsWorking(CncUnit, 2, Friday));

        working.Cancel();
        Assert.False(new WorkingCalendar(SaturdayToThursday(), [working], [onlyOnRequest], [FridayMorning()]).IsWorking(CncUnit, 2, Saturday));
    }

    [Fact]
    public void Several_intervals_a_day_and_a_midnight_crossing_interval_are_merged_without_counting_overlapped_time_twice()
    {
        // Friday 06:00-12:00 plus Friday 22:00-06:00 (ends Saturday morning): two intervals, one crossing midnight.
        var calendar = new WorkingCalendar(
            SaturdayToThursday(), [], [OnDays(1, CncUnit, 2, DayOfWeek.Friday), OnDays(2, CncUnit, 3, DayOfWeek.Friday)], [FridayMorning(), Night()]);

        var intervals = calendar.WorkingIntervals(CncUnit, new DateTime(2026, 10, 23, 0, 0, 0), 2).ToArray();
        Assert.Equal(new[] { (new DateTime(2026, 10, 23, 6, 0, 0), new DateTime(2026, 10, 23, 12, 0, 0)), (new DateTime(2026, 10, 23, 22, 0, 0), new DateTime(2026, 10, 24, 6, 0, 0)) }, intervals);
        // One hour is left before noon, the other hour is worked from 22:00.
        Assert.Equal(new DateTime(2026, 10, 23, 23, 0, 0), calendar.AddWorkingMinutes(CncUnit, new DateTime(2026, 10, 23, 11, 0, 0), 120));
    }

    [Fact]
    public void The_regular_days_mask_accepts_only_the_seven_weekday_bits_and_reports_the_days_a_shift_can_run_on()
    {
        var shift = Regular(1, CncUnit, 1);
        Assert.Null(shift.RegularDays);
        Assert.True(Enum.GetValues<DayOfWeek>().All(shift.CanRunOn));

        shift.SetRegularDays(UnitShift.MaskOf([DayOfWeek.Friday, DayOfWeek.Monday]), Now);
        Assert.True(shift.CanRunOn(DayOfWeek.Friday));
        Assert.True(shift.CanRunOn(DayOfWeek.Monday));
        Assert.False(shift.CanRunOn(DayOfWeek.Saturday));
        shift.SetRegularDays(null, Now);
        Assert.True(shift.CanRunOn(DayOfWeek.Saturday));

        Assert.Throws<ArgumentOutOfRangeException>(() => shift.SetRegularDays(128, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => shift.SetRegularDays(-1, Now));
    }
}
