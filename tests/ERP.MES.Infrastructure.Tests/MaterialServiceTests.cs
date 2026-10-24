using ERP.MES.Application.Persistence;
using ERP.MES.Application.Planning;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Materials;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// The Warehouse material list and the price-per-kg history (Decision Log #10) on a real SQLite file with the real authorization.
public class MaterialServiceTests
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

        public MaterialService Mat => Services.Materials;

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

    private static async Task<Stored<Material>> MaterialAsync(World w, string code = "ST37-R30")
    {
        var outcome = await w.Mat.AddMaterialAsync(w.Warehouse, code, "میلگرد ST37 قطر ۳۰", "ST37", "میلگرد", Token);
        Assert.True(outcome.Succeeded, outcome.Status + " " + outcome.Issue);
        return outcome.Value!;
    }

    private static long Count(World w, string tableAndWhere) => long.Parse(w.Fixture.Db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Warehouse_registers_a_material_with_audit_and_every_active_user_reads_the_list()
    {
        using var w = await StartAsync();
        var material = await MaterialAsync(w);

        foreach (var reader in new[] { w.Planning, w.Management, w.Warehouse, w.Production, w.Qc })
        {
            var data = (await w.Mat.LoadAsync(reader, Token)).Value!;
            Assert.Equal("ST37-R30", Assert.Single(data.Materials).Value.Code);
        }

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Mat.LoadAsync(null, Token)).Status);
        Assert.Equal("MaterialAdded", Assert.Single(w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode = 'MaterialAdded'")));
        Assert.Equal(w.Warehouse.UserId, material.Value.CreatedByUserId);
        Assert.Equal("ST37", material.Value.Grade);
    }

    [Fact]
    public async Task Only_warehouse_maintains_the_material_list()
    {
        using var w = await StartAsync();
        var material = await MaterialAsync(w);

        foreach (var user in new Principal?[] { w.Planning, w.Management, w.Production, w.Qc, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Mat.AddMaterialAsync(user, "AL-01", "x", null, null, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Mat.UpdateMaterialAsync(user, material.Value.Id, material.Version, "x", null, null, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Mat.SetMaterialActiveAsync(user, material.Value.Id, material.Version, false, Token)).Status);
        }

        Assert.Equal(1, Count(w, "Materials"));
    }

    [Theory]
    [InlineData("", "x", ValidationIssue.CodeInvalid)]
    [InlineData("st37", "x", ValidationIssue.CodeInvalid)]
    [InlineData("AL-01", " ", ValidationIssue.NameRequired)]
    [InlineData("ST37-R30", "x", ValidationIssue.CodeDuplicate)]
    public async Task An_invalid_or_duplicate_material_is_refused_and_nothing_is_saved(string code, string name, ValidationIssue expected)
    {
        using var w = await StartAsync();
        await MaterialAsync(w);

        var outcome = await w.Mat.AddMaterialAsync(w.Warehouse, code, name, null, null, Token);

        Assert.Equal(OperationStatus.ValidationFailed, outcome.Status);
        Assert.Equal(expected, outcome.Issue);
        Assert.Equal(1, Count(w, "Materials"));
    }

    [Fact]
    public async Task Update_and_retire_keep_the_code_and_a_stale_version_conflicts()
    {
        using var w = await StartAsync();
        var material = await MaterialAsync(w);

        Assert.Equal(ValidationIssue.MaterialTextTooLong, (await w.Mat.UpdateMaterialAsync(w.Warehouse, material.Value.Id, material.Version, "x", new string('g', 101), null, Token)).Issue);
        var updated = (await w.Mat.UpdateMaterialAsync(w.Warehouse, material.Value.Id, material.Version, "میلگرد جدید", "ST52", null, Token)).Value!;
        Assert.Equal("ST52", updated.Value.Grade);
        Assert.Null(updated.Value.Form);
        Assert.Equal("ST37-R30", updated.Value.Code);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Mat.SetMaterialActiveAsync(w.Warehouse, material.Value.Id, material.Version, false, Token)).Status);
        Assert.Equal(OperationStatus.NotFound, (await w.Mat.UpdateMaterialAsync(w.Warehouse, 4242, 1, "x", null, null, Token)).Status);

        var off = (await w.Mat.SetMaterialActiveAsync(w.Warehouse, material.Value.Id, updated.Version, false, Token)).Value!;
        Assert.False(off.Value.IsActive);
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'MaterialChanged'"));
    }

    [Fact]
    public async Task Warehouse_and_management_add_prices_planning_reads_them_and_others_cannot()
    {
        using var w = await StartAsync();
        var material = await MaterialAsync(w);

        var first = await w.Mat.AddPriceAsync(w.Warehouse, material.Value.Id, 90_000m, "1405/01/01", "قیمت اولیه", Token);
        Assert.True(first.Succeeded, first.Status + " " + first.Issue);
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await w.Mat.AddPriceAsync(w.Management, material.Value.Id, 105_500.5m, "1405/07/01", null, Token);
        Assert.True(second.Succeeded);

        foreach (var reader in new[] { w.Warehouse, w.Management, w.Planning })
        {
            var data = (await w.Mat.LoadAsync(reader, Token)).Value!;
            Assert.True(data.PricesVisible);
            Assert.Equal(2, data.Prices.Count);
        }

        foreach (var hidden in new[] { w.Production, w.Qc })
        {
            var data = (await w.Mat.LoadAsync(hidden, Token)).Value!;
            Assert.False(data.PricesVisible);
            Assert.Empty(data.Prices);
        }

        foreach (var user in new Principal?[] { w.Planning, w.Production, w.Qc, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Mat.AddPriceAsync(user, material.Value.Id, 1m, "1406/01/01", null, Token)).Status);
        }

        // 1405/07/01 = 2026-09-23: before it the first price applies, from it the second.
        var prices = (await w.Mat.LoadAsync(w.Planning, Token)).Value!.Prices;
        Assert.Equal(90_000m, MaterialPrices.PriceOn(prices, material.Value.Id, new DateOnly(2026, 9, 22))!.PricePerKgRial);
        Assert.Equal(105_500.5m, MaterialPrices.PriceOn(prices, material.Value.Id, new DateOnly(2026, 9, 23))!.PricePerKgRial);
        Assert.Null(MaterialPrices.PriceOn(prices, material.Value.Id, new DateOnly(2025, 1, 1)));
        Assert.Equal(2, Count(w, "AuditLog WHERE ActionCode = 'MaterialPriceChanged'"));
    }

    [Fact]
    public async Task A_price_must_be_positive_dated_valid_and_later_than_the_latest_and_history_is_never_rewritten()
    {
        using var w = await StartAsync();
        var material = await MaterialAsync(w);
        Assert.True((await w.Mat.AddPriceAsync(w.Warehouse, material.Value.Id, 90_000m, "1405/07/01", null, Token)).Succeeded);

        Assert.Equal(ValidationIssue.PriceInvalid, (await w.Mat.AddPriceAsync(w.Warehouse, material.Value.Id, 0m, "1405/08/01", null, Token)).Issue);
        Assert.Equal(ValidationIssue.PriceInvalid, (await w.Mat.AddPriceAsync(w.Warehouse, material.Value.Id, -5m, "1405/08/01", null, Token)).Issue);
        Assert.Equal(ValidationIssue.EffectiveDateRequired, (await w.Mat.AddPriceAsync(w.Warehouse, material.Value.Id, 1m, " ", null, Token)).Issue);
        Assert.Equal(ValidationIssue.EffectiveDateInvalid, (await w.Mat.AddPriceAsync(w.Warehouse, material.Value.Id, 1m, "1405/13/40", null, Token)).Issue);
        Assert.Equal(ValidationIssue.PriceNotLater, (await w.Mat.AddPriceAsync(w.Warehouse, material.Value.Id, 1m, "1405/07/01", null, Token)).Issue);
        Assert.Equal(ValidationIssue.PriceNotLater, (await w.Mat.AddPriceAsync(w.Warehouse, material.Value.Id, 1m, "1405/06/01", null, Token)).Issue);
        Assert.Equal(ValidationIssue.ReasonTooLong, (await w.Mat.AddPriceAsync(w.Warehouse, material.Value.Id, 1m, "1405/08/01", new string('r', 501), Token)).Issue);
        Assert.Equal(ValidationIssue.MaterialInvalid, (await w.Mat.AddPriceAsync(w.Warehouse, 4242, 1m, "1405/08/01", null, Token)).Issue);
        Assert.Equal(1, Count(w, "MaterialPrices"));

        var off = (await w.Mat.SetMaterialActiveAsync(w.Warehouse, material.Value.Id, material.Version, false, Token)).Value!;
        Assert.Equal(ValidationIssue.MaterialInvalid, (await w.Mat.AddPriceAsync(w.Warehouse, off.Value.Id, 1m, "1405/08/01", null, Token)).Issue);
    }

    [Fact]
    public async Task Capabilities_follow_the_departments()
    {
        using var w = await StartAsync();

        Assert.Equal(new MaterialCapabilities(true, true, true), await w.Mat.GetCapabilitiesAsync(w.Warehouse, Token));
        Assert.Equal(new MaterialCapabilities(false, true, true), await w.Mat.GetCapabilitiesAsync(w.Management, Token));
        Assert.Equal(new MaterialCapabilities(false, false, true), await w.Mat.GetCapabilitiesAsync(w.Planning, Token));
        Assert.Equal(new MaterialCapabilities(false, false, false), await w.Mat.GetCapabilitiesAsync(w.Production, Token));
        Assert.Equal(MaterialCapabilities.None, await w.Mat.GetCapabilitiesAsync(null, Token));
    }

    [Fact]
    public async Task The_material_list_and_prices_survive_a_restart()
    {
        using var w = await StartAsync();
        var material = await MaterialAsync(w);
        await w.Mat.AddPriceAsync(w.Warehouse, material.Value.Id, 90_000m, "1405/01/01", null, Token);

        var restarted = WorkflowServices.Create(w.Fixture.Db.ContextFactory, w.Fixture.Services.Authorization, PersistenceTestData.Dates, w.Fixture.Clock);
        var data = (await restarted.Materials.LoadAsync(w.Management, Token)).Value!;

        Assert.Single(data.Materials);
        Assert.Equal(90_000m, Assert.Single(data.Prices).PricePerKgRial);
    }
}
