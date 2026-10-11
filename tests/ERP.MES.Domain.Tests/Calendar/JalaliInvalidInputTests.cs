using ERP.MES.Domain.Calendar;

namespace ERP.MES.Domain.Tests.Calendar;

// Invalid input is reported with a specific error and never silently corrected (docs/JALALI_DATE_SERVICE.md section 4).
public class JalaliInvalidInputTests
{
    private readonly JalaliDateService _service = TestServices.Latin();

    [Theory]
    [InlineData(null, JalaliDateError.Empty)]
    [InlineData("", JalaliDateError.Empty)]
    [InlineData("   ", JalaliDateError.Empty)]
    [InlineData("\t", JalaliDateError.Empty)]
    // characters that are not allowed
    [InlineData("1405-07-19", JalaliDateError.InvalidCharacter)]
    [InlineData("1405.07.19", JalaliDateError.InvalidCharacter)]
    [InlineData("1405\\07\\19", JalaliDateError.InvalidCharacter)]
    [InlineData(" 1405/07/19", JalaliDateError.InvalidCharacter)]
    [InlineData("1405/07/19 ", JalaliDateError.InvalidCharacter)]
    [InlineData("1405/07/19\n", JalaliDateError.InvalidCharacter)]
    [InlineData("1405/07/19 10:30", JalaliDateError.InvalidCharacter)]
    [InlineData("abcd/07/19", JalaliDateError.InvalidCharacter)]
    [InlineData("1405/07/1x", JalaliDateError.InvalidCharacter)]
    [InlineData("١٤٠٥/٠٧/١٩", JalaliDateError.InvalidCharacter)]
    [InlineData("1405/07/١٩", JalaliDateError.InvalidCharacter)]
    // structure
    [InlineData("1405", JalaliDateError.InvalidFormat)]
    [InlineData("/", JalaliDateError.InvalidFormat)]
    [InlineData("1405/07", JalaliDateError.InvalidFormat)]
    [InlineData("1405/07/19/01", JalaliDateError.InvalidFormat)]
    [InlineData("/1405/07/19", JalaliDateError.InvalidFormat)]
    [InlineData("1405/07/19/", JalaliDateError.InvalidFormat)]
    [InlineData("1405//19", JalaliDateError.InvalidFormat)]
    [InlineData("1405/07/", JalaliDateError.InvalidFormat)]
    [InlineData("05/07/19", JalaliDateError.InvalidFormat)]
    [InlineData("14050/07/19", JalaliDateError.InvalidFormat)]
    [InlineData("1405/007/19", JalaliDateError.InvalidFormat)]
    [InlineData("1405/07/019", JalaliDateError.InvalidFormat)]
    // values
    [InlineData("0000/01/01", JalaliDateError.YearOutOfRange)]
    [InlineData("9999/01/01", JalaliDateError.YearOutOfRange)]
    [InlineData("1405/00/10", JalaliDateError.MonthOutOfRange)]
    [InlineData("1405/13/01", JalaliDateError.MonthOutOfRange)]
    [InlineData("1405/13/32", JalaliDateError.MonthOutOfRange)]
    [InlineData("1405/01/00", JalaliDateError.DayOutOfRange)]
    [InlineData("1405/01/32", JalaliDateError.DayOutOfRange)]
    [InlineData("1405/06/32", JalaliDateError.DayOutOfRange)]
    [InlineData("1405/07/31", JalaliDateError.DayOutOfRange)]
    [InlineData("1405/11/31", JalaliDateError.DayOutOfRange)]
    [InlineData("1400/12/30", JalaliDateError.DayOutOfRange)]
    [InlineData("1404/12/30", JalaliDateError.DayOutOfRange)]
    [InlineData("1405/12/30", JalaliDateError.DayOutOfRange)]
    public void Invalid_date_text_is_rejected_with_a_specific_error_and_no_value(string? text, JalaliDateError expected)
    {
        var ok = _service.TryParseDate(text, out var date, out var error);

        Assert.False(ok);
        Assert.Equal(expected, error);
        Assert.True(date.IsDefault, "a failed parse must not return a partly or silently corrected date");
    }

    [Theory]
    [InlineData(null, JalaliDateError.Empty)]
    [InlineData("  ", JalaliDateError.Empty)]
    // characters
    [InlineData("1405-07-20 10:30", JalaliDateError.InvalidCharacter)]
    [InlineData("1405/07/20T10:30", JalaliDateError.InvalidCharacter)]
    [InlineData("1405/07/20 10.30", JalaliDateError.InvalidCharacter)]
    [InlineData("1405/07/20 ١٠:٣٠", JalaliDateError.InvalidCharacter)]
    [InlineData("1405/07/20 10:30\n", JalaliDateError.InvalidCharacter)]
    [InlineData("1405/07/20 10:30 AM", JalaliDateError.InvalidCharacter)]
    // structure
    [InlineData("1405/07/20", JalaliDateError.InvalidFormat)]
    [InlineData("1405/07/20 ", JalaliDateError.InvalidFormat)]
    [InlineData(" 1405/07/20 10:30", JalaliDateError.InvalidFormat)]
    [InlineData("1405/07/20  10:30", JalaliDateError.InvalidFormat)]
    [InlineData("1405/07/20 10", JalaliDateError.InvalidFormat)]
    [InlineData("1405/07/20 10:5", JalaliDateError.InvalidFormat)]
    [InlineData("1405/07/20 10:30:15", JalaliDateError.InvalidFormat)]
    [InlineData("1405/07/20 :30", JalaliDateError.InvalidFormat)]
    [InlineData("1405/07/20 100:30", JalaliDateError.InvalidFormat)]
    [InlineData("1405/07 10:30", JalaliDateError.InvalidFormat)]
    // values: date errors are reported before time errors
    [InlineData("1405/07/20 24:00", JalaliDateError.HourOutOfRange)]
    [InlineData("1405/07/20 99:00", JalaliDateError.HourOutOfRange)]
    [InlineData("1405/07/20 23:60", JalaliDateError.MinuteOutOfRange)]
    [InlineData("1405/07/20 12:99", JalaliDateError.MinuteOutOfRange)]
    [InlineData("1405/13/01 24:00", JalaliDateError.MonthOutOfRange)]
    [InlineData("1405/07/31 10:00", JalaliDateError.DayOutOfRange)]
    [InlineData("1404/12/30 10:00", JalaliDateError.DayOutOfRange)]
    public void Invalid_date_time_text_is_rejected_with_a_specific_error_and_no_value(string? text, JalaliDateError expected)
    {
        var ok = _service.TryParseDateTime(text, out var utc, out var error);

        Assert.False(ok);
        Assert.Equal(expected, error);
        Assert.Equal(default(DateTime), utc);
    }

    [Fact]
    public void Local_time_that_does_not_exist_in_the_display_time_zone_is_rejected_not_moved()
    {
        var service = new JalaliDateService(DigitStyle.Latin, TestZones.WithDaylightSaving);

        Assert.False(service.TryParseDateTime("1405/01/02 00:30", out var utc, out var error));
        Assert.Equal(JalaliDateError.NonExistentLocalTime, error);
        Assert.Equal(default(DateTime), utc);
    }

    [Fact]
    public void Local_time_that_occurs_twice_in_the_display_time_zone_is_rejected_not_guessed()
    {
        var service = new JalaliDateService(DigitStyle.Latin, TestZones.WithDaylightSaving);

        Assert.False(service.TryParseDateTime("1405/06/30 23:30", out var utc, out var error));
        Assert.Equal(JalaliDateError.AmbiguousLocalTime, error);
        Assert.Equal(default(DateTime), utc);
    }

    [Theory]
    [InlineData(0, 1, 1, JalaliDateError.YearOutOfRange)]
    [InlineData(10000, 1, 1, JalaliDateError.YearOutOfRange)]
    [InlineData(1405, 0, 1, JalaliDateError.MonthOutOfRange)]
    [InlineData(1405, 13, 1, JalaliDateError.MonthOutOfRange)]
    [InlineData(1405, 7, 0, JalaliDateError.DayOutOfRange)]
    [InlineData(1405, 7, 31, JalaliDateError.DayOutOfRange)]
    [InlineData(1404, 12, 30, JalaliDateError.DayOutOfRange)]
    [InlineData(-1405, 7, 19, JalaliDateError.YearOutOfRange)]
    public void Creating_a_date_from_numbers_reports_the_first_invalid_part(int year, int month, int day, JalaliDateError expected)
    {
        var ok = _service.TryCreateDate(year, month, day, out var date, out var error);

        Assert.False(ok);
        Assert.Equal(expected, error);
        Assert.True(date.IsDefault);
    }

    [Fact]
    public void The_day_after_the_last_supported_day_is_rejected()
    {
        var last = _service.ToJalali(DateOnly.MaxValue);

        Assert.True(_service.TryCreateDate(last.Year, last.Month, last.Day, out _, out _));
        Assert.True(last.Day < _service.GetDaysInMonth(last.Year, last.Month));
        Assert.False(_service.TryCreateDate(last.Year, last.Month, last.Day + 1, out _, out var error));
        Assert.Equal(JalaliDateError.YearOutOfRange, error);
        Assert.False(_service.TryCreateDate(last.Year + 1, 1, 1, out _, out var nextYearError));
        Assert.Equal(JalaliDateError.YearOutOfRange, nextYearError);
    }

    [Fact]
    public void A_day_that_does_not_exist_is_never_rolled_into_the_next_month()
    {
        // 31 Mehr does not exist; the next month must not be silently used.
        Assert.False(_service.TryParseDate("1405/07/31", out var date, out var error));
        Assert.Equal(JalaliDateError.DayOutOfRange, error);
        Assert.NotEqual(_service.Date(1405, 8, 1), date);
        Assert.True(date.IsDefault);
    }
}
