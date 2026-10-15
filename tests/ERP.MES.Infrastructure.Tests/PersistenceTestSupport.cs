using ERP.MES.Infrastructure.Persistence;
using ERP.MES.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;

namespace ERP.MES.Infrastructure.Tests;

/// <summary>A real SQLite database file in the temp folder, deleted afterwards. Every context opens its own connection (no pooling).</summary>
internal sealed class TempDatabase : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "erpmes-test-" + Guid.NewGuid().ToString("N") + ".db");

    public bool Exists => File.Exists(Path);

    public ErpMesDbContext NewContext() => new(SqliteDatabase.CreateOptions(Path));

    public Task MigrateAsync() => SqliteDatabase.MigrateAsync(Path);

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
