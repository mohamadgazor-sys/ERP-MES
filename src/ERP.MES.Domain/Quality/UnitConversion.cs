namespace ERP.MES.Domain.Quality;

/// <summary>
/// "One <see cref="FromUnitId"/> equals <see cref="Factor"/> of <see cref="ToUnitId"/>" (for example PAIR to PCS, factor 2). The reciprocal is NOT stored: it is derived when
/// needed (PCS to PAIR is 1/2), as Decision Log #9 requires; <see cref="UnitConversions.TryResolve"/> does it. A conversion is never deleted; it is retired by deactivating it.
/// </summary>
public sealed class UnitConversion
{
    public UnitConversion(long id, long fromUnitId, long toUnitId, decimal factor, long? createdByUserId, DateTime createdAtUtc)
    {
        if (fromUnitId == toUnitId)
        {
            throw new ArgumentException("A conversion needs two different units.", nameof(toUnitId));
        }

        if (factor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "The conversion factor must be positive and not zero.");
        }

        Id = id;
        FromUnitId = fromUnitId;
        ToUnitId = toUnitId;
        Factor = factor;
        IsActive = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Requirements.Utc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
    }

    public long Id { get; }

    public long FromUnitId { get; }

    public long ToUnitId { get; }

    /// <summary>How many <see cref="ToUnitId"/> one <see cref="FromUnitId"/> is. Exact decimal, positive.</summary>
    public decimal Factor { get; }

    public bool IsActive { get; private set; }

    public long? CreatedByUserId { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void SetActive(bool isActive, DateTime changedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }
}

public static class UnitConversions
{
    /// <summary>
    /// The factor that turns one <paramref name="from"/> into units of <paramref name="to"/>: the active direct conversion, or the reciprocal of the active opposite conversion
    /// (PAIR to PCS = 2 gives PCS to PAIR = 0.5). A unit converts to itself with factor 1. Returns false when no active conversion links the two units. No chaining through a
    /// third unit is attempted.
    /// </summary>
    public static bool TryResolve(IEnumerable<UnitConversion> conversions, long from, long to, out decimal factor)
    {
        ArgumentNullException.ThrowIfNull(conversions);
        if (from == to)
        {
            factor = 1m;
            return true;
        }

        var active = conversions.Where(c => c.IsActive).ToList();
        var direct = active.FirstOrDefault(c => c.FromUnitId == from && c.ToUnitId == to);
        if (direct is not null)
        {
            factor = direct.Factor;
            return true;
        }

        var opposite = active.FirstOrDefault(c => c.FromUnitId == to && c.ToUnitId == from);
        if (opposite is not null)
        {
            factor = 1m / opposite.Factor;
            return true;
        }

        factor = 0m;
        return false;
    }
}
