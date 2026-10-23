namespace ERP.MES.Domain.Requests;

/// <summary>
/// The production specification Planning gives an item when it defines it (Decision Log #86, #95; docs/WORKFLOW_REDESIGN.md U-15). The quantity type comes from the QC-owned definitions (its
/// base-unit mapping says how many base units one ordered unit counts), the material from the Warehouse list. The weight is kilograms per BASE unit, the length and diameter millimetres.
/// A MAKE item needs the quantity type, the material and the weight; a REPAIR item may omit them where they do not apply. Values are exact: more decimals than the scale allows are refused,
/// never rounded.
/// </summary>
public sealed record ItemSpecification(long? QuantityTypeId, long? MaterialId, decimal? WeightKgPerBaseUnit, decimal? LengthMm, decimal? DiameterMm, int BaseUnitsPerUnit = 1)
{
    /// <summary>How many base units one ordered unit counts, taken from the quantity type when the item is defined (piece 1, pair 2, a set the sum of its members; Decision Log #90, #101). QC inspects in base units.</summary>
    /// <summary>Weight scale (decimal 12,4): at most 8 integer digits and 4 decimals.</summary>
    public const decimal MaxWeightKg = 99_999_999.9999m;

    /// <summary>Length and diameter scale (decimal 10,2): at most 8 integer digits and 2 decimals.</summary>
    public const decimal MaxMillimetres = 99_999_999.99m;

    public static ItemSpecification None { get; } = new(null, null, null, null, null);

    /// <summary>What the specification lacks for a MAKE item; <see cref="SpecificationProblem.None"/> when it is complete and valid.</summary>
    public SpecificationProblem Check(WorkType workType)
    {
        if (WeightKgPerBaseUnit is { } weight && !IsValid(weight, MaxWeightKg, 4))
        {
            return SpecificationProblem.WeightInvalid;
        }

        if ((LengthMm is { } length && !IsValid(length, MaxMillimetres, 2)) || (DiameterMm is { } diameter && !IsValid(diameter, MaxMillimetres, 2)))
        {
            return SpecificationProblem.DimensionInvalid;
        }

        if (workType == WorkType.Make && (QuantityTypeId is null || MaterialId is null || WeightKgPerBaseUnit is null))
        {
            return SpecificationProblem.IncompleteForMake;
        }

        return SpecificationProblem.None;
    }

    private static bool IsValid(decimal value, decimal max, int scale) => value > 0 && value <= max && decimal.Round(value, scale) == value;
}

public enum SpecificationProblem
{
    None = 0,
    WeightInvalid,
    DimensionInvalid,

    /// <summary>A MAKE item needs a quantity type, a material and a weight.</summary>
    IncompleteForMake,
}
