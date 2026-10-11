using ERP.MES.Domain.Calendar;

namespace ERP.MES.Domain.Tests.Calendar;

// Supported input and Persian-digit input (Decision Log #45).
public class JalaliParseTests
{
    private readonly JalaliDateService _service = TestServices.Latin();

    [Theory]
    [InlineData("1405/07/19", 1405, 7, 19)]
    [InlineData("1405/7/19", 1405, 7, 19)]
    [InlineData("1405/07/9", 1405, 7, 9)]
    [InlineData("1405/7/9", 1405, 7, 9)]
    [InlineData("1403/12/30", 1403, 12, 30)]
    [InlineData("1399/12/30", 1399, 12, 30)]
    [InlineData("1405/01/01", 1405, 1, 1)]
    [InlineData("0001/01/01", 1, 1, 1)]
    public void Latin_digit_dates_are_accepted(string text, int year, int month, int day)
    {
        Assert.True(_service.TryParseDate(text, out var date, out var error));
        Assert.Equal(JalaliDateError.None, error);
        Assert.Equal(_service.Date(year, month, day), date);
    }

    [Theory]
    [InlineData("۱۴۰۵/۰۷/۱۹", 1405, 7, 19)]
    [InlineData("۱۴۰۵/۷/۹", 1405, 7, 9)]
    [InlineData("۱۴۰۳/۱۲/۳۰", 1403, 12, 30)]
    [InlineData("۱۴۰۵/۰۱/۰۱", 1405, 1, 1)]
    [InlineData("۱۴۰۵/07/۱۹", 1405, 7, 19)]
    [InlineData("1405/۰۷/19", 1405, 7, 19)]
    public void Persian_digit_dates_and_mixed_digit_dates_are_accepted(string text, int year, int month, int day)
    {
        Assert.True(_service.TryParseDate(text, out var date, out var error));
        Assert.Equal(JalaliDateError.None, error);
        Assert.Equal(_service.Date(year, month, day), date);
    }

    [Fact]
    public void Persian_and_latin_input_give_the_same_result_whatever_the_output_digit_style()
    {
        var persianOutput = TestServices.Persian();

        Assert.True(persianOutput.TryParseDate("1405/07/19", out var fromLatin, out _));
        Assert.True(persianOutput.TryParseDate("۱۴۰۵/۰۷/۱۹", out var fromPersian, out _));
        Assert.Equal(fromLatin, fromPersian);
    }

    [Theory]
    [InlineData("1405/07/20 00:43", 2026, 10, 11, 21, 13)]
    [InlineData("1405/07/20 0:43", 2026, 10, 11, 21, 13)]
    [InlineData("۱۴۰۵/۰۷/۲۰ ۰۰:۴۳", 2026, 10, 11, 21, 13)]
    [InlineData("۱۴۰۵/۰۷/۲۰ 00:43", 2026, 10, 11, 21, 13)]
    [InlineData("1405/07/19 23:59", 2026, 10, 11, 20, 29)]
    [InlineData("1405/01/01 00:00", 2026, 3, 20, 20, 30)]
    [InlineData("1404/12/29 23:59", 2026, 3, 20, 20, 29)]
    [InlineData("1405/07/20 09:05", 2026, 10, 12, 5, 35)]
    [InlineData("1405/7/20 9:05", 2026, 10, 12, 5, 35)]
    public void Local_date_time_text_becomes_a_utc_timestamp(string text, int year, int month, int day, int hour, int minute)
    {
        Assert.True(_service.TryParseDateTime(text, out var utc, out var error));
        Assert.Equal(JalaliDateError.None, error);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(TestServices.Utc(year, month, day, hour, minute), utc);
    }

    [Fact]
    public void Time_after_a_daylight_saving_gap_and_before_an_overlap_is_accepted()
    {
        var service = new JalaliDateService(DigitStyle.Latin, TestZones.WithDaylightSaving);

        Assert.True(service.TryParseDateTime("1405/01/02 01:00", out var afterGap, out var gapError));
        Assert.Equal(JalaliDateError.None, gapError);
        Assert.Equal(DateTimeKind.Utc, afterGap.Kind);

        Assert.True(service.TryParseDateTime("1405/06/30 22:59", out _, out var beforeOverlapError));
        Assert.Equal(JalaliDateError.None, beforeOverlapError);
    }

    [Fact]
    public void Gregorian_leap_day_is_a_valid_jalali_day_when_written_in_jalali()
    {
        Assert.True(_service.TryParseDate("1402/12/10", out var date, out _));
        Assert.Equal(new DateOnly(2024, 2, 29), _service.ToGregorian(date));
    }
}
