using ERP.MES.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.MES.Infrastructure.Tests;

// The overtime migration only adds two tables: existing data must survive it exactly and the keys must restrict deletes.
public class OvertimeMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Existing_data_survives_and_the_overtime_tables_start_empty_with_restricting_keys_and_a_single_limits_row()
    {
        using var db = new TempDatabase();
        await using (var context = db.NewContext())
        {
            var all = context.Database.GetMigrations().ToList();
            var index = all.FindIndex(m => m.EndsWith("_AddOvertime", StringComparison.Ordinal));
            Assert.True(index > 0);
            await context.GetService<IMigrator>().MigrateAsync(all[index - 1], Token);
        }

        const string at = "'2026-10-15 09:30:15'";
        db.Execute($"INSERT INTO Users (Id, Username, UsernameKey, DisplayName, DepartmentId, RoleId, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'test.creator', 'TEST.CREATOR', 'Test creator', 1, 3, 1, {at}, {at}, 1)");
        db.Execute($"INSERT INTO ProductionUnits (Id, Code, Name, IsActive, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'MILLING', 'Milling', 1, 1, {at}, {at}, 1)");
        db.Execute($"INSERT INTO UserUnitDesignations (UserId, ProductionUnitId, AssignedByUserId, AssignedAtUtc, IsSupervisor, Version) VALUES (1, 1, 1, {at}, 1, 1)");

        await SqliteDatabase.MigrateAsync(db.Path, Token);

        Assert.Equal("1|1|1", Assert.Single(db.Query("SELECT UserId || '|' || ProductionUnitId || '|' || IsSupervisor FROM UserUnitDesignations")));
        Assert.Empty(db.Query("SELECT * FROM OvertimeAssignments"));
        Assert.Empty(db.Query("SELECT * FROM OvertimeLimits"));
        Assert.Empty(db.Query("PRAGMA foreign_key_check"));
        Assert.Equal(27, db.Query("SELECT MigrationId FROM __EFMigrationsHistory").Count);

        var keys = new List<string>();
        foreach (var table in new[] { "OvertimeAssignments", "OvertimeLimits" })
        {
            keys.AddRange(db.Query($"SELECT '{table}:' || \"from\" || '->' || \"table\" || ':' || on_delete FROM pragma_foreign_key_list('{table}')"));
        }

        Assert.All(keys, k => Assert.EndsWith(":RESTRICT", k, StringComparison.Ordinal));
        Assert.Contains("OvertimeAssignments:OperatorId->Operators:RESTRICT", keys);
        Assert.Contains("OvertimeAssignments:MachineId->Machines:RESTRICT", keys);
        Assert.Contains("OvertimeLimits:ChangedByUserId->Users:RESTRICT", keys);

        // The limits table holds one row at most.
        db.Execute($"INSERT INTO OvertimeLimits (Id, MaxMinutesPerDay, MaxMinutesPerWeek, ChangedByUserId, ChangedAtUtc, Version) VALUES (1, 120, 480, 1, {at}, 1)");
        Assert.ThrowsAny<Exception>(() => db.Execute($"INSERT INTO OvertimeLimits (Id, MaxMinutesPerDay, MaxMinutesPerWeek, ChangedByUserId, ChangedAtUtc, Version) VALUES (2, 120, 480, 1, {at}, 1)"));

        await using var check = db.NewContext();
        Assert.False(check.Database.HasPendingModelChanges());
    }
}
