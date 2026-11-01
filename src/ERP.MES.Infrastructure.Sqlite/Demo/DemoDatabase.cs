using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Infrastructure.Demo;
using ERP.MES.Infrastructure.Persistence;
using ERP.MES.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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

/// <summary>What initialising or resetting did: the sample data summary, how many demonstration accounts were created, and where their one-time credentials were written (null when none were created).</summary>
public sealed record DemoDatabaseResult(DemoSeedSummary Seed, int AccountsCreated, string? CredentialFile);

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
    /// A new database also gets the fictional demonstration accounts; their one-time passwords go only to a protected credential file outside the repository
    /// (<paramref name="credentialFilePath"/>, or a default under the user's local application data), never to the console or a log.
    /// </summary>
    public static async Task<DemoDatabaseResult> InitializeAsync(
        string databasePath,
        IJalaliDateService dates,
        string? credentialFilePath = null,
        AuthenticationPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dates);
        var path = FullPath(databasePath);

        switch (Inspect(path))
        {
            case DemoDatabaseState.Missing:
            case DemoDatabaseState.EmptyFile:
                return await BuildAndInstallAsync(path, dates, credentialFilePath, policy, replacesDemoDatabase: false, cancellationToken);

            case DemoDatabaseState.Demo:
                try
                {
                    await SqliteDatabase.MigrateAsync(path, cancellationToken);
                }
                catch (RequestWorkTypeInvalidException exception)
                {
                    // A demonstration database from before one-type-per-request existed: its sample requests mix MAKE and REPAIR items.
                    throw new DemoDatabaseRefusedException(
                        DemoDatabaseState.Demo,
                        "Refused to initialise '" + path + "': it holds sample requests whose single work type cannot be determined (" + exception.MixedRequests
                        + " mixed, " + exception.RequestsWithoutItems + " without items). Nothing was changed. Replace it with the explicit reset: demo-reset <file> --yes.");
                }
                catch (ActorReferencesInvalidException exception)
                {
                    // A demonstration database from before sign-in existed: its sample records refer to a placeholder user that never existed.
                    throw new DemoDatabaseRefusedException(
                        DemoDatabaseState.Demo,
                        "Refused to initialise '" + path + "': it holds sample data created before sign-in existed, so " + exception.RecordCount
                        + " record(s) refer to a user that does not exist. Nothing was changed. Replace it with the explicit reset: demo-reset <file> --yes.");
                }

                return await SeedExistingAsync(path, dates, credentialFilePath, policy, cancellationToken);

            case var state:
                throw Refused(path, state, "initialise");
        }
    }

    /// <summary>
    /// Replaces a marked demonstration database by a freshly migrated, freshly seeded one with new demonstration accounts and new one-time passwords. Refused - with
    /// the reason, changing nothing - unless the target already is a positively identified demonstration database. A missing target is refused too: reset never
    /// creates a database (use <see cref="InitializeAsync"/>).
    /// </summary>
    public static async Task<DemoDatabaseResult> ResetAsync(
        string databasePath,
        IJalaliDateService dates,
        string? credentialFilePath = null,
        AuthenticationPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dates);
        var path = FullPath(databasePath);

        var state = Inspect(path);
        if (state != DemoDatabaseState.Demo)
        {
            throw Refused(path, state, "reset");
        }

        return await BuildAndInstallAsync(path, dates, credentialFilePath, policy, replacesDemoDatabase: true, cancellationToken);
    }

    private static async Task<DemoDatabaseResult> BuildAndInstallAsync(
        string path, IJalaliDateService dates, string? credentialFilePath, AuthenticationPolicy? policy, bool replacesDemoDatabase, CancellationToken cancellationToken)
    {
        // Refuse an unsafe credential file location BEFORE anything is built or replaced.
        var credentialPath = Path.GetFullPath(credentialFilePath ?? DefaultCredentialFilePath());
        EnsureCredentialFileCanBeCreated(credentialPath);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // A unique name of our own, so nothing that already exists is touched while the new database is built.
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var credentialFileWritten = false;
        try
        {
            await SqliteDatabase.MigrateAsync(temporaryPath, cancellationToken);
            WriteMarker(temporaryPath);
            var (provisioner, contexts) = CreateProvisioner(temporaryPath, policy);
            var credentials = await provisioner.ProvisionAsync(cancellationToken);
            var summary = await SeedAsync(contexts, provisioner, dates, cancellationToken);

            // The passwords are written (to the protected file only) before the new database replaces the old one.
            CredentialFile.WriteNew(credentialPath, path, credentials);
            credentialFileWritten = true;

            // Journal files of the demonstration database being replaced must not be applied to the new file. They are removed only for a
            // positively identified demonstration database, and only now that the replacement is complete.
            if (replacesDemoDatabase)
            {
                DeleteSidecars(path);
            }

            File.Move(temporaryPath, path, overwrite: true);
            return new DemoDatabaseResult(summary, credentials.Count, credentialPath);
        }
        catch
        {
            if (credentialFileWritten)
            {
                DeleteIfExists(credentialPath);
            }

            throw;
        }
        finally
        {
            DeleteIfExists(temporaryPath);
            DeleteSidecars(temporaryPath);
        }
    }

    // Initialising a database that already is a marked demonstration database: seed what is missing. A database that was seeded before sign-in existed never gets
    // here (its migration is refused, see above); a marked but still empty one is provisioned.
    private static async Task<DemoDatabaseResult> SeedExistingAsync(
        string path, IJalaliDateService dates, string? credentialFilePath, AuthenticationPolicy? policy, CancellationToken cancellationToken)
    {
        var (provisioner, contexts) = CreateProvisioner(path, policy);
        var state = await SecurityServices.Create(contexts, policy).Bootstrap.GetStateAsync(cancellationToken);

        if (state == SetupState.Initialized)
        {
            return new DemoDatabaseResult(await SeedAsync(contexts, provisioner, dates, cancellationToken), 0, null);
        }

        var credentialPath = Path.GetFullPath(credentialFilePath ?? DefaultCredentialFilePath());
        EnsureCredentialFileCanBeCreated(credentialPath);
        var credentials = await provisioner.ProvisionAsync(cancellationToken);
        CredentialFile.WriteNew(credentialPath, path, credentials);
        return new DemoDatabaseResult(await SeedAsync(contexts, provisioner, dates, cancellationToken), credentials.Count, credentialPath);
    }

    private static (DemoAccountProvisioner Provisioner, IDbContextFactory<ErpMesDbContext> Contexts) CreateProvisioner(string path, AuthenticationPolicy? policy)
    {
        var contexts = SqliteDatabase.CreateContextFactory(path);
        var services = SecurityServices.Create(contexts, policy);
        return (new DemoAccountProvisioner(services.Bootstrap, services.Accounts, contexts), contexts);
    }

    private static async Task<DemoSeedSummary> SeedAsync(
        IDbContextFactory<ErpMesDbContext> contexts, DemoAccountProvisioner provisioner, IJalaliDateService dates, CancellationToken cancellationToken)
    {
        var actors = await provisioner.ResolveActorsAsync(cancellationToken);
        var seeder = new DemoDataSeeder(new EfCustomerRepository(contexts), new EfRequestRepository(contexts), dates, actors, new EfItemDrawingRepository(contexts));
        var summary = await seeder.SeedAsync(cancellationToken);

        // Master data for the end-to-end walkthrough, written like the sample requests (docs/DEMO_WALKTHROUGH.md); the time is fixed so a reset gives the same rows.
        await new DemoMasterDataSeeder(
            new EfPlanningMasterRepository(contexts), new EfSchedulingRepository(contexts), new EfMaterialRepository(contexts), new EfOperatorRepository(contexts), new EfUnitQueueRepository(contexts),
            dates, await provisioner.ResolveMasterActorsAsync(cancellationToken), new DateTime(2026, 10, 23, 5, 0, 0, DateTimeKind.Utc)).SeedAsync(cancellationToken);
        return summary;
    }

    // Outside the repository by construction: the user's local application data. Never overwritten (the name is unique).
    private static string DefaultCredentialFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ERP-MES",
            "demo-credentials",
            "erpmes-demo-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8] + ".credentials.txt");

    private static void EnsureCredentialFileCanBeCreated(string credentialPath)
    {
        var directory = Path.GetDirectoryName(credentialPath) ?? throw new CredentialFileException("The credential file needs a folder.");
        if (CredentialFile.IsInsideGitWorkingTree(directory))
        {
            throw new CredentialFileException("The credential file must be outside the repository: '" + credentialPath + "' is inside a Git working tree.");
        }

        if (File.Exists(credentialPath) || Directory.Exists(credentialPath))
        {
            throw new CredentialFileException("The credential file already exists and is never overwritten: '" + credentialPath + "'.");
        }
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
