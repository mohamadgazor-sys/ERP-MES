namespace ERP.MES.Application.Security;

/// <summary>
/// Actions that are authorized by department and role. Only actions with an approved rule are listed: Domain and Database Specification 15.2 (the canonical matrix)
/// and 15.4 (the provisional development map, Decision Log #34). Anything not listed is denied (default deny); in particular there is no "administrator may do
/// everything": the Admin role administers accounts and nothing else.
/// </summary>
public enum Permission
{
    AdministerUsers = 1,
    ApproveOrRejectInitialRequest,
    RecordDrawingDoabilityDecision,
    CreateOrSubmitContract,
    ApproveOrRejectContract,
    InspectOrCorrectQc,
    ChangePlanningPriority,
    OverrideQueueOrder,
    ChangeMachineHourlyRates,
    ChangeMaterialPricePerKg,
    RecordFinalWarehouseReceipt,
    MaintainUnitsOfMeasure,
    MaintainHolidayCalendar,
    RegisterDowntime,
    CreateAndSubmitRequest,
    ReceiveAndEditRequestDetails,
    ReopenRejectedRequest,
    DefineMakeItems,
    DefineRepairItemsAndEditAnyItem,
    ImportItemsFromExcel,
}

/// <summary>
/// The default-deny rule set. The decision uses the department and role of an active user; the UI only mirrors it. Called at the use-case boundary
/// (Architecture Specification 17.2).
/// </summary>
public static class AuthorizationRules
{
    private sealed record Rule(IReadOnlyList<string> Departments, string? Role);

    private static readonly Dictionary<Permission, Rule> Rules = new()
    {
        [Permission.AdministerUsers] = new([], RoleCodes.Admin),
        [Permission.ApproveOrRejectInitialRequest] = new([DepartmentCodes.Management], RoleCodes.Manager),
        [Permission.RecordDrawingDoabilityDecision] = new([DepartmentCodes.Drawing], null),
        [Permission.CreateOrSubmitContract] = new([DepartmentCodes.Planning], null),
        [Permission.ApproveOrRejectContract] = new([DepartmentCodes.Management], null),
        [Permission.InspectOrCorrectQc] = new([DepartmentCodes.Qc], null),
        [Permission.ChangePlanningPriority] = new([DepartmentCodes.Planning, DepartmentCodes.Management], null),
        [Permission.OverrideQueueOrder] = new([DepartmentCodes.Management], null),
        [Permission.ChangeMachineHourlyRates] = new([DepartmentCodes.Planning, DepartmentCodes.Management], null),
        [Permission.ChangeMaterialPricePerKg] = new([DepartmentCodes.Warehouse, DepartmentCodes.Management], null),
        [Permission.RecordFinalWarehouseReceipt] = new([DepartmentCodes.Warehouse], null),
        [Permission.MaintainUnitsOfMeasure] = new([DepartmentCodes.Qc], null),
        [Permission.MaintainHolidayCalendar] = new([DepartmentCodes.Planning], null),
        [Permission.RegisterDowntime] = new([DepartmentCodes.MaintenancePm], null),
        [Permission.CreateAndSubmitRequest] = new([DepartmentCodes.Central], null),
        [Permission.ReceiveAndEditRequestDetails] = new([DepartmentCodes.Planning], null),
        [Permission.ReopenRejectedRequest] = new([DepartmentCodes.Planning, DepartmentCodes.Management], null),
        [Permission.DefineMakeItems] = new([DepartmentCodes.Drawing], null),
        [Permission.DefineRepairItemsAndEditAnyItem] = new([DepartmentCodes.Planning], null),
        [Permission.ImportItemsFromExcel] = new([DepartmentCodes.Planning, DepartmentCodes.Drawing], null),
    };

    /// <summary>All permissions that have a rule, for tests and documentation.</summary>
    public static IReadOnlyCollection<Permission> Defined => Rules.Keys;

    /// <summary>True only for an existing rule that the department and role satisfy. An unknown permission, or no principal, is denied.</summary>
    public static bool IsAllowed(string? departmentCode, string? roleCode, Permission permission)
    {
        if (departmentCode is null || roleCode is null || !Rules.TryGetValue(permission, out var rule))
        {
            return false;
        }

        var departmentOk = rule.Departments.Count == 0 || rule.Departments.Contains(departmentCode, StringComparer.Ordinal);
        var roleOk = rule.Role is null || string.Equals(rule.Role, roleCode, StringComparison.Ordinal);
        return departmentOk && roleOk;
    }

    public static bool IsAllowed(Principal? principal, Permission permission) =>
        principal is not null && IsAllowed(principal.DepartmentCode, principal.RoleCode, permission);

    internal static bool IsAllowed(UserAccount? user, Permission permission) =>
        user is { IsActive: true } && IsAllowed(user.Department.Code, user.Role.Code, permission);
}
