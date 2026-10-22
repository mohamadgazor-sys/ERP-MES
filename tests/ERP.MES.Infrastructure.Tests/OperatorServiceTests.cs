using ERP.MES.Application.Persistence;
using ERP.MES.Application.Planning;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Planning;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// Operators, master assignments and qualifications (Decision Log #82 R2, #89) on a real SQLite file with the real authorization.
public class OperatorServiceTests
{
    private const string InitialPassword = "Staff-Pass-Phrase-2";
    private const string OwnPassword = "My-Own-Pass-Phrase-3";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class World : IDisposable
    {
        public required SecurityFixture Fixture { get; init; }

        public required Principal Planning { get; init; }

        public required Principal Management { get; init; }

        public required Principal Warehouse { get; init; }

        public required Principal Production { get; init; }

        public required Principal Qc { get; init; }

        public required WorkflowServices Services { get; init; }

        public OperatorService Ops => Services.Operators;

        public PlanningMasterService Master => Services.PlanningMaster;

        public void Dispose() => Fixture.Dispose();
    }

    private static async Task<Principal> UserAsync(SecurityFixture fixture, Principal admin, string name, string department)
    {
        await fixture.CreateUserAsync(admin, name, InitialPassword, department, RoleCodes.Staff, Token);
        var first = await fixture.SignInAsync(name, InitialPassword, Token);
        await fixture.Services.Accounts.ChangeOwnPasswordAsync(first, InitialPassword, OwnPassword, Token);
        return await fixture.SignInAsync(name, OwnPassword, Token);
    }

    private static async Task<World> StartAsync()
    {
        var fixture = await SecurityFixture.CreateAsync();
        var admin = await fixture.BootstrapAdminAsync(cancellationToken: Token);
        return new World
        {
            Fixture = fixture,
            Planning = await UserAsync(fixture, admin, "planning.one", DepartmentCodes.Planning),
            Management = await UserAsync(fixture, admin, "management.one", DepartmentCodes.Management),
            Warehouse = await UserAsync(fixture, admin, "warehouse.one", DepartmentCodes.Warehouse),
            Production = await UserAsync(fixture, admin, "production.one", DepartmentCodes.Production),
            Qc = await UserAsync(fixture, admin, "qc.one", DepartmentCodes.Qc),
            Services = WorkflowServices.Create(fixture.Db.ContextFactory, fixture.Services.Authorization, PersistenceTestData.Dates, fixture.Clock),
        };
    }

    private static async Task<Stored<Machine>> MachineAsync(World w, string code = "FCC-06")
    {
        var unit = (await w.Master.AddUnitAsync(w.Planning, "MILLING", "فرزکاری", Token)).Value!;
        var machine = await w.Master.AddMachineAsync(w.Planning, code, "فرز CNC", unit.Value.Id, Token);
        Assert.True(machine.Succeeded, machine.Status + " " + machine.Issue);
        return machine.Value!;
    }

    private static async Task<Stored<Operator>> OperatorAsync(World w, string code = "OP-01", string name = "علی رضایی")
    {
        var outcome = await w.Ops.RegisterAsync(w.Planning, code, name, null, Token);
        Assert.True(outcome.Succeeded, outcome.Status + " " + outcome.Issue);
        return outcome.Value!;
    }

    private static long Count(World w, string tableAndWhere) => long.Parse(w.Fixture.Db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Planning_registers_an_operator_with_audit_and_planning_management_and_production_read_it()
    {
        using var w = await StartAsync();
        var worker = await OperatorAsync(w);

        foreach (var reader in new[] { w.Planning, w.Management, w.Production })
        {
            var data = (await w.Ops.LoadAsync(reader, Token)).Value!;
            Assert.Equal("OP-01", Assert.Single(data.Operators).Value.Code);
        }

        foreach (var denied in new Principal?[] { w.Qc, w.Warehouse, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Ops.LoadAsync(denied, Token)).Status);
        }

        Assert.Equal("OperatorRegistered", Assert.Single(w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode = 'OperatorRegistered'")));
        Assert.Equal(w.Planning.UserId, worker.Value.CreatedByUserId);
        Assert.True(worker.Value.IsActive);
    }

    [Fact]
    public async Task Only_planning_changes_operators_assignments_and_qualifications()
    {
        using var w = await StartAsync();
        var worker = await OperatorAsync(w);
        var machine = await MachineAsync(w);

        foreach (var user in new Principal?[] { w.Management, w.Warehouse, w.Production, w.Qc, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Ops.RegisterAsync(user, "OP-02", "x", null, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Ops.RenameAsync(user, worker.Value.Id, worker.Version, "x", Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Ops.SetActiveAsync(user, worker.Value.Id, worker.Version, false, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Ops.AssignAsync(user, worker.Value.Id, machine.Value.Id, "1405/07/01", true, null, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Ops.RecordQualificationAsync(user, worker.Value.Id, machine.Value.Id, "1405/07/01", null, Token)).Status);
            Assert.False(await w.Ops.CanMaintainAsync(user, Token));
        }

        Assert.True(await w.Ops.CanMaintainAsync(w.Planning, Token));
        Assert.Equal(1, Count(w, "Operators"));
        Assert.Equal(0, Count(w, "OperatorMachineAssignments"));
        Assert.Equal(0, Count(w, "OperatorMachineQualifications"));
    }

    [Theory]
    [InlineData("", "x", ValidationIssue.CodeInvalid)]
    [InlineData("op-02", "x", ValidationIssue.CodeInvalid)]
    [InlineData("OP-02", " ", ValidationIssue.OperatorNameInvalid)]
    [InlineData("OP-01", "x", ValidationIssue.CodeDuplicate)]
    public async Task An_invalid_or_duplicate_operator_is_refused_and_nothing_is_saved(string code, string name, ValidationIssue expected)
    {
        using var w = await StartAsync();
        await OperatorAsync(w);

        var outcome = await w.Ops.RegisterAsync(w.Planning, code, name, null, Token);

        Assert.Equal(OperationStatus.ValidationFailed, outcome.Status);
        Assert.Equal(expected, outcome.Issue);
        Assert.Equal(1, Count(w, "Operators"));
    }

    [Fact]
    public async Task An_operator_links_to_one_active_user_only_and_a_user_cannot_be_linked_twice()
    {
        using var w = await StartAsync();
        var first = (await w.Ops.RegisterAsync(w.Planning, "OP-01", "علی", w.Production.UserId, Token)).Value!;
        Assert.Equal(w.Production.UserId, first.Value.UserId);

        Assert.Equal(ValidationIssue.UserAlreadyLinked, (await w.Ops.RegisterAsync(w.Planning, "OP-02", "رضا", w.Production.UserId, Token)).Issue);
        Assert.Equal(ValidationIssue.UserInvalid, (await w.Ops.RegisterAsync(w.Planning, "OP-03", "حسن", 987654, Token)).Issue);
        Assert.Equal(1, Count(w, "Operators"));

        var second = await OperatorAsync(w, "OP-04", "حسن");
        Assert.Equal(ValidationIssue.UserAlreadyLinked, (await w.Ops.LinkUserAsync(w.Planning, second.Value.Id, second.Version, w.Production.UserId, Token)).Issue);
        var linked = (await w.Ops.LinkUserAsync(w.Planning, second.Value.Id, second.Version, w.Qc.UserId, Token)).Value!;
        Assert.Equal(w.Qc.UserId, linked.Value.UserId);
        Assert.Equal("OperatorLinkedToUser", Assert.Single(w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode = 'OperatorLinkedToUser'")));
    }

    [Fact]
    public async Task Rename_and_deactivate_keep_the_operator_and_stale_versions_conflict()
    {
        using var w = await StartAsync();
        var worker = await OperatorAsync(w);

        var renamed = (await w.Ops.RenameAsync(w.Planning, worker.Value.Id, worker.Version, "علی رضایی‌نژاد", Token)).Value!;
        Assert.Equal("علی رضایی‌نژاد", renamed.Value.FullName);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Ops.SetActiveAsync(w.Planning, worker.Value.Id, worker.Version, false, Token)).Status);

        var off = (await w.Ops.SetActiveAsync(w.Planning, worker.Value.Id, renamed.Version, false, Token)).Value!;
        Assert.False(off.Value.IsActive);
        Assert.Equal(1, Count(w, "Operators"));
    }

    [Fact]
    public async Task Assigning_without_a_qualification_succeeds_with_a_warning_and_with_one_without()
    {
        using var w = await StartAsync();
        var worker = await OperatorAsync(w);
        var machine = await MachineAsync(w);

        var unqualified = await w.Ops.AssignAsync(w.Planning, worker.Value.Id, machine.Value.Id, "1405/07/01", true, "نیاز تولید", Token);
        Assert.True(unqualified.Succeeded);
        Assert.Contains(OperatorService.NoQualificationWarning, unqualified.WarningList);
        Assert.True(unqualified.Value!.Value.IsPrimary);

        Assert.True((await w.Ops.CloseAssignmentAsync(w.Planning, unqualified.Value.Value.Id, unqualified.Value.Version, "1405/07/10", Token)).Succeeded);
        Assert.True((await w.Ops.RecordQualificationAsync(w.Planning, worker.Value.Id, machine.Value.Id, "1405/06/01", "گواهی داخلی", Token)).Succeeded);

        var qualified = await w.Ops.AssignAsync(w.Planning, worker.Value.Id, machine.Value.Id, "1405/07/11", false, null, Token);
        Assert.True(qualified.Succeeded);
        Assert.DoesNotContain(OperatorService.NoQualificationWarning, qualified.WarningList);
        Assert.Equal(2, Count(w, "OperatorMachineAssignments"));
    }

    [Fact]
    public async Task Only_one_open_assignment_per_operator_and_machine_and_closed_ones_stay_as_history()
    {
        using var w = await StartAsync();
        var worker = await OperatorAsync(w);
        var machine = await MachineAsync(w);
        var first = (await w.Ops.AssignAsync(w.Planning, worker.Value.Id, machine.Value.Id, "1405/07/01", true, null, Token)).Value!;

        Assert.Equal(ValidationIssue.AssignmentExists, (await w.Ops.AssignAsync(w.Planning, worker.Value.Id, machine.Value.Id, "1405/07/05", true, null, Token)).Issue);
        Assert.Equal(ValidationIssue.EndBeforeStart, (await w.Ops.CloseAssignmentAsync(w.Planning, first.Value.Id, first.Version, "1405/06/30", Token)).Issue);
        Assert.Equal(ValidationIssue.DateInvalid, (await w.Ops.CloseAssignmentAsync(w.Planning, first.Value.Id, first.Version, "not a date", Token)).Issue);

        var closed = (await w.Ops.CloseAssignmentAsync(w.Planning, first.Value.Id, first.Version, "1405/07/20", Token)).Value!;
        Assert.False(closed.Value.IsOpen);
        Assert.Equal(ValidationIssue.AssignmentClosed, (await w.Ops.CloseAssignmentAsync(w.Planning, first.Value.Id, closed.Version, "1405/07/21", Token)).Issue);
        Assert.True((await w.Ops.AssignAsync(w.Planning, worker.Value.Id, machine.Value.Id, "1405/07/21", true, null, Token)).Succeeded);

        var actions = w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode LIKE 'OperatorMachineAssignment%' OR ActionCode = 'OperatorMachineAssigned' ORDER BY Id").ToArray();
        Assert.Equal(new[] { "OperatorMachineAssigned", "OperatorMachineAssignmentClosed", "OperatorMachineAssigned" }, actions);
        Assert.Equal(2, Count(w, "OperatorMachineAssignments"));
    }

    [Fact]
    public async Task An_assignment_needs_an_active_operator_machine_and_a_valid_date_and_a_stale_close_conflicts()
    {
        using var w = await StartAsync();
        var worker = await OperatorAsync(w);
        var machine = await MachineAsync(w);

        Assert.Equal(ValidationIssue.OperatorInvalid, (await w.Ops.AssignAsync(w.Planning, 4242, machine.Value.Id, "1405/07/01", true, null, Token)).Issue);
        Assert.Equal(ValidationIssue.MachineInvalid, (await w.Ops.AssignAsync(w.Planning, worker.Value.Id, 4242, "1405/07/01", true, null, Token)).Issue);
        Assert.Equal(ValidationIssue.DateRequired, (await w.Ops.AssignAsync(w.Planning, worker.Value.Id, machine.Value.Id, " ", true, null, Token)).Issue);
        Assert.Equal(ValidationIssue.DateInvalid, (await w.Ops.AssignAsync(w.Planning, worker.Value.Id, machine.Value.Id, "1405/13/40", true, null, Token)).Issue);
        Assert.Equal(0, Count(w, "OperatorMachineAssignments"));

        var off = (await w.Ops.SetActiveAsync(w.Planning, worker.Value.Id, worker.Version, false, Token)).Value!;
        Assert.Equal(ValidationIssue.OperatorInvalid, (await w.Ops.AssignAsync(w.Planning, off.Value.Id, machine.Value.Id, "1405/07/01", true, null, Token)).Issue);
        Assert.Equal(OperationStatus.NotFound, (await w.Ops.CloseAssignmentAsync(w.Planning, 4242, 1, "1405/07/01", Token)).Status);
    }

    [Fact]
    public async Task A_qualification_is_recorded_ended_and_kept_as_history()
    {
        using var w = await StartAsync();
        var worker = await OperatorAsync(w);
        var machine = await MachineAsync(w);
        var recorded = (await w.Ops.RecordQualificationAsync(w.Planning, worker.Value.Id, machine.Value.Id, "1405/01/01", "آموزش", Token)).Value!;

        Assert.Equal(ValidationIssue.QualificationExists, (await w.Ops.RecordQualificationAsync(w.Planning, worker.Value.Id, machine.Value.Id, "1405/02/01", null, Token)).Issue);
        Assert.Equal(ValidationIssue.EndBeforeStart, (await w.Ops.EndQualificationAsync(w.Planning, recorded.Value.Id, recorded.Version, "1404/12/29", Token)).Issue);

        var ended = (await w.Ops.EndQualificationAsync(w.Planning, recorded.Value.Id, recorded.Version, "1405/06/31", Token)).Value!;
        Assert.False(ended.Value.IsOpen);
        Assert.Equal(ValidationIssue.QualificationEnded, (await w.Ops.EndQualificationAsync(w.Planning, recorded.Value.Id, ended.Version, "1405/06/31", Token)).Issue);
        Assert.True((await w.Ops.RecordQualificationAsync(w.Planning, worker.Value.Id, machine.Value.Id, "1405/08/01", null, Token)).Succeeded);

        // After the first qualification ended, an assignment on a later day carries the warning; one inside the period does not.
        var inside = await w.Ops.AssignAsync(w.Planning, worker.Value.Id, machine.Value.Id, "1405/03/01", true, null, Token);
        Assert.DoesNotContain(OperatorService.NoQualificationWarning, inside.WarningList);
        Assert.Equal(2, Count(w, "OperatorMachineQualifications"));
        Assert.Equal(new[] { "OperatorQualificationRecorded", "OperatorQualificationEnded", "OperatorQualificationRecorded" }, w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode LIKE 'OperatorQualification%' ORDER BY Id").ToArray());
    }

    [Fact]
    public async Task The_operator_history_survives_a_restart()
    {
        using var w = await StartAsync();
        var worker = await OperatorAsync(w);
        var machine = await MachineAsync(w);
        await w.Ops.AssignAsync(w.Planning, worker.Value.Id, machine.Value.Id, "1405/07/01", true, null, Token);
        await w.Ops.RecordQualificationAsync(w.Planning, worker.Value.Id, machine.Value.Id, "1405/07/01", null, Token);

        var restarted = WorkflowServices.Create(w.Fixture.Db.ContextFactory, w.Fixture.Services.Authorization, PersistenceTestData.Dates, w.Fixture.Clock);
        var data = (await restarted.Operators.LoadAsync(w.Management, Token)).Value!;

        Assert.Single(data.Operators);
        Assert.Single(data.Assignments);
        Assert.Single(data.Qualifications);
    }
}
