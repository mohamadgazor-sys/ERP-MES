using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Persistence;
using ERP.MES.Infrastructure.Sqlite;
using ERP.MES.Infrastructure.Sqlite.Demo;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.MES.Infrastructure.Tests;

// Creators and approvers are real foreign keys to Users with deletion restricted (Decision Log #70, owner decision on M10), and the way an older database is
// upgraded - or refused, unchanged, with an explanation - is safe. No sentinel user id and no fake account is ever used to make a constraint pass.
public class ActorReferenceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Insert(TempDatabase db) => "INSERT INTO Customers (Name, IsActive, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES ('Old sample customer', 1, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1)";

    // A database as it was before sign-in existed: only the initial migration is applied.
    private static async Task MigrateToInitialAsync(TempDatabase db)
    {
        await using var context = db.NewContext();
        var initial = context.Database.GetMigrations().First();
        await context.GetService<IMigrator>().MigrateAsync(initial, Token);
        Assert.Single(db.Query("SELECT MigrationId FROM __EFMigrationsHistory"));
    }

    // ---- the keys ----

    [Fact]
    public async Task A_record_cannot_name_a_creator_or_approver_who_is_not_a_user()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();
        var unknownUser = 12_345L;

        await using (var context = db.NewContext())
        {
            context.Customers.Add(new Customer(0, "No such creator", unknownUser, PersistenceTestData.Now));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
        }

        await using (var context = db.NewContext())
        {
            var customer = PersistenceTestData.NewCustomer();
            context.Customers.Add(customer);
            await context.SaveChangesAsync(Token);

            var request = Request.Create(0, customer, RequestNo.Create("R-1"), WorkType.Make, new DateOnly(2026, 10, 12), null, null, unknownUser, PersistenceTestData.Now, PersistenceTestData.Dates);
            context.Requests.Add(request);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
        }

        await using (var context = db.NewContext())
        {
            var customer = await context.Customers.SingleAsync(Token);
            var request = PersistenceTestData.NewRequest(customer, "R-2");
            request.AddItem(0, "1.10", "Ring", 1m, unknownUser, PersistenceTestData.Now);
            context.Requests.Add(request);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
        }

        await using (var context = db.NewContext())
        {
            var customer = await context.Customers.SingleAsync(Token);
            var request = PersistenceTestData.NewRequest(customer, "R-3");
            var item = request.AddItem(0, "1.10", "Ring", 1m, TempDatabase.TestCreatorId, PersistenceTestData.Now);
            item.DefineInitialStage("Turning", 1m);
            item.ReleaseFirstStage();
            item.StartStage(1);
            item.SubmitStageForQc(1);
            item.ApproveStageWholeQuantity(1, unknownUser, PersistenceTestData.Now);
            context.Requests.Add(request);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
        }

        Assert.Empty(db.Query("SELECT Id FROM Requests"));
        Assert.Empty(db.Query("SELECT Id FROM QcApprovals"));
        Assert.Equal(new[] { "1" }, db.Query("SELECT COUNT(*) FROM Customers"));
    }

    [Fact]
    public async Task A_record_with_real_creators_and_approvers_is_accepted_and_the_database_is_consistent()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();

        await PersistenceTestData.SeedSampleRequestAsync(db);

        Assert.Equal(new[] { "1" }, db.Query("SELECT COUNT(*) FROM QcApprovals"));
        Assert.Empty(db.Query("PRAGMA foreign_key_check"));
    }

    [Fact]
    public async Task A_user_who_created_or_approved_something_cannot_be_deleted()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();
        await PersistenceTestData.SeedSampleRequestAsync(db);

        Assert.Throws<SqliteException>(() => db.Execute($"DELETE FROM Users WHERE Id = {TempDatabase.TestCreatorId}"));
        Assert.Throws<SqliteException>(() => db.Execute($"DELETE FROM Users WHERE Id = {TempDatabase.TestApproverId}"));
        Assert.Equal(2, db.Query("SELECT Id FROM Users").Count);
    }

    [Fact]
    public async Task Only_the_references_that_can_genuinely_be_unknown_are_optional()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();

        string NotNull(string table, string column) => db.Query($"SELECT \"notnull\" FROM pragma_table_info('{table}') WHERE name = '{column}'").Single();

        // Business records always have a creator or approver; an audit entry may describe an event with no known user (an unknown login name, an operator running
        // Provisioning), and no recovery is pending in the normal state.
        Assert.Equal("1", NotNull("Customers", "CreatedByUserId"));
        Assert.Equal("1", NotNull("Requests", "CreatedByUserId"));
        Assert.Equal("1", NotNull("RequestItems", "CreatedByUserId"));
        Assert.Equal("1", NotNull("QcApprovals", "ApprovedByUserId"));
        Assert.Equal("0", NotNull("AuditLog", "UserId"));
        Assert.Equal("0", NotNull("SystemSetup", "RecoveryTargetUserId"));
        Assert.Equal("1", NotNull("UserCredentials", "UserId"));
    }

    [Fact]
    public async Task The_demonstration_data_names_only_real_users_and_passes_the_foreign_key_check()
    {
        using var db = new TempDatabase();
        await DemoDatabase.InitializeAsync(db.Path, PersistenceTestData.Dates, db.NextCredentialPath(), SecurityFixture.FastPolicy, Token);

        Assert.Empty(db.Query("PRAGMA foreign_key_check"));
        Assert.Equal(new[] { "demo.central" }, db.Query("SELECT DISTINCT u.Username FROM Customers c JOIN Users u ON u.Id = c.CreatedByUserId"));
    }

    // ---- upgrading an older database ----

    [Fact]
    public async Task An_older_database_without_business_records_is_upgraded_and_keeps_its_history()
    {
        using var db = new TempDatabase();
        await MigrateToInitialAsync(db);

        await SqliteDatabase.MigrateAsync(db.Path, Token);

        Assert.Equal(17, db.Query("SELECT MigrationId FROM __EFMigrationsHistory").Count);
        Assert.Equal("Customers:CreatedByUserId->Users", db.Query("SELECT 'Customers:' || \"from\" || '->' || \"table\" FROM pragma_foreign_key_list('Customers')").Single());
        Assert.Empty(db.Query("PRAGMA foreign_key_check"));
    }

    [Fact]
    public async Task An_older_database_whose_records_name_a_user_who_never_existed_is_refused_and_left_exactly_as_it_was()
    {
        using var db = new TempDatabase();
        await MigrateToInitialAsync(db);
        db.Execute(Insert(db));
        var bytesBefore = await File.ReadAllBytesAsync(db.Path, Token);

        var refusal = await Assert.ThrowsAsync<ActorReferencesInvalidException>(() => SqliteDatabase.MigrateAsync(db.Path, Token));

        // The explanation names the problem, the unchanged state and the supported ways forward.
        Assert.Equal(1, refusal.RecordCount);
        Assert.Contains("refer to a creator or approver that is not a user", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("demo-reset", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("reviewed data repair", refusal.Message, StringComparison.Ordinal);

        // Nothing was deleted, invented or altered: same bytes, same single migration, same row, no users.
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(db.Path, Token));
        Assert.Single(db.Query("SELECT MigrationId FROM __EFMigrationsHistory"));
        Assert.Equal(new[] { "Old sample customer" }, db.Query("SELECT Name FROM Customers"));
        Assert.Throws<SqliteException>(() => db.Query("SELECT Id FROM Users"));

        // The start-up path of the desktop application refuses in the same way.
        await Assert.ThrowsAsync<ActorReferencesInvalidException>(() => SqliteDatabase.MigrateExistingAsync(db.Path, Token));
    }

    [Fact]
    public async Task The_application_start_up_path_never_creates_a_database()
    {
        using var db = new TempDatabase();

        await Assert.ThrowsAsync<FileNotFoundException>(() => SqliteDatabase.MigrateExistingAsync(db.Path, Token));

        Assert.False(db.Exists);

        // An existing database is simply brought up to date.
        await db.MigrateAsync();
        await SqliteDatabase.MigrateExistingAsync(db.Path, Token);
        await using var context = db.NewContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(Token));
    }

    [Fact]
    public async Task An_older_demonstration_database_is_refused_by_initialise_and_replaced_by_the_explicit_reset()
    {
        using var db = new TempDatabase();
        await MigrateToInitialAsync(db);
        db.Execute("PRAGMA application_id = " + DemoDatabase.ApplicationId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        db.Execute(Insert(db));
        var bytesBefore = await File.ReadAllBytesAsync(db.Path, Token);
        Assert.Equal(DemoDatabaseState.Demo, DemoDatabase.Inspect(db.Path));

        var refusal = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(
            () => DemoDatabase.InitializeAsync(db.Path, PersistenceTestData.Dates, db.NextCredentialPath(), SecurityFixture.FastPolicy, Token));

        Assert.Contains("demo-reset", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(db.Path, Token));
        Assert.False(File.Exists(db.CredentialFiles[0]));

        // The supported path: the explicit reset builds a new, consistent database and replaces the old one.
        var reset = await DemoDatabase.ResetAsync(db.Path, PersistenceTestData.Dates, db.NextCredentialPath(), SecurityFixture.FastPolicy, Token);

        Assert.Equal(8, reset.AccountsCreated);
        Assert.Equal(17, db.Query("SELECT MigrationId FROM __EFMigrationsHistory").Count);
        Assert.DoesNotContain("Old sample customer", db.Query("SELECT Name FROM Customers"));
        Assert.Equal(3, db.Query("SELECT Id FROM Customers").Count);
        Assert.Empty(db.Query("PRAGMA foreign_key_check"));
    }

    [Fact]
    public async Task An_older_database_that_is_not_marked_as_a_demonstration_database_is_never_reset()
    {
        using var db = new TempDatabase();
        await MigrateToInitialAsync(db);
        db.Execute(Insert(db));
        var bytesBefore = await File.ReadAllBytesAsync(db.Path, Token);

        var refusal = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(
            () => DemoDatabase.ResetAsync(db.Path, PersistenceTestData.Dates, db.NextCredentialPath(), SecurityFixture.FastPolicy, Token));

        Assert.Equal(DemoDatabaseState.Unrecognized, refusal.State);
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(db.Path, Token));
    }
}
