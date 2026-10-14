using ERP.MES.Domain.Calendar;

namespace ERP.MES.Domain.Tests.Calendar;

/// <summary>Time zones used by the tests. They are built in code so results do not depend on the machine's time-zone database.</summary>
internal static class TestZones
{
    /// <summary>Fixed UTC+03:30, no daylight saving.</summary>
    public static TimeZoneInfo FixedIran { get; } = TimeZoneInfo.CreateCustomTimeZone(
        "Test+03:30", TimeSpan.FromMinutes(210), "UTC+03:30", "UTC+03:30");

    /// <summary>
    /// UTC+03:30 with a made-up daylight-saving rule for 2026 only: clocks jump from 00:00 to 01:00 on 2026-03-22
    /// (local 00:00-00:59 does not exist) and go back from 00:00 to 23:00 on 2026-09-22 (local 23:00-23:59 on 2026-09-21 occurs twice).
    /// </summary>
    public static TimeZoneInfo WithDaylightSaving { get; } = CreateWithDaylightSaving();

    private static TimeZoneInfo CreateWithDaylightSaving()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            dateStart: new DateTime(2026, 1, 1),
            dateEnd: new DateTime(2026, 12, 31),
            daylightDelta: TimeSpan.FromHours(1),
            daylightTransitionStart: TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 22),
            daylightTransitionEnd: TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 9, 22));

        return TimeZoneInfo.CreateCustomTimeZone(
            "Test+03:30 with daylight saving",
            TimeSpan.FromMinutes(210),
            "UTC+03:30 (test, daylight saving)",
            "UTC+03:30",
            "UTC+04:30",
            new[] { rule });
    }
}

internal static class TestServices
{
    public static JalaliDateService Latin() => new(DigitStyle.Latin, TestZones.FixedIran);

    public static JalaliDateService Persian() => new(DigitStyle.Persian, TestZones.FixedIran);

    /// <summary>Creates a date that the test expects to be valid.</summary>
    public static JalaliDate Date(this IJalaliDateService service, int year, int month, int day)
    {
        Assert.True(service.TryCreateDate(year, month, day, out var date, out var error), $"{year}/{month}/{day} should be valid but was {error}");
        return date;
    }

    public static DateTime Utc(int year, int month, int day, int hour, int minute, int second = 0) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);
}
