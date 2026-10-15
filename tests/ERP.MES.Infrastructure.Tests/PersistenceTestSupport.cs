using ERP.MES.Infrastructure.Persistence;
using ERP.MES.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Tests;

/// <summary>A real SQLite database file in the temp folder, deleted afterwards. Every context opens its own connection (no pooling).</summary>
internal sealed class TempDatabase : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "erpmes-test-" + Guid.NewGuid().ToString("N") + ".db");

    public bool Exists => File.Exists(Path);

    private readonly List<string> _credentialFiles = [];

    /// <summary>A new, unique path for a demonstration credential file in the temp folder (outside the repository); removed with the database.</summary>
    public string NextCredentialPath()
    {
        var path = Path + "." + _credentialFiles.Count + ".credentials.txt";
        _credentialFiles.Add(path);
        return path;
    }

    public IReadOnlyList<string> CredentialFiles => _credentialFiles;

    public ErpMesDbContext NewContext() => new(SqliteDatabase.CreateOptions(Path));

    public Task MigrateAsync() => SqliteDatabase.MigrateAsync(Path);

    /// <summary>The user ids that the persistence tests use as creator (1) and QC approver (9).</summary>
    public const long TestCreatorId = 1;

    public const long TestApproverId = 9;

    /// <summary>
    /// Migrates and adds two ordinary user rows with the ids the persistence tests use as creator and approver. Since creators and approvers are real foreign keys
    /// (Decision Log #70), a record needs an existing user. These rows exist only in throwaway test databases; the product creates users only through the account use
    /// cases, and no sentinel id is ever inserted by product code.
    /// </summary>
    public async Task MigrateWithActorsAsync()
    {
        await MigrateAsync();
        Execute(
            "INSERT OR IGNORE INTO Users (Id, Username, UsernameKey, DisplayName, DepartmentId, RoleId, IsActive, CreatedAtUtc, UpdatedAtUtc, Version) VALUES "
            + $"({TestCreatorId}, 'test.creator', 'TEST.CREATOR', 'Test creator', 1, 3, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1), "
            + $"({TestApproverId}, 'test.approver', 'TEST.APPROVER', 'Test approver', 7, 3, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1)");
    }

    public IDbContextFactory<ErpMesDbContext> ContextFactory => SqliteDatabase.CreateContextFactory(Path);

    /// <summary>A new repository instance (and so new contexts) on every call, as after a restart.</summary>
    public EfCustomerRepository Customers() => new(ContextFactory);

    public EfRequestRepository Requests() => new(ContextFactory);

    public SqliteConnection OpenRaw()
    {
        var connection = new SqliteConnection(SqliteDatabase.BuildConnectionString(Path));
        connection.Open();
        return connection;
    }

    public List<string> Query(string sql)
    {
        using var connection = OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            var cells = new string[reader.FieldCount];
            for (var i = 0; i < cells.Length; i++)
            {
                cells[i] = Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            }

            rows.Add(string.Join("|", cells));
        }

        return rows;
    }

    public int Execute(string sql)
    {
        using var connection = OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        foreach (var file in _credentialFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // A leftover temp file does not affect the result.
            }
        }

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            try
            {
                File.Delete(Path + suffix);
            }
            catch (IOException)
            {
                // A leftover temp file does not affect the result.
            }
        }
    }
}
