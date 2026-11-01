using ERP.MES.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.MES.Infrastructure.Tests;

// The warehouse completion migration only adds three tables: existing data must survive it and the keys must restrict deletes and keep one receipt and one mark per item.
public class CompletionMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Existing_data_survives_and_the_tables_start_empty_with_restricting_keys_and_unique_receipt_and_mark_per_item()
    {
        using var db = new TempDatabase();
        await using (var context = db.NewContext())
        {
            var all = context.Database.GetMigrations().ToList();
            var index = all.FindIndex(m => m.EndsWith("_AddWarehouseCompletion", StringComparison.Ordinal));
            Assert.True(index > 0);
            await context.GetService<IMigrator>().MigrateAsync(all[index - 1], Token);
        }

        const string at = "'2026-10-15 09:30:15'";
        db.Execute($"INSERT INTO Users (Id, Username, UsernameKey, DisplayName, DepartmentId, RoleId, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'test.creator', 'TEST.CREATOR', 'Test creator', 1, 3, 1, {at}, {at}, 1)");
        db.Execute($"INSERT INTO Customers (Id, Name, IsActive, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'Old customer', 1, 1, {at}, {at}, 1)");
        db.Execute($"INSERT INTO Requests (Id, CustomerId, RequestNo, WorkType, RequestDate, JalaliRequestYear, Title, Description, Status, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, 'M-1', 1, '2026-10-12', 1405, NULL, NULL, 6, 1, {at}, {at}, 7)");
        db.Execute($"INSERT INTO RequestItems (Id, RequestId, ItemNo, PartName, RequestedQuantity, Priority, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, '1.10', 'Ring', 2.5, 1, 1, {at}, {at}, 3)");

        await SqliteDatabase.MigrateAsync(db.Path, Token);

        Assert.Equal("1|M-1-001|Ring|2.5|3", Assert.Single(db.Query("SELECT Id, ItemNo, PartName, RequestedQuantity, Version FROM RequestItems")));
        Assert.Empty(db.Query("SELECT * FROM WarehouseReceipts"));
        Assert.Empty(db.Query("SELECT * FROM ItemFinishedSent"));
        Assert.Empty(db.Query("SELECT * FROM CustomerDispatches"));
        Assert.Empty(db.Query("PRAGMA foreign_key_check"));
        Assert.Equal(30, db.Query("SELECT MigrationId FROM __EFMigrationsHistory").Count);

        var keys = new List<string>();
        foreach (var table in new[] { "WarehouseReceipts", "ItemFinishedSent", "CustomerDispatches" })
        {
            keys.AddRange(db.Query($"SELECT '{table}:' || \"from\" || '->' || \"table\" || ':' || on_delete FROM pragma_foreign_key_list('{table}')"));
        }

        Assert.All(keys, k => Assert.EndsWith(":RESTRICT", k, StringComparison.Ordinal));
        Assert.Contains("ItemFinishedSent:ReceiptId->WarehouseReceipts:RESTRICT", keys);
        Assert.Contains("ItemFinishedSent:PriceDecisionId->ItemPriceDecisions:RESTRICT", keys);

        // One receipt and one finished mark per item (a duplicate is refused by the database as well).
        db.Execute($"INSERT INTO WarehouseReceipts (Id, RequestItemId, ReceivedBaseUnits, ReceivedByUserId, ReceivedAtUtc) VALUES (1, 1, 5, 1, {at})");
        Assert.ThrowsAny<Exception>(() => db.Execute($"INSERT INTO WarehouseReceipts (Id, RequestItemId, ReceivedBaseUnits, ReceivedByUserId, ReceivedAtUtc) VALUES (2, 1, 5, 1, {at})"));

        await using var check = db.NewContext();
        Assert.False(check.Database.HasPendingModelChanges());
    }
}
