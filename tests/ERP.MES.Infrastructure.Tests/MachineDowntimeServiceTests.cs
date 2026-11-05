using ERP.MES.Application.Requests;
using ERP.MES.Application.Scheduling;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Scheduling;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// Machine downtime reported by Maintenance/PM and reviewed by Planning (Master Specification section 21, Decision Log #14, #128) on a real SQLite file with the real authorization.
public class MachineDowntimeServiceTests
{
    private const string InitialPassword = "Staff-Pass-Phrase-2";
    private const string OwnPassword = "My-Own-Pass-Phrase-3";

    private static readonly DateOnly Saturday = new(2026, 10, 24);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class World : IDisposable
    {
        public required SecurityFixture Fixture { get; init; }

        public required Principal Admin { get; init; }

        public required Principal Maintenance { get; init; }

        public required Principal Planning { get; init; }

        public required Principal Manager { get; init; }

        public required Principal ProductionManager { get; init; }

        public required Principal Production { get; init; }

        public required Principal Qc { get; init; }

        public required WorkflowServices Services { get; init; }

        public MachineDowntimeService Downtime => Services.Downtime;

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
            Maintenance = await UserAsync(fixture, admin, "maintenance.one", DepartmentCodes.MaintenancePm, RoleCodes.Staff),
            Planning = await UserAsync(fixture, admin, "planning.one", DepartmentCodes.Planning, RoleCodes.Staff),
            Manager = await UserAsync(fixture, admin, "manager.one", DepartmentCodes.Management, RoleCodes.Manager),
            ProductionManager = await UserAsync(fixture, admin, "prodmanager.one", DepartmentCodes.ProductionManager, RoleCodes.Manager),
            Production = await UserAsync(fixture, admin, "production.one", DepartmentCodes.Production, RoleCodes.Staff),
            Qc = await UserAsync(fixture, admin, "qc.one", DepartmentCodes.Qc, RoleCodes.Staff),
            Services = WorkflowServices.Create(fixture.Db.ContextFactory, fixture.Services.Authorization, PersistenceTestData.Dates, fixture.Clock),
        };
    }

    private sealed record Setup(long Unit, long Machine, long Second, long Inactive);

    private static async Task<Setup> SetupAsync(World w)
    {
        var unit = (await w.Services.PlanningMaster.AddUnitAsync(w.Admin, "MILLING", "فرز", Token)).Value!.Value.Id;
        var machine = (await w.Services.PlanningMaster.AddMachineAsync(w.Admin, "FCC-06", "فرز ۶", unit, Token)).Value!.Value.Id;
        var second = (await w.Services.PlanningMaster.AddMachineAsync(w.Admin, "FCC-07", "فرز ۷", unit, Token)).Value!.Value.Id;
        var inactive = (await w.Services.PlanningMaster.AddMachineAsync(w.Admin, "FCC-99", "فرز قدیمی", unit, Token)).Value!;
        Assert.True((await w.Services.PlanningMaster.SetMachineActiveAsync(w.Admin, inactive.Value.Id, inactive.Version, false, Token)).Succeeded);
        return new Setup(unit, machine, second, inactive.Value.Id);
    }

    private static DateTime At(int day, int hour, int minute = 0) => Saturday.ToDateTime(TimeOnly.MinValue).AddDays(day).AddHours(hour).AddMinutes(minute);

    private static long Count(World w, string tableAndWhere) => long.Parse(w.Fixture.Db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Only_maintenance_reports_downtime_of_an_active_machine_with_valid_times_and_no_overlap()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);

        foreach (var user in new Principal?[] { w.Planning, w.Manager, w.ProductionManager, w.Production, w.Qc, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Downtime.ReportAsync(user, s.Machine, DowntimeReason.Breakdown, At(0, 9), At(0, 11), null, Token)).Status);
        }

        Assert.Equal(ValidationIssue.DowntimeInvalid, (await w.Downtime.ReportAsync(w.Maintenance, s.Machine, DowntimeReason.Other, At(0, 9), At(0, 11), " ", Token)).Issue);
        Assert.Equal(ValidationIssue.DowntimeInvalid, (await w.Downtime.ReportAsync(w.Maintenance, s.Machine, DowntimeReason.Breakdown, At(0, 11), At(0, 9), null, Token)).Issue);
        Assert.Equal(ValidationIssue.DowntimeInvalid, (await w.Downtime.ReportAsync(w.Maintenance, s.Machine, (DowntimeReason)42, At(0, 9), At(0, 11), null, Token)).Issue);
        Assert.Equal(ValidationIssue.DowntimeMachineNotUsable, (await w.Downtime.ReportAsync(w.Maintenance, s.Inactive, DowntimeReason.Breakdown, At(0, 9), At(0, 11), null, Token)).Issue);
        Assert.Equal(ValidationIssue.DowntimeMachineNotUsable, (await w.Downtime.ReportAsync(w.Maintenance, 987654, DowntimeReason.Breakdown, At(0, 9), At(0, 11), null, Token)).Issue);
        Assert.Equal(0, Count(w, "MachineDowntimes"));

        var reported = await w.Downtime.ReportAsync(w.Maintenance, s.Machine, DowntimeReason.Breakdown, At(0, 9), At(0, 11), "  یاتاقان اسپیندل ", Token);
        Assert.True(reported.Succeeded, reported.Status + " " + reported.Issue);
        Assert.Equal(DowntimeStatus.Reported, reported.Value!.Value.Status);
        Assert.Equal(s.Unit, reported.Value.Value.ProductionUnitId);
        Assert.Equal("یاتاقان اسپیندل", reported.Value.Value.Note);

        // A second counting record of the same machine may not overlap (time would be subtracted twice); touching, another machine and another day are fine.
        Assert.Equal(ValidationIssue.DowntimeOverlap, (await w.Downtime.ReportAsync(w.Maintenance, s.Machine, DowntimeReason.PowerOutage, At(0, 10), At(0, 12), null, Token)).Issue);
        Assert.Equal(ValidationIssue.DowntimeOverlap, (await w.Downtime.ReportAsync(w.Maintenance, s.Machine, DowntimeReason.PowerOutage, At(-1, 20), At(1, 0), null, Token)).Issue);
        Assert.True((await w.Downtime.ReportAsync(w.Maintenance, s.Machine, DowntimeReason.PowerOutage, At(0, 11), At(0, 12), null, Token)).Succeeded);
        Assert.True((await w.Downtime.ReportAsync(w.Maintenance, s.Second, DowntimeReason.PowerOutage, At(0, 10), At(0, 12), null, Token)).Succeeded);
        Assert.True((await w.Downtime.ReportAsync(w.Maintenance, s.Machine, DowntimeReason.PreventiveMaintenance, At(1, 8), At(3, 8), null, Token)).Succeeded);
        Assert.Equal(4, Count(w, "MachineDowntimes"));
        Assert.Equal(4, Count(w, "AuditLog WHERE ActionCode = 'DowntimeReported' AND TargetType = 'MachineDowntime'"));
        Assert.Contains("FCC-06", w.Fixture.AllAuditText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Maintenance_planning_management_and_the_production_manager_see_the_register_and_only_planning_decides()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        var reported = (await w.Downtime.ReportAsync(w.Maintenance, s.Machine, DowntimeReason.NoMaterial, At(0, 9), At(0, 11), null, Token)).Value!;

        foreach (var user in new Principal?[] { w.Production, w.Qc, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Downtime.LoadAsync(user, Saturday, Saturday, Token)).Status);
        }

        var forMaintenance = (await w.Downtime.LoadAsync(w.Maintenance, Saturday, Saturday, Token)).Value!;
        Assert.Equal(new DowntimeCapabilities(true, true, false), forMaintenance.Capabilities);
        var row = forMaintenance.Items.Single();
        Assert.Equal(("FCC-06", "فرز", false, true), (row.MachineCode, row.UnitName, row.CanDecide, row.CanChange));
        Assert.Equal(["FCC-06", "FCC-07"], forMaintenance.Machines.Select(m => m.Code));

        var forPlanning = (await w.Downtime.LoadAsync(w.Planning, Saturday, Saturday, Token)).Value!;
        Assert.Equal(new DowntimeCapabilities(true, false, true), forPlanning.Capabilities);
        Assert.Equal((true, false), (forPlanning.Items.Single().CanDecide, forPlanning.Items.Single().CanChange));
        Assert.Single((await w.Downtime.LoadAsync(w.Manager, Saturday, Saturday, Token)).Value!.Items);
        Assert.Single((await w.Downtime.LoadAsync(w.ProductionManager, Saturday, Saturday, Token)).Value!.Items);
        Assert.Empty((await w.Downtime.LoadAsync(w.Planning, Saturday.AddDays(1), Saturday.AddDays(2), Token)).Value!.Items);
        Assert.Equal(ValidationIssue.EndBeforeStart, (await w.Downtime.LoadAsync(w.Planning, Saturday, Saturday.AddDays(-1), Token)).Issue);

        foreach (var user in new Principal?[] { w.Maintenance, w.Manager, w.ProductionManager, w.Production, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Downtime.DecideAsync(user, reported.Value.Id, reported.Version, true, null, Token)).Status);
        }

        Assert.Equal(ValidationIssue.DowntimeInvalid, (await w.Downtime.DecideAsync(w.Planning, reported.Value.Id, reported.Version, false, " ", Token)).Issue);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Downtime.DecideAsync(w.Planning, reported.Value.Id, reported.Version + 3, true, null, Token)).Status);
        var rejected = await w.Downtime.DecideAsync(w.Planning, reported.Value.Id, reported.Version, false, "دستگاه کار می‌کرد", Token);
        Assert.True(rejected.Succeeded, rejected.Status + " " + rejected.Issue);
        Assert.Equal(DowntimeStatus.Rejected, rejected.Value!.Value.Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Downtime.DecideAsync(w.Planning, reported.Value.Id, rejected.Value.Version, true, null, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Downtime.CancelAsync(w.Maintenance, reported.Value.Id, rejected.Value.Version, "x", Token)).Status);

        // A rejected record no longer blocks a corrected report of the same time; the history stays.
        Assert.True((await w.Downtime.ReportAsync(w.Maintenance, s.Machine, DowntimeReason.NoMaterial, At(0, 9), At(0, 10), null, Token)).Succeeded);
        Assert.Equal(2, Count(w, "MachineDowntimes"));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'DowntimeDecided'"));
    }

    [Fact]
    public async Task Maintenance_corrects_the_end_or_cancels_with_a_reason_and_a_corrected_end_goes_back_to_planning()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        var first = (await w.Downtime.ReportAsync(w.Maintenance, s.Machine, DowntimeReason.Breakdown, At(0, 9), At(0, 11), null, Token)).Value!;
        var later = (await w.Downtime.ReportAsync(w.Maintenance, s.Machine, DowntimeReason.SetupChangeover, At(0, 14), At(0, 15), null, Token)).Value!;
        var accepted = (await w.Downtime.DecideAsync(w.Planning, first.Value.Id, first.Version, true, "تأیید", Token)).Value!;
        Assert.Equal(DowntimeStatus.Accepted, accepted.Value.Status);

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Downtime.ChangeEndAsync(w.Planning, first.Value.Id, accepted.Version, At(0, 13), "طول کشید", Token)).Status);
        Assert.Equal(ValidationIssue.DowntimeInvalid, (await w.Downtime.ChangeEndAsync(w.Maintenance, first.Value.Id, accepted.Version, At(0, 13), " ", Token)).Issue);
        Assert.Equal(ValidationIssue.DowntimeInvalid, (await w.Downtime.ChangeEndAsync(w.Maintenance, first.Value.Id, accepted.Version, At(0, 8), "اشتباه", Token)).Issue);
        Assert.Equal(ValidationIssue.DowntimeOverlap, (await w.Downtime.ChangeEndAsync(w.Maintenance, first.Value.Id, accepted.Version, At(0, 14, 30), "طول کشید", Token)).Issue);

        var changed = await w.Downtime.ChangeEndAsync(w.Maintenance, first.Value.Id, accepted.Version, At(0, 13), "طول کشید", Token);
        Assert.True(changed.Succeeded, changed.Status + " " + changed.Issue);
        Assert.Equal((DowntimeStatus.Reported, 240, (long?)null), (changed.Value!.Value.Status, changed.Value.Value.DurationMinutes, changed.Value.Value.DecidedByUserId));
        Assert.Contains("end changed from 2026-10-24 11:00 to 2026-10-24 13:00 (status was Accepted); reason: طول کشید", w.Fixture.AllAuditText(), StringComparison.Ordinal);

        foreach (var user in new Principal?[] { w.Planning, w.Manager, w.ProductionManager, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Downtime.CancelAsync(user, later.Value.Id, later.Version, "اشتباه", Token)).Status);
        }

        Assert.Equal(ValidationIssue.DowntimeInvalid, (await w.Downtime.CancelAsync(w.Maintenance, later.Value.Id, later.Version, null, Token)).Issue);
        var cancelled = await w.Downtime.CancelAsync(w.Maintenance, later.Value.Id, later.Version, "گزارش اشتباه", Token);
        Assert.True(cancelled.Succeeded, cancelled.Status + " " + cancelled.Issue);
        Assert.Equal(DowntimeStatus.Cancelled, cancelled.Value!.Value.Status);

        // Nothing is deleted; every write is in the audit log.
        Assert.Equal(2, Count(w, "MachineDowntimes"));
        Assert.Equal(["DowntimeReported", "DowntimeReported", "DowntimeDecided", "DowntimeEndChanged", "DowntimeCancelled"], w.Fixture.AuditActionsInOrder().Where(a => a.StartsWith("Downtime", StringComparison.Ordinal)));
    }
}
