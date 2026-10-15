using ERP.MES.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Sqlite;

/// <summary>
/// SQLite wiring for <see cref="ErpMesDbContext"/>: connection options and the startup path that applies migrations. The schema is
/// only ever created by EF Core migrations (never by EnsureCreated), and opening an up-to-date database applies nothing.
/// </summary>
public static class SqliteDatabase
{
    public static string BuildConnectionString(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,

            // Single-user development database: no connection pool, so the file is released as soon as a context is disposed.
            Pooling = false,
        }.ToString();
    }

    public static DbContextOptions<ErpMesDbContext> CreateOptions(string databasePath) =>
        new DbContextOptionsBuilder<ErpMesDbContext>()
            .UseSqlite(
                BuildConnectionString(databasePath),
                sqlite => sqlite.MigrationsAssembly(typeof(SqliteDatabase).Assembly.GetName().Name))
            .Options;

    /// <summary>Applies pending migrations. On a database that is already up to date this changes nothing.</summary>
    public static async Task MigrateAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        await using var context = new ErpMesDbContext(CreateOptions(databasePath));
        await context.Database.MigrateAsync(cancellationToken);
    }
}
