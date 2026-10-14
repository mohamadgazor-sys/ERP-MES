using ERP.MES.Domain.Calendar;

namespace ERP.MES.Domain.Tests.Calendar;

// Output formats: yyyy/MM/dd and yyyy/MM/dd HH:mm (24-hour) in the display time zone.
public class JalaliFormatTests
{
    [Theory]
    [InlineData(1405, 1, 1, "1405/01/01")]
    [InlineData(1405, 7, 19, "1405/07/19")]
    [InlineData(1403, 12, 30, "1403/12/30")]
    [InlineData(1, 1, 1, "0001/01/01")]
    public void Date_is_formatted_with_zero_padding_and_latin_digits(int year, int month, int day, string expected)
    {
        var service = TestServices.Latin();

        Assert.Equal(expected, service.FormatDate(service.Date(year, month, day)));
    }

    [Theory]
    [InlineData(1405, 1, 1, "۱۴۰۵/۰۱/۰۱")]
    [InlineData(1405, 7, 19, "۱۴۰۵/۰۷/۱۹")]
    [InlineData(1403, 12, 30, "۱۴۰۳/۱۲/۳۰")]
    public void Date_is_formatted_with_persian_digits_when_requested(int year, int month, int day, string expected)
    {
        var service = TestServices.Persian();

        Assert.Equal(expected, service.FormatDate(service.Date(year, month, day)));
    }

    [Fact]
    public void Stored_gregorian_date_is_formatted_as_jalali()
    {
        Assert.Equal("1405/07/19", TestServices.Latin().FormatDate(new DateOnly(2026, 10, 11)));
        Assert.Equal("۱۴۰۵/۰۷/۱۹", TestServices.Persian().FormatDate(new DateOnly(2026, 10, 11)));
    }

    [Theory]
    [InlineData(2026, 10, 11, 21, 13, 0, "1405/07/20 00:43")]
    [InlineData(2026, 10, 11, 20, 29, 0, "1405/07/19 23:59")]
    [InlineData(2026, 10, 11, 20, 30, 0, "1405/07/20 00:00")]
    [InlineData(2026, 10, 11, 20, 29, 59, "1405/07/19 23:59")]
    [InlineData(2026, 3, 20, 20, 30, 0, "1405/01/01 00:00")]
    [InlineData(2026, 3, 20, 20, 29, 0, "1404/12/29 23:59")]
    [InlineData(2025, 3, 20, 20, 29, 0, "1403/12/30 23:59")]
    [InlineData(2026, 10, 12, 5, 35, 0, "1405/07/20 09:05")]
    public void Utc_timestamp_is_shown_as_local_jalali_date_and_24_hour_time(int year, int month, int day, int hour, int minute, int second, string expected)
    {
        var service = TestServices.Latin();

        Assert.Equal(expected, service.FormatDateTime(TestServices.Utc(year, month, day, hour, minute, second)));
    }

    [Fact]
    public void Midnight_is_00_00_and_never_24_00()
    {
        var text = TestServices.Latin().FormatDateTime(TestServices.Utc(2026, 10, 11, 20, 30));

        Assert.EndsWith(" 00:00", text);
        Assert.DoesNotContain("24:", text);
    }

    [Fact]
    public void Date_time_uses_persian_digits_when_requested()
    {
        Assert.Equal("۱۴۰۵/۰۷/۲۰ ۰۰:۴۳", TestServices.Persian().FormatDateTime(TestServices.Utc(2026, 10, 11, 21, 13)));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Timestamp_that_is_not_utc_is_rejected_not_reinterpreted(DateTimeKind kind)
    {
        var timestamp = new DateTime(2026, 10, 11, 21, 13, 0, kind);

        Assert.Throws<ArgumentException>(() => TestServices.Latin().FormatDateTime(timestamp));
    }

    [Fact]
    public void Display_time_zone_decides_the_local_date()
    {
        var utc = TestServices.Utc(2026, 10, 11, 21, 13);
        var plusThreeThirty = new JalaliDateService(DigitStyle.Latin, TestZones.FixedIran);
        var utcZone = new JalaliDateService(DigitStyle.Latin, TimeZoneInfo.Utc);

        Assert.Equal("1405/07/20 00:43", plusThreeThirty.FormatDateTime(utc));
        Assert.Equal("1405/07/19 21:13", utcZone.FormatDateTime(utc));
    }

    [Fact]
    public void Constructor_requires_a_time_zone_and_a_known_digit_style()
    {
        Assert.Throws<ArgumentNullException>(() => new JalaliDateService(DigitStyle.Latin, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new JalaliDateService((DigitStyle)99, TestZones.FixedIran));
    }

    [Fact]
    public void Format_then_parse_returns_the_same_minute_for_a_long_series_of_timestamps()
    {
        var service = TestServices.Latin();
        var timestamp = TestServices.Utc(2024, 1, 1, 0, 0);

        for (var i = 0; i < 5000; i++)
        {
            var text = service.FormatDateTime(timestamp);

            Assert.True(service.TryParseDateTime(text, out var parsed, out var error), $"{text} should parse but was {error}");
            Assert.Equal(DateTimeKind.Utc, parsed.Kind);
            Assert.Equal(timestamp, parsed);

            timestamp = timestamp.AddMinutes(437);
        }
    }

    [Fact]
    public void Format_then_parse_returns_the_same_date_for_every_day_of_a_leap_cycle()
    {
        var service = TestServices.Persian();

        for (var day = new DateOnly(2024, 1, 1); day <= new DateOnly(2028, 12, 31); day = day.AddDays(1))
        {
            var text = service.FormatDate(day);

            Assert.True(service.TryParseDate(text, out var parsed, out var error), $"{text} should parse but was {error}");
            Assert.Equal(day, service.ToGregorian(parsed));
        }
    }
}
