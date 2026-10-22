using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Planning;

/// <summary>
/// One entry of a machine's hourly-rate history (Domain and Database Specification 12.2; Decision Log #10, #82 R2/R4). The rate is in Iranian Rial per hour and applies from
/// <see cref="EffectiveFrom"/> until a later entry replaces it. Entries are append-only: a rate is never edited or deleted, so a cost calculated earlier can always be explained by the
/// rate that was in force on the work date. The Domain checks the value; the Application checks that the new entry is later than the machine's latest one.
/// </summary>
public sealed class MachineHourlyRate
{
    /// <summary>A sanity bound only (10^13 Rial per hour); the real limits are business decisions.</summary>
    public const decimal MaxRatePerHourRial = 10_000_000_000_000m;

    public const int MaxReasonLength = 500;

    public MachineHourlyRate(long id, long machineId, decimal ratePerHourRial, DateOnly effectiveFrom, string? reason, long changedByUserId, DateTime changedAtUtc)
    {
        if (ratePerHourRial <= 0 || ratePerHourRial > MaxRatePerHourRial)
        {
            throw new ArgumentOutOfRangeException(nameof(ratePerHourRial), ratePerHourRial, "The hourly rate must be greater than zero (Rial per hour).");
        }

        var note = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (note is { Length: > MaxReasonLength })
        {
            throw new ArgumentException("The reason is limited to " + MaxReasonLength + " characters.", nameof(reason));
        }

        Id = id;
        MachineId = machineId;
        RatePerHourRial = ratePerHourRial;
        EffectiveFrom = effectiveFrom;
        Reason = note;
        ChangedByUserId = changedByUserId;
        ChangedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    public long Id { get; }

    public long MachineId { get; }

    /// <summary>Iranian Rial per hour. Exact decimal; never converted to Toman.</summary>
    public decimal RatePerHourRial { get; }

    /// <summary>The first work date the rate applies to (stored as a calendar date; shown as a Jalali date).</summary>
    public DateOnly EffectiveFrom { get; }

    public string? Reason { get; }

    public long ChangedByUserId { get; }

    public DateTime ChangedAtUtc { get; }
}

public static class MachineRates
{
    /// <summary>
    /// The rate in force for <paramref name="machineId"/> on <paramref name="workDate"/>: the entry with the latest <see cref="MachineHourlyRate.EffectiveFrom"/> that is not after the date.
    /// Null when the machine has no rate yet on that date (a cost must then be refused, never defaulted).
    /// </summary>
    public static MachineHourlyRate? RateOn(IEnumerable<MachineHourlyRate> rates, long machineId, DateOnly workDate)
    {
        ArgumentNullException.ThrowIfNull(rates);
        return rates
            .Where(r => r.MachineId == machineId && r.EffectiveFrom <= workDate)
            .OrderByDescending(r => r.EffectiveFrom)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();
    }
}
