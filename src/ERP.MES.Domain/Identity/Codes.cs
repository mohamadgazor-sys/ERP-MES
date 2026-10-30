namespace ERP.MES.Domain.Identity;

/// <summary>
/// Stable department codes (Domain and Database Specification 15.3, Decision Log #30, #67 and #122). The code CENTRAL keeps its value; its display name is «منشی» (Secretary). Authorization and every other rule use these codes;
/// the display name is a replaceable label that never drives a decision.
/// </summary>
public static class DepartmentCodes
{
    public const string Central = "CENTRAL";
    public const string Management = "MANAGEMENT";
    public const string Planning = "PLANNING";
    public const string Drawing = "DRAWING";
    public const string Warehouse = "WAREHOUSE";
    public const string Production = "PRODUCTION";
    public const string Qc = "QC";
    public const string MaintenancePm = "MAINTENANCE_PM";
    public const string System = "SYSTEM";

    /// <summary>Production Manager («مدیر تولید»): operational authority over every production unit (Decision Log #122). Role Manager.</summary>
    public const string ProductionManager = "PRODUCTION_MANAGER";

    public static IReadOnlyList<string> All { get; } =
        [Central, Management, Planning, Drawing, Warehouse, Production, Qc, MaintenancePm, System, ProductionManager];
}

/// <summary>Stable role codes (Decision Log #30 and #67).</summary>
public static class RoleCodes
{
    public const string Admin = "ADMIN";
    public const string Manager = "MANAGER";
    public const string Staff = "STAFF";

    public static IReadOnlyList<string> All { get; } = [Admin, Manager, Staff];
}
