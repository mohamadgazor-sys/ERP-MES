namespace ERP.MES.Domain.Calendar;

/// <summary>
/// The shape of digits in text produced by <see cref="IJalaliDateService"/>.
/// There is deliberately no default: which shape appears on screen is an open question (see docs/JALALI_DATE_SERVICE.md).
/// </summary>
public enum DigitStyle
{
    /// <summary>0123456789</summary>
    Latin = 0,

    /// <summary>Persian digits ۰۱۲۳۴۵۶۷۸۹ (U+06F0 to U+06F9).</summary>
    Persian = 1,
}
