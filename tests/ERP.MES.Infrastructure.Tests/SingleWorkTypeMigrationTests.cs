using ERP.MES.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.MES.Infrastructure.Tests;

// Decision Log #84: the work type moves from the items to the request. A database from before the change is upgraded only when every request has exactly one derivable type.
public class SingleWorkTypeMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // A database as it was before the change: the migrations up to and including AddAuthentication (users exist), with the two ordinary test users.
    private static async Task<TempDatabase> OlderDatabaseAsync()
    {
        var db = new TempDatabase();
        await using (var context = db.NewContext())
        {
            var target = context.Database.GetMigrations().First(m => m.EndsWith(SqliteDatabase.ActorReferencesMigrationSuffix, StringComparison.Ordinal));
            await context.GetService<IMigrator>().MigrateAsync(target, Token);
        }

        db.Execute(
            "INSERT INTO Users (Id, Username, UsernameKey, DisplayName, DepartmentId, RoleId, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES "
            + "(1, 'test.creator', 'TEST.CREATOR', 'Test creator', 1, 3, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1)");
        db.Execute("INSERT INTO Customers (Id, Name, IsActive, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'Old customer', 1, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1)");
        return db;
    }

    private static void AddRequest(TempDatabase db, long id, string requestNo)
    {
        db.Execute(
            "INSERT INTO Requests (Id, CustomerId, RequestNo, RequestDate, JalaliRequestYear, Title, Description, Status, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES "
            + $"({id}, 1, '{requestNo}', '2026-10-12', 1405, NULL, NULL, 1, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1)");
    }

    private static void AddItem(TempDatabase db, long id, long requestId, string itemNo, int workType)
    {
        db.Execute(
            "INSERT INTO RequestItems (Id, RequestId, ItemNo, PartName, WorkType, RequestedQuantity, Priority, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES "
            + $"({id}, {requestId}, '{itemNo}', 'Part {itemNo}', {workType}, 2.5, 1, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1)");
    }

    [Fact]
    public async Task Consistent_requests_take_the_single_type_of_their_items_and_all_data_is_kept()
    {
        using var db = await OlderDatabaseAsync();
        AddRequest(db, 1, "M-1");
        AddItem(db, 1, 1, "1.10", 1);
        AddItem(db, 2, 1, "1.20", 1);
        AddRequest(db, 2, "R-1");
        AddItem(db, 3, 2, "2.10", 2);

        await SqliteDatabase.MigrateAsync(db.Path, Token);

        Assert.Equal(new[] { "1|1", "2|2" }, db.Query("SELECT Id, WorkType FROM Requests ORDER BY Id").ToArray());
        Assert.Equal(new[] { "1.10|1|2.5", "1.20|1|2.5", "2.10|2|2.5" }, db.Query("SELECT ItemNo, RequestId, RequestedQuantity FROM RequestItems ORDER BY Id").ToArray());
        Assert.DoesNotContain("WorkType", db.Query("SELECT name FROM pragma_table_info('RequestItems')"));
        Assert.Contains("WorkType", db.Query("SELECT name FROM pragma_table_info('Requests')"));
        Assert.Empty(db.Query("PRAGMA foreign_key_check"));
        Assert.Equal(12, db.Query("SELECT MigrationId FROM __EFMigrationsHistory").Count);

        // The business keys survive the table rebuild.
        Assert.Contains("CustomerId,RequestNo,JalaliRequestYear", db.Query("SELECT group_concat(c.name, ',') FROM pragma_index_list('Requests') l, pragma_index_info(l.name) c WHERE l.\"unique\" = 1 GROUP BY l.name"));
        await using var context = db.NewContext();
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task A_request_with_both_types_refuses_the_upgrade_and_leaves_the_database_exactly_as_it_was()
    {
        using var db = await OlderDatabaseAsync();
        AddRequest(db, 1, "MIX-1");
        AddItem(db, 1, 1, "1.10", 1);
        AddItem(db, 2, 1, "1.20", 2);
        AddRequest(db, 2, "OK-1");
        AddItem(db, 3, 2, "2.10", 1);
        var bytesBefore = await File.ReadAllBytesAsync(db.Path, Token);

        var refusal = await Assert.ThrowsAsync<RequestWorkTypeInvalidException>(() => SqliteDatabase.MigrateAsync(db.Path, Token));

        Assert.Equal(1, refusal.MixedRequests);
        Assert.Equal(0, refusal.RequestsWithoutItems);
        Assert.Contains("Nothing was changed", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("demo-reset", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(db.Path, Token));
        Assert.Equal(2, db.Query("SELECT MigrationId FROM __EFMigrationsHistory").Count);
        Assert.Contains("WorkType", db.Query("SELECT name FROM pragma_table_info('RequestItems')"));
    }

    [Fact]
    public async Task A_request_without_items_has_no_derivable_type_so_the_upgrade_is_refused_without_changes()
    {
        using var db = await OlderDatabaseAsync();
        AddRequest(db, 1, "EMPTY-1");
        var bytesBefore = await File.ReadAllBytesAsync(db.Path, Token);

        var refusal = await Assert.ThrowsAsync<RequestWorkTypeInvalidException>(() => SqliteDatabase.MigrateAsync(db.Path, Token));

        Assert.Equal(0, refusal.MixedRequests);
        Assert.Equal(1, refusal.RequestsWithoutItems);
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(db.Path, Token));
    }

    [Fact]
    public async Task An_older_database_without_requests_is_upgraded()
    {
        using var db = await OlderDatabaseAsync();

        await SqliteDatabase.MigrateAsync(db.Path, Token);

        Assert.Equal(12, db.Query("SELECT MigrationId FROM __EFMigrationsHistory").Count);
        Assert.Equal("Old customer", db.Query("SELECT Name FROM Customers").Single());
    }
}
