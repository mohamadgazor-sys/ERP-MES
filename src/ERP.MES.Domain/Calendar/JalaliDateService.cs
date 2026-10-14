using System.Globalization;

namespace ERP.MES.Domain.Calendar;

/// <summary>
/// Jalali conversion, formatting and parsing built on <see cref="PersianCalendar"/> (Decision Log #45).
/// The digit shape of produced text and the time zone used for local times are explicit constructor
/// arguments because neither is decided yet. The instance holds no mutable state and is thread-safe.
/// </summary>
public sealed class JalaliDateService : IJalaliDateService
{
    private const char PersianZero = '\u06F0';

    private readonly PersianCalendar _calendar = new();
    private readonly DigitStyle _outputDigits;
    private readonly TimeZoneInfo _displayTimeZone;
    private readonly DateOnly _minGregorian;
    private readonly DateOnly _maxGregorian;
    private readonly JalaliDate _minJalali;
    private readonly JalaliDate _maxJalali;

    public JalaliDateService(DigitStyle outputDigits, TimeZoneInfo displayTimeZone)
    {
        if (!Enum.IsDefined(outputDigits))
        {
            throw new ArgumentOutOfRangeException(nameof(outputDigits), outputDigits, "Unknown digit style.");
        }

        ArgumentNullException.ThrowIfNull(displayTimeZone);

        _outputDigits = outputDigits;
        _displayTimeZone = displayTimeZone;

        var min = _calendar.MinSupportedDateTime;
        var max = _calendar.MaxSupportedDateTime;
        _minGregorian = DateOnly.FromDateTime(min);
        _maxGregorian = DateOnly.FromDateTime(max);
        _minJalali = new JalaliDate(_calendar.GetYear(min), _calendar.GetMonth(min), _calendar.GetDayOfMonth(min));
        _maxJalali = new JalaliDate(_calendar.GetYear(max), _calendar.GetMonth(max), _calendar.GetDayOfMonth(max));
    }

    public JalaliDate ToJalali(DateOnly gregorianDate)
    {
        if (gregorianDate < _minGregorian || gregorianDate > _maxGregorian)
        {
            // Invariant culture: a Persian current culture would otherwise print these years in the Persian calendar.
            var min = _minGregorian.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var max = _maxGregorian.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            throw new ArgumentOutOfRangeException(nameof(gregorianDate), gregorianDate, "The date is outside the supported range " + min + " to " + max + ".");
        }

        var value = gregorianDate.ToDateTime(TimeOnly.MinValue);
        return new JalaliDate(_calendar.GetYear(value), _calendar.GetMonth(value), _calendar.GetDayOfMonth(value));
    }

    public DateOnly ToGregorian(JalaliDate date)
    {
        RequireNotDefault(date, nameof(date));
        var value = _calendar.ToDateTime(date.Year, date.Month, date.Day, 0, 0, 0, 0);
        return DateOnly.FromDateTime(value);
    }

    public int GetJalaliYear(DateOnly gregorianDate) => ToJalali(gregorianDate).Year;

    public bool IsLeapYear(int jalaliYear)
    {
        RequireSupportedYear(jalaliYear, nameof(jalaliYear));
        return _calendar.IsLeapYear(jalaliYear);
    }

    public int GetDaysInMonth(int jalaliYear, int jalaliMonth)
    {
        RequireSupportedYear(jalaliYear, nameof(jalaliYear));
        if (jalaliMonth < 1 || jalaliMonth > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(jalaliMonth), jalaliMonth, "The month must be between 1 and 12.");
        }

        if (IsAfterLastSupportedMonth(jalaliYear, jalaliMonth))
        {
            throw new ArgumentOutOfRangeException(nameof(jalaliMonth), jalaliMonth, "The calendar ends before this month of the last supported year.");
        }

        return _calendar.GetDaysInMonth(jalaliYear, jalaliMonth);
    }

    public bool TryCreateDate(int year, int month, int day, out JalaliDate date, out JalaliDateError error)
    {
        date = default;

        if (year < _minJalali.Year || year > _maxJalali.Year)
        {
            error = JalaliDateError.YearOutOfRange;
            return false;
        }

        if (month < 1 || month > 12)
        {
            error = JalaliDateError.MonthOutOfRange;
            return false;
        }

        if (IsAfterLastSupportedMonth(year, month))
        {
            // PersianCalendar throws if asked about these months, so they must be rejected before the day check.
            error = JalaliDateError.YearOutOfRange;
            return false;
        }

        if (day < 1 || day > _calendar.GetDaysInMonth(year, month))
        {
            error = JalaliDateError.DayOutOfRange;
            return false;
        }

        var candidate = new JalaliDate(year, month, day);
        if (candidate > _maxJalali)
        {
            // The calendar's last year is only partly supported (it ends where Gregorian DateTime ends).
            error = JalaliDateError.YearOutOfRange;
            return false;
        }

        date = candidate;
        error = JalaliDateError.None;
        return true;
    }

    public DateRange GetYearRange(int jalaliYear)
    {
        var start = CreateOrThrow(jalaliYear, 1, 1, nameof(jalaliYear));
        var next = CreateOrThrow(jalaliYear + 1, 1, 1, nameof(jalaliYear));
        return new DateRange(ToGregorian(start), ToGregorian(next));
    }

    public DateRange GetMonthRange(int jalaliYear, int jalaliMonth)
    {
        var start = CreateOrThrow(jalaliYear, jalaliMonth, 1, nameof(jalaliMonth));
        var next = jalaliMonth < 12
            ? CreateOrThrow(jalaliYear, jalaliMonth + 1, 1, nameof(jalaliMonth))
            : CreateOrThrow(jalaliYear + 1, 1, 1, nameof(jalaliYear));
        return new DateRange(ToGregorian(start), ToGregorian(next));
    }

    public bool TryGetRange(JalaliDate start, JalaliDate endInclusive, out DateRange range, out JalaliDateError error)
    {
        RequireNotDefault(start, nameof(start));
        RequireNotDefault(endInclusive, nameof(endInclusive));
        range = default;

        if (endInclusive < start)
        {
            error = JalaliDateError.RangeEndBeforeStart;
            return false;
        }

        var endGregorian = ToGregorian(endInclusive);
        if (endGregorian >= _maxGregorian)
        {
            // The day after the last supported day cannot be represented.
            error = JalaliDateError.YearOutOfRange;
            return false;
        }

        range = new DateRange(ToGregorian(start), endGregorian.AddDays(1));
        error = JalaliDateError.None;
        return true;
    }

    public string FormatDate(JalaliDate date)
    {
        RequireNotDefault(date, nameof(date));
        return ApplyDigits(DatePart(date));
    }

    public string FormatDate(DateOnly gregorianDate) => FormatDate(ToJalali(gregorianDate));

    public string FormatDateTime(DateTime utcTimestamp)
    {
        if (utcTimestamp.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamps are stored in UTC; the value must have Kind = Utc.", nameof(utcTimestamp));
        }

        DateTime local;
        try
        {
            local = TimeZoneInfo.ConvertTimeFromUtc(utcTimestamp, _displayTimeZone);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(nameof(utcTimestamp), utcTimestamp, "The local time is outside the supported range.");
        }

        var date = ToJalali(DateOnly.FromDateTime(local));
        var text = DatePart(date) + " " + Two(local.Hour) + ":" + Two(local.Minute);
        return ApplyDigits(text);
    }

    public bool TryParseDate(string? text, out JalaliDate date, out JalaliDateError error)
    {
        date = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = JalaliDateError.Empty;
            return false;
        }

        if (!TryNormalise(text, allowTime: false, out var normalised, out error))
        {
            return false;
        }

        return TryParseNormalisedDate(normalised, out date, out error);
    }

    public bool TryParseDateTime(string? text, out DateTime utcTimestamp, out JalaliDateError error)
    {
        utcTimestamp = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = JalaliDateError.Empty;
            return false;
        }

        if (!TryNormalise(text, allowTime: true, out var normalised, out error))
        {
            return false;
        }

        var halves = normalised.Split(' ');
        if (halves.Length != 2 || halves[0].Length == 0 || halves[1].Length == 0)
        {
            error = JalaliDateError.InvalidFormat;
            return false;
        }

        if (!TryParseNormalisedDate(halves[0], out var date, out error))
        {
            return false;
        }

        var timeParts = halves[1].Split(':');
        if (timeParts.Length != 2
            || timeParts[0].Length is < 1 or > 2
            || timeParts[1].Length != 2)
        {
            error = JalaliDateError.InvalidFormat;
            return false;
        }

        var hour = ToNumber(timeParts[0]);
        var minute = ToNumber(timeParts[1]);
        if (hour > 23)
        {
            error = JalaliDateError.HourOutOfRange;
            return false;
        }

        if (minute > 59)
        {
            error = JalaliDateError.MinuteOutOfRange;
            return false;
        }

        var gregorian = ToGregorian(date);
        var local = new DateTime(gregorian.Year, gregorian.Month, gregorian.Day, hour, minute, 0, DateTimeKind.Unspecified);

        if (_displayTimeZone.IsInvalidTime(local))
        {
            error = JalaliDateError.NonExistentLocalTime;
            return false;
        }

        if (_displayTimeZone.IsAmbiguousTime(local))
        {
            error = JalaliDateError.AmbiguousLocalTime;
            return false;
        }

        try
        {
            utcTimestamp = TimeZoneInfo.ConvertTimeToUtc(local, _displayTimeZone);
        }
        catch (ArgumentOutOfRangeException)
        {
            error = JalaliDateError.YearOutOfRange;
            return false;
        }

        error = JalaliDateError.None;
        return true;
    }

    private bool TryParseNormalisedDate(string normalised, out JalaliDate date, out JalaliDateError error)
    {
        date = default;

        var parts = normalised.Split('/');
        if (parts.Length != 3
            || parts[0].Length != 4
            || parts[1].Length is < 1 or > 2
            || parts[2].Length is < 1 or > 2)
        {
            error = JalaliDateError.InvalidFormat;
            return false;
        }

        return TryCreateDate(ToNumber(parts[0]), ToNumber(parts[1]), ToNumber(parts[2]), out date, out error);
    }

    /// <summary>
    /// Maps Persian digits to Latin and rejects every character that is not allowed. Arabic-Indic digits
    /// (U+0660 to U+0669), other separators, letters and surrounding white space are rejected, not converted.
    /// </summary>
    private static bool TryNormalise(string text, bool allowTime, out string normalised, out JalaliDateError error)
    {
        var buffer = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is >= '0' and <= '9')
            {
                buffer[i] = c;
            }
            else if (c is >= '\u06F0' and <= '\u06F9')
            {
                buffer[i] = (char)('0' + (c - PersianZero));
            }
            else if (c == '/' || (allowTime && (c == ' ' || c == ':')))
            {
                buffer[i] = c;
            }
            else
            {
                normalised = string.Empty;
                error = JalaliDateError.InvalidCharacter;
                return false;
            }
        }

        normalised = new string(buffer);
        error = JalaliDateError.None;
        return true;
    }

    // Only called with strings that consist of 1 to 4 ASCII digits.
    private static int ToNumber(string digits)
    {
        var value = 0;
        foreach (var c in digits)
        {
            if (c is < '0' or > '9')
            {
                return int.MaxValue;
            }

            value = (value * 10) + (c - '0');
        }

        return value;
    }

    private static string Two(int value) => value.ToString("D2", CultureInfo.InvariantCulture);

    private static string DatePart(JalaliDate date) =>
        date.Year.ToString("D4", CultureInfo.InvariantCulture) + "/" + Two(date.Month) + "/" + Two(date.Day);

    private string ApplyDigits(string latinText)
    {
        if (_outputDigits == DigitStyle.Latin)
        {
            return latinText;
        }

        return string.Create(latinText.Length, latinText, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = c is >= '0' and <= '9' ? (char)(PersianZero + (c - '0')) : c;
            }
        });
    }

    private JalaliDate CreateOrThrow(int year, int month, int day, string parameterName)
    {
        if (!TryCreateDate(year, month, day, out var date, out var error))
        {
            throw new ArgumentOutOfRangeException(parameterName, $"{year}/{month}/{day} is not a supported Jalali date ({error}).");
        }

        return date;
    }

    /// <summary>The calendar's last year is only partly supported: it ends at the month that contains DateTime.MaxValue.</summary>
    private bool IsAfterLastSupportedMonth(int year, int month) =>
        year == _maxJalali.Year && month > _maxJalali.Month;

    private void RequireSupportedYear(int year, string parameterName)
    {
        if (year < _minJalali.Year || year > _maxJalali.Year)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, year, $"The year must be between {_minJalali.Year} and {_maxJalali.Year}.");
        }
    }

    private static void RequireNotDefault(JalaliDate date, string parameterName)
    {
        if (date.IsDefault)
        {
            throw new ArgumentException("default(JalaliDate) is not a valid date; create one with IJalaliDateService.", parameterName);
        }
    }
}
