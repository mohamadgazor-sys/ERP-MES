namespace ERP.MES.Domain.Calendar;

/// <summary>
/// A calendar date in the Jalali (Shamsi) calendar.
/// Instances are created only by <see cref="IJalaliDateService"/>, so every value other than
/// <c>default</c> is a valid date. Use the service to format or convert it; <see cref="ToString"/>
/// is a diagnostic aid and not a display format.
/// </summary>
public readonly struct JalaliDate : IEquatable<JalaliDate>, IComparable<JalaliDate>
{
    internal JalaliDate(int year, int month, int day)
    {
        Year = year;
        Month = month;
        Day = day;
    }

    public int Year { get; }

    public int Month { get; }

    public int Day { get; }

    /// <summary>True for <c>default(JalaliDate)</c>, which is not a valid date.</summary>
    public bool IsDefault => Year == 0;

    public bool Equals(JalaliDate other) => Year == other.Year && Month == other.Month && Day == other.Day;

    public override bool Equals(object? obj) => obj is JalaliDate other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Year, Month, Day);

    public int CompareTo(JalaliDate other)
    {
        var byYear = Year.CompareTo(other.Year);
        if (byYear != 0)
        {
            return byYear;
        }

        var byMonth = Month.CompareTo(other.Month);
        return byMonth != 0 ? byMonth : Day.CompareTo(other.Day);
    }

    public static bool operator ==(JalaliDate left, JalaliDate right) => left.Equals(right);

    public static bool operator !=(JalaliDate left, JalaliDate right) => !left.Equals(right);

    public static bool operator <(JalaliDate left, JalaliDate right) => left.CompareTo(right) < 0;

    public static bool operator <=(JalaliDate left, JalaliDate right) => left.CompareTo(right) <= 0;

    public static bool operator >(JalaliDate left, JalaliDate right) => left.CompareTo(right) > 0;

    public static bool operator >=(JalaliDate left, JalaliDate right) => left.CompareTo(right) >= 0;

    public override string ToString() => $"JalaliDate({Year}, {Month}, {Day})";
}
