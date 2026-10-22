using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Materials;

/// <summary>
/// One entry of a material's price history (Decision Log #10, #91; docs/COSTING_DESIGN.md). Iranian Rial per kilogram, applying from <see cref="EffectiveFrom"/> until a later entry
/// replaces it. Append-only: a price is never edited or deleted, so a cost calculated earlier can always be explained by the price in force on its pricing date.
/// </summary>
public sealed class MaterialPrice
{
    /// <summary>A sanity bound only (10^13 Rial per kg); the real limits are business decisions.</summary>
    public const decimal MaxPricePerKgRial = 10_000_000_000_000m;

    public const int MaxReasonLength = 500;

    public MaterialPrice(long id, long materialId, decimal pricePerKgRial, DateOnly effectiveFrom, string? reason, long changedByUserId, DateTime changedAtUtc)
    {
        if (pricePerKgRial <= 0 || pricePerKgRial > MaxPricePerKgRial)
        {
            throw new ArgumentOutOfRangeException(nameof(pricePerKgRial), pricePerKgRial, "The price must be greater than zero (Rial per kg).");
        }

        var note = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (note is { Length: > MaxReasonLength })
        {
            throw new ArgumentException("The reason is limited to " + MaxReasonLength + " characters.", nameof(reason));
        }

        Id = id;
        MaterialId = materialId;
        PricePerKgRial = pricePerKgRial;
        EffectiveFrom = effectiveFrom;
        Reason = note;
        ChangedByUserId = changedByUserId;
        ChangedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    public long Id { get; }

    public long MaterialId { get; }

    /// <summary>Iranian Rial per kilogram. Exact decimal; never converted to Toman.</summary>
    public decimal PricePerKgRial { get; }

    public DateOnly EffectiveFrom { get; }

    public string? Reason { get; }

    public long ChangedByUserId { get; }

    public DateTime ChangedAtUtc { get; }
}

public static class MaterialPrices
{
    /// <summary>The price in force on <paramref name="date"/>, or null when the material has none yet (a cost must then be refused, never defaulted).</summary>
    public static MaterialPrice? PriceOn(IEnumerable<MaterialPrice> prices, long materialId, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(prices);
        return prices
            .Where(p => p.MaterialId == materialId && p.EffectiveFrom <= date)
            .OrderByDescending(p => p.EffectiveFrom)
            .ThenByDescending(p => p.Id)
            .FirstOrDefault();
    }
}
