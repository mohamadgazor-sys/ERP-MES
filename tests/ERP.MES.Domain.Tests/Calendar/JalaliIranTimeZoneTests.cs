using ERP.MES.Domain.Calendar;

namespace ERP.MES.Domain.Tests.Calendar;

// Decision Log #73: the local time of the application is Iran Standard Time (UTC+03:30; Iran has had no daylight saving since 2022) and the screen shows
// Persian digits. The zone is looked up by its fixed identifier - the tests never use the machine's own current time zone - and every expected value below is
// written out by hand from the rules: Iran Standard Time = UTC + 3 h 30 min, so local midnight is 20:30 UTC of the day before.
public class JalaliIranTimeZoneTests
{
    private static TimeZoneInfo IranZone { get; } = FindIranZone();

    private static TimeZoneInfo FindIranZone()
    {
        // "Iran Standard Time" is the Windows identifier the application uses; "Asia/Tehran" is the IANA name of the same zone (only for developers on other systems).
        foreach (var id in new[] { "Iran Standard Time", "Asia/Tehran" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // try the next identifier
            }
        }

        throw new InvalidOperationException("Neither 'Iran Standard Time' nor 'Asia/Tehran' is known to this system.");
    }

    private static JalaliDateService Latin() => new(DigitStyle.Latin, IranZone);

    private static JalaliDateService Persian() => new(DigitStyle.Persian, IranZone);

    [Theory]
    [InlineData(2023, 3, 21)]
    [InlineData(2024, 7, 1)]
    [InlineData(2025, 12, 31)]
    [InlineData(2026, 3, 21)]
    [InlineData(2026, 9, 23)]
    [InlineData(2027, 6, 15)]
    public void Iran_standard_time_is_utc_plus_03_30_all_year_after_the_end_of_daylight_saving(int year, int month, int day)
    {
        var offset = IranZone.GetUtcOffset(TestServices.Utc(year, month, day, 12, 0));

        Assert.Equal(TimeSpan.FromMinutes(210), offset);
    }

    // (UTC instant, expected local Jalali text) - hand-written golden values.
    [Theory]
    // Nowruz 1405 (1405/01/01 = 2026-03-21): the first local minute of the year is 20:30 UTC on 2026-03-20.
    [InlineData(2026, 3, 20, 20, 29, 59, "1404/12/29 23:59")]
    [InlineData(2026, 3, 20, 20, 30, 0, "1405/01/01 00:00")]
    [InlineData(2026, 3, 21, 20, 29, 59, "1405/01/01 23:59")]
    [InlineData(2026, 3, 21, 20, 30, 0, "1405/01/02 00:00")]
    // Leap year 1403: 30 Esfand exists (2025-03-20), Nowruz 1404 is 2025-03-21.
    [InlineData(2025, 3, 19, 20, 29, 59, "1403/12/29 23:59")]
    [InlineData(2025, 3, 19, 20, 30, 0, "1403/12/30 00:00")]
    [InlineData(2025, 3, 20, 20, 29, 59, "1403/12/30 23:59")]
    [InlineData(2025, 3, 20, 20, 30, 0, "1404/01/01 00:00")]
    // Common year 1404: 29 Esfand is the last day (2026-03-20).
    [InlineData(2026, 3, 19, 20, 30, 0, "1404/12/29 00:00")]
    // Month boundary Shahrivar -> Mehr 1404 (31 Shahrivar = 2025-09-22, 1 Mehr = 2025-09-23).
    [InlineData(2025, 9, 22, 20, 29, 59, "1404/06/31 23:59")]
    [InlineData(2025, 9, 22, 20, 30, 0, "1404/07/01 00:00")]
    // The Gregorian new year is not a Jalali boundary: 2024-12-31 23:59 UTC is already 1403/10/12 03:29 locally.
    [InlineData(2024, 12, 31, 23, 59, 0, "1403/10/12 03:29")]
    [InlineData(2025, 1, 1, 0, 0, 0, "1403/10/12 03:30")]
    // Gregorian leap day 2024-02-29 = 1402/12/10.
    [InlineData(2024, 2, 29, 12, 0, 0, "1402/12/10 15:30")]
    // The UTC date and the Jalali date can differ: 2026-10-11 21:13 UTC is 00:43 on 2026-10-12 in Iran.
    [InlineData(2026, 10, 11, 21, 13, 0, "1405/07/20 00:43")]
    public void A_utc_timestamp_is_shown_as_the_local_jalali_date_and_24_hour_time(int year, int month, int day, int hour, int minute, int second, string expected)
    {
        Assert.Equal(expected, Latin().FormatDateTime(TestServices.Utc(year, month, day, hour, minute, second)));
    }

    [Fact]
    public void The_screen_text_uses_persian_digits()
    {
        Assert.Equal("۱۴۰۵/۰۱/۰۱ ۰۰:۰۰", Persian().FormatDateTime(TestServices.Utc(2026, 3, 20, 20, 30)));
        Assert.Equal("۱۴۰۳/۱۲/۳۰ ۲۳:۵۹", Persian().FormatDateTime(TestServices.Utc(2025, 3, 20, 20, 29)));
        Assert.Equal("۱۴۰۵/۰۷/۲۰", Persian().FormatDate(new DateOnly(2026, 10, 12)));
    }

    // (local Jalali text, UTC instant it stands for) - the same instants as above, read back.
    [Theory]
    [InlineData("1405/01/01 00:00", 2026, 3, 20, 20, 30)]
    [InlineData("1404/12/29 23:59", 2026, 3, 20, 20, 29)]
    [InlineData("1403/12/30 00:00", 2025, 3, 19, 20, 30)]
    [InlineData("1403/12/30 23:59", 2025, 3, 20, 20, 29)]
    [InlineData("1404/01/01 00:00", 2025, 3, 20, 20, 30)]
    [InlineData("1404/07/01 00:00", 2025, 9, 22, 20, 30)]
    [InlineData("۱۴۰۵/۰۷/۲۰ ۰۰:۴۳", 2026, 10, 11, 21, 13)]
    [InlineData("١٤٠٥/٠١/٠١ ٠٠:٠٠", 2026, 3, 20, 20, 30)]
    [InlineData("  1405/01/01 00:00  ", 2026, 3, 20, 20, 30)]
    public void Local_jalali_text_becomes_the_utc_timestamp_to_store(string text, int year, int month, int day, int hour, int minute)
    {
        Assert.True(Latin().TryParseDateTime(text, out var utc, out var error), error.ToString());
        Assert.Equal(TestServices.Utc(year, month, day, hour, minute), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void A_30_esfand_that_does_not_exist_in_a_common_year_is_rejected_whatever_the_time_zone()
    {
        Assert.False(Latin().TryParseDateTime("1404/12/30 12:00", out var utc, out var error));
        Assert.Equal(JalaliDateError.DayOutOfRange, error);
        Assert.Equal(default(DateTime), utc);
    }

    [Fact]
    public void Times_in_iran_never_hit_a_daylight_saving_gap_or_overlap_today()
    {
        // The Iranian summer-time rule ended in 2022. A time such as 00:30 on 1 Farvardin, which used to be skipped, is now an ordinary time.
        Assert.True(Latin().TryParseDateTime("1405/01/01 00:30", out var utc, out var error), error.ToString());
        Assert.Equal(TestServices.Utc(2026, 3, 20, 21, 0), utc);
        Assert.True(Latin().TryParseDateTime("1405/06/31 23:30", out utc, out error), error.ToString());
        Assert.Equal(TestServices.Utc(2026, 9, 22, 20, 0), utc);
    }
}
