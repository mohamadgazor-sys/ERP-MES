using ERP.MES.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ERP.MES.Infrastructure.Tests;

// The contract gate migration is additive: a database from before it keeps all its data and gains the contract tables.
public class ContractMigrationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_existing_database_keeps_its_requests_and_documents_and_gains_empty_contract_tables()
    {
        using var db = new TempDatabase();
        string previous;
        await using (var context = db.NewContext())
        {
            var all = context.Database.GetMigrations().ToList();
            var index = all.FindIndex(m => m.EndsWith("_AddContractGate", StringComparison.Ordinal));
            Assert.True(index > 0);
            previous = all[index - 1];
            await context.GetService<IMigrator>().MigrateAsync(previous, Token);
        }

        db.Execute(
            "INSERT INTO Users (Id, Username, UsernameKey, DisplayName, DepartmentId, RoleId, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES "
            + "(1, 'test.creator', 'TEST.CREATOR', 'Test creator', 1, 3, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1)");
        db.Execute("INSERT INTO Customers (Id, Name, IsActive, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'Old customer', 1, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1)");
        db.Execute(
            "INSERT INTO Requests (Id, CustomerId, RequestNo, WorkType, RequestDate, JalaliRequestYear, Title, Description, Status, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES "
            + "(1, 1, 'M-1', 1, '2026-10-12', 1405, NULL, NULL, 6, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 7)");
        Assert.DoesNotContain("Contracts", db.Query("SELECT name FROM sqlite_master WHERE type = 'table'"));

        await SqliteDatabase.MigrateAsync(db.Path, Token);

        Assert.Equal("1|M-1|6|7", Assert.Single(db.Query("SELECT Id, RequestNo, Status, Version FROM Requests")));
        foreach (var table in new[] { "Contracts", "ContractVersions", "ContractDocumentLinks", "ContractHistory" })
        {
            Assert.Contains(table, db.Query("SELECT name FROM sqlite_master WHERE type = 'table'"));
            Assert.Equal("0", Assert.Single(db.Query("SELECT COUNT(*) FROM " + table)));
        }

        Assert.Empty(db.Query("PRAGMA foreign_key_check"));
        Assert.Equal(28, db.Query("SELECT MigrationId FROM __EFMigrationsHistory").Count);
        await using var check = db.NewContext();
        Assert.False(check.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task One_contract_per_request_is_enforced_by_the_database()
    {
        using var db = new TempDatabase();
        await SqliteDatabase.MigrateAsync(db.Path, Token);
        db.Execute(
            "INSERT INTO Users (Id, Username, UsernameKey, DisplayName, DepartmentId, RoleId, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES "
            + "(1, 'test.creator', 'TEST.CREATOR', 'Test creator', 1, 3, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1)");
        db.Execute("INSERT INTO Customers (Id, Name, IsActive, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 'C', 1, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1)");
        db.Execute(
            "INSERT INTO Requests (Id, CustomerId, RequestNo, WorkType, RequestDate, JalaliRequestYear, Title, Description, Status, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES "
            + "(1, 1, 'M-1', 1, '2026-10-12', 1405, NULL, NULL, 6, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1)");
        const string insert = "INSERT INTO Contracts (RequestId, Requirement, Status, CreatedAtUtc, UpdatedAtUtc, Version) VALUES (1, 2, 2, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1)";
        db.Execute(insert);

        Assert.ThrowsAny<Exception>(() => db.Execute(insert));
    }
}
