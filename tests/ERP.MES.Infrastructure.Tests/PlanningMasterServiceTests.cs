using ERP.MES.Application.Persistence;
using ERP.MES.Application.Planning;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Planning;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// Planning resource master data (Decision Log #10, #82 R2, #89) on a real SQLite file with the real authorization.
public class PlanningMasterServiceTests
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
        var fixture = await SecurityFixture.CreateFastAsync();
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

    private static async Task<(Stored<ProductionUnit> Unit, Stored<Machine> Machine)> MachineAsync(World w, string code = "FCC-06")
    {
        var unit = (await w.Master.AddUnitAsync(w.Planning, "MILLING", "فرزکاری", Token)).Value!;
        var machine = await w.Master.AddMachineAsync(w.Planning, code, "فرز CNC", unit.Value.Id, Token);
        Assert.True(machine.Succeeded, machine.Status + " " + machine.Issue);
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        return (unit, machine.Value!);
    }

    private static long Count(World w, string tableAndWhere) => long.Parse(w.Fixture.Db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Planning_registers_units_and_machines_with_audit_and_everyone_active_reads_them()
    {
        using var w = await StartAsync();

        var (unit, machine) = await MachineAsync(w);

        var restarted = WorkflowServices.Create(w.Fixture.Db.ContextFactory, w.Fixture.Services.Authorization, PersistenceTestData.Dates, w.Fixture.Clock);
        foreach (var reader in new[] { w.Production, w.Qc, w.Warehouse })
        {
            var data = (await restarted.PlanningMaster.LoadAsync(reader, Token)).Value!;
            Assert.Equal("FCC-06", Assert.Single(data.Machines).Value.Code);
            Assert.Equal(unit.Value.Id, data.Machines[0].Value.ProductionUnitId);
            Assert.Equal("MILLING", Assert.Single(data.Units).Value.Code);
        }

        Assert.Equal(new[] { "ProductionUnitAdded", "MachineAdded" }, w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode IN ('ProductionUnitAdded','MachineAdded') ORDER BY Id").ToArray());
        Assert.Equal(w.Planning.UserId, machine.Value.CreatedByUserId);
    }

    [Fact]
    public async Task Only_planning_registers_units_and_machines()
    {
        using var w = await StartAsync();
        var (unit, machine) = await MachineAsync(w);

        foreach (var user in new Principal?[] { w.Management, w.Warehouse, w.Production, w.Qc, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Master.AddUnitAsync(user, "TURN", "x", Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Master.AddMachineAsync(user, "TCC-12", "x", unit.Value.Id, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Master.RenameMachineAsync(user, machine.Value.Id, machine.Version, "x", Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Master.SetMachineActiveAsync(user, machine.Value.Id, machine.Version, false, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Master.RenameUnitAsync(user, unit.Value.Id, unit.Version, "x", Token)).Status);
        }

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Master.LoadAsync(null, Token)).Status);
        Assert.Equal(1, Count(w, "Machines"));
        Assert.Equal(1, Count(w, "ProductionUnits"));
    }

    [Theory]
    [InlineData("", "x", ValidationIssue.CodeInvalid)]
    [InlineData("fcc-06", "x", ValidationIssue.CodeInvalid)]
    [InlineData("TCC-12", " ", ValidationIssue.NameRequired)]
    [InlineData("FCC-06", "x", ValidationIssue.CodeDuplicate)]
    public async Task An_invalid_or_duplicate_machine_is_refused_and_nothing_is_saved(string code, string name, ValidationIssue expected)
    {
        using var w = await StartAsync();
        var (unit, _) = await MachineAsync(w);

        var outcome = await w.Master.AddMachineAsync(w.Planning, code, name, unit.Value.Id, Token);

        Assert.Equal(OperationStatus.ValidationFailed, outcome.Status);
        Assert.Equal(expected, outcome.Issue);
        Assert.Equal(1, Count(w, "Machines"));
    }

    [Fact]
    public async Task A_machine_needs_an_existing_active_unit_and_the_code_stays_unique_also_after_retiring()
    {
        using var w = await StartAsync();
        var (unit, machine) = await MachineAsync(w);
        var retiredUnit = (await w.Master.AddUnitAsync(w.Planning, "OLD", "قدیمی", Token)).Value!;
        await w.Master.SetUnitActiveAsync(w.Planning, retiredUnit.Value.Id, retiredUnit.Version, false, Token);

        Assert.Equal(ValidationIssue.ProductionUnitInvalid, (await w.Master.AddMachineAsync(w.Planning, "TCC-12", "x", 4242, Token)).Issue);
        Assert.Equal(ValidationIssue.ProductionUnitInvalid, (await w.Master.AddMachineAsync(w.Planning, "TCC-12", "x", retiredUnit.Value.Id, Token)).Issue);

        // A retired machine keeps its code: the code is never reused.
        Assert.True((await w.Master.SetMachineActiveAsync(w.Planning, machine.Value.Id, machine.Version, false, Token)).Succeeded);
        Assert.Equal(ValidationIssue.CodeDuplicate, (await w.Master.AddMachineAsync(w.Planning, "FCC-06", "دیگر", unit.Value.Id, Token)).Issue);
        Assert.Equal(ValidationIssue.CodeDuplicate, (await w.Master.AddUnitAsync(w.Planning, "OLD", "x", Token)).Issue);
    }

    [Fact]
    public async Task Rename_and_retire_use_the_row_version_and_the_code_never_changes()
    {
        using var w = await StartAsync();
        var (_, machine) = await MachineAsync(w);

        var renamed = await w.Master.RenameMachineAsync(w.Planning, machine.Value.Id, machine.Version, "فرز عمودی", Token);
        Assert.True(renamed.Succeeded);
        Assert.Equal("FCC-06", renamed.Value!.Value.Code);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Master.SetMachineActiveAsync(w.Planning, machine.Value.Id, machine.Version, false, Token)).Status);
        Assert.Equal(OperationStatus.NotFound, (await w.Master.RenameMachineAsync(w.Planning, 4242, 1, "x", Token)).Status);
        Assert.Equal(ValidationIssue.NameRequired, (await w.Master.RenameMachineAsync(w.Planning, machine.Value.Id, renamed.Value.Version, " ", Token)).Issue);
    }

    [Fact]
    public async Task Planning_and_management_add_rates_as_history_with_audit_and_the_rate_in_force_follows_the_work_date()
    {
        using var w = await StartAsync();
        var (_, machine) = await MachineAsync(w);

        var first = await w.Master.AddRateAsync(w.Planning, machine.Value.Id, 1_200_000m, "1405/01/01", "قرارداد اولیه", Token);
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await w.Master.AddRateAsync(w.Management, machine.Value.Id, 1_500_000.5m, "1405/07/01", "افزایش سالانه", Token);

        Assert.True(first.Succeeded, first.Status + " " + first.Issue);
        Assert.True(second.Succeeded, second.Status + " " + second.Issue);
        var data = (await w.Master.LoadAsync(w.Warehouse, Token)).Value!;
        Assert.True(data.RatesVisible);
        Assert.Equal(new[] { 1_200_000m, 1_500_000.5m }, data.Rates.Select(r => r.RatePerHourRial).ToArray());
        Assert.Equal(new[] { w.Planning.UserId, w.Management.UserId }, data.Rates.Select(r => r.ChangedByUserId).ToArray());
        Assert.Equal(new[] { "قرارداد اولیه", "افزایش سالانه" }, data.Rates.Select(r => r.Reason).ToArray());

        // 1405/07/01 = 2026-09-23. A work date before it uses the first rate, on or after it the second.
        Assert.Equal(1_200_000m, MachineRates.RateOn(data.Rates, machine.Value.Id, new DateOnly(2026, 9, 22))!.RatePerHourRial);
        Assert.Equal(1_500_000.5m, MachineRates.RateOn(data.Rates, machine.Value.Id, new DateOnly(2026, 9, 23))!.RatePerHourRial);
        Assert.Null(MachineRates.RateOn(data.Rates, machine.Value.Id, new DateOnly(2026, 3, 1)));

        var audit = w.Fixture.Db.Query("SELECT ActionCode, TargetType, TargetId, UserId FROM AuditLog WHERE ActionCode = 'MachineRateChanged' ORDER BY Id");
        Assert.Equal(new[] { "MachineRateChanged|Machine|FCC-06|" + w.Planning.UserId, "MachineRateChanged|Machine|FCC-06|" + w.Management.UserId }, audit.ToArray());
        var summary = w.Fixture.Db.Query("SELECT Summary FROM AuditLog WHERE ActionCode = 'MachineRateChanged' ORDER BY Id DESC LIMIT 1").Single();
        Assert.Contains("1200000 ->", summary, StringComparison.Ordinal);
        Assert.Contains("1500000.5", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Warehouse_may_read_rates_but_not_change_them_and_other_departments_cannot_even_read_them()
    {
        using var w = await StartAsync();
        var (_, machine) = await MachineAsync(w);
        Assert.True((await w.Master.AddRateAsync(w.Planning, machine.Value.Id, 1m, "1405/01/01", null, Token)).Succeeded);

        foreach (var user in new Principal?[] { w.Warehouse, w.Production, w.Qc, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Master.AddRateAsync(user, machine.Value.Id, 5m, "1405/07/01", null, Token)).Status);
        }

        Assert.Equal(1, Count(w, "MachineHourlyRates"));
        Assert.True((await w.Master.LoadAsync(w.Warehouse, Token)).Value!.RatesVisible);
        foreach (var user in new[] { w.Production, w.Qc })
        {
            var data = (await w.Master.LoadAsync(user, Token)).Value!;
            Assert.False(data.RatesVisible);
            Assert.Empty(data.Rates);
            Assert.Single(data.Machines);
        }

        Assert.Equal(new PlanningMasterCapabilities(false, false, true), await w.Master.GetCapabilitiesAsync(w.Warehouse, Token));
        Assert.Equal(new PlanningMasterCapabilities(true, true, true), await w.Master.GetCapabilitiesAsync(w.Planning, Token));
        Assert.Equal(new PlanningMasterCapabilities(false, true, true), await w.Master.GetCapabilitiesAsync(w.Management, Token));
        Assert.Equal(PlanningMasterCapabilities.None, await w.Master.GetCapabilitiesAsync(w.Qc, Token));
        Assert.Equal(PlanningMasterCapabilities.None, await w.Master.GetCapabilitiesAsync(null, Token));
    }

    [Fact]
    public async Task An_invalid_rate_is_refused_with_the_reason_and_history_is_never_rewritten()
    {
        using var w = await StartAsync();
        var (_, machine) = await MachineAsync(w);
        var id = machine.Value.Id;
        Assert.True((await w.Master.AddRateAsync(w.Planning, id, 100m, "1405/05/01", null, Token)).Succeeded);

        Assert.Equal(ValidationIssue.RateInvalid, (await w.Master.AddRateAsync(w.Planning, id, 0m, "1405/07/01", null, Token)).Issue);
        Assert.Equal(ValidationIssue.RateInvalid, (await w.Master.AddRateAsync(w.Planning, id, -5m, "1405/07/01", null, Token)).Issue);
        Assert.Equal(ValidationIssue.EffectiveDateRequired, (await w.Master.AddRateAsync(w.Planning, id, 5m, " ", null, Token)).Issue);
        var badDate = await w.Master.AddRateAsync(w.Planning, id, 5m, "۱۴۰۵/۱۳/۰۱", null, Token);
        Assert.Equal(ValidationIssue.EffectiveDateInvalid, badDate.Issue);
        Assert.NotEqual(ERP.MES.Domain.Calendar.JalaliDateError.None, badDate.DateError);
        Assert.Equal(ValidationIssue.ReasonTooLong, (await w.Master.AddRateAsync(w.Planning, id, 5m, "1405/07/01", new string('x', 501), Token)).Issue);
        Assert.Equal(ValidationIssue.RateNotLater, (await w.Master.AddRateAsync(w.Planning, id, 5m, "1405/05/01", null, Token)).Issue);
        Assert.Equal(ValidationIssue.RateNotLater, (await w.Master.AddRateAsync(w.Planning, id, 5m, "1405/04/01", null, Token)).Issue);
        Assert.Equal(ValidationIssue.MachineInvalid, (await w.Master.AddRateAsync(w.Planning, 4242, 5m, "1405/07/01", null, Token)).Issue);

        Assert.True((await w.Master.SetMachineActiveAsync(w.Planning, id, machine.Version, false, Token)).Succeeded);
        Assert.Equal(ValidationIssue.MachineInvalid, (await w.Master.AddRateAsync(w.Planning, id, 5m, "1405/07/01", null, Token)).Issue);
        Assert.Equal(1, Count(w, "MachineHourlyRates"));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'MachineRateChanged'"));
    }

    [Fact]
    public async Task The_rate_history_is_protected_by_the_database_foreign_keys_and_one_entry_per_date()
    {
        using var w = await StartAsync();
        var (_, machine) = await MachineAsync(w);
        Assert.True((await w.Master.AddRateAsync(w.Planning, machine.Value.Id, 100m, "1405/05/01", null, Token)).Succeeded);

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => w.Fixture.Db.Execute("DELETE FROM Machines"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => w.Fixture.Db.Execute("DELETE FROM ProductionUnits"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => w.Fixture.Db.Execute(
            "INSERT INTO MachineHourlyRates (MachineId, RatePerHourRial, EffectiveFrom, ChangedByUserId, ChangedAtUtc) SELECT MachineId, '5', EffectiveFrom, ChangedByUserId, ChangedAtUtc FROM MachineHourlyRates"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => w.Fixture.Db.Execute(
            "INSERT INTO Machines (Code, Name, ProductionUnitId, IsActive, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) SELECT Code, 'dup', ProductionUnitId, 1, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, 1 FROM Machines"));
        Assert.Empty(w.Fixture.Db.Query("PRAGMA foreign_key_check"));
    }
}
