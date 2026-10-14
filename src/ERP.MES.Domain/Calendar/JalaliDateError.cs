namespace ERP.MES.Domain.Calendar;

/// <summary>
/// Why a Jalali date or date-time could not be created or parsed. Invalid input is reported with one of
/// these values; it is never silently corrected (for example "1405/07/31" is not turned into 1405/08/01).
/// </summary>
public enum JalaliDateError
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>The text is null, empty or only white space.</summary>
    Empty,

    /// <summary>The text contains a character that is not allowed (anything other than Latin or Persian digits, '/', and for date-times one space and ':').</summary>
    InvalidCharacter,

    /// <summary>The characters are allowed but the structure is wrong (missing or extra parts, wrong number of digits).</summary>
    InvalidFormat,

    YearOutOfRange,

    MonthOutOfRange,

    /// <summary>The day does not exist in that month, including day 30 of Esfand in a common year.</summary>
    DayOutOfRange,

    HourOutOfRange,

    MinuteOutOfRange,

    /// <summary>The local time falls in a daylight-saving gap of the display time zone and does not exist.</summary>
    NonExistentLocalTime,

    /// <summary>The local time occurs twice in the display time zone; the service does not guess which one was meant.</summary>
    AmbiguousLocalTime,

    /// <summary>The end of a date range is earlier than its start.</summary>
    RangeEndBeforeStart,
}
