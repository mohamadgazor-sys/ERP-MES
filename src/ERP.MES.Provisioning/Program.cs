using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Infrastructure.Security;
using ERP.MES.Infrastructure.Sqlite;
using ERP.MES.Infrastructure.Sqlite.Demo;

// Restricted provisioning commands for authorised operators (IT). This tool is never part of the factory-PC distribution (Decision Log #38, #47).
// The operator supplies the database at run time; no privileged credential is stored in the repository or in configuration.
//
// Production:
//   migrate              --database <file>                                   applies the committed migrations (creates the file if it is missing)
//   issue-admin-token    --database <file> [--hours N]                       issues the one-time bootstrap token; printed ONCE, never stored or logged
//   issue-recovery-token --database <file> --username <administrator> --reason <text>
//                                                                            issues a one-time recovery token for an existing administrator account
//   benchmark-hash       [--iterations N]                                    times one password hash on this machine (Decision Log #42)
//
// Local FICTIONAL demonstration database only (acts only on a database carrying the demonstration marker, see docs/DEMO_SEED_AND_RESET.md):
//   demo-status <file>
//   demo-init   <file> [--credentials <new file outside the repository>]
//   demo-reset  <file> --yes [--credentials <new file outside the repository>]
//   (both demo commands accept --presentation: after creating the accounts, every demonstration account replaces its one-time password, through the real
//    sign-in and change-password use cases, with the demo passphrase read from the environment variable ERPMES_DEMO_PASSPHRASE; the one-time file is then deleted.
//    Used only by the offline presentation package, docs/OFFLINE_DEMO_GUIDE.md.)
//
// Passwords are never printed. The demonstration accounts' one-time passwords go only to the protected credential file.
const string Usage =
    "Usage:\n"
    + "  migrate              --database <file>\n"
    + "  issue-admin-token    --database <file> [--hours N]\n"
    + "  issue-recovery-token --database <file> --username <administrator> --reason <text>\n"
    + "  benchmark-hash       [--iterations N]\n"
    + "  demo-status          <file>\n"
    + "  demo-init            <file> [--credentials <new file outside the repository>] [--presentation]\n"
    + "  demo-reset           <file> --yes [--credentials <new file outside the repository>] [--presentation]   (replaces ALL data in a marked demonstration database)";

if (args.Length < 1)
{
    return Usage2();
}

var command = args[0];
var options = new Dictionary<string, string?>(StringComparer.Ordinal);
var positional = new List<string>();
for (var i = 1; i < args.Length; i++)
{
    if (!args[i].StartsWith("--", StringComparison.Ordinal))
    {
        positional.Add(args[i]);
        continue;
    }

    var name = args[i];
    var takesValue = name is "--database" or "--hours" or "--username" or "--reason" or "--iterations" or "--credentials";
    if (takesValue)
    {
        if (i + 1 >= args.Length)
        {
            return Fail2("The option " + name + " needs a value.");
        }

        options[name] = args[++i];
    }
    else
    {
        options[name] = null;
    }
}

try
{
    switch (command)
    {
        case "migrate":
        {
            var database = RequireDatabase();
            if (database is null)
            {
                return 2;
            }

            await SqliteDatabase.MigrateAsync(database);
            Console.WriteLine("The database is up to date: " + Path.GetFullPath(database));
            return 0;
        }

        case "issue-admin-token":
        {
            var database = RequireExistingDatabase();
            if (database is null)
            {
                return 2;
            }

            var hours = 0;
            if (options.TryGetValue("--hours", out var hoursText) && !int.TryParse(hoursText, NumberStyles.None, CultureInfo.InvariantCulture, out hours))
            {
                return Fail2("--hours must be a whole number.");
            }

            var services = SecurityServices.Create(SqliteDatabase.CreateContextFactory(database));
            var issued = await services.Bootstrap.IssueTokenAsync(hours > 0 ? TimeSpan.FromHours(hours) : null);
            if (!issued.Issued)
            {
                Console.Error.WriteLine("Refused: the system is already initialised. The bootstrap path is permanently closed (use issue-recovery-token if administrator access is lost).");
                return 1;
            }

            Console.WriteLine("Bootstrap token (shown ONCE; only its digest is stored; hand it over out-of-band):");
            Console.WriteLine(issued.Token);
            Console.WriteLine("Valid until (UTC): " + issued.ExpiresAtUtc!.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "; five attempts; issuing again invalidates this token.");
            return 0;
        }

        case "issue-recovery-token":
        {
            var database = RequireExistingDatabase();
            if (database is null)
            {
                return 2;
            }

            if (!options.TryGetValue("--username", out var username) || string.IsNullOrWhiteSpace(username)
                || !options.TryGetValue("--reason", out var reason) || string.IsNullOrWhiteSpace(reason))
            {
                return Fail2("issue-recovery-token needs --username and --reason.");
            }

            var services = SecurityServices.Create(SqliteDatabase.CreateContextFactory(database));
            var issued = await services.Recovery.IssueTokenAsync(username, reason);
            if (issued.Status != RecoveryStatus.Succeeded)
            {
                Console.Error.WriteLine("Refused: " + issued.Status + ". Nothing was issued.");
                return 1;
            }

            Console.WriteLine("Recovery token (shown ONCE; only its digest is stored; hand it over out-of-band):");
            Console.WriteLine(issued.Token);
            Console.WriteLine("Valid until (UTC): " + issued.ExpiresAtUtc!.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "; five attempts; the person who redeems it sets a NEW password.");
            return 0;
        }

        case "benchmark-hash":
        {
            var iterations = AuthenticationPolicy.Default.Pbkdf2Iterations;
            if (options.TryGetValue("--iterations", out var iterationsText) && !int.TryParse(iterationsText, NumberStyles.None, CultureInfo.InvariantCulture, out iterations))
            {
                return Fail2("--iterations must be a whole number.");
            }

            var hasher = new Pbkdf2PasswordHasher();
            var sample = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            var timings = new List<double>();
            for (var run = 0; run < 5; run++)
            {
                var watch = Stopwatch.StartNew();
                hasher.Hash(sample, iterations);
                watch.Stop();
                timings.Add(watch.Elapsed.TotalMilliseconds);
            }

            timings.Sort();
            Console.WriteLine($"PBKDF2-HMAC-SHA256, {iterations} iterations: median {timings[2]:F0} ms, fastest {timings[0]:F0} ms, slowest {timings[^1]:F0} ms (target about 500 ms on the lowest-spec supported PC; the work factor is never lowered automatically).");
            return 0;
        }

        case "demo-status":
        {
            if (positional.Count != 1)
            {
                return Usage2();
            }

            Console.WriteLine(DemoDatabase.Inspect(positional[0]));
            return 0;
        }

        case "demo-init":
        case "demo-reset":
        {
            if (positional.Count != 1 || options.Keys.Any(k => k is not ("--yes" or "--credentials" or "--presentation")) || (options.ContainsKey("--yes") && command != "demo-reset"))
            {
                return Usage2();
            }

            if (command == "demo-reset" && !options.ContainsKey("--yes"))
            {
                Console.Error.WriteLine("Reset replaces all data in the demonstration database. Repeat the command with --yes to confirm.");
                return 2;
            }

            var presentation = options.ContainsKey("--presentation");
            var passphrase = Environment.GetEnvironmentVariable("ERPMES_DEMO_PASSPHRASE");
            if (presentation && string.IsNullOrWhiteSpace(passphrase))
            {
                Console.Error.WriteLine("--presentation needs the demo passphrase in the environment variable ERPMES_DEMO_PASSPHRASE. Nothing was changed.");
                return 2;
            }

            // Only date conversion is used by the seed, so the digit style and time zone do not affect the stored data.
            var dates = new JalaliDateService(DigitStyle.Latin, TimeZoneInfo.Utc);
            options.TryGetValue("--credentials", out var credentials);
            var result = command == "demo-init"
                ? await DemoDatabase.InitializeAsync(positional[0], dates, credentials)
                : await DemoDatabase.ResetAsync(positional[0], dates, credentials);

            Console.WriteLine(
                $"{(command == "demo-init" ? "Initialised" : "Reset done")}. Customers added {result.Seed.CustomersAdded}, already present {result.Seed.CustomersExisting}; "
                + $"requests added {result.Seed.RequestsAdded}, already present {result.Seed.RequestsExisting}; demonstration accounts created {result.AccountsCreated}.");
            if (presentation && result.CredentialFile is not null)
            {
                var replaced = await ReplaceOneTimePasswordsAsync(positional[0], result.CredentialFile, passphrase!);
                if (replaced < 0)
                {
                    return 1;
                }

                Console.WriteLine($"Presentation mode: {replaced} demonstration accounts now use the demo passphrase (set through the real change-password use case); the one-time file was deleted.");
                return 0;
            }

            if (result.CredentialFile is not null)
            {
                Console.WriteLine("The one-time passwords were written (not printed) to the protected file: " + result.CredentialFile);
            }

            return 0;
        }

        default:
            return Usage2();
    }
}
catch (DemoDatabaseRefusedException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
catch (CredentialFileException exception)
{
    Console.Error.WriteLine("Refused: " + exception.Message + " Nothing was changed.");
    return 1;
}

// Offline presentation package only: each new demonstration account signs in with its one-time password and replaces it, exactly as a person would at the first sign-in.
async Task<int> ReplaceOneTimePasswordsAsync(string database, string credentialFile, string passphrase)
{
    var services = SecurityServices.Create(SqliteDatabase.CreateContextFactory(database));
    var count = 0;
    foreach (var line in File.ReadAllLines(credentialFile).Where(l => l.Contains('\t')))
    {
        var parts = line.Split('\t');
        var first = await services.Authentication.SignInAsync(parts[0], parts[1]);
        if (first.Status != SignInStatus.Succeeded || first.Principal is null)
        {
            Console.Error.WriteLine("Presentation mode: the sign-in of '" + parts[0] + "' failed (" + first.Status + "). The accounts keep their one-time passwords in " + credentialFile + ".");
            return -1;
        }

        var changed = await services.Accounts.ChangeOwnPasswordAsync(first.Principal, parts[1], passphrase);
        if (changed.Status != AccountStatus.Succeeded)
        {
            Console.Error.WriteLine("Presentation mode: the password of '" + parts[0] + "' was refused (" + changed.Status + "); the demo passphrase may not meet the password policy. The accounts keep their one-time passwords in " + credentialFile + ".");
            return -1;
        }

        count++;
    }

    File.Delete(credentialFile);
    return count;
}

string? RequireDatabase()
{
    if (options.TryGetValue("--database", out var database) && !string.IsNullOrWhiteSpace(database))
    {
        return database;
    }

    Console.Error.WriteLine("The command needs --database <file>.");
    return null;
}

string? RequireExistingDatabase()
{
    var database = RequireDatabase();
    if (database is not null && !File.Exists(database))
    {
        Console.Error.WriteLine("The database file does not exist: " + Path.GetFullPath(database) + ". Run the migrate command first.");
        return null;
    }

    return database;
}

int Usage2()
{
    Console.Error.WriteLine(Usage);
    return 2;
}

int Fail2(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine(Usage);
    return 2;
}
