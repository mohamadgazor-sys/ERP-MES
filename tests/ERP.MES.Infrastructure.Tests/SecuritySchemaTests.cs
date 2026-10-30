using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Tests;

// The authentication tables created by the committed migration (Milestone 1 plan 3.4, 5.3, 6.2).
public class SecuritySchemaTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_departments_and_roles_are_the_development_seed()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();

        Assert.Equal(
            new[] { "CENTRAL", "DRAWING", "MAINTENANCE_PM", "MANAGEMENT", "PLANNING", "PRODUCTION", "PRODUCTION_MANAGER", "QC", "SYSTEM", "WAREHOUSE" },
            db.Query("SELECT Code FROM Departments ORDER BY Code"));
        Assert.Equal(new[] { "ADMIN", "MANAGER", "STAFF" }, db.Query("SELECT Code FROM Roles ORDER BY Code"));
        Assert.Empty(db.Query("SELECT Id FROM Departments WHERE Name = '' OR Name IS NULL"));
        Assert.Equal(ReferenceDataSeed.Departments.Select(d => d.Name), db.Query("SELECT Name FROM Departments ORDER BY Id"));
    }

    [Fact]
    public async Task There_is_exactly_one_setup_row_and_a_second_one_is_refused_by_the_database()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();

        Assert.Equal(new[] { "1|NotProvisioned|0|0|1" }, db.Query("SELECT Id || '|' || State || '|' || BootstrapFailedAttempts || '|' || AdminChangeStamp || '|' || Version FROM SystemSetup"));
        Assert.Throws<SqliteException>(() => db.Execute(
            "INSERT INTO SystemSetup (Id, State, BootstrapFailedAttempts, AdminChangeStamp, RecoveryFailedAttempts, Version) VALUES (2, 'NotProvisioned', 0, 0, 0, 1)"));
    }

    [Fact]
    public async Task Only_hash_salt_algorithm_version_and_work_factor_are_stored_for_a_password()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();

        const string Credential = "(name LIKE '%assword%' OR name LIKE '%ecret%' OR name LIKE '%oken%' OR name LIKE '%ash%' OR name LIKE '%alt%') AND name <> 'MustChangePassword'";
        var credentialColumns = db.Query($"SELECT name FROM pragma_table_info('UserCredentials') WHERE {Credential} ORDER BY name");

        Assert.Equal(new[] { "PasswordAlgorithm", "PasswordHash", "PasswordHashVersion", "PasswordSalt", "PasswordWorkFactor" }, credentialColumns);

        // The Domain user table holds nothing about credentials (Decision Log #61).
        Assert.Empty(db.Query($"SELECT name FROM pragma_table_info('Users') WHERE {Credential}"));
    }

    [Fact]
    public async Task Names_and_references_are_unique_and_every_foreign_key_restricts_deletion()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();

        Assert.Equal(new[] { "UsernameKey" }, UniqueIndexColumns(db, "Users"));
        Assert.Equal(new[] { "Code" }, UniqueIndexColumns(db, "Departments"));
        Assert.Equal(new[] { "Code" }, UniqueIndexColumns(db, "Roles"));

        var keys = new List<string>();
        foreach (var table in new[] { "Users", "UserCredentials", "AuditLog", "SystemSetup" })
        {
            keys.AddRange(db.Query($"SELECT '{table}' || ':' || \"from\" || '->' || \"table\" || ':' || on_delete FROM pragma_foreign_key_list('{table}')"));
        }

        Assert.Equal(
            new[]
            {
                "AuditLog:UserId->Users:RESTRICT",
                "SystemSetup:RecoveryTargetUserId->Users:RESTRICT",
                "UserCredentials:UserId->Users:RESTRICT",
                "Users:DepartmentId->Departments:RESTRICT",
                "Users:RoleId->Roles:RESTRICT",
            },
            keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Two_names_with_the_same_canonical_key_are_refused_by_the_database()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        const string Insert =
            "INSERT INTO Users (Username, UsernameKey, DisplayName, DepartmentId, RoleId, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES ('{0}', '{1}', 'X', 1, 3, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1)";

        Assert.Equal(1, db.Execute(string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, "Alice", Username.Create("Alice").Key)));

        // "alice" has the same key as "Alice": the unique index on the key rejects it, whatever the application did before.
        Assert.Throws<SqliteException>(() => db.Execute(string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, "alice", Username.Create("alice").Key)));
        Assert.Equal(1, db.Execute(string.Format(System.Globalization.CultureInfo.InvariantCulture, Insert, "Alicia", Username.Create("Alicia").Key)));
    }

    [Fact]
    public async Task The_audit_log_is_append_only()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        await using (var context = db.NewContext())
        {
            context.AuditLog.Add(new AuditLogEntry(new DateTime(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc), null, "PC", "operator", null, AuditActions.LoginFailed, "User", null, "first"));
            await context.SaveChangesAsync(Token);
        }

        await using (var context = db.NewContext())
        {
            var entry = await context.AuditLog.SingleAsync(Token);
            context.Entry(entry).Property(e => e.Summary).CurrentValue = "changed";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Token));
        }

        await using (var context = db.NewContext())
        {
            context.AuditLog.Remove(await context.AuditLog.SingleAsync(Token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Token));
        }

        Assert.Equal(new[] { "first" }, db.Query("SELECT Summary FROM AuditLog"));
    }

    [Fact]
    public async Task Users_and_the_setup_row_carry_the_row_version_and_a_stale_update_is_rejected()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();

        await using (var first = db.NewContext())
        await using (var second = db.NewContext())
        {
            var a = await first.SystemSetups.SingleAsync(Token);
            var b = await second.SystemSetups.SingleAsync(Token);
            a.AdvanceAdminChangeStamp();
            b.AdvanceAdminChangeStamp();

            await first.SaveChangesAsync(Token);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));
            Assert.Equal(2L, first.GetRowVersion(a));
        }

        Assert.Equal(new[] { "1|2" }, db.Query("SELECT AdminChangeStamp || '|' || Version FROM SystemSetup"));
    }

    private static string[] UniqueIndexColumns(TempDatabase db, string table)
    {
        var result = new List<string>();
        foreach (var index in db.Query($"SELECT name FROM pragma_index_list('{table}') WHERE \"unique\" = 1 AND origin = 'c'"))
        {
            result.Add(string.Join(",", db.Query($"SELECT name FROM pragma_index_info('{index}') ORDER BY seqno")));
        }

        return result.ToArray();
    }
}
