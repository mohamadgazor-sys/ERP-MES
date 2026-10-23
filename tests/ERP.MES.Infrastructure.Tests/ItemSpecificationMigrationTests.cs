using ERP.MES.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.MES.Infrastructure.Tests;

// The item-specification migration adds nullable columns and foreign keys to items and stages: existing requests, items, stages and QC approvals must survive it exactly.
public class ItemSpecificationMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Existing_items_stages_and_qc_approvals_survive_with_empty_specifications()
    {
        using var db = new TempDatabase();
        await using (var context = db.NewContext())
        {
            var all = context.Database.GetMigrations().ToList();
            var index = all.FindIndex(m => m.EndsWith("_AddItemSpecifications", StringComparison.Ordinal));
            Assert.True(index > 0);
            await context.GetService<IMigrator>().MigrateAsync(all[index - 1], Token);
        }

        const string at = "'2026-10-15 09:30:15'";
        db.Execute($"INSERT INTO Users (Id, Username, UsernameKey, DisplayName, DepartmentId, RoleId, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'test.creator', 'TEST.CREATOR', 'Test creator', 1, 3, 1, {at}, {at}, 1)");
        db.Execute($"INSERT INTO Customers (Id, Name, IsActive, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'Old customer', 1, 1, {at}, {at}, 1)");
        db.Execute($"INSERT INTO Requests (Id, CustomerId, RequestNo, WorkType, RequestDate, JalaliRequestYear, Title, Description, Status, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, 'M-1', 1, '2026-10-12', 1405, NULL, NULL, 6, 1, {at}, {at}, 7)");
        db.Execute($"INSERT INTO RequestItems (Id, RequestId, ItemNo, PartName, RequestedQuantity, Priority, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, '1.10', 'Ring', 2.5, 1, 1, {at}, {at}, 3)");
        db.Execute($"INSERT INTO ItemStages (Id, RequestItemId, StageNo, StageTitle, PlannedMinutesPerPiece, PlannedQuantity, Status, Version) VALUES (1, 1, 1, 'Turning', 4.5, 2.5, 6, 2)");
        db.Execute($"INSERT INTO QcApprovals (RequestItemId, StageNo, ApprovedQuantity, ApprovedByUserId, ApprovedAtUtc) VALUES (1, 1, 2.5, 1, {at})");

        await SqliteDatabase.MigrateAsync(db.Path, Token);

        Assert.Equal("1|1.10|Ring|2.5|3", Assert.Single(db.Query("SELECT Id, ItemNo, PartName, RequestedQuantity, Version FROM RequestItems")));
        Assert.Equal("||||", Assert.Single(db.Query("SELECT IFNULL(QuantityTypeId,''), IFNULL(MaterialId,''), IFNULL(WeightKgPerBaseUnit,''), IFNULL(LengthMm,''), IFNULL(DiameterMm,'') FROM RequestItems")).Replace("|", "|"));
        Assert.Equal("1|1|Turning|4.5|2.5|6|2", Assert.Single(db.Query("SELECT Id, StageNo, StageTitle, PlannedMinutesPerPiece, PlannedQuantity, Status, Version FROM ItemStages")));
        Assert.Equal("1|1|2.5", Assert.Single(db.Query("SELECT RequestItemId, StageNo, ApprovedQuantity FROM QcApprovals")));
        Assert.Equal("1|M-1|6|7", Assert.Single(db.Query("SELECT Id, RequestNo, Status, Version FROM Requests")));
        Assert.Empty(db.Query("PRAGMA foreign_key_check"));
        Assert.Equal(17, db.Query("SELECT MigrationId FROM __EFMigrationsHistory").Count);

        // The composite key from a QC approval to its stage and the item number key still hold.
        Assert.Contains("RequestId,ItemNo", db.Query("SELECT group_concat(c.name, ',') FROM pragma_index_list('RequestItems') l, pragma_index_info(l.name) c WHERE l.\"unique\" = 1 GROUP BY l.name"));
        await using var check = db.NewContext();
        Assert.False(check.Database.HasPendingModelChanges());
    }
}
