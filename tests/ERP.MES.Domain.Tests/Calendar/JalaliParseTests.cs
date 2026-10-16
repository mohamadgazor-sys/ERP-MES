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

    // Decision Log #73: Arabic-Indic digits (U+0660 to U+0669) are accepted as input, alone or mixed with the other digit shapes.
    [Theory]
    [InlineData("\u0661\u0664\u0660\u0665/\u0660\u0667/\u0661\u0669", 1405, 7, 19)]
    [InlineData("\u0661\u0664\u0660\u0665/\u0667/\u0669", 1405, 7, 9)]
    [InlineData("1405/07/\u0661\u0669", 1405, 7, 19)]
    [InlineData("\u06F1\u06F4\u06F0\u06F5/\u0660\u0667/19", 1405, 7, 19)]
    [InlineData("\u0661\u0664\u0660\u0663/\u0661\u0662/\u0663\u0660", 1403, 12, 30)]
    public void Arabic_indic_digit_dates_are_accepted_and_mean_the_same_as_the_other_digit_shapes(string text, int year, int month, int day)
    {
        Assert.True(_service.TryParseDate(text, out var date, out var error));
        Assert.Equal(JalaliDateError.None, error);
        Assert.Equal(_service.Date(year, month, day), date);
    }

    [Fact]
    public void The_three_digit_shapes_are_exactly_the_ten_digits_each()
    {
        // Every digit 0-9 in each shape, as the last day-of-month digit of a valid date, must give the same day as the Latin digit.
        for (var digit = 1; digit <= 9; digit++)
        {
            var latin = "1405/01/0" + digit;
            var persian = "1405/01/0" + (char)('\u06F0' + digit);
            var arabic = "1405/01/0" + (char)('\u0660' + digit);

            Assert.True(_service.TryParseDate(latin, out var expected, out _));
            Assert.True(_service.TryParseDate(persian, out var fromPersian, out _));
            Assert.True(_service.TryParseDate(arabic, out var fromArabic, out _));
            Assert.Equal(expected, fromPersian);
            Assert.Equal(expected, fromArabic);
        }
    }

    // Decision Log #73: white space around the text is ignored; white space inside the text is not.
    [Theory]
    [InlineData(" 1405/07/19")]
    [InlineData("1405/07/19 ")]
    [InlineData("  1405/07/19\t")]
    [InlineData("\n1405/07/19\r\n")]
    [InlineData("\u00A01405/07/19\u00A0")]
    [InlineData("\u30001405/07/19\u3000")]
    public void White_space_around_a_date_is_ignored(string text)
    {
        Assert.True(_service.TryParseDate(text, out var date, out var error));
        Assert.Equal(JalaliDateError.None, error);
        Assert.Equal(_service.Date(1405, 7, 19), date);
    }

    [Theory]
    [InlineData(" 1405/07/20 10:30")]
    [InlineData("1405/07/20 10:30 ")]
    [InlineData("\t1405/07/20 10:30\n")]
    [InlineData("\u00A01405/07/20 10:30\u3000")]
    [InlineData("\u0661\u0664\u0660\u0665/\u0660\u0667/\u0662\u0660 \u0661\u0660:\u0663\u0660")]
    public void White_space_around_a_date_time_is_ignored_and_arabic_indic_digits_are_accepted(string text)
    {
        Assert.True(_service.TryParseDateTime(text, out var utc, out var error));
        Assert.Equal(JalaliDateError.None, error);
        Assert.Equal(TestServices.Utc(2026, 10, 12, 7, 0), utc);
    }

    [Fact]
    public void Surrounding_white_space_is_not_silently_turned_into_a_value()
    {
        // Only white space: nothing to parse. Not a date, not today, not the minimum.
        Assert.False(_service.TryParseDate(" \t\n\u00A0\u3000 ", out var date, out var error));
        Assert.Equal(JalaliDateError.Empty, error);
        Assert.True(date.IsDefault);
        Assert.False(_service.TryParseDateTime("\u00A0", out var utc, out error));
        Assert.Equal(JalaliDateError.Empty, error);
        Assert.Equal(default(DateTime), utc);
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
