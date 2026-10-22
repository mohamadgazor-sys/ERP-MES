using ERP.MES.Domain.Identity;

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

    /// <summary>Create, rename, deactivate or reactivate a customer. Not in the original matrix; added by the owner (Decision Log #75): Central and Planning.</summary>
    MaintainCustomers,

    /// <summary>Read the customer list and the request list and details. Not in the original matrix; added by the owner (Decision Log #75): every active signed-in user, read-only.</summary>
    ViewCustomersAndRequests,

    /// <summary>
    /// Read the read-only production overview (the "production queue" page). Approved by the owner (Decision Log #78) for active users of the Management and Planning
    /// departments; every other department is denied by default. It grants no write permission: changing priorities, overriding the queue order and stage or QC actions keep
    /// their own permissions. Views of one production unit's queue may get separate permissions later.
    /// </summary>
    ViewProductionQueue,

    /// <summary>
    /// Release a planned stage for production (demonstration flow, Decision Log #79). IMPLEMENTATION ASSUMPTION derived from Master Specification 1.4A and 25.4 ("Planning may release
    /// production"); it is not in the section 15.2/15.4 tables and needs the owner's confirmation. Department: Planning.
    /// </summary>
    ReleaseStage,

    /// <summary>
    /// Start a waiting stage and hand it to QC (demonstration flow, Decision Log #79). IMPLEMENTATION ASSUMPTION derived from Master Specification 25.4 (production units "record work
    /// sessions, deliver completed work to QC"); not in the section 15.2/15.4 tables, needs the owner's confirmation. Department: Production.
    /// </summary>
    RunStage,
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
        [Permission.MaintainCustomers] = new([DepartmentCodes.Central, DepartmentCodes.Planning], null),

        // Read-only access for every active user (an empty department list means any department, no role is required).
        [Permission.ViewCustomersAndRequests] = new([], null),

        // Read-only production overview: Management and Planning (Decision Log #78).
        [Permission.ViewProductionQueue] = new([DepartmentCodes.Management, DepartmentCodes.Planning], null),

        // Demonstration stage flow (Decision Log #79): assumptions derived from Master Specification 1.4A / 25.4, to be confirmed by the owner. QC approval uses InspectOrCorrectQc.
        [Permission.ReleaseStage] = new([DepartmentCodes.Planning], null),
        [Permission.RunStage] = new([DepartmentCodes.Production], null),
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

    /// <summary>Judged by the stable codes of the user's department and role, never by their display names (Decision Log #67). An inactive user is denied everything.</summary>
    internal static bool IsAllowed(User? user, Permission permission) =>
        user is { IsActive: true } && IsAllowed(user.Department.Code, user.Role.Code, permission);
}
