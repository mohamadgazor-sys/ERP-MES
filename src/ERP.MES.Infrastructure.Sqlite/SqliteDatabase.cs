using ERP.MES.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Sqlite;

/// <summary>
/// SQLite wiring for <see cref="ErpMesDbContext"/>: connection options and the startup path that applies migrations. The schema is
/// only ever created by EF Core migrations (never by the create-from-model shortcut), and opening an up-to-date database applies nothing.
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

    /// <summary>A factory that creates a new context on the SQLite file for each operation; the repositories use it, one context per call.</summary>
    public static IDbContextFactory<ErpMesDbContext> CreateContextFactory(string databasePath) =>
        new SqliteContextFactory(CreateOptions(databasePath));

    /// <summary>
    /// The migration that makes the actor columns (customer, request and item creators, QC approvers) real foreign keys to the users table (Decision Log #70).
    /// Identified by name so the guard below can run before it is applied.
    /// </summary>
    public const string ActorReferencesMigrationSuffix = "_AddAuthentication";

    /// <summary>
    /// The migration that moves the work type from the items to the request (Decision Log #84). Identified by name so the guard below can run before it is applied.
    /// </summary>
    public const string SingleWorkTypeMigrationSuffix = "_SingleWorkTypePerRequest";

    /// <summary>
    /// Applies pending migrations. On a database that is already up to date this changes nothing. A missing file is created (this is the entry point for
    /// IT and for the demonstration tools); the desktop application must use <see cref="MigrateExistingAsync"/>.
    /// If the pending migrations include the actor foreign keys and the database already holds records whose creator or approver is not a user, the migration is
    /// NOT started: <see cref="ActorReferencesInvalidException"/> explains what is wrong and what to do, and nothing is changed.
    /// </summary>
    public static async Task MigrateAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        await using var context = new ErpMesDbContext(CreateOptions(databasePath));
        var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pending.Any(m => m.EndsWith(ActorReferencesMigrationSuffix, StringComparison.Ordinal)))
        {
            await EnsureActorReferencesAreValidAsync(context, databasePath, cancellationToken);
        }

        if (pending.Any(m => m.EndsWith(SingleWorkTypeMigrationSuffix, StringComparison.Ordinal)))
        {
            await EnsureRequestsHaveOneWorkTypeAsync(context, databasePath, cancellationToken);
        }

        await context.Database.MigrateAsync(cancellationToken);
    }

    /// <summary>
    /// The normal start-up path of the desktop application (Decision Log #69): applies pending migrations to a database file that already exists and never creates
    /// one. A missing file is an error, not a reason to create an empty database.
    /// </summary>
    public static Task MigrateExistingAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        return File.Exists(databasePath)
            ? MigrateAsync(databasePath, cancellationToken)
            : throw new FileNotFoundException("The database file does not exist and is not created by the application: " + Path.GetFullPath(databasePath), databasePath);
    }

    // Counts the records that carry a creator or approver. Before the migration there is no users table, so no such id can refer to a user: every record counts.
    // (The migration creates the users table and the foreign keys together.)
    private static async Task EnsureActorReferencesAreValidAsync(ErpMesDbContext context, string databasePath, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            long records = 0;
            foreach (var table in new[] { "Customers", "Requests", "RequestItems", "QcApprovals" })
            {
                if (!await TableExistsAsync(connection, table, cancellationToken))
                {
                    continue;
                }

                await using var command = connection.CreateCommand();

                // The names come from the constant list above, not from input.
                command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
                records += Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
            }

            if (records > 0)
            {
                throw new ActorReferencesInvalidException(Path.GetFullPath(databasePath), records);
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    // Before the single-work-type migration a request may hold items of both types, or no items at all (then its type cannot be known). Neither is guessed: the
    // migration is not started and the database stays exactly as it was (Decision Log #84).
    private static async Task EnsureRequestsHaveOneWorkTypeAsync(ErpMesDbContext context, string databasePath, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            if (!await TableExistsAsync(connection, "Requests", cancellationToken) || !await TableExistsAsync(connection, "RequestItems", cancellationToken))
            {
                return;
            }

            var mixed = await CountAsync(connection, "SELECT COUNT(*) FROM Requests r WHERE (SELECT COUNT(DISTINCT i.WorkType) FROM RequestItems i WHERE i.RequestId = r.Id) > 1", cancellationToken);
            var withoutItems = await CountAsync(connection, "SELECT COUNT(*) FROM Requests r WHERE NOT EXISTS (SELECT 1 FROM RequestItems i WHERE i.RequestId = r.Id)", cancellationToken);
            if (mixed + withoutItems > 0)
            {
                throw new RequestWorkTypeInvalidException(Path.GetFullPath(databasePath), mixed, withoutItems);
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task<long> CountAsync(System.Data.Common.DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<bool> TableExistsAsync(System.Data.Common.DbConnection connection, string table, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$name";
        parameter.Value = table;
        command.Parameters.Add(parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private sealed class SqliteContextFactory(DbContextOptions<ErpMesDbContext> options) : IDbContextFactory<ErpMesDbContext>
    {
        public ErpMesDbContext CreateDbContext() => new(options);
    }
}

/// <summary>
/// The database holds records whose creator or approver is not a user, so the actor foreign keys cannot be added (Decision Log #70). This happens to databases
/// from before sign-in existed: their records carry a placeholder user id that never belonged to anyone. No fake account is invented to satisfy the constraint
/// and nothing is deleted or changed; the database is left exactly as it was.
/// </summary>
public sealed class ActorReferencesInvalidException(string databasePath, long recordCount)
    : InvalidOperationException(
        $"The database '{databasePath}' cannot be upgraded: {recordCount} record(s) refer to a creator or approver that is not a user "
        + "(it was created before sign-in existed). Nothing was changed. If this is a demonstration database, replace it with the explicit reset: "
        + "Provisioning 'demo-reset <file> --yes'. Any other database needs a reviewed data repair by its owner.")
{
    public string DatabasePath { get; } = databasePath;

    public long RecordCount { get; } = recordCount;
}

/// <summary>
/// The database holds requests whose work type cannot be derived (Decision Log #84: a request has exactly one type): requests with items of both types, or
/// requests without items. Nothing is guessed, changed or deleted; the database is left exactly as it was.
/// </summary>
public sealed class RequestWorkTypeInvalidException(string databasePath, long mixedRequests, long requestsWithoutItems)
    : InvalidOperationException(
        $"The database '{databasePath}' cannot be upgraded: {mixedRequests} request(s) hold both MAKE and REPAIR items and {requestsWithoutItems} request(s) hold no items, "
        + "so their single work type cannot be determined. Nothing was changed. If this is a demonstration database, replace it with the explicit reset: "
        + "Provisioning 'demo-reset <file> --yes'. Any other database needs a reviewed data repair by its owner (split each mixed request into two requests; give each empty request a type).")
{
    public string DatabasePath { get; } = databasePath;

    public long MixedRequests { get; } = mixedRequests;

    public long RequestsWithoutItems { get; } = requestsWithoutItems;
}
