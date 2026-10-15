using ERP.MES.Domain.Calendar;
using ERP.MES.Infrastructure.Sqlite.Demo;

// Provisioning commands for the local FICTIONAL demonstration database. Nothing here runs at application start-up and nothing here
// touches a database that is not a marked demonstration database (docs/DEMO_SEED_AND_RESET.md).
//
//   demo-status <database-file>          shows what is at the path (changes nothing)
//   demo-init   <database-file>          creates the demonstration database, or adds missing sample data to an existing one
//   demo-reset  <database-file> --yes    replaces a marked demonstration database by a fresh, freshly seeded one
//
// No accounts or credentials are created or printed: sign-in is a later, separate piece of work.
const string Usage =
    "Usage:\n"
    + "  demo-status <database-file>\n"
    + "  demo-init   <database-file>\n"
    + "  demo-reset  <database-file> --yes   (replaces ALL data in a marked demonstration database)";

if (args.Length < 2)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

var command = args[0];
var databasePath = args[1];
var confirmed = args.Skip(2).Contains("--yes", StringComparer.Ordinal);
var unknown = args.Skip(2).Where(a => !string.Equals(a, "--yes", StringComparison.Ordinal)).ToList();
if (unknown.Count > 0 || (confirmed && command != "demo-reset"))
{
    Console.Error.WriteLine("Unexpected argument(s): " + (unknown.Count > 0 ? string.Join(" ", unknown) : "--yes is only valid with demo-reset"));
    Console.Error.WriteLine(Usage);
    return 2;
}

// Only date conversion is used by the seed, so the digit style and time zone do not affect the stored data.
var dates = new JalaliDateService(DigitStyle.Latin, TimeZoneInfo.Utc);

try
{
    switch (command)
    {
        case "demo-status":
            Console.WriteLine(DemoDatabase.Inspect(databasePath));
            return 0;

        case "demo-init":
        {
            var summary = await DemoDatabase.InitializeAsync(databasePath, dates);
            Console.WriteLine(
                $"Customers added {summary.CustomersAdded}, already present {summary.CustomersExisting}; requests added {summary.RequestsAdded}, already present {summary.RequestsExisting}.");
            return 0;
        }

        case "demo-reset":
        {
            if (!confirmed)
            {
                Console.Error.WriteLine("Reset replaces all data in the demonstration database. Repeat the command with --yes to confirm.");
                return 2;
            }

            var summary = await DemoDatabase.ResetAsync(databasePath, dates);
            Console.WriteLine(
                $"Reset done. Customers added {summary.CustomersAdded}; requests added {summary.RequestsAdded}.");
            return 0;
        }

        default:
            Console.Error.WriteLine("Unknown command '" + command + "'.");
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (DemoDatabaseRefusedException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
