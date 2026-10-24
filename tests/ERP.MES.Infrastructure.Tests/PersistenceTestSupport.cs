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

    /// <summary>
    /// Brings the database to the latest committed migration. The first call of a test run applies every migration to one template file; later calls copy that file, which gives the same database
    /// (schema, seeded rows, migration history) without applying the migrations again for every test - on the Windows CI runner that was the dominant per-test cost. A database that already exists
    /// (a second call) is migrated in place, which is idempotent. Tests of the migrations themselves open their own context and never use this shortcut.
    /// </summary>
    public async Task MigrateAsync()
    {
        if (File.Exists(Path))
        {
            await SqliteDatabase.MigrateAsync(Path);
            return;
        }

        var template = await MigratedTemplate.GetAsync();
        if (template is null)
        {
            await SqliteDatabase.MigrateAsync(Path);
            return;
        }

        File.Copy(template, Path, overwrite: false);
    }

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

/// <summary>One migrated SQLite file per test run, created on first use and deleted when the process exits. Null (callers migrate themselves) if the file is not self-contained.</summary>
internal static class MigratedTemplate
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _path;
    private static bool _unusable;

    public static async Task<string?> GetAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_unusable)
            {
                return null;
            }

            if (_path is not null)
            {
                return _path;
            }

            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "erpmes-test-template-" + Guid.NewGuid().ToString("N") + ".db");
            await SqliteDatabase.MigrateAsync(path);

            // A copy of the main file is only the same database when no journal or write-ahead log is left behind.
            if (File.Exists(path + "-wal") || File.Exists(path + "-journal"))
            {
                _unusable = true;
                return null;
            }

            AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(path);
            _path = path;
            return path;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static void TryDelete(string path)
    {
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            try
            {
                File.Delete(path + suffix);
            }
            catch (IOException)
            {
                // A leftover temp file does not affect the result.
            }
        }
    }
}
