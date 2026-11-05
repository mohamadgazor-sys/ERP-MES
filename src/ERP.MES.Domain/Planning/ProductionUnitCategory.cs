namespace ERP.MES.Domain.Planning;

/// <summary>
/// The controlled production-unit category list of the Access system (Master Specification §16.2, Decision Log #129): exactly these nine codes, stored as written. A unit is external when its
/// category is not <see cref="InternalProduction"/>. No code may be added or renamed; the Persian texts of the screens are display labels only.
/// </summary>
public static class ProductionUnitCategory
{
    public const string InternalProduction = "INTERNAL_PRODUCTION";
    public const string ExternalPlating = "EXTERNAL_PLATING";
    public const string ExternalHeatTreatment = "EXTERNAL_HEAT_TREATMENT";
    public const string ExternalCasting = "EXTERNAL_CASTING";
    public const string ExternalCeramic = "EXTERNAL_CERAMIC";
    public const string ExternalGrinding = "EXTERNAL_GRINDING";
    public const string ExternalGearCutting = "EXTERNAL_GEAR_CUTTING";
    public const string ExternalTehran = "EXTERNAL_TEHRAN";
    public const string ExternalOther = "EXTERNAL_OTHER";

    public const int MaxLength = 40;

    /// <summary>The nine codes in the order of Master Specification §16.2.</summary>
    public static IReadOnlyList<string> All { get; } =
        [InternalProduction, ExternalPlating, ExternalHeatTreatment, ExternalCasting, ExternalCeramic, ExternalGrinding, ExternalGearCutting, ExternalTehran, ExternalOther];

    public static bool IsValid(string? code) => code is not null && All.Contains(code, StringComparer.Ordinal);

    public static bool IsExternal(string code) => !string.Equals(code, InternalProduction, StringComparison.Ordinal);
}
