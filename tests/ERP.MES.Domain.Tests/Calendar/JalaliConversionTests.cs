using ERP.MES.Domain.Calendar;

namespace ERP.MES.Domain.Tests.Calendar;

// Golden pairs, Nowruz / year boundaries and leap years for conversion (Decision Log #45).
// The pairs are widely documented Iranian calendar dates; docs/JALALI_DATE_SERVICE.md lists them for review.
public class JalaliConversionTests
{
    private readonly JalaliDateService _service = TestServices.Latin();

    [Theory]
    // Nowruz (1 Farvardin) and neighbouring days
    [InlineData(1300, 1, 1, 1921, 3, 21)]
    [InlineData(1357, 1, 1, 1978, 3, 21)]
    [InlineData(1380, 1, 1, 2001, 3, 21)]
    [InlineData(1398, 1, 1, 2019, 3, 21)]
    [InlineData(1399, 1, 1, 2020, 3, 20)]
    [InlineData(1400, 1, 1, 2021, 3, 21)]
    [InlineData(1401, 1, 1, 2022, 3, 21)]
    [InlineData(1402, 1, 1, 2023, 3, 21)]
    [InlineData(1403, 1, 1, 2024, 3, 20)]
    [InlineData(1404, 1, 1, 2025, 3, 21)]
    [InlineData(1405, 1, 1, 2026, 3, 21)]
    [InlineData(1405, 1, 2, 2026, 3, 22)]
    [InlineData(1406, 1, 1, 2027, 3, 21)]
    // Last day of a year (29 Esfand in a common year, 30 Esfand in a leap year)
    [InlineData(1397, 12, 29, 2019, 3, 20)]
    [InlineData(1398, 12, 29, 2020, 3, 19)]
    [InlineData(1399, 12, 30, 2021, 3, 20)]
    [InlineData(1402, 12, 29, 2024, 3, 19)]
    [InlineData(1403, 12, 30, 2025, 3, 20)]
    [InlineData(1404, 12, 29, 2026, 3, 20)]
    [InlineData(1405, 12, 29, 2027, 3, 20)]
    // Well-known dates and month boundaries
    [InlineData(1342, 3, 15, 1963, 6, 5)]
    [InlineData(1357, 11, 22, 1979, 2, 11)]
    [InlineData(1402, 12, 10, 2024, 2, 29)]
    [InlineData(1403, 9, 30, 2024, 12, 20)]
    [InlineData(1403, 10, 1, 2024, 12, 21)]
    [InlineData(1403, 10, 12, 2025, 1, 1)]
    [InlineData(1403, 12, 1, 2025, 2, 19)]
    [InlineData(1404, 6, 31, 2025, 9, 22)]
    [InlineData(1404, 7, 1, 2025, 9, 23)]
    [InlineData(1405, 6, 30, 2026, 9, 21)]
    [InlineData(1405, 7, 1, 2026, 9, 23)]
    [InlineData(1405, 7, 19, 2026, 10, 11)]
    [InlineData(1405, 7, 20, 2026, 10, 12)]
    public void Golden_pair_converts_in_both_directions(int jy, int jm, int jd, int gy, int gm, int gd)
    {
        var gregorian = new DateOnly(gy, gm, gd);
        var jalali = _service.Date(jy, jm, jd);

        Assert.Equal(jalali, _service.ToJalali(gregorian));
        Assert.Equal(gregorian, _service.ToGregorian(jalali));
        Assert.Equal(jy, _service.GetJalaliYear(gregorian));
    }

    [Theory]
    [InlineData(1375)]
    [InlineData(1379)]
    [InlineData(1383)]
    [InlineData(1387)]
    [InlineData(1391)]
    [InlineData(1395)]
    [InlineData(1399)]
    [InlineData(1403)]
    [InlineData(1408)]
    public void Documented_leap_years_are_leap_years(int year)
    {
        Assert.True(_service.IsLeapYear(year));
        Assert.Equal(30, _service.GetDaysInMonth(year, 12));
        Assert.True(_service.TryCreateDate(year, 12, 30, out _, out _));
    }

    [Theory]
    [InlineData(1376)]
    [InlineData(1397)]
    [InlineData(1398)]
    [InlineData(1400)]
    [InlineData(1401)]
    [InlineData(1402)]
    [InlineData(1404)]
    [InlineData(1405)]
    [InlineData(1406)]
    [InlineData(1407)]
    [InlineData(1409)]
    public void Documented_common_years_are_not_leap_years(int year)
    {
        Assert.False(_service.IsLeapYear(year));
        Assert.Equal(29, _service.GetDaysInMonth(year, 12));
        Assert.False(_service.TryCreateDate(year, 12, 30, out _, out var error));
        Assert.Equal(JalaliDateError.DayOutOfRange, error);
    }

    [Fact]
    public void Year_length_matches_the_leap_flag_and_the_distance_between_two_Nowruz_days_for_1300_to_1500()
    {
        for (var year = 1300; year <= 1500; year++)
        {
            var thisNowruz = _service.ToGregorian(_service.Date(year, 1, 1));
            var nextNowruz = _service.ToGregorian(_service.Date(year + 1, 1, 1));
            var length = nextNowruz.DayNumber - thisNowruz.DayNumber;

            Assert.Equal(_service.IsLeapYear(year) ? 366 : 365, length);
        }
    }

    [Fact]
    public void Day_after_the_last_day_of_every_year_is_Nowruz_of_the_next_year_for_1300_to_1500()
    {
        for (var year = 1300; year <= 1500; year++)
        {
            var lastDay = _service.Date(year, 12, _service.GetDaysInMonth(year, 12));
            var next = _service.ToJalali(_service.ToGregorian(lastDay).AddDays(1));

            Assert.Equal(_service.Date(year + 1, 1, 1), next);
        }
    }

    [Fact]
    public void Month_lengths_follow_the_calendar_rule()
    {
        foreach (var year in new[] { 1403, 1404 })
        {
            for (var month = 1; month <= 6; month++)
            {
                Assert.Equal(31, _service.GetDaysInMonth(year, month));
            }

            for (var month = 7; month <= 11; month++)
            {
                Assert.Equal(30, _service.GetDaysInMonth(year, month));
            }
        }

        Assert.Equal(30, _service.GetDaysInMonth(1403, 12));
        Assert.Equal(29, _service.GetDaysInMonth(1404, 12));
    }

    [Fact]
    public void Every_day_from_1900_to_2100_round_trips_and_advances_by_exactly_one_day()
    {
        var day = new DateOnly(1900, 1, 1);
        var end = new DateOnly(2100, 12, 31);
        var previous = _service.ToJalali(day);
        Assert.Equal(day, _service.ToGregorian(previous));

        for (day = day.AddDays(1); day <= end; day = day.AddDays(1))
        {
            var current = _service.ToJalali(day);
            Assert.Equal(day, _service.ToGregorian(current));

            var sameMonth = current.Year == previous.Year && current.Month == previous.Month && current.Day == previous.Day + 1;
            var nextMonth = current.Day == 1
                && previous.Day == _service.GetDaysInMonth(previous.Year, previous.Month)
                && ((current.Year == previous.Year && current.Month == previous.Month + 1)
                    || (current.Year == previous.Year + 1 && previous.Month == 12 && current.Month == 1));

            Assert.True(sameMonth || nextMonth, $"{previous} was followed by {current} on {day}");
            previous = current;
        }
    }

    [Fact]
    public void First_supported_day_is_year_1_month_1_day_1()
    {
        var first = _service.ToJalali(new DateOnly(622, 3, 22));

        Assert.Equal(_service.Date(1, 1, 1), first);
        Assert.Equal(new DateOnly(622, 3, 22), _service.ToGregorian(first));
    }

    [Fact]
    public void Dates_before_the_first_supported_day_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.ToJalali(new DateOnly(622, 3, 21)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.ToJalali(DateOnly.MinValue));
    }

    [Fact]
    public void Last_supported_day_round_trips_and_is_in_a_late_year()
    {
        var last = _service.ToJalali(DateOnly.MaxValue);

        Assert.Equal(DateOnly.MaxValue, _service.ToGregorian(last));
        Assert.True(last.Year > 9000);
    }

    [Fact]
    public void Default_jalali_date_is_rejected()
    {
        Assert.True(default(JalaliDate).IsDefault);
        Assert.Throws<ArgumentException>(() => _service.ToGregorian(default));
    }

    [Fact]
    public void Leap_year_and_month_queries_reject_unsupported_arguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.IsLeapYear(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.IsLeapYear(10000));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetDaysInMonth(1405, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetDaysInMonth(1405, 13));
    }

    [Fact]
    public void Jalali_dates_order_by_year_then_month_then_day()
    {
        var a = _service.Date(1404, 12, 29);
        var b = _service.Date(1405, 1, 1);
        var c = _service.Date(1405, 1, 2);

        Assert.True(a < b);
        Assert.True(b < c);
        Assert.True(c >= b);
        Assert.Equal(_service.Date(1405, 1, 1), b);
        Assert.NotEqual(b, c);
    }
}
