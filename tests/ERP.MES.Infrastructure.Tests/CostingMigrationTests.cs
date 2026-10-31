using ERP.MES.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.MES.Infrastructure.Tests;

// The costing migration only adds three tables: existing data must survive it exactly and the keys must restrict deletes.
public class CostingMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Existing_data_survives_and_the_costing_tables_start_empty_with_restricting_keys_and_unique_numbering()
    {
        using var db = new TempDatabase();
        await using (var context = db.NewContext())
        {
            var all = context.Database.GetMigrations().ToList();
            var index = all.FindIndex(m => m.EndsWith("_AddCosting", StringComparison.Ordinal));
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
        Assert.Empty(db.Query("SELECT * FROM ItemCostCalculations"));
        Assert.Empty(db.Query("SELECT * FROM ItemCostLines"));
        Assert.Empty(db.Query("SELECT * FROM ItemPriceDecisions"));
        Assert.Empty(db.Query("PRAGMA foreign_key_check"));
        Assert.Equal(30, db.Query("SELECT MigrationId FROM __EFMigrationsHistory").Count);

        var keys = new List<string>();
        foreach (var table in new[] { "ItemCostCalculations", "ItemCostLines", "ItemPriceDecisions" })
        {
            keys.AddRange(db.Query($"SELECT '{table}:' || \"from\" || '->' || \"table\" || ':' || on_delete FROM pragma_foreign_key_list('{table}')"));
        }

        Assert.All(keys, k => Assert.EndsWith(":RESTRICT", k, StringComparison.Ordinal));
        Assert.Contains("ItemCostCalculations:RequestItemId->RequestItems:RESTRICT", keys);
        Assert.Contains("ItemCostLines:CalculationId->ItemCostCalculations:RESTRICT", keys);
        Assert.Contains("ItemPriceDecisions:CalculationId->ItemCostCalculations:RESTRICT", keys);

        // Snapshots are numbered once per item, and a snapshot has one decision.
        Assert.Contains("RequestItemId,SequenceNo", db.Query("SELECT group_concat(c.name, ',') FROM pragma_index_list('ItemCostCalculations') l, pragma_index_info(l.name) c WHERE l.\"unique\" = 1 GROUP BY l.name"));
        Assert.Contains("CalculationId", db.Query("SELECT group_concat(c.name, ',') FROM pragma_index_list('ItemPriceDecisions') l, pragma_index_info(l.name) c WHERE l.\"unique\" = 1 GROUP BY l.name"));

        await using var check = db.NewContext();
        Assert.False(check.Database.HasPendingModelChanges());
    }
}
