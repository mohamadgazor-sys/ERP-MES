using ERP.MES.Domain.Calendar;
using ERP.MES.Infrastructure.Demo;
using ERP.MES.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace ERP.MES.Infrastructure.Sqlite.Demo;

/// <summary>What <see cref="DemoDatabase.Inspect"/> found at a path.</summary>
public enum DemoDatabaseState
{
    /// <summary>No file exists there.</summary>
    Missing,

    /// <summary>A file of length zero: nothing in it can be lost.</summary>
    EmptyFile,

    /// <summary>A SQLite database carrying the demonstration marker.</summary>
    Demo,

    /// <summary>A SQLite database without the demonstration marker (possibly a real database).</summary>
    Unrecognized,

    /// <summary>A file or folder that SQLite cannot open as a database.</summary>
    NotADatabase,
}

/// <summary>The requested demonstration operation was refused because the target is not a positively identified demonstration database.</summary>
public sealed class DemoDatabaseRefusedException(DemoDatabaseState state, string message) : InvalidOperationException(message)
{
    public DemoDatabaseState State { get; } = state;
}

/// <summary>
/// The only entry point that creates, seeds or resets the local DEMONSTRATION database (docs/FOUR_DAY_DEMO_PLAN.md).
///
/// A database is a demonstration database only if it carries the marker: the SQLite file header field <c>application_id</c> holds
/// <see cref="ApplicationId"/>. The marker is written by <see cref="InitializeAsync"/> only on a database it creates itself (no file, or a
/// zero-length file), so no existing database can be turned into a demonstration database by this code. Anything else - an unmarked database, a
/// file that is not a database, a folder - is refused with the reason, and nothing is changed. (The marker is a header field rather than a table
/// so the accepted migration is untouched; see docs/DEMO_SEED_AND_RESET.md.)
///
/// The schema always comes from the committed EF Core migrations. Initialising or resetting builds the complete database (migrations, marker,
/// seed) in a temporary file next to the target and moves it into place only when that succeeded, so a failure leaves the previous database as it
/// was. Nothing here runs at application start-up; reset is an explicit call.
/// </summary>
public static class DemoDatabase
{
    /// <summary>ASCII "ERMD" (ERp Mes Demo) as a 32-bit integer; the value stored in the SQLite header field application_id.</summary>
    public const int ApplicationId = 0x45524D44;

    /// <summary>Looks at the target without changing it (the file is opened read-only, and never created).</summary>
    public static DemoDatabaseState Inspect(string databasePath)
    {
        var path = FullPath(databasePath);
        if (Directory.Exists(path))
        {
            return DemoDatabaseState.NotADatabase;
        }

        if (!File.Exists(path))
        {
            return DemoDatabaseState.Missing;
        }

        if (new FileInfo(path).Length == 0)
        {
            return DemoDatabaseState.EmptyFile;
        }

        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        try
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA application_id;";
            var value = Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
            return value == ApplicationId ? DemoDatabaseState.Demo : DemoDatabaseState.Unrecognized;
        }
        catch (SqliteException)
        {
            return DemoDatabaseState.NotADatabase;
        }
    }

    /// <summary>
    /// Creates the demonstration database when there is none (or only an empty file), or - when the target already is a marked demonstration database -
    /// applies pending migrations and adds whatever sample data is missing, without changing or duplicating what exists. Any other target is refused.
    /// </summary>
    public static async Task<DemoSeedSummary> InitializeAsync(string databasePath, IJalaliDateService dates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dates);
        var path = FullPath(databasePath);

        switch (Inspect(path))
        {
            case DemoDatabaseState.Missing:
            case DemoDatabaseState.EmptyFile:
                return await BuildAndInstallAsync(path, dates, replacesDemoDatabase: false, cancellationToken);

            case DemoDatabaseState.Demo:
                await SqliteDatabase.MigrateAsync(path, cancellationToken);
                return await SeedAsync(path, dates, cancellationToken);

            case var state:
                throw Refused(path, state, "initialise");
        }
    }

    /// <summary>
    /// Replaces a marked demonstration database by a freshly migrated, freshly seeded one. Refused - with the reason, changing nothing - unless the
    /// target already is a positively identified demonstration database. A missing target is refused too: reset never creates a database
    /// (use <see cref="InitializeAsync"/>).
    /// </summary>
    public static async Task<DemoSeedSummary> ResetAsync(string databasePath, IJalaliDateService dates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dates);
        var path = FullPath(databasePath);

        var state = Inspect(path);
        if (state != DemoDatabaseState.Demo)
        {
            throw Refused(path, state, "reset");
        }

        return await BuildAndInstallAsync(path, dates, replacesDemoDatabase: true, cancellationToken);
    }

    private static async Task<DemoSeedSummary> BuildAndInstallAsync(string path, IJalaliDateService dates, bool replacesDemoDatabase, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // A unique name of our own, so nothing that already exists is touched while the new database is built.
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await SqliteDatabase.MigrateAsync(temporaryPath, cancellationToken);
            WriteMarker(temporaryPath);
            var summary = await SeedAsync(temporaryPath, dates, cancellationToken);

            // Journal files of the demonstration database being replaced must not be applied to the new file. They are removed only for a
            // positively identified demonstration database, and only now that the replacement is complete.
            if (replacesDemoDatabase)
            {
                DeleteSidecars(path);
            }

            File.Move(temporaryPath, path, overwrite: true);
            return summary;
        }
        finally
        {
            DeleteIfExists(temporaryPath);
            DeleteSidecars(temporaryPath);
        }
    }

    private static Task<DemoSeedSummary> SeedAsync(string path, IJalaliDateService dates, CancellationToken cancellationToken)
    {
        var contexts = SqliteDatabase.CreateContextFactory(path);
        var seeder = new DemoDataSeeder(new EfCustomerRepository(contexts), new EfRequestRepository(contexts), dates);
        return seeder.SeedAsync(cancellationToken);
    }

    private static void WriteMarker(string path)
    {
        using var connection = new SqliteConnection(SqliteDatabase.BuildConnectionString(path));
        connection.Open();
        using var command = connection.CreateCommand();

        // The value is a compile-time constant, not user input.
        command.CommandText = "PRAGMA application_id = " + ApplicationId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";";
        command.ExecuteNonQuery();
    }

    private static DemoDatabaseRefusedException Refused(string path, DemoDatabaseState state, string operation)
    {
        var reason = state switch
        {
            DemoDatabaseState.Missing => "no database exists there; reset never creates one (use the initialise operation)",
            DemoDatabaseState.Unrecognized => "it is a SQLite database without the demonstration marker, so it may be a real database",
            DemoDatabaseState.NotADatabase => "it is not a SQLite database file, or it could not be opened read-only",
            _ => "it is not a positively identified demonstration database",
        };

        return new DemoDatabaseRefusedException(
            state,
            "Refused to " + operation + " '" + path + "': " + reason + ". Nothing was changed.");
    }

    private static string FullPath(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        return Path.GetFullPath(databasePath);
    }

    private static void DeleteSidecars(string path)
    {
        foreach (var suffix in new[] { "-journal", "-wal", "-shm" })
        {
            DeleteIfExists(path + suffix);
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover temporary file does not affect the result; the replacement itself reports its own failure.
        }
    }
}
