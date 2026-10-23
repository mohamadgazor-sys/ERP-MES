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
    /// Register and maintain production units and machines (stable machine codes). Planning owns the resource master data (Decision Log #82 R2, #89); units are a labelled default,
    /// not separately approved. Rates have their own permission, <see cref="ChangeMachineHourlyRates"/>.
    /// </summary>
    MaintainProductionResources,

    /// <summary>Create, fill, send, close and cancel raw-material inquiries. Drawing owns them (Decision Log #86).</summary>
    ManageRawMaterialInquiries,

    /// <summary>Answer the lines of a raw-material inquiry Yes or No. Warehouse (Decision Log #88).</summary>
    AnswerRawMaterialInquiry,

    /// <summary>Read raw-material inquiries, answers, dispositions and history: Drawing and Warehouse, and Planning and Management read-only (owner decision D8, Decision Log #95). Reading grants no right to edit or answer.</summary>
    ViewRawMaterialInquiries,

    /// <summary>Planning records the supply route for a line Warehouse answered No (owner decision D1, Decision Log #95).</summary>
    RecordInquirySupplyRoute,

    /// <summary>Drawing asks Planning a clarification question while it reviews a request, and closes its own questions (owner decision D1).</summary>
    AskDrawingClarification,

    /// <summary>Planning answers Drawing's clarification questions (owner decision D1).</summary>
    AnswerDrawingClarification,

    /// <summary>Read clarification questions and answers: Drawing and Planning.</summary>
    ViewDrawingClarifications,

    /// <summary>Read the production-gate state of a request (Management/Drawing/contract/inquiry/clarification gates): Planning, Management, Drawing, Warehouse.</summary>
    ViewProductionGates,

    /// <summary>
    /// Read contracts, their versions, PDFs and decision history. Planning and Management (who act on them) and Warehouse (read-only, Decision Log #85). Every other department is denied.
    /// </summary>
    ViewContracts,

    /// <summary>Maintain the material list (register, change name/grade/form, retire). Warehouse owns it (a labelled default, docs/RAW_MATERIAL_INQUIRY_DESIGN.md U-08).</summary>
    MaintainMaterials,

    /// <summary>Read the material list. Every active user (Drawing needs it for raw-material inquiries); a labelled default.</summary>
    ViewMaterials,

    /// <summary>Read material prices per kg: Warehouse and Management (who maintain them) and Planning (cost inputs, docs/COSTING_DESIGN.md). A labelled default.</summary>
    ViewMaterialPrices,

    /// <summary>
    /// Read operators, their machine assignments and qualifications. Planning and Management, and Production (supervisors and operators need to see who works where). A labelled
    /// default, not separately approved; every other department is denied.
    /// </summary>
    ViewOperators,

    /// <summary>Read production units and machines. Read-only, every active signed-in user (a labelled default).</summary>
    ViewPlanningMasterData,

    /// <summary>
    /// Read the machine hourly-rate history. Planning and Management (who may change it, Decision Log #10) and Warehouse (which calculates costs from it, Decision Log #82 R4).
    /// A labelled default, not separately approved; every other department is denied.
    /// </summary>
    ViewMachineRates,

    /// <summary>
    /// Read the QC master data (units of measure, conversions, quantity types) that other departments select from, for example Planning when it defines items (Decision Log #90).
    /// Read-only, for every active signed-in user. Changing the definitions stays with <see cref="MaintainUnitsOfMeasure"/> (QC).
    /// </summary>
    ViewQcMasterData,

    /// <summary>
    /// Read the read-only production overview (the "production queue" page). Approved by the owner (Decision Log #78) for active users of the Management and Planning
    /// departments; every other department is denied by default. It grants no write permission: changing priorities, overriding the queue order and stage or QC actions keep
    /// their own permissions. Views of one production unit's queue may get separate permissions later.
    /// </summary>
    ViewProductionQueue,

    /// <summary>
    /// Release a planned stage for production (demonstration flow, Decision Log #79). DEMO-ONLY mapping approved by the owner (Decision Log #80), from Master Specification 1.4A and 25.4
    /// ("Planning may release production"); it does not finalize the production authorization matrix. Department: Planning.
    /// </summary>
    ReleaseStage,

    /// <summary>Read a production unit's operational queue (Decision Log #99): Management and Planning for every unit, Production for the units they are designated to.</summary>
    ViewUnitQueue,

    /// <summary>Give released stages their Planning position within a unit queue (Planning controls the normal order, Decision Log #78).</summary>
    OrderUnitQueue,

    /// <summary>Stop a released stage because of a problem, and resume it (Planning, and Production for their own unit; Decision Log #99).</summary>
    ManageStageHold,

    /// <summary>Read the working calendar, shifts, capacity and the worker schedule (Planning, Management and Production; Decision Log #100).</summary>
    ViewCalendar,

    /// <summary>Plan operators on unit shifts and record their off-days (Planning; Decision Log #100).</summary>
    ScheduleWorkers,

    /// <summary>Designate Production users to production units (labelled default: Planning; Decision Log #99).</summary>
    ManageUnitDesignations,

    /// <summary>
    /// Start a waiting stage and hand it to QC (demonstration flow, Decision Log #79). DEMO-ONLY mapping approved by the owner (Decision Log #80), from Master Specification 25.4
    /// (production units "record work sessions, deliver completed work to QC"); it does not finalize the production authorization matrix. Department: Production.
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
        [Permission.DefineMakeItems] = new([DepartmentCodes.Planning], null),
        [Permission.DefineRepairItemsAndEditAnyItem] = new([DepartmentCodes.Planning], null),
        [Permission.ImportItemsFromExcel] = new([DepartmentCodes.Planning], null),
        [Permission.MaintainCustomers] = new([DepartmentCodes.Central, DepartmentCodes.Planning], null),

        // Read-only access for every active user (an empty department list means any department, no role is required).
        [Permission.ViewCustomersAndRequests] = new([], null),

        // Planning resource master data (Decision Log #82 R2, #89): Planning registers units and machines; every active user reads them; rates are read by their owners and by Warehouse.
        [Permission.MaintainProductionResources] = new([DepartmentCodes.Planning], null),
        [Permission.ViewPlanningMasterData] = new([], null),
        [Permission.ManageRawMaterialInquiries] = new([DepartmentCodes.Drawing], null),
        [Permission.AnswerRawMaterialInquiry] = new([DepartmentCodes.Warehouse], null),
        [Permission.ViewRawMaterialInquiries] = new([DepartmentCodes.Drawing, DepartmentCodes.Warehouse, DepartmentCodes.Planning, DepartmentCodes.Management], null),
        [Permission.RecordInquirySupplyRoute] = new([DepartmentCodes.Planning], null),
        [Permission.AskDrawingClarification] = new([DepartmentCodes.Drawing], null),
        [Permission.AnswerDrawingClarification] = new([DepartmentCodes.Planning], null),
        [Permission.ViewDrawingClarifications] = new([DepartmentCodes.Drawing, DepartmentCodes.Planning], null),
        [Permission.ViewProductionGates] = new([DepartmentCodes.Planning, DepartmentCodes.Management, DepartmentCodes.Drawing, DepartmentCodes.Warehouse], null),
        [Permission.ViewContracts] = new([DepartmentCodes.Planning, DepartmentCodes.Management, DepartmentCodes.Warehouse], null),
        [Permission.MaintainMaterials] = new([DepartmentCodes.Warehouse], null),
        [Permission.ViewMaterials] = new([], null),
        [Permission.ViewMaterialPrices] = new([DepartmentCodes.Warehouse, DepartmentCodes.Management, DepartmentCodes.Planning], null),
        [Permission.ViewOperators] = new([DepartmentCodes.Planning, DepartmentCodes.Management, DepartmentCodes.Production], null),
        [Permission.ViewMachineRates] = new([DepartmentCodes.Planning, DepartmentCodes.Management, DepartmentCodes.Warehouse], null),

        // QC master data is read by every active user (Planning selects from it); only QC changes it (MaintainUnitsOfMeasure, Decision Log #90).
        [Permission.ViewQcMasterData] = new([], null),

        // Read-only production overview: Management and Planning (Decision Log #78).
        [Permission.ViewProductionQueue] = new([DepartmentCodes.Management, DepartmentCodes.Planning], null),

        // Demonstration stage flow (Decision Log #79): DEMO-ONLY mapping approved by the owner (Decision Log #80); not a finalized production matrix. QC approval uses InspectOrCorrectQc.
        [Permission.ReleaseStage] = new([DepartmentCodes.Planning], null),

        // Operational unit queues (Decision Log #78, #99). Management overrides use OverrideQueueOrder; priority changes use ChangePlanningPriority. Production sees only designated units (checked in the use case).
        [Permission.ViewUnitQueue] = new([DepartmentCodes.Management, DepartmentCodes.Planning, DepartmentCodes.Production], null),
        [Permission.OrderUnitQueue] = new([DepartmentCodes.Planning], null),
        [Permission.ManageStageHold] = new([DepartmentCodes.Planning, DepartmentCodes.Production], null),
        [Permission.ManageUnitDesignations] = new([DepartmentCodes.Planning], null),
        [Permission.ViewCalendar] = new([DepartmentCodes.Planning, DepartmentCodes.Management, DepartmentCodes.Production], null),
        [Permission.ScheduleWorkers] = new([DepartmentCodes.Planning], null),
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
