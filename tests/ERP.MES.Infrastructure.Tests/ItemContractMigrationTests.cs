using ERP.MES.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.MES.Infrastructure.Tests;

// The item contract migration only adds an empty table: existing requests, items and request-level contracts are not reinterpreted (Decision Log #111).
public class ItemContractMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Existing_request_contracts_survive_and_the_item_decisions_start_empty_so_every_item_still_follows_its_request()
    {
        using var db = new TempDatabase();
        await using (var context = db.NewContext())
        {
            var all = context.Database.GetMigrations().ToList();
            var index = all.FindIndex(m => m.EndsWith("_AddItemContractDecisions", StringComparison.Ordinal));
            Assert.True(index > 0);
            await context.GetService<IMigrator>().MigrateAsync(all[index - 1], Token);
        }

        const string at = "'2026-10-15 09:30:15'";
        db.Execute($"INSERT INTO Users (Id, Username, UsernameKey, DisplayName, DepartmentId, RoleId, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'test.creator', 'TEST.CREATOR', 'Test creator', 1, 3, 1, {at}, {at}, 1)");
        db.Execute($"INSERT INTO Customers (Id, Name, IsActive, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'Old customer', 1, 1, {at}, {at}, 1)");
        db.Execute($"INSERT INTO Requests (Id, CustomerId, RequestNo, WorkType, RequestDate, JalaliRequestYear, Title, Description, Status, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, 'M-1', 1, '2026-10-12', 1405, NULL, NULL, 6, 1, {at}, {at}, 7)");
        db.Execute($"INSERT INTO RequestItems (Id, RequestId, ItemNo, PartName, RequestedQuantity, Priority, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, '1.10', 'Ring', 2.5, 1, 1, {at}, {at}, 3)");
        db.Execute($"INSERT INTO Contracts (Id, RequestId, Requirement, Status, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, 2, 4, {at}, {at}, 5)");

        await SqliteDatabase.MigrateAsync(db.Path, Token);

        Assert.Equal("1|2|4|5", Assert.Single(db.Query("SELECT Id, Requirement, Status, Version FROM Contracts")));
        Assert.Equal("1|1.10", Assert.Single(db.Query("SELECT Id, ItemNo FROM RequestItems")));
        Assert.Empty(db.Query("SELECT * FROM ItemContractDecisions"));
        Assert.Empty(db.Query("PRAGMA foreign_key_check"));
        var keys = db.Query("SELECT \"from\" || '->' || \"table\" || ':' || on_delete FROM pragma_foreign_key_list('ItemContractDecisions')");
        Assert.All(keys, k => Assert.EndsWith(":RESTRICT", k, StringComparison.Ordinal));
        Assert.Equal(3, keys.Count);

        await using var check = db.NewContext();
        Assert.False(check.Database.HasPendingModelChanges());
    }
}
