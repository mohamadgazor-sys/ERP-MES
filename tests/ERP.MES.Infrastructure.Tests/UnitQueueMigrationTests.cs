using ERP.MES.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.MES.Infrastructure.Tests;

// The unit-queue migration adds nullable stage columns (a table rebuild in SQLite) and three tables: existing requests, items, stages and QC approvals must survive it exactly.
public class UnitQueueMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Existing_stages_keep_their_data_with_empty_queue_state_and_the_new_tables_start_empty_with_restricting_keys()
    {
        using var db = new TempDatabase();
        await using (var context = db.NewContext())
        {
            var all = context.Database.GetMigrations().ToList();
            var index = all.FindIndex(m => m.EndsWith("_AddUnitQueue", StringComparison.Ordinal));
            Assert.True(index > 0);
            await context.GetService<IMigrator>().MigrateAsync(all[index - 1], Token);
        }

        const string at = "'2026-10-15 09:30:15'";
        db.Execute($"INSERT INTO Users (Id, Username, UsernameKey, DisplayName, DepartmentId, RoleId, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'test.creator', 'TEST.CREATOR', 'Test creator', 1, 3, 1, {at}, {at}, 1)");
        db.Execute($"INSERT INTO Customers (Id, Name, IsActive, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'Old customer', 1, 1, {at}, {at}, 1)");
        db.Execute($"INSERT INTO Requests (Id, CustomerId, RequestNo, WorkType, RequestDate, JalaliRequestYear, Title, Description, Status, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, 'M-1', 1, '2026-10-12', 1405, NULL, NULL, 6, 1, {at}, {at}, 7)");
        db.Execute($"INSERT INTO RequestItems (Id, RequestId, ItemNo, PartName, RequestedQuantity, Priority, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 1, '1.10', 'Ring', 2.5, 1, 1, {at}, {at}, 3)");
        db.Execute("INSERT INTO ItemStages (Id, RequestItemId, StageNo, StageTitle, PlannedMinutesPerPiece, PlannedQuantity, Status, Version) VALUES (1, 1, 1, 'Turning', 4.5, 2.5, 3, 2)");
        db.Execute($"INSERT INTO QcApprovals (RequestItemId, StageNo, ApprovedQuantity, ApprovedByUserId, ApprovedAtUtc) VALUES (1, 1, 2.5, 1, {at})");

        await SqliteDatabase.MigrateAsync(db.Path, Token);

        Assert.Equal("1|1|Turning|4.5|2.5|3|2", Assert.Single(db.Query("SELECT Id, StageNo, StageTitle, PlannedMinutesPerPiece, PlannedQuantity, Status, Version FROM ItemStages")));
        Assert.Equal("||||", Assert.Single(db.Query("SELECT IFNULL(PlanningPosition,''), IFNULL(AvailableSinceUtc,''), IFNULL(StoppedAtUtc,''), IFNULL(StopReason,''), IFNULL(StoppedByUserId,'') FROM ItemStages")));
        Assert.Equal("1|1.10|Ring|2.5|3", Assert.Single(db.Query("SELECT Id, ItemNo, PartName, RequestedQuantity, Version FROM RequestItems")));
        Assert.Equal("1|1|2.5", Assert.Single(db.Query("SELECT RequestItemId, StageNo, ApprovedQuantity FROM QcApprovals")));
        Assert.Empty(db.Query("SELECT * FROM QueueOverrides"));
        Assert.Empty(db.Query("SELECT * FROM QueueOverrideHistory"));
        Assert.Empty(db.Query("SELECT * FROM UserUnitDesignations"));
        Assert.Empty(db.Query("PRAGMA foreign_key_check"));
        Assert.Equal(18, db.Query("SELECT MigrationId FROM __EFMigrationsHistory").Count);

        var keys = new List<string>();
        foreach (var table in new[] { "QueueOverrides", "QueueOverrideHistory", "UserUnitDesignations" })
        {
            keys.AddRange(db.Query($"SELECT '{table}:' || \"from\" || '->' || \"table\" || ':' || on_delete FROM pragma_foreign_key_list('{table}')"));
        }

        Assert.All(keys, k => Assert.EndsWith(":RESTRICT", k, StringComparison.Ordinal));
        Assert.Contains("QueueOverrides:ProductionUnitId->ProductionUnits:RESTRICT", keys);
        Assert.Contains("UserUnitDesignations:UserId->Users:RESTRICT", keys);
        Assert.Contains("QueueOverrides:RequestItemId->ItemStages:RESTRICT", keys);

        // One override row per stage and one designation per user and unit.
        Assert.Contains("RequestItemId,StageNo", db.Query("SELECT group_concat(c.name, ',') FROM pragma_index_list('QueueOverrides') l, pragma_index_info(l.name) c WHERE l.\"unique\" = 1 GROUP BY l.name"));
        Assert.Contains("UserId,ProductionUnitId", db.Query("SELECT group_concat(c.name, ',') FROM pragma_index_list('UserUnitDesignations') l, pragma_index_info(l.name) c WHERE l.\"unique\" = 1 GROUP BY l.name"));
        await using var check = db.NewContext();
        Assert.False(check.Database.HasPendingModelChanges());
    }
}
