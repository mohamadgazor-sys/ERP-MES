using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Scheduling;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Scheduling;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// Overtime assignments and limits (Decision Log #89, #95 D5, #103) on a real SQLite file with the real authorization.
public class OvertimeTests
{
    private const string InitialPassword = "Staff-Pass-Phrase-2";
    private const string OwnPassword = "My-Own-Pass-Phrase-3";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class World : IDisposable
    {
        public required SecurityFixture Fixture { get; init; }

        public required Principal Admin { get; init; }

        public required Principal Manager { get; init; }

        public required Principal Planning { get; init; }
        public required Principal ProductionManager { get; init; }

        public required Principal Qc { get; init; }

        public required Principal Supervisor { get; init; }

        public required Principal OtherSupervisor { get; init; }

        public required Principal Operator { get; init; }

        public required Principal Stranger { get; init; }

        public required WorkflowServices Services { get; init; }

        public OvertimeService Overtime => Services.Overtime;

        public void Dispose() => Fixture.Dispose();
    }

    private static async Task<Principal> UserAsync(SecurityFixture fixture, Principal admin, string name, string department, string role)
    {
        await fixture.CreateUserAsync(admin, name, InitialPassword, department, role, Token);
        var first = await fixture.SignInAsync(name, InitialPassword, Token);
        await fixture.Services.Accounts.ChangeOwnPasswordAsync(first, InitialPassword, OwnPassword, Token);
        return await fixture.SignInAsync(name, OwnPassword, Token);
    }

    private static async Task<World> StartAsync()
    {
        var fixture = await SecurityFixture.CreateFastAsync();
        var admin = await fixture.BootstrapAdminAsync(cancellationToken: Token);
        return new World
        {
            Fixture = fixture,
            Admin = admin,
            Manager = await UserAsync(fixture, admin, "manager.one", DepartmentCodes.Management, RoleCodes.Manager),
            Planning = await UserAsync(fixture, admin, "planning.one", DepartmentCodes.Planning, RoleCodes.Staff),
            ProductionManager = await UserAsync(fixture, admin, "prodmanager.one", DepartmentCodes.ProductionManager, RoleCodes.Manager),
            Qc = await UserAsync(fixture, admin, "qc.one", DepartmentCodes.Qc, RoleCodes.Staff),
            Supervisor = await UserAsync(fixture, admin, "supervisor.one", DepartmentCodes.Production, RoleCodes.Staff),
            OtherSupervisor = await UserAsync(fixture, admin, "supervisor.two", DepartmentCodes.Production, RoleCodes.Staff),
            Operator = await UserAsync(fixture, admin, "production.one", DepartmentCodes.Production, RoleCodes.Staff),
            Stranger = await UserAsync(fixture, admin, "production.two", DepartmentCodes.Production, RoleCodes.Staff),
            Services = WorkflowServices.Create(fixture.Db.ContextFactory, fixture.Services.Authorization, PersistenceTestData.Dates, fixture.Clock),
        };
    }

    private static readonly DateTime Saturday = new(2026, 10, 24, 0, 0, 0, DateTimeKind.Unspecified);

    private static DateTime At(int day, int hour, int minute = 0) => Saturday.AddDays(day).AddHours(hour).AddMinutes(minute);

    private sealed record Setup(long Unit, long OtherUnit, long Machine, long SecondMachine, long OtherUnitMachine, long OperatorId, long SecondOperatorId, long Template);

    /// <summary>Unit MILLING with shift D (08:00-17:00), two machines, supervisor one designated as its supervisor, supervisor two supervising another unit; two operators; the first planned on Saturday.</summary>
    private static async Task<Setup> SetupAsync(World w, bool plan = true)
    {
        var units = w.Services.PlanningMaster;
        var unit = (await units.AddUnitAsync(w.Admin, "MILLING", "فرز", Token)).Value!.Value.Id;
        var other = (await units.AddUnitAsync(w.Admin, "TURNING", "تراش", Token)).Value!.Value.Id;
        var machine = (await units.AddMachineAsync(w.Admin, "FCC-06", "فرز", unit, Token)).Value!.Value.Id;
        var second = (await units.AddMachineAsync(w.Admin, "FCC-07", "فرز ۲", unit, Token)).Value!.Value.Id;
        var otherMachine = (await units.AddMachineAsync(w.Admin, "TRN-01", "تراش", other, Token)).Value!.Value.Id;
        var template = (await w.Services.Calendar.AddTemplateAsync(w.Planning, "D", "شیفت D", new TimeOnly(8, 0), new TimeOnly(17, 0), new TimeOnly(12, 0), new TimeOnly(13, 0), Token)).Value!.Value.Id;
        Assert.True((await w.Services.Calendar.AddUnitShiftAsync(w.Planning, unit, template, 3, 1, false, Token)).Succeeded);
        await DesignateSupervisorAsync(w, w.Supervisor, unit);
        await DesignateSupervisorAsync(w, w.OtherSupervisor, other);
        var person = (await w.Services.Operators.RegisterAsync(w.Admin, "OP-1", "علی رضایی", w.Operator.UserId, Token)).Value!.Value.Id;
        var person2 = (await w.Services.Operators.RegisterAsync(w.Admin, "OP-2", "رضا احمدی", null, Token)).Value!.Value.Id;
        if (plan)
        {
            Assert.True((await w.Services.WorkerSchedule.ScheduleAsync(w.Planning, person, DateOnly.FromDateTime(Saturday), unit, template, machine, null, null, Token)).Succeeded);
        }

        return new Setup(unit, other, machine, second, otherMachine, person, person2, template);
    }

    private static async Task DesignateSupervisorAsync(World w, Principal user, long unit)
    {
        Assert.True((await w.Services.UnitDesignations.AssignAsync(w.Admin, user.UserId, unit, Token)).Succeeded);
        var row = (await w.Services.UnitDesignations.LoadAsync(w.ProductionManager, Token)).Value!.Rows.Single(r => r.UserId == user.UserId && r.UnitId == unit);
        Assert.True((await w.Services.UnitDesignations.SetSupervisorAsync(w.ProductionManager, row.DesignationId, row.Version, true, Token)).Succeeded);
    }

    private static async Task<Stored<OvertimeLimits>> LimitsAsync(World w, int day, int week, long version = 0)
    {
        var set = await w.Overtime.SetLimitsAsync(w.Manager, version, day, week, Token);
        Assert.True(set.Succeeded, set.Status + " " + set.Issue);
        return set.Value!;
    }

    private static async Task<Stored<OvertimeAssignment>> ProposedAsync(World w, Setup s, DateTime start, DateTime end, long? machine = null, long? person = null)
    {
        var proposed = await w.Overtime.ProposeAsync(w.Supervisor, person ?? s.OperatorId, s.Unit, machine ?? s.Machine, start, end, "سفارش فوری", Token);
        Assert.True(proposed.Succeeded, proposed.Status + " " + proposed.Issue);
        return proposed.Value!;
    }

    private static long Count(World w, string tableAndWhere) => long.Parse(w.Fixture.Db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_supervisor_proposes_the_production_manager_approves_within_the_limits_and_everything_is_audited()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        await LimitsAsync(w, 120, 480);

        var proposed = await ProposedAsync(w, s, At(0, 17, 30), At(0, 19));
        Assert.Equal(OvertimeStatus.Proposed, proposed.Value.Status);
        Assert.Equal(90, proposed.Value.Minutes);
        Assert.Equal(w.Supervisor.UserId, proposed.Value.ProposedByUserId);

        var approved = await w.Overtime.DecideAsync(w.ProductionManager, proposed.Value.Id, proposed.Version, true, null, Token);
        Assert.True(approved.Succeeded, approved.Status + " " + approved.Issue);
        Assert.Equal(OvertimeStatus.Approved, approved.Value!.Value.Status);
        Assert.False(approved.Value.Value.ExceedsLimits);
        Assert.Equal(w.ProductionManager.UserId, approved.Value.Value.PlanningUserId);
        Assert.Null(approved.Value.Value.ManagementUserId);
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'OvertimeProposed'"));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'OvertimeDecided'"));

        // An approved assignment is final for the decisions: Management has nothing to decide, Planning cannot decide twice.
        Assert.Equal(OperationStatus.InvalidState, (await w.Overtime.ManagementDecideAsync(w.Manager, approved.Value.Value.Id, approved.Value.Version, true, null, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Overtime.DecideAsync(w.ProductionManager, approved.Value.Value.Id, approved.Value.Version, true, null, Token)).Status);
    }

    [Fact]
    public async Task Overtime_cannot_be_approved_before_planning_configures_the_limits_but_it_can_be_rejected()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        var proposed = await ProposedAsync(w, s, At(0, 17, 30), At(0, 19));

        var blocked = await w.Overtime.DecideAsync(w.ProductionManager, proposed.Value.Id, proposed.Version, true, null, Token);
        Assert.Equal(ValidationIssue.OvertimeLimitsNotConfigured, blocked.Issue);
        var view = (await w.Overtime.LoadAsync(w.Planning, DateOnly.FromDateTime(Saturday), DateOnly.FromDateTime(Saturday), Token)).Value!;
        Assert.Equal(OvertimeStatus.Proposed, view.Items.Single().Assignment.Value.Status);
        Assert.Null(view.Limits);

        Assert.Equal(ValidationIssue.OvertimeInvalid, (await w.Overtime.DecideAsync(w.ProductionManager, proposed.Value.Id, proposed.Version, false, "  ", Token)).Issue);
        var rejected = await w.Overtime.DecideAsync(w.ProductionManager, proposed.Value.Id, proposed.Version, false, "کمبود بودجه", Token);
        Assert.Equal(OvertimeStatus.Rejected, rejected.Value!.Value.Status);
        Assert.Equal("کمبود بودجه", rejected.Value.Value.DecisionNote);
    }

    [Fact]
    public async Task Overtime_outside_the_daily_limit_goes_to_management_which_approves_or_rejects()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        await LimitsAsync(w, 60, 480);

        var proposed = await ProposedAsync(w, s, At(0, 17, 30), At(0, 19));
        var waiting = await w.Overtime.DecideAsync(w.ProductionManager, proposed.Value.Id, proposed.Version, true, "نیاز تولید", Token);
        Assert.Equal(OvertimeStatus.AwaitingManagement, waiting.Value!.Value.Status);
        Assert.True(waiting.Value.Value.ExceedsLimits);

        // Only Management decides it now, and Planning cannot also push it through.
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Overtime.ManagementDecideAsync(w.Planning, waiting.Value.Value.Id, waiting.Value.Version, true, null, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Overtime.DecideAsync(w.ProductionManager, waiting.Value.Value.Id, waiting.Value.Version, true, null, Token)).Status);
        Assert.Equal(ValidationIssue.OvertimeInvalid, (await w.Overtime.ManagementDecideAsync(w.Manager, waiting.Value.Value.Id, waiting.Value.Version, false, null, Token)).Issue);

        var approved = await w.Overtime.ManagementDecideAsync(w.Manager, waiting.Value.Value.Id, waiting.Value.Version, true, "تأیید", Token);
        Assert.Equal(OvertimeStatus.Approved, approved.Value!.Value.Status);
        Assert.Equal(w.Manager.UserId, approved.Value.Value.ManagementUserId);
        Assert.Equal(w.ProductionManager.UserId, approved.Value.Value.PlanningUserId);
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'OvertimeManagementDecided'"));

        // A second proposal on another day that Management rejects.
        var second = await ProposedAsync(w, s, At(1, 17, 30), At(1, 19));
        var waiting2 = (await w.Overtime.DecideAsync(w.ProductionManager, second.Value.Id, second.Version, true, null, Token)).Value!;
        var rejected = await w.Overtime.ManagementDecideAsync(w.Manager, waiting2.Value.Id, waiting2.Version, false, "خارج از سقف", Token);
        Assert.Equal(OvertimeStatus.Rejected, rejected.Value!.Value.Status);
    }

    [Fact]
    public async Task The_weekly_limit_counts_approved_overtime_of_the_operator_in_the_saturday_week()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        await LimitsAsync(w, 240, 300);

        var first = await ProposedAsync(w, s, At(0, 17, 30), At(0, 21, 30)); // 240 minutes, within both limits
        Assert.Equal(OvertimeStatus.Approved, (await w.Overtime.DecideAsync(w.ProductionManager, first.Value.Id, first.Version, true, null, Token)).Value!.Value.Status);

        var second = await ProposedAsync(w, s, At(1, 17, 30), At(1, 19)); // 90 more in the same week: 330 > 300
        Assert.Equal(OvertimeStatus.AwaitingManagement, (await w.Overtime.DecideAsync(w.ProductionManager, second.Value.Id, second.Version, true, null, Token)).Value!.Value.Status);

        // The next week starts on Saturday again: 90 minutes there is within the limits.
        var nextWeek = await ProposedAsync(w, s, At(7, 17, 30), At(7, 19));
        Assert.Equal(OvertimeStatus.Approved, (await w.Overtime.DecideAsync(w.ProductionManager, nextWeek.Value.Id, nextWeek.Version, true, null, Token)).Value!.Value.Status);
    }

    [Fact]
    public async Task Management_approves_a_proposal_beyond_the_limits_in_one_step_and_records_itself_as_both_deciders()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        await LimitsAsync(w, 60, 480);

        var proposed = await ProposedAsync(w, s, At(0, 17, 30), At(0, 19)); // 90 minutes against a 60-minute day
        var approved = await w.Overtime.DecideAsync(w.Manager, proposed.Value.Id, proposed.Version, true, "نیاز فوری", Token);

        Assert.True(approved.Succeeded, approved.Status + " " + approved.Issue);
        Assert.Equal(OvertimeStatus.Approved, approved.Value!.Value.Status);
        Assert.True(approved.Value.Value.ExceedsLimits);
        Assert.Equal(w.Manager.UserId, approved.Value.Value.PlanningUserId);
        Assert.Equal(w.Manager.UserId, approved.Value.Value.ManagementUserId);
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'OvertimeDecided'"));
    }

    [Fact]
    public async Task The_production_manager_cannot_finish_a_proposal_beyond_the_limits_it_goes_to_management()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        await LimitsAsync(w, 60, 480);

        var proposed = await ProposedAsync(w, s, At(0, 17, 30), At(0, 19));
        var waiting = await w.Overtime.DecideAsync(w.ProductionManager, proposed.Value.Id, proposed.Version, true, null, Token);

        Assert.Equal(OvertimeStatus.AwaitingManagement, waiting.Value!.Value.Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Overtime.ManagementDecideAsync(w.ProductionManager, waiting.Value.Value.Id, waiting.Value.Version, true, null, Token)).Status);
        Assert.Equal(OvertimeStatus.Approved, (await w.Overtime.ManagementDecideAsync(w.Manager, waiting.Value.Value.Id, waiting.Value.Version, true, null, Token)).Value!.Value.Status);
    }

    [Fact]
    public async Task A_proposer_never_decides_their_own_proposal_even_after_moving_to_a_deciding_department()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        await LimitsAsync(w, 120, 480);
        var proposed = await ProposedAsync(w, s, At(0, 17, 30), At(0, 19));

        // The supervisor is moved to the Production Manager department: now allowed to decide in general, but never their own proposal.
        Assert.Equal(AccountStatus.Succeeded, (await w.Fixture.Services.Accounts.ChangeDepartmentAsync(w.Admin, w.Supervisor.UserId, DepartmentCodes.ProductionManager, Token)).Status);
        var own = await w.Overtime.DecideAsync(w.Supervisor, proposed.Value.Id, proposed.Version, true, null, Token);

        Assert.Equal(ValidationIssue.OvertimeSelfDecision, own.Issue);
        Assert.Equal(OvertimeStatus.Proposed, (await w.Overtime.LoadAsync(w.ProductionManager, DateOnly.FromDateTime(Saturday), DateOnly.FromDateTime(Saturday), Token)).Value!.Items.Single().Assignment.Value.Status);
        Assert.Equal(OvertimeStatus.Approved, (await w.Overtime.DecideAsync(w.ProductionManager, proposed.Value.Id, proposed.Version, true, null, Token)).Value!.Value.Status);
    }

    [Fact]
    public async Task Only_the_right_people_can_propose_decide_and_configure()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);

        foreach (var user in new Principal?[] { w.Planning, w.Manager, w.Qc, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Overtime.ProposeAsync(user, s.OperatorId, s.Unit, s.Machine, At(0, 17, 30), At(0, 19), "x", Token)).Status);
        }

        // A Production user who does not supervise the unit, or supervises another unit, cannot propose for it.
        Assert.Equal(ValidationIssue.OvertimeNotSupervisor, (await w.Overtime.ProposeAsync(w.Stranger, s.OperatorId, s.Unit, s.Machine, At(0, 17, 30), At(0, 19), "x", Token)).Issue);
        Assert.Equal(ValidationIssue.OvertimeNotSupervisor, (await w.Overtime.ProposeAsync(w.OtherSupervisor, s.OperatorId, s.Unit, s.Machine, At(0, 17, 30), At(0, 19), "x", Token)).Issue);
        Assert.Equal(0, Count(w, "OvertimeAssignments"));

        // Nobody proposes overtime for themselves.
        var self = (await w.Services.Operators.RegisterAsync(w.Admin, "OP-S", "سرپرست", w.Supervisor.UserId, Token)).Value!.Value.Id;
        Assert.Equal(ValidationIssue.OvertimeNotSupervisor, (await w.Overtime.ProposeAsync(w.Supervisor, self, s.Unit, s.Machine, At(0, 17, 30), At(0, 19), "x", Token)).Issue);

        await LimitsAsync(w, 120, 480);
        var proposed = await ProposedAsync(w, s, At(0, 17, 30), At(0, 19));
        foreach (var user in new Principal?[] { w.Supervisor, w.Planning, w.Qc, w.Admin, w.Operator, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Overtime.DecideAsync(user, proposed.Value.Id, proposed.Version, true, null, Token)).Status);
        }

        foreach (var user in new Principal?[] { w.Supervisor, w.Planning, w.ProductionManager, w.Qc, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Overtime.ManagementDecideAsync(user, proposed.Value.Id, proposed.Version, true, null, Token)).Status);
        }

        foreach (var user in new Principal?[] { w.Supervisor, w.Planning, w.ProductionManager, w.Qc, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Overtime.SetLimitsAsync(user, 0, 60, 240, Token)).Status);
        }

        Assert.Equal(OvertimeStatus.Proposed, (await w.Overtime.LoadAsync(w.Planning, DateOnly.FromDateTime(Saturday), DateOnly.FromDateTime(Saturday), Token)).Value!.Items.Single().Assignment.Value.Status);
    }

    [Fact]
    public async Task The_booking_rules_refuse_shift_absence_and_machine_conflicts_and_invalid_input()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        Task<Outcome<Stored<OvertimeAssignment>>> Propose(long person, long machine, DateTime start, DateTime end, string? reason = "سفارش", long? unit = null) =>
            w.Overtime.ProposeAsync(w.Supervisor, person, unit ?? s.Unit, machine, start, end, reason, Token);

        // Overtime is outside the normal shift: the operator is planned 08:00-17:00 on Saturday.
        Assert.Equal(ValidationIssue.OvertimeOperatorOverlap, (await Propose(s.OperatorId, s.Machine, At(0, 16), At(0, 18))).Issue);

        // A machine of another unit, or an unknown operator.
        Assert.Equal(ValidationIssue.MachineInvalid, (await Propose(s.OperatorId, s.OtherUnitMachine, At(0, 17, 30), At(0, 19))).Issue);
        Assert.Equal(ValidationIssue.OperatorInvalid, (await Propose(9999, s.Machine, At(0, 17, 30), At(0, 19))).Issue);

        // An absent operator.
        Assert.True((await w.Services.WorkerSchedule.AddOffDayAsync(w.Planning, s.SecondOperatorId, DateOnly.FromDateTime(Saturday).AddDays(1), DateOnly.FromDateTime(Saturday).AddDays(1), OffDayKind.Sick, "گواهی", Token)).Succeeded);
        Assert.Equal(ValidationIssue.OvertimeOperatorAbsent, (await Propose(s.SecondOperatorId, s.Machine, At(1, 17, 30), At(1, 19))).Issue);

        // One operator at a time on a machine, and one booking at a time for an operator.
        await ProposedAsync(w, s, At(0, 17, 30), At(0, 19));
        Assert.Equal(ValidationIssue.OvertimeMachineBusy, (await Propose(s.SecondOperatorId, s.Machine, At(0, 18), At(0, 20))).Issue);
        Assert.Equal(ValidationIssue.OvertimeOperatorOverlap, (await Propose(s.OperatorId, s.SecondMachine, At(0, 18), At(0, 20))).Issue);
        Assert.True((await Propose(s.SecondOperatorId, s.Machine, At(0, 19), At(0, 20))).Succeeded); // touching is not overlapping

        // Invalid input: end not after start, longer than 12 hours, no reason.
        Assert.Equal(ValidationIssue.OvertimeInvalid, (await Propose(s.SecondOperatorId, s.SecondMachine, At(2, 18), At(2, 18))).Issue);
        Assert.Equal(ValidationIssue.OvertimeInvalid, (await Propose(s.SecondOperatorId, s.SecondMachine, At(2, 6), At(2, 19))).Issue);
        Assert.Equal(ValidationIssue.OvertimeInvalid, (await Propose(s.SecondOperatorId, s.SecondMachine, At(2, 18), At(2, 19), "  ")).Issue);
        Assert.Equal(2, Count(w, "OvertimeAssignments"));
    }

    [Fact]
    public async Task A_cancelled_or_rejected_assignment_frees_the_slot_and_cancellation_is_restricted_and_needs_a_reason()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        await LimitsAsync(w, 120, 480);
        var proposed = await ProposedAsync(w, s, At(0, 17, 30), At(0, 19));

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Overtime.CancelAsync(w.OtherSupervisor, proposed.Value.Id, proposed.Version, "x", Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Overtime.CancelAsync(w.Stranger, proposed.Value.Id, proposed.Version, "x", Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Overtime.CancelAsync(w.Planning, proposed.Value.Id, proposed.Version, "x", Token)).Status);
        Assert.Equal(ValidationIssue.OvertimeInvalid, (await w.Overtime.CancelAsync(w.Supervisor, proposed.Value.Id, proposed.Version, " ", Token)).Issue);

        var cancelled = await w.Overtime.CancelAsync(w.Supervisor, proposed.Value.Id, proposed.Version, "لغو سفارش", Token);
        Assert.Equal(OvertimeStatus.Cancelled, cancelled.Value!.Value.Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Overtime.CancelAsync(w.ProductionManager, cancelled.Value.Value.Id, cancelled.Value.Version, "دوباره", Token)).Status);
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'OvertimeCancelled'"));

        // The slot is free again; the Production Manager (or Management) may cancel an approved assignment.
        var again = await ProposedAsync(w, s, At(0, 17, 30), At(0, 19));
        var approved = (await w.Overtime.DecideAsync(w.ProductionManager, again.Value.Id, again.Version, true, null, Token)).Value!;
        Assert.Equal(OvertimeStatus.Cancelled, (await w.Overtime.CancelAsync(w.ProductionManager, approved.Value.Id, approved.Version, "تغییر برنامه", Token)).Value!.Value.Status);
    }

    [Fact]
    public async Task A_stale_version_changes_nothing_and_the_limits_are_validated_and_versioned()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);

        Assert.Equal(ValidationIssue.OvertimeLimitsInvalid, (await w.Overtime.SetLimitsAsync(w.Manager, 0, 0, 100, Token)).Issue);
        Assert.Equal(ValidationIssue.OvertimeLimitsInvalid, (await w.Overtime.SetLimitsAsync(w.Manager, 0, 120, 60, Token)).Issue);
        Assert.Equal(ValidationIssue.OvertimeLimitsInvalid, (await w.Overtime.SetLimitsAsync(w.Manager, 0, 1441, 2000, Token)).Issue);
        Assert.Equal(ValidationIssue.OvertimeLimitsInvalid, (await w.Overtime.SetLimitsAsync(w.Manager, 0, 120, 10081, Token)).Issue);
        Assert.Equal(0, Count(w, "OvertimeLimits"));

        var first = await LimitsAsync(w, 120, 480);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Overtime.SetLimitsAsync(w.Manager, 0, 90, 300, Token)).Status); // "none exist" is stale now
        var second = await LimitsAsync(w, 90, 300, first.Version);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Overtime.SetLimitsAsync(w.Manager, first.Version, 60, 200, Token)).Status);
        Assert.Equal(90, (await w.Overtime.LoadAsync(w.Planning, DateOnly.FromDateTime(Saturday), DateOnly.FromDateTime(Saturday), Token)).Value!.Limits!.Value.MaxMinutesPerDay);
        Assert.Equal(2, Count(w, "AuditLog WHERE ActionCode = 'OvertimeLimitsChanged'"));
        Assert.True(second.Version > first.Version);

        var proposed = await ProposedAsync(w, s, At(0, 17, 30), At(0, 18));
        Assert.True((await w.Overtime.DecideAsync(w.ProductionManager, proposed.Value.Id, proposed.Version, true, null, Token)).Succeeded);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Overtime.DecideAsync(w.ProductionManager, proposed.Value.Id, proposed.Version, true, null, Token)).Status);
        Assert.Equal(OperationStatus.NotFound, (await w.Overtime.DecideAsync(w.ProductionManager, 9999, 1, true, null, Token)).Status);
    }

    [Fact]
    public async Task Visibility_follows_the_role_planning_and_management_see_all_supervisors_their_unit_and_operators_their_own()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        await ProposedAsync(w, s, At(0, 17, 30), At(0, 19)); // for the operator linked to w.Operator
        await ProposedAsync(w, s, At(0, 19), At(0, 20), person: s.SecondOperatorId); // for an unlinked operator
        var range = DateOnly.FromDateTime(Saturday);

        async Task<OvertimeView> ViewAsync(Principal user) => (await w.Overtime.LoadAsync(user, range, range, Token)).Value!;

        Assert.Equal(2, (await ViewAsync(w.Planning)).Items.Count);
        Assert.Equal(2, (await ViewAsync(w.Manager)).Items.Count);
        var supervisor = await ViewAsync(w.Supervisor);
        Assert.Equal(2, supervisor.Items.Count);
        Assert.True(supervisor.Capabilities.CanPropose);
        var operatorView = await ViewAsync(w.Operator);
        Assert.Equal(s.OperatorId, operatorView.Items.Single().Assignment.Value.OperatorId);
        Assert.False(operatorView.Capabilities.CanPropose);
        Assert.Empty((await ViewAsync(w.Stranger)).Items);
        Assert.Empty((await ViewAsync(w.OtherSupervisor)).Items); // supervises another unit

        var planning = await ViewAsync(w.Planning);
        Assert.False(planning.Capabilities.CanDecide || planning.Capabilities.CanConfigureLimits || planning.Capabilities.CanManagementDecide);
        Assert.All(planning.Items, i => Assert.False(i.CanDecide));
        var productionManager = await ViewAsync(w.ProductionManager);
        Assert.Equal(2, productionManager.Items.Count);
        Assert.True(productionManager.Capabilities.CanDecide && !productionManager.Capabilities.CanConfigureLimits && !productionManager.Capabilities.CanManagementDecide);
        Assert.All(productionManager.Items, i => Assert.True(i.CanDecide));
        Assert.True((await ViewAsync(w.Manager)).Capabilities.CanDecide && (await ViewAsync(w.Manager)).Capabilities.CanConfigureLimits);
        Assert.All((await ViewAsync(w.Supervisor)).Items, i => Assert.False(i.CanDecide));
        Assert.True((await ViewAsync(w.Manager)).Capabilities.CanManagementDecide);

        foreach (var user in new Principal?[] { w.Qc, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Overtime.LoadAsync(user, range, range, Token)).Status);
        }

        Assert.Equal(ValidationIssue.EndBeforeStart, (await w.Overtime.LoadAsync(w.Planning, range, range.AddDays(-1), Token)).Issue);
    }
}
