using ERP.MES.Application.Persistence;
using ERP.MES.Application.Quality;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Quality;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// QC master data (Decision Log #9, #90) on a real SQLite file with the real authorization: QC alone changes units, conversions and quantity types; every active user reads them.
public class QcMasterServiceTests
{
    private const string InitialPassword = "Staff-Pass-Phrase-2";
    private const string OwnPassword = "My-Own-Pass-Phrase-3";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class World : IDisposable
    {
        public required SecurityFixture Fixture { get; init; }

        public required Principal Qc { get; init; }

        public required Principal Planning { get; init; }

        public required Principal Central { get; init; }

        public required WorkflowServices Services { get; init; }

        public QcMasterService Master => Services.QcMaster;

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
            Qc = await UserAsync(fixture, admin, "qc.one", DepartmentCodes.Qc),
            Planning = await UserAsync(fixture, admin, "planning.one", DepartmentCodes.Planning),
            Central = await UserAsync(fixture, admin, "central.one", DepartmentCodes.Central),
            Services = WorkflowServices.Create(fixture.Db.ContextFactory, fixture.Services.Authorization, PersistenceTestData.Dates, fixture.Clock),
        };
    }

    private static async Task<QcMasterData> LoadAsync(World w, Principal? user = null)
    {
        var outcome = await w.Master.LoadAsync(user ?? w.Qc, Token);
        Assert.True(outcome.Succeeded, outcome.Status.ToString());
        return outcome.Value!;
    }

    private static long Count(World w, string table) => long.Parse(w.Fixture.Db.Query("SELECT COUNT(*) FROM " + table).Single(), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task The_standard_definitions_ship_with_the_database_and_have_no_creator()
    {
        using var w = await StartAsync();

        var data = await LoadAsync(w, w.Central);

        Assert.Equal(new[] { "PCS", "PAIR", "KG", "MM" }, data.Units.Select(u => u.Value.Code).ToArray());
        Assert.Equal(new[] { UnitKind.Count, UnitKind.Count, UnitKind.Mass, UnitKind.Length }, data.Units.Select(u => u.Value.Kind).ToArray());
        Assert.All(data.Units, u => Assert.Null(u.Value.CreatedByUserId));
        var conversion = Assert.Single(data.Conversions).Value;
        Assert.Equal((2L, 1L, 2m), (conversion.FromUnitId, conversion.ToUnitId, conversion.Factor));
        Assert.Equal(new[] { "PIECE", "PAIR" }, data.QuantityTypes.Select(q => q.Value.Code).ToArray());
        Assert.Equal(new[] { 1, 2 }, data.QuantityTypes.Select(q => q.Value.BaseUnitsPerType).ToArray());
        Assert.All(data.QuantityTypes, q => Assert.Equal(1, q.Value.BaseUnitId));

        // The derived reciprocal: pair to piece 2, piece to pair 0.5.
        Assert.True(UnitConversions.TryResolve(data.Conversions.Select(c => c.Value), 1, 2, out var half));
        Assert.Equal(0.5m, half);
    }

    [Fact]
    public async Task Only_qc_changes_the_definitions_and_everyone_active_reads_them()
    {
        using var w = await StartAsync();

        foreach (var user in new Principal?[] { w.Planning, w.Central, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Master.AddUnitAsync(user, "BOX", "جعبه", UnitKind.Count, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Master.AddConversionAsync(user, 1, 3, 2m, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Master.AddQuantityTypeAsync(user, new AddQuantityTypeCommand("TRIPLE", "سه‌تایی", 1, 3), Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Master.RenameUnitAsync(user, 1, 1, "x", Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Master.SetQuantityTypeActiveAsync(user, 1, 1, false, Token)).Status);
        }

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Master.LoadAsync(null, Token)).Status);
        Assert.True((await w.Master.LoadAsync(w.Planning, Token)).Succeeded);
        Assert.True(await w.Master.CanMaintainAsync(w.Qc, Token));
        Assert.False(await w.Master.CanMaintainAsync(w.Planning, Token));
        Assert.False(await w.Master.CanMaintainAsync(null, Token));
        Assert.Equal(4, Count(w, "UnitsOfMeasure"));
    }

    [Fact]
    public async Task Qc_adds_a_unit_it_is_stored_with_its_creator_and_survives_a_restart()
    {
        using var w = await StartAsync();

        var added = await w.Master.AddUnitAsync(w.Qc, "BOX", "  جعبه  ", UnitKind.Count, Token);

        Assert.True(added.Succeeded, added.Status + " " + added.Issue);
        var restarted = WorkflowServices.Create(w.Fixture.Db.ContextFactory, w.Fixture.Services.Authorization, PersistenceTestData.Dates, w.Fixture.Clock);
        var unit = (await restarted.QcMaster.LoadAsync(w.Planning, Token)).Value!.Units.Single(u => u.Value.Code == "BOX").Value;
        Assert.Equal("جعبه", unit.Name);
        Assert.Equal(UnitKind.Count, unit.Kind);
        Assert.Equal(w.Qc.UserId, unit.CreatedByUserId);
        Assert.True(unit.IsActive);
    }

    [Theory]
    [InlineData("", "نام", ValidationIssue.CodeInvalid)]
    [InlineData("box", "نام", ValidationIssue.CodeInvalid)]
    [InlineData("B X", "نام", ValidationIssue.CodeInvalid)]
    [InlineData("BOX", "  ", ValidationIssue.NameRequired)]
    [InlineData("PCS", "نام", ValidationIssue.CodeDuplicate)]
    public async Task An_invalid_or_duplicate_unit_is_refused_with_the_reason_and_nothing_is_saved(string code, string name, ValidationIssue expected)
    {
        using var w = await StartAsync();

        var outcome = await w.Master.AddUnitAsync(w.Qc, code, name, UnitKind.Count, Token);

        Assert.Equal(OperationStatus.ValidationFailed, outcome.Status);
        Assert.Equal(expected, outcome.Issue);
        Assert.Equal(4, Count(w, "UnitsOfMeasure"));
        Assert.Equal(OperationStatus.ValidationFailed, (await w.Master.AddUnitAsync(w.Qc, "OK", "نام", (UnitKind)0, Token)).Status);
    }

    [Fact]
    public async Task Rename_and_deactivate_use_the_version_and_a_stale_one_is_a_conflict()
    {
        using var w = await StartAsync();
        var unit = (await w.Master.AddUnitAsync(w.Qc, "BOX", "جعبه", UnitKind.Count, Token)).Value!;
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var renamed = await w.Master.RenameUnitAsync(w.Qc, unit.Value.Id, unit.Version, "جعبهٔ بزرگ", Token);
        Assert.True(renamed.Succeeded);
        Assert.Equal("جعبهٔ بزرگ", renamed.Value!.Value.Name);

        var stale = await w.Master.SetUnitActiveAsync(w.Qc, unit.Value.Id, unit.Version, false, Token);
        Assert.Equal(OperationStatus.ConcurrencyConflict, stale.Status);
        Assert.True((await LoadAsync(w)).Units.Single(u => u.Value.Code == "BOX").Value.IsActive);

        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await w.Master.SetUnitActiveAsync(w.Qc, unit.Value.Id, renamed.Value.Version, false, Token)).Succeeded);
        Assert.False((await LoadAsync(w)).Units.Single(u => u.Value.Code == "BOX").Value.IsActive);
        Assert.Equal(OperationStatus.NotFound, (await w.Master.RenameUnitAsync(w.Qc, 4242, 1, "x", Token)).Status);
        Assert.Equal(ValidationIssue.NameRequired, (await w.Master.RenameUnitAsync(w.Qc, unit.Value.Id, renamed.Value.Version, " ", Token)).Issue);
    }

    [Fact]
    public async Task A_conversion_is_validated_and_the_opposite_direction_is_refused_because_it_is_derived()
    {
        using var w = await StartAsync();
        var box = (await w.Master.AddUnitAsync(w.Qc, "BOX", "جعبه", UnitKind.Count, Token)).Value!.Value.Id;

        var added = await w.Master.AddConversionAsync(w.Qc, box, QcMasterSeedIds.Piece, 12m, Token);
        Assert.True(added.Succeeded, added.Status + " " + added.Issue);

        Assert.Equal(ValidationIssue.ConversionSameUnit, (await w.Master.AddConversionAsync(w.Qc, box, box, 2m, Token)).Issue);
        Assert.Equal(ValidationIssue.FactorInvalid, (await w.Master.AddConversionAsync(w.Qc, box, QcMasterSeedIds.Pair, 0m, Token)).Issue);
        Assert.Equal(ValidationIssue.FactorInvalid, (await w.Master.AddConversionAsync(w.Qc, box, QcMasterSeedIds.Pair, -1m, Token)).Issue);
        Assert.Equal(ValidationIssue.UnitNotFound, (await w.Master.AddConversionAsync(w.Qc, box, 4242, 2m, Token)).Issue);
        Assert.Equal(ValidationIssue.ConversionDuplicate, (await w.Master.AddConversionAsync(w.Qc, box, QcMasterSeedIds.Piece, 6m, Token)).Issue);
        Assert.Equal(ValidationIssue.ConversionDuplicate, (await w.Master.AddConversionAsync(w.Qc, QcMasterSeedIds.Piece, box, 0.1m, Token)).Issue);
        Assert.Equal(ValidationIssue.ConversionDuplicate, (await w.Master.AddConversionAsync(w.Qc, QcMasterSeedIds.Piece, QcMasterSeedIds.Pair, 0.5m, Token)).Issue);
        Assert.Equal(2, Count(w, "UnitConversions"));

        // The exact decimal factor is stored without rounding.
        var exact = (await w.Master.AddConversionAsync(w.Qc, QcMasterSeedIds.Kilogram, QcMasterSeedIds.Millimetre, 0.123456789012m, Token)).Value!;
        Assert.Equal(0.123456789012m, (await LoadAsync(w)).Conversions.Single(c => c.Value.Id == exact.Value.Id).Value.Factor);
    }

    [Fact]
    public async Task A_deactivated_conversion_stops_resolving_and_can_be_reactivated()
    {
        using var w = await StartAsync();
        var seeded = (await LoadAsync(w)).Conversions.Single();

        var off = await w.Master.SetConversionActiveAsync(w.Qc, seeded.Value.Id, seeded.Version, false, Token);

        Assert.True(off.Succeeded);
        Assert.False(UnitConversions.TryResolve((await LoadAsync(w)).Conversions.Select(c => c.Value), 2, 1, out _));
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await w.Master.SetConversionActiveAsync(w.Qc, seeded.Value.Id, off.Value!.Version, true, Token)).Succeeded);
        Assert.True(UnitConversions.TryResolve((await LoadAsync(w)).Conversions.Select(c => c.Value), 1, 2, out var factor));
        Assert.Equal(0.5m, factor);
    }

    [Fact]
    public async Task A_simple_quantity_type_maps_to_the_base_unit_and_a_set_is_stored_with_its_explicit_members()
    {
        using var w = await StartAsync();

        var triple = await w.Master.AddQuantityTypeAsync(w.Qc, new AddQuantityTypeCommand("TRIPLE", "سه‌تایی", QcMasterSeedIds.Piece, 3), Token);
        var set = await w.Master.AddQuantityTypeAsync(
            w.Qc, new AddQuantityTypeCommand("SET-A", "ست الف", QcMasterSeedIds.Piece, 0, [new SetMemberInput("شفت", 1), new SetMemberInput("بوش", 2), new SetMemberInput("واشر", 4)]), Token);

        Assert.True(triple.Succeeded, triple.Status + " " + triple.Issue);
        Assert.True(set.Succeeded, set.Status + " " + set.Issue);
        var restarted = WorkflowServices.Create(w.Fixture.Db.ContextFactory, w.Fixture.Services.Authorization, PersistenceTestData.Dates, w.Fixture.Clock);
        var types = (await restarted.QcMaster.LoadAsync(w.Planning, Token)).Value!.QuantityTypes.ToDictionary(q => q.Value.Code, q => q.Value);
        Assert.Equal(3, types["TRIPLE"].BaseUnitsPerType);
        Assert.False(types["TRIPLE"].IsSet);
        Assert.True(types["SET-A"].IsSet);
        Assert.Equal(7, types["SET-A"].BaseUnitsPerType);
        Assert.Equal(new[] { "شفت", "بوش", "واشر" }, types["SET-A"].Members.Select(m => m.Description).ToArray());
        Assert.Equal(new[] { 1, 2, 4 }, types["SET-A"].Members.Select(m => m.BaseUnits).ToArray());
        Assert.Equal(3, Count(w, "QuantityTypeMembers"));
    }

    [Fact]
    public async Task An_invalid_quantity_type_is_refused_with_the_reason_and_nothing_is_saved()
    {
        using var w = await StartAsync();
        var kg = QcMasterSeedIds.Kilogram;
        var box = (await w.Master.AddUnitAsync(w.Qc, "BOX", "جعبه", UnitKind.Count, Token)).Value!;
        await w.Master.SetUnitActiveAsync(w.Qc, box.Value.Id, box.Version, false, Token);

        Assert.Equal(ValidationIssue.CodeInvalid, (await w.Master.AddQuantityTypeAsync(w.Qc, new AddQuantityTypeCommand("bad", "x", 1, 1), Token)).Issue);
        Assert.Equal(ValidationIssue.NameRequired, (await w.Master.AddQuantityTypeAsync(w.Qc, new AddQuantityTypeCommand("OK", " ", 1, 1), Token)).Issue);
        Assert.Equal(ValidationIssue.BaseUnitsInvalid, (await w.Master.AddQuantityTypeAsync(w.Qc, new AddQuantityTypeCommand("OK", "x", 1, 0), Token)).Issue);
        Assert.Equal(ValidationIssue.BaseUnitInvalid, (await w.Master.AddQuantityTypeAsync(w.Qc, new AddQuantityTypeCommand("OK", "x", kg, 1), Token)).Issue);
        Assert.Equal(ValidationIssue.BaseUnitInvalid, (await w.Master.AddQuantityTypeAsync(w.Qc, new AddQuantityTypeCommand("OK", "x", box.Value.Id, 1), Token)).Issue);
        Assert.Equal(ValidationIssue.BaseUnitInvalid, (await w.Master.AddQuantityTypeAsync(w.Qc, new AddQuantityTypeCommand("OK", "x", 4242, 1), Token)).Issue);
        Assert.Equal(ValidationIssue.CodeDuplicate, (await w.Master.AddQuantityTypeAsync(w.Qc, new AddQuantityTypeCommand("PAIR", "x", 1, 2), Token)).Issue);
        Assert.Equal(ValidationIssue.SetMembersInvalid, (await w.Master.AddQuantityTypeAsync(w.Qc, new AddQuantityTypeCommand("S1", "x", 1, 0, [new SetMemberInput("a", 1)]), Token)).Issue);
        Assert.Equal(ValidationIssue.SetMembersInvalid, (await w.Master.AddQuantityTypeAsync(w.Qc, new AddQuantityTypeCommand("S2", "x", 1, 0, [new SetMemberInput("a", 1), new SetMemberInput(" ", 1)]), Token)).Issue);
        Assert.Equal(ValidationIssue.SetMembersInvalid, (await w.Master.AddQuantityTypeAsync(w.Qc, new AddQuantityTypeCommand("S3", "x", 1, 0, [new SetMemberInput("a", 1), new SetMemberInput("b", 0)]), Token)).Issue);

        Assert.Equal(2, Count(w, "QuantityTypes"));
        Assert.Equal(0, Count(w, "QuantityTypeMembers"));
    }

    [Fact]
    public async Task A_quantity_type_can_be_renamed_and_retired_but_its_mapping_never_changes()
    {
        using var w = await StartAsync();
        var pair = (await LoadAsync(w)).QuantityTypes.Single(q => q.Value.Code == "PAIR");

        var renamed = await w.Master.RenameQuantityTypeAsync(w.Qc, pair.Value.Id, pair.Version, "جفت (۲ قطعه)", Token);
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var retired = await w.Master.SetQuantityTypeActiveAsync(w.Qc, pair.Value.Id, renamed.Value!.Version, false, Token);

        Assert.True(renamed.Succeeded);
        Assert.True(retired.Succeeded);
        var now = (await LoadAsync(w)).QuantityTypes.Single(q => q.Value.Code == "PAIR").Value;
        Assert.Equal("جفت (۲ قطعه)", now.Name);
        Assert.False(now.IsActive);
        Assert.Equal(2, now.BaseUnitsPerType);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Master.RenameQuantityTypeAsync(w.Qc, pair.Value.Id, pair.Version, "x", Token)).Status);
    }

    [Fact]
    public async Task The_database_keeps_codes_unique_and_refuses_to_orphan_a_unit()
    {
        using var w = await StartAsync();

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => w.Fixture.Db.Execute(
            "INSERT INTO UnitsOfMeasure (Code, Name, Kind, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES ('PCS', 'dup', 1, 1, '2026-10-22 00:00:00', '2026-10-22 00:00:00', 1)"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => w.Fixture.Db.Execute("DELETE FROM UnitsOfMeasure WHERE Code = 'PCS'"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => w.Fixture.Db.Execute(
            "INSERT INTO UnitConversions (FromUnitId, ToUnitId, Factor, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (2, 1, '3', 1, '2026-10-22 00:00:00', '2026-10-22 00:00:00', 1)"));
        Assert.Empty(w.Fixture.Db.Query("PRAGMA foreign_key_check"));
    }

    /// <summary>The ids of the standard units seeded by the migration (the same constants the seed uses).</summary>
    private static class QcMasterSeedIds
    {
        public const long Piece = 1;
        public const long Pair = 2;
        public const long Kilogram = 3;
        public const long Millimetre = 4;
    }
}
