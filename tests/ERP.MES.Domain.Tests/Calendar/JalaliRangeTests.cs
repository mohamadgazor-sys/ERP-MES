using ERP.MES.Domain.Calendar;

namespace ERP.MES.Domain.Tests.Calendar;

// Jalali filters become half-open Gregorian date ranges before querying (Master Specification section 0.6).
public class JalaliRangeTests
{
    private readonly JalaliDateService _service = TestServices.Latin();

    [Theory]
    [InlineData(1404, 2025, 3, 21, 2026, 3, 21, 365)]
    [InlineData(1403, 2024, 3, 20, 2025, 3, 21, 366)]
    [InlineData(1405, 2026, 3, 21, 2027, 3, 21, 365)]
    public void Year_range_is_half_open(int year, int sy, int sm, int sd, int ey, int em, int ed, int days)
    {
        var range = _service.GetYearRange(year);

        Assert.Equal(new DateOnly(sy, sm, sd), range.Start);
        Assert.Equal(new DateOnly(ey, em, ed), range.EndExclusive);
        Assert.Equal(days, range.DayCount);
    }

    [Theory]
    [InlineData(1405, 7, 2026, 9, 23, 2026, 10, 23, 30)]
    [InlineData(1403, 12, 2025, 2, 19, 2025, 3, 21, 30)]
    [InlineData(1404, 12, 2026, 2, 20, 2026, 3, 21, 29)]
    [InlineData(1405, 1, 2026, 3, 21, 2026, 4, 21, 31)]
    public void Month_range_is_half_open(int year, int month, int sy, int sm, int sd, int ey, int em, int ed, int days)
    {
        var range = _service.GetMonthRange(year, month);

        Assert.Equal(new DateOnly(sy, sm, sd), range.Start);
        Assert.Equal(new DateOnly(ey, em, ed), range.EndExclusive);
        Assert.Equal(days, range.DayCount);
    }

    [Fact]
    public void Range_contains_its_first_day_and_excludes_the_day_after_its_last()
    {
        var mehr1405 = _service.GetMonthRange(1405, 7);

        Assert.True(mehr1405.Contains(new DateOnly(2026, 9, 23)));
        Assert.True(mehr1405.Contains(new DateOnly(2026, 10, 22)));
        Assert.False(mehr1405.Contains(new DateOnly(2026, 10, 23)));
        Assert.False(mehr1405.Contains(new DateOnly(2026, 9, 22)));
    }

    [Fact]
    public void Month_ranges_tile_each_year_without_gaps_or_overlaps_for_1380_to_1420()
    {
        for (var year = 1380; year <= 1420; year++)
        {
            var yearRange = _service.GetYearRange(year);
            var expectedStart = yearRange.Start;
            var totalDays = 0;

            for (var month = 1; month <= 12; month++)
            {
                var monthRange = _service.GetMonthRange(year, month);
                Assert.Equal(expectedStart, monthRange.Start);
                Assert.Equal(_service.GetDaysInMonth(year, month), monthRange.DayCount);
                expectedStart = monthRange.EndExclusive;
                totalDays += monthRange.DayCount;
            }

            Assert.Equal(yearRange.EndExclusive, expectedStart);
            Assert.Equal(yearRange.DayCount, totalDays);
            Assert.Equal(_service.GetYearRange(year + 1).Start, yearRange.EndExclusive);
        }
    }

    [Fact]
    public void Inclusive_user_range_converts_to_a_half_open_gregorian_range()
    {
        var first = _service.Date(1405, 1, 1);

        Assert.True(_service.TryGetRange(first, first, out var oneDay, out var error));
        Assert.Equal(JalaliDateError.None, error);
        Assert.Equal(new DateRange(new DateOnly(2026, 3, 21), new DateOnly(2026, 3, 22)), oneDay);

        Assert.True(_service.TryGetRange(_service.Date(1405, 7, 1), _service.Date(1405, 7, 30), out var wholeMonth, out _));
        Assert.Equal(_service.GetMonthRange(1405, 7), wholeMonth);
    }

    [Fact]
    public void Reversed_range_is_rejected_not_swapped()
    {
        var ok = _service.TryGetRange(_service.Date(1405, 7, 20), _service.Date(1405, 7, 19), out var range, out var error);

        Assert.False(ok);
        Assert.Equal(JalaliDateError.RangeEndBeforeStart, error);
        Assert.Equal(default(DateRange), range);
    }

    [Fact]
    public void Range_ending_on_the_last_supported_day_is_rejected_because_the_day_after_cannot_be_represented()
    {
        var last = _service.ToJalali(DateOnly.MaxValue);

        Assert.False(_service.TryGetRange(_service.Date(1405, 1, 1), last, out _, out var error));
        Assert.Equal(JalaliDateError.YearOutOfRange, error);
    }

    [Fact]
    public void Unsupported_years_and_months_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetYearRange(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetYearRange(10000));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetYearRange(_service.ToJalali(DateOnly.MaxValue).Year));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetMonthRange(1405, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GetMonthRange(1405, 13));
    }

    [Fact]
    public void Default_dates_are_rejected_for_ranges()
    {
        var valid = _service.Date(1405, 1, 1);

        Assert.Throws<ArgumentException>(() => _service.TryGetRange(default, valid, out _, out _));
        Assert.Throws<ArgumentException>(() => _service.TryGetRange(valid, default, out _, out _));
    }
}
