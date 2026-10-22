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
        [Permission.MaintainHolidayCalendar] = (["PLANNING"], null),
        [Permission.RegisterDowntime] = (["MAINTENANCE_PM"], null),
        [Permission.CreateAndSubmitRequest] = (["CENTRAL"], null),
        [Permission.ReceiveAndEditRequestDetails] = (["PLANNING"], null),
        [Permission.ReopenRejectedRequest] = (["PLANNING", "MANAGEMENT"], null),
        [Permission.DefineMakeItems] = (["DRAWING"], null),
        [Permission.DefineRepairItemsAndEditAnyItem] = (["PLANNING"], null),
        [Permission.ImportItemsFromExcel] = (["PLANNING", "DRAWING"], null),

        // Added by the owner (Decision Log #75): customer maintenance for Central and Planning; read-only viewing for every active user.
        [Permission.MaintainCustomers] = (["CENTRAL", "PLANNING"], null),
        [Permission.ViewCustomersAndRequests] = ([], null),

        // Read-only production overview (Decision Log #78): Management and Planning, any role; every other department is denied.
        [Permission.MaintainProductionResources] = (["PLANNING"], null),
        [Permission.ViewPlanningMasterData] = ([], null),
        [Permission.ViewOperators] = (["PLANNING", "MANAGEMENT", "PRODUCTION"], null),
        [Permission.ViewMachineRates] = (["PLANNING", "MANAGEMENT", "WAREHOUSE"], null),
        [Permission.ViewQcMasterData] = ([], null),
        [Permission.ViewProductionQueue] = (["MANAGEMENT", "PLANNING"], null),

        // Demonstration stage flow (Decision Log #79): demo-only mapping approved by the owner (Decision Log #80), not a finalized production matrix. QC approval uses InspectOrCorrectQc above.
        [Permission.ReleaseStage] = (["PLANNING"], null),
        [Permission.RunStage] = (["PRODUCTION"], null),
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
    public void The_administrator_role_administers_accounts_reads_and_has_no_business_write_permission()
    {
        var allowed = Enum.GetValues<Permission>().Where(p => AuthorizationRules.IsAllowed("SYSTEM", "ADMIN", p)).ToArray();

        // Read-only viewing (requests, QC master data) is open to every active user (Decision Log #75, #90); the only write permission of the Admin role is administering accounts.
        Assert.Equal(new[] { Permission.AdministerUsers, Permission.ViewCustomersAndRequests, Permission.ViewPlanningMasterData, Permission.ViewQcMasterData }.OrderBy(p => (int)p), allowed.OrderBy(p => (int)p));
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
}
