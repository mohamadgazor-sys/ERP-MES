using ERP.MES.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.MES.Infrastructure.Tests;

// The import-log migration only adds two tables: existing requests, items and stages must survive it untouched.
public class ItemImportMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Existing_data_survives_and_the_log_tables_start_empty_with_restricting_keys()
    {
        using var db = new TempDatabase();
        await using (var context = db.NewContext())
        {
            var all = context.Database.GetMigrations().ToList();
            var index = all.FindIndex(m => m.EndsWith("_AddItemImportLog", StringComparison.Ordinal));
            Assert.True(index > 0);
            await context.GetService<IMigrator>().MigrateAsync(all[index - 1], Token);
        }

        const string at = "'2026-10-15 09:30:15'";
        db.Execute($"INSERT INTO Users (Id, Username, UsernameKey, DisplayName, DepartmentId, RoleId, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'test.creator', 'TEST.CREATOR', 'Test creator', 1, 3, 1, {at}, {at}, 1)");
        db.Execute($"INSERT INTO Customers (Id, Name, IsActive, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'Old customer', 1, 1, {at}, {at}, 1)");
        db.Execute($"INSERT INTO Requests (Id, CustomerId, RequestNo, WorkType, RequestDate, JalaliRequestYear, Title, Description, Status, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, 'M-1', 1, '2026-10-12', 1405, NULL, NULL, 6, 1, {at}, {at}, 7)");
        db.Execute($"INSERT INTO RequestItems (Id, RequestId, ItemNo, PartName, RequestedQuantity, Priority, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, '1.10', 'Ring', 2.5, 1, 1, {at}, {at}, 3)");

        await SqliteDatabase.MigrateAsync(db.Path, Token);

        Assert.Equal("1|1.10|Ring|2.5|3", Assert.Single(db.Query("SELECT Id, ItemNo, PartName, RequestedQuantity, Version FROM RequestItems")));
        Assert.Equal("1|M-1|6|7", Assert.Single(db.Query("SELECT Id, RequestNo, Status, Version FROM Requests")));
        Assert.Empty(db.Query("SELECT * FROM ItemImports"));
        Assert.Empty(db.Query("SELECT * FROM ItemImportRows"));
        Assert.Empty(db.Query("PRAGMA foreign_key_check"));
        Assert.Equal(21, db.Query("SELECT MigrationId FROM __EFMigrationsHistory").Count);

        var keys = db.Query("SELECT 'ItemImports:' || \"from\" || '->' || \"table\" || ':' || on_delete FROM pragma_foreign_key_list('ItemImports') UNION ALL SELECT 'ItemImportRows:' || \"from\" || '->' || \"table\" || ':' || on_delete FROM pragma_foreign_key_list('ItemImportRows')");
        Assert.Equal(
            ["ItemImportRows:ItemImportId->ItemImports:RESTRICT", "ItemImports:ImportedByUserId->Users:RESTRICT", "ItemImports:RequestId->Requests:RESTRICT"],
            keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        await using var check = db.NewContext();
        Assert.False(check.Database.HasPendingModelChanges());
    }
}
