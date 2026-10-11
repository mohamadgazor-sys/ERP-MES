namespace ERP.MES.Domain.Calendar;

/// <summary>
/// The single place where Jalali (Shamsi) dates are converted, formatted and parsed.
/// Storage conventions (Decision Log #24): calendar dates are <see cref="DateOnly"/>; timestamps are
/// <see cref="DateTime"/> values in UTC; Jalali and local time appear only in text produced or read here.
/// Behaviour for invalid input is documented in docs/JALALI_DATE_SERVICE.md.
/// </summary>
public interface IJalaliDateService
{
    /// <summary>Converts a Gregorian date to Jalali.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The date is outside the supported range (from Gregorian 0622-03-22).</exception>
    JalaliDate ToJalali(DateOnly gregorianDate);

    /// <summary>Converts a Jalali date to Gregorian.</summary>
    /// <exception cref="ArgumentException"><paramref name="date"/> is <c>default</c>.</exception>
    DateOnly ToGregorian(JalaliDate date);

    /// <summary>The Jalali year of a Gregorian date, for example for the Request business key.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The date is outside the supported range.</exception>
    int GetJalaliYear(DateOnly gregorianDate);

    /// <exception cref="ArgumentOutOfRangeException">The year is outside the supported range.</exception>
    bool IsLeapYear(int jalaliYear);

    /// <exception cref="ArgumentOutOfRangeException">The year or month is outside the supported range.</exception>
    int GetDaysInMonth(int jalaliYear, int jalaliMonth);

    /// <summary>Creates a validated Jalali date. On failure <paramref name="date"/> is <c>default</c>.</summary>
    bool TryCreateDate(int year, int month, int day, out JalaliDate date, out JalaliDateError error);

    /// <summary>The whole Jalali year as a half-open Gregorian range.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The year, or the year after it, is outside the supported range.</exception>
    DateRange GetYearRange(int jalaliYear);

    /// <summary>The whole Jalali month as a half-open Gregorian range.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The month, or the month after it, is outside the supported range.</exception>
    DateRange GetMonthRange(int jalaliYear, int jalaliMonth);

    /// <summary>
    /// Converts an inclusive Jalali range (as a user enters it) to a half-open Gregorian range.
    /// Fails with <see cref="JalaliDateError.RangeEndBeforeStart"/> if the end is before the start.
    /// </summary>
    /// <exception cref="ArgumentException">A date is <c>default</c>.</exception>
    bool TryGetRange(JalaliDate start, JalaliDate endInclusive, out DateRange range, out JalaliDateError error);

    /// <summary>Formats as yyyy/MM/dd.</summary>
    /// <exception cref="ArgumentException"><paramref name="date"/> is <c>default</c>.</exception>
    string FormatDate(JalaliDate date);

    /// <summary>Formats a stored Gregorian date as Jalali yyyy/MM/dd.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The date is outside the supported range.</exception>
    string FormatDate(DateOnly gregorianDate);

    /// <summary>
    /// Formats a stored UTC timestamp as Jalali yyyy/MM/dd HH:mm in the display time zone, 24-hour clock.
    /// Seconds are not shown (they are dropped, not rounded).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="utcTimestamp"/> is not <see cref="DateTimeKind.Utc"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The result is outside the supported range.</exception>
    string FormatDateTime(DateTime utcTimestamp);

    /// <summary>Parses user text such as "1405/07/19" (Latin or Persian digits).</summary>
    bool TryParseDate(string? text, out JalaliDate date, out JalaliDateError error);

    /// <summary>
    /// Parses user text such as "1405/07/20 09:05" as local time in the display time zone and returns
    /// the UTC timestamp (<see cref="DateTimeKind.Utc"/>) to store.
    /// </summary>
    bool TryParseDateTime(string? text, out DateTime utcTimestamp, out JalaliDateError error);
}
