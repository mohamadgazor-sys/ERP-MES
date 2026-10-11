namespace ERP.MES.Domain.Calendar;

/// <summary>
/// A half-open range of calendar dates: <see cref="Start"/> is included, <see cref="EndExclusive"/> is not.
/// Gregorian dates are what the database stores and compares, so a Jalali filter is converted to this
/// before querying (Master Specification section 0.6).
/// </summary>
public readonly record struct DateRange(DateOnly Start, DateOnly EndExclusive)
{
    public int DayCount => EndExclusive.DayNumber - Start.DayNumber;

    public bool Contains(DateOnly date) => date >= Start && date < EndExclusive;
}
