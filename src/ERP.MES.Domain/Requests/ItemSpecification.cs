namespace ERP.MES.Domain.Requests;

/// <summary>
/// The production specification Planning gives an item when it defines it (Decision Log #86, #95; docs/WORKFLOW_REDESIGN.md U-15). The quantity type comes from the QC-owned definitions (its
/// base-unit mapping says how many base units one ordered unit counts), the material from the Warehouse list. The weight is kilograms per BASE unit, the length and diameter millimetres.
/// A MAKE item needs the quantity type, the material and the weight; a REPAIR item may omit them where they do not apply. Values are exact: more decimals than the scale allows are refused,
/// never rounded.
/// </summary>
public sealed record ItemSpecification(
    long? QuantityTypeId, long? MaterialId, decimal? WeightKgPerBaseUnit, decimal? LengthMm, decimal? DiameterMm, int BaseUnitsPerUnit = 1, DateOnly? DeliveryDate = null, string? WarehouseItemCode = null)
{
    /// <summary>The longest warehouse item code (Decision Log #124 B5).</summary>
    public const int MaxWarehouseItemCodeLength = 40;

    /// <summary>How many base units one ordered unit counts, taken from the quantity type when the item is defined (piece 1, pair 2, a set the sum of its members; Decision Log #90, #101). QC inspects in base units.</summary>
    /// <summary>Weight scale (decimal 12,4): at most 8 integer digits and 4 decimals.</summary>
    public const decimal MaxWeightKg = 99_999_999.9999m;

    /// <summary>Length and diameter scale (decimal 10,2): at most 8 integer digits and 2 decimals.</summary>
    public const decimal MaxMillimetres = 99_999_999.99m;

    public static ItemSpecification None { get; } = new(null, null, null, null, null);

    /// <summary>
    /// What the specification lacks; <see cref="SpecificationProblem.None"/> when it is complete and valid. A REPAIR item needs a delivery date on or after the request date and a warehouse item
    /// code of 1 to 40 characters (Decision Log #124 B5); a MAKE item needs neither, but a value that is given is checked the same way.
    /// </summary>
    public SpecificationProblem Check(WorkType workType, DateOnly requestDate)
    {
        if (WarehouseItemCode is { } code && (code.Length == 0 || code.Length > MaxWarehouseItemCodeLength || code.Trim() != code))
        {
            return SpecificationProblem.WarehouseCodeInvalid;
        }

        if (DeliveryDate is { } delivery && delivery < requestDate)
        {
            return SpecificationProblem.DeliveryBeforeRequest;
        }

        if (workType == WorkType.Repair && (DeliveryDate is null || WarehouseItemCode is null))
        {
            return SpecificationProblem.RepairFieldsMissing;
        }

        return Check(workType);
    }

    /// <summary>What the specification lacks for a MAKE item, without the REPAIR fields (they need the request date).</summary>
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

    /// <summary>A REPAIR item needs a delivery date and a warehouse item code (Decision Log #124 B5).</summary>
    RepairFieldsMissing,

    /// <summary>The warehouse item code is empty, longer than 40 characters or has leading or trailing white space.</summary>
    WarehouseCodeInvalid,

    /// <summary>The delivery date is before the request date.</summary>
    DeliveryBeforeRequest,
}
