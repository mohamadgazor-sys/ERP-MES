using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;

namespace ERP.MES.Infrastructure.Tests;

// The default-deny authorization rule set (Domain and Database Specification 15.2 and 15.4, Decision Log #34, T5). The expectations below are written out
// independently of the production table, so an accidental edit of either is caught.
public class AuthorizationRulesTests
{
    // permission -> (allowed departments; empty = any department, required role; null = any role)
    private static readonly Dictionary<Permission, (string[] Departments, string? Role)> Expected = new()
    {
        [Permission.AdministerUsers] = ([], "ADMIN"),
        [Permission.ApproveOrRejectInitialRequest] = (["MANAGEMENT"], "MANAGER"),
        [Permission.RecordDrawingDoabilityDecision] = (["DRAWING"], null),
        [Permission.CreateOrSubmitContract] = (["PLANNING"], null),
        [Permission.ApproveOrRejectContract] = (["MANAGEMENT"], null),
        [Permission.InspectOrCorrectQc] = (["QC"], null),
        [Permission.ChangePlanningPriority] = (["PLANNING", "MANAGEMENT"], null),
        [Permission.OverrideQueueOrder] = (["MANAGEMENT"], null),
        [Permission.ChangeMachineHourlyRates] = (["PLANNING", "MANAGEMENT"], null),
        [Permission.ChangeMaterialPricePerKg] = (["WAREHOUSE", "MANAGEMENT"], null),
        [Permission.RecordFinalWarehouseReceipt] = (["WAREHOUSE"], null),
        [Permission.MaintainUnitsOfMeasure] = (["QC"], null),
        [Permission.MaintainOperatorQualifications] = (["PLANNING"], null),
        [Permission.MaintainHolidayCalendar] = (["PLANNING", "PRODUCTION_MANAGER"], null),
        [Permission.RegisterDowntime] = (["MAINTENANCE_PM"], null),
        [Permission.CreateAndSubmitRequest] = (["CENTRAL"], null),
        [Permission.ReceiveAndEditRequestDetails] = (["PLANNING"], null),
        [Permission.ReopenRejectedRequest] = (["PLANNING", "MANAGEMENT"], null),
        [Permission.DefineMakeItems] = (["PLANNING"], null),
        [Permission.DefineRepairItemsAndEditAnyItem] = (["PLANNING"], null),
        [Permission.ImportItemsFromExcel] = (["PLANNING"], null),

        // Added by the owner (Decision Log #75): customer maintenance for Central and Planning; read-only viewing for every active user.
        [Permission.MaintainCustomers] = (["CENTRAL", "PLANNING"], null),
        [Permission.ViewCustomersAndRequests] = ([], null),

        // Read-only production overview (Decision Log #78): Management and Planning, any role; every other department is denied.
        [Permission.MaintainProductionResources] = ([], "ADMIN"),
        [Permission.ViewPlanningMasterData] = ([], null),
        [Permission.ManageRawMaterialInquiries] = (["DRAWING"], null),
        [Permission.AnswerRawMaterialInquiry] = (["WAREHOUSE"], null),
        [Permission.ViewRawMaterialInquiries] = (["DRAWING", "WAREHOUSE", "PLANNING", "MANAGEMENT"], null),
        [Permission.RecordInquirySupplyRoute] = (["PLANNING"], null),
        [Permission.AskDrawingClarification] = (["DRAWING"], null),
        [Permission.AnswerDrawingClarification] = (["PLANNING"], null),
        [Permission.ViewDrawingClarifications] = (["DRAWING", "PLANNING"], null),
        [Permission.ViewProductionGates] = (["PLANNING", "MANAGEMENT", "DRAWING", "WAREHOUSE"], null),
        [Permission.ViewContracts] = (["PLANNING", "MANAGEMENT", "WAREHOUSE"], null),
        [Permission.MaintainMaterials] = (["WAREHOUSE"], null),
        [Permission.ViewMaterials] = ([], null),
        [Permission.ViewMaterialPrices] = (["WAREHOUSE", "MANAGEMENT", "PLANNING"], null),
        [Permission.ViewOperators] = (["PLANNING", "MANAGEMENT", "PRODUCTION", "PRODUCTION_MANAGER", "SYSTEM"], null),
        [Permission.ViewMachineRates] = (["PLANNING", "MANAGEMENT", "WAREHOUSE"], null),
        [Permission.ViewQcMasterData] = ([], null),
        [Permission.ViewProductionQueue] = (["MANAGEMENT", "PLANNING", "PRODUCTION_MANAGER"], null),

        // Demonstration stage flow (Decision Log #79): demo-only mapping approved by the owner (Decision Log #80), not a finalized production matrix. QC approval uses InspectOrCorrectQc above.
        [Permission.ReleaseStage] = (["PLANNING"], null),
        [Permission.RunStage] = (["PRODUCTION"], null),
        [Permission.ViewWorkSessions] = (["PRODUCTION", "PLANNING", "MANAGEMENT", "PRODUCTION_MANAGER"], null),
        [Permission.OperateWorkSessions] = (["PRODUCTION", "PRODUCTION_MANAGER"], null),
        [Permission.CorrectWorkSessions] = (["PLANNING"], null),
        [Permission.ApproveSessionTime] = (["PLANNING", "PRODUCTION_MANAGER"], null),
        [Permission.ManagePauseReasons] = (["PLANNING"], null),
        [Permission.ViewUnitQueue] = (["MANAGEMENT", "PLANNING", "PRODUCTION", "PRODUCTION_MANAGER"], null),
        [Permission.OrderUnitQueue] = (["PLANNING", "PRODUCTION_MANAGER"], null),
        [Permission.ManageStageHold] = (["PLANNING", "PRODUCTION", "PRODUCTION_MANAGER"], null),
        [Permission.ManageUnitDesignations] = (["PRODUCTION_MANAGER"], null),
        [Permission.AssignWorkingUnits] = ([], "ADMIN"),
        [Permission.SubmitJobReport] = (["PRODUCTION"], null),
        [Permission.ReviewJobReports] = (["PRODUCTION", "PLANNING", "PRODUCTION_MANAGER"], null),
        [Permission.ViewJobReports] = (["PRODUCTION", "PLANNING", "MANAGEMENT", "PRODUCTION_MANAGER"], null),
        [Permission.CorrectStageAction] = (["PLANNING", "PRODUCTION_MANAGER"], null),
        [Permission.ViewKpi] = (["PLANNING", "MANAGEMENT", "PRODUCTION", "PRODUCTION_MANAGER"], null),
        [Permission.RecordWarehouseReceipt] = (["WAREHOUSE"], null),
        [Permission.MarkItemFinishedSent] = (["WAREHOUSE"], null),
        [Permission.RecordCustomerDispatch] = (["WAREHOUSE"], null),
        [Permission.ViewWarehouseCompletion] = (["WAREHOUSE", "PLANNING", "MANAGEMENT"], null),
        [Permission.CalculateItemCost] = (["WAREHOUSE"], null),
        [Permission.ApproveItemPrice] = (["MANAGEMENT"], null),
        [Permission.ViewItemCosts] = (["WAREHOUSE", "PLANNING", "MANAGEMENT"], null),
        [Permission.ProposeOvertime] = (["PRODUCTION"], null),
        [Permission.DecideOvertime] = (["MANAGEMENT", "PRODUCTION_MANAGER"], null),
        [Permission.ConfigureOvertimeLimits] = (["MANAGEMENT"], null),
        [Permission.DecideOvertimeBeyondLimits] = (["MANAGEMENT"], null),
        [Permission.ViewOvertime] = (["PRODUCTION", "PLANNING", "MANAGEMENT", "PRODUCTION_MANAGER"], null),
        [Permission.ViewQcInspections] = (["QC", "PLANNING", "MANAGEMENT"], null),
        [Permission.AcceptShortfall] = (["MANAGEMENT"], null),
        [Permission.OrderShortfallReplacement] = (["PLANNING"], null),
        [Permission.ViewCalendar] = (["MANAGEMENT", "PLANNING", "PRODUCTION", "PRODUCTION_MANAGER"], null),
        [Permission.ScheduleWorkers] = (["PLANNING", "PRODUCTION_MANAGER"], null),
        [Permission.MaintainStageTitles] = (["PLANNING"], null),
        [Permission.ManageItemDrawings] = (["DRAWING"], null),
        [Permission.ViewItemDrawings] = ([], null),
    };

    [Fact]
    public void Every_permission_has_exactly_one_rule_and_nothing_else_is_defined()
    {
        Assert.Equal(Enum.GetValues<Permission>().OrderBy(p => (int)p), AuthorizationRules.Defined.OrderBy(p => (int)p));
        Assert.Equal(Enum.GetValues<Permission>().OrderBy(p => (int)p), Expected.Keys.OrderBy(p => (int)p));
    }

    [Fact]
    public void Every_department_and_role_combination_gets_exactly_the_specified_answer()
    {
        foreach (var (permission, (departments, role)) in Expected)
        {
            foreach (var department in DepartmentCodes.All)
            {
                foreach (var candidateRole in RoleCodes.All)
                {
                    var expected = (departments.Length == 0 || departments.Contains(department)) && (role is null || role == candidateRole);

                    Assert.True(
                        expected == AuthorizationRules.IsAllowed(department, candidateRole, permission),
                        $"{permission} for {department}/{candidateRole}: expected {expected}");
                }
            }
        }
    }

    [Fact]
    public void Only_a_manager_in_management_may_make_the_initial_request_decision()
    {
        Assert.True(AuthorizationRules.IsAllowed("MANAGEMENT", "MANAGER", Permission.ApproveOrRejectInitialRequest));
        Assert.False(AuthorizationRules.IsAllowed("MANAGEMENT", "STAFF", Permission.ApproveOrRejectInitialRequest));
        Assert.False(AuthorizationRules.IsAllowed("PLANNING", "MANAGER", Permission.ApproveOrRejectInitialRequest));
        Assert.False(AuthorizationRules.IsAllowed("SYSTEM", "ADMIN", Permission.ApproveOrRejectInitialRequest));
    }

    [Fact]
    public void The_administrator_role_administers_accounts_and_production_master_data_and_has_no_other_business_write_permission()
    {
        var allowed = Enum.GetValues<Permission>().Where(p => AuthorizationRules.IsAllowed("SYSTEM", "ADMIN", p)).ToArray();

        // Read-only viewing (requests, QC master data) is open to every active user (Decision Log #75, #90); the write permissions of the Admin role are administering accounts, the working-unit assignment and the production master data (Decision Log #122).
        Assert.Equal(new[] { Permission.AdministerUsers, Permission.AssignWorkingUnits, Permission.MaintainProductionResources, Permission.ViewCustomersAndRequests, Permission.ViewMaterials, Permission.ViewOperators, Permission.ViewPlanningMasterData, Permission.ViewQcMasterData, Permission.ViewItemDrawings }.OrderBy(p => (int)p), allowed.OrderBy(p => (int)p));
        Assert.False(AuthorizationRules.IsAllowed("PLANNING", "STAFF", Permission.AdministerUsers));
        Assert.False(AuthorizationRules.IsAllowed("MANAGEMENT", "MANAGER", Permission.AdministerUsers));
    }

    [Fact]
    public void Anything_unknown_or_missing_is_denied()
    {
        Assert.False(AuthorizationRules.IsAllowed("QC", "STAFF", (Permission)9999));
        Assert.False(AuthorizationRules.IsAllowed("QC", "STAFF", (Permission)0));
        Assert.False(AuthorizationRules.IsAllowed(null, "STAFF", Permission.InspectOrCorrectQc));
        Assert.False(AuthorizationRules.IsAllowed("QC", null, Permission.InspectOrCorrectQc));
        Assert.False(AuthorizationRules.IsAllowed("qc", "STAFF", Permission.InspectOrCorrectQc));
        Assert.False(AuthorizationRules.IsAllowed("UNKNOWN", "STAFF", Permission.InspectOrCorrectQc));
        Assert.False(AuthorizationRules.IsAllowed((Principal?)null, Permission.InspectOrCorrectQc));
    }

    [Fact]
    public void A_principal_is_judged_by_its_department_and_role()
    {
        var qc = new Principal(1, "qc.user", "QC", "QC", "STAFF", false);
        var planning = new Principal(2, "plan.user", "Planning", "PLANNING", "STAFF", false);

        Assert.True(AuthorizationRules.IsAllowed(qc, Permission.InspectOrCorrectQc));
        Assert.False(AuthorizationRules.IsAllowed(planning, Permission.InspectOrCorrectQc));
        Assert.True(AuthorizationRules.IsAllowed(planning, Permission.ChangePlanningPriority));
    }

    [Fact]
    public void Overtime_is_decided_by_management_or_the_production_manager_and_never_by_planning()
    {
        foreach (var role in RoleCodes.All)
        {
            Assert.True(AuthorizationRules.IsAllowed("MANAGEMENT", role, Permission.DecideOvertime));
            Assert.True(AuthorizationRules.IsAllowed("PRODUCTION_MANAGER", role, Permission.DecideOvertime));
            Assert.False(AuthorizationRules.IsAllowed("PLANNING", role, Permission.DecideOvertime));
            Assert.False(AuthorizationRules.IsAllowed("PLANNING", role, Permission.ConfigureOvertimeLimits));
            Assert.False(AuthorizationRules.IsAllowed("PRODUCTION_MANAGER", role, Permission.ConfigureOvertimeLimits));
            Assert.True(AuthorizationRules.IsAllowed("MANAGEMENT", role, Permission.ConfigureOvertimeLimits));

            // Beyond the limits only Management decides (Decision Log #122 Q11).
            Assert.True(AuthorizationRules.IsAllowed("MANAGEMENT", role, Permission.DecideOvertimeBeyondLimits));
            Assert.False(AuthorizationRules.IsAllowed("PRODUCTION_MANAGER", role, Permission.DecideOvertimeBeyondLimits));
        }
    }

    [Fact]
    public void The_production_manager_has_operational_authority_but_no_master_data_account_or_commercial_authority()
    {
        var allowed = Enum.GetValues<Permission>().Where(p => AuthorizationRules.IsAllowed("PRODUCTION_MANAGER", "MANAGER", p)).ToHashSet();

        Assert.Contains(Permission.ViewUnitQueue, allowed);
        Assert.Contains(Permission.OrderUnitQueue, allowed);
        Assert.Contains(Permission.ManageStageHold, allowed);
        Assert.Contains(Permission.OperateWorkSessions, allowed);
        Assert.Contains(Permission.ReviewJobReports, allowed);
        Assert.Contains(Permission.ManageUnitDesignations, allowed);
        Assert.Contains(Permission.CorrectStageAction, allowed);
        Assert.Contains(Permission.ApproveSessionTime, allowed);
        Assert.Contains(Permission.MaintainHolidayCalendar, allowed);

        // Not Program Admin's authority and not Management's commercial or queue-override authority.
        Assert.DoesNotContain(Permission.AdministerUsers, allowed);
        Assert.DoesNotContain(Permission.AssignWorkingUnits, allowed);
        Assert.DoesNotContain(Permission.MaintainProductionResources, allowed);
        Assert.DoesNotContain(Permission.ApproveItemPrice, allowed);
        Assert.DoesNotContain(Permission.ChangeMachineHourlyRates, allowed);
        Assert.DoesNotContain(Permission.OverrideQueueOrder, allowed);
        Assert.DoesNotContain(Permission.ChangePlanningPriority, allowed);
        Assert.DoesNotContain(Permission.ReleaseStage, allowed);
    }

    [Fact]
    public void Production_master_data_and_working_units_belong_to_the_program_admin_and_no_longer_to_planning()
    {
        Assert.True(AuthorizationRules.IsAllowed("SYSTEM", "ADMIN", Permission.MaintainProductionResources));
        Assert.True(AuthorizationRules.IsAllowed("SYSTEM", "ADMIN", Permission.AssignWorkingUnits));
        Assert.False(AuthorizationRules.IsAllowed("PLANNING", "MANAGER", Permission.MaintainProductionResources));
        Assert.False(AuthorizationRules.IsAllowed("PLANNING", "MANAGER", Permission.AssignWorkingUnits));
        Assert.False(AuthorizationRules.IsAllowed("PLANNING", "MANAGER", Permission.ManageUnitDesignations));
        Assert.True(AuthorizationRules.IsAllowed("PLANNING", "STAFF", Permission.ChangeMachineHourlyRates));
    }
}
