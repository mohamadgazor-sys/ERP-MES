using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Calendar;
using ERP.MES.Infrastructure.Demo;
using ERP.MES.Infrastructure.Security;
using ERP.MES.Infrastructure.Sqlite.Demo;
using ERP.MES.Tests.Shared;

namespace ERP.MES.Infrastructure.Tests;

// The fictional demonstration accounts (Four-Day Demonstration Plan T6): real hashing, random one-time passwords in a protected file outside the repository, the real
// bootstrap and account use cases, and a seed whose created-by / approved-by values refer to real user rows.
public class DemoAccountsTests
{
    private static IJalaliDateService Dates => PersistenceTestData.Dates;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<DemoDatabaseResult> Init(TempDatabase db) =>
        DemoDatabase.InitializeAsync(db.Path, Dates, db.NextCredentialPath(), SecurityFixture.FastPolicy, Token);

    private static Task<DemoDatabaseResult> Reset(TempDatabase db) =>
        DemoDatabase.ResetAsync(db.Path, Dates, db.NextCredentialPath(), SecurityFixture.FastPolicy, Token);

    private static Dictionary<string, string> ReadCredentials(string path) =>
        File.ReadAllLines(path)
            .Where(line => line.Contains('\t'))
            .Select(line => line.Split('\t'))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

    private static SecurityServices ServicesFor(TempDatabase db) => SecurityServices.Create(db.ContextFactory, SecurityFixture.FastPolicy);

    [Fact]
    public async Task A_new_demonstration_database_gets_one_administrator_and_a_user_for_each_walkthrough_department()
    {
        using var db = new TempDatabase();

        var result = await Init(db);

        Assert.Equal(7, result.AccountsCreated);
        Assert.Equal(
            new[]
            {
                "demo.admin|SYSTEM|ADMIN", "demo.central|CENTRAL|STAFF", "demo.drawing|DRAWING|STAFF", "demo.management|MANAGEMENT|MANAGER",
                "demo.planning|PLANNING|STAFF", "demo.qc|QC|STAFF", "demo.warehouse|WAREHOUSE|STAFF",
            },
            db.Query("SELECT u.Username || '|' || d.Code || '|' || r.Code FROM Users u JOIN Departments d ON d.Id = u.DepartmentId JOIN Roles r ON r.Id = u.RoleId ORDER BY u.Username"));
        Assert.Equal(new[] { "Initialized" }, db.Query("SELECT State FROM SystemSetup"));
        Assert.Equal(new[] { "7" }, db.Query("SELECT COUNT(*) FROM UserCredentials WHERE MustChangePassword = 1"));
        Assert.All(db.Query("SELECT DisplayName FROM Users"), name => Assert.EndsWith("(نمونه آزمایشی)", name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_accounts_come_from_the_real_bootstrap_and_account_use_cases()
    {
        using var db = new TempDatabase();
        await Init(db);

        var actions = db.Query("SELECT ActionCode FROM AuditLog ORDER BY Id");
        Assert.Equal(AuditActions.AdminTokenIssued, actions[0]);
        Assert.Equal(AuditActions.BootstrapCompleted, actions[1]);
        Assert.Equal(AuditActions.PasswordReset, actions[2]);
        Assert.Equal(6, actions.Count(a => a == AuditActions.UserCreated));

        // The only administrator is protected like any other.
        var admin = new Principal(1, "demo.admin", "x", DepartmentCodes.System, RoleCodes.Admin, false);
        Assert.Equal(AccountStatus.LastAdministrator, (await ServicesFor(db).Accounts.SetActiveAsync(admin, 1, false, Token)).Status);
    }

    [Fact]
    public async Task The_one_time_passwords_are_random_distinct_long_and_written_only_to_the_protected_file()
    {
        using var db = new TempDatabase();

        var result = await Init(db);

        Assert.NotNull(result.CredentialFile);
        var credentials = ReadCredentials(result.CredentialFile);
        Assert.Equal(7, credentials.Count);
        Assert.Equal(7, credentials.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.All(credentials.Values, password => Assert.Equal(20, password.Length));

        // The passwords are nowhere else: not in the result, not in the database file, not in the audit log.
        var databaseBytes = await File.ReadAllBytesAsync(db.Path, Token);
        Assert.DoesNotContain(string.Join("|", credentials.Values), result.ToString(), StringComparison.Ordinal);
        foreach (var password in credentials.Values)
        {
            Assert.DoesNotContain(password, result.ToString(), StringComparison.Ordinal);
            Assert.True(databaseBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(password)) < 0, "a password was found in the database file");
            Assert.DoesNotContain(password, string.Join("\n", db.Query("SELECT Summary FROM AuditLog")), StringComparison.Ordinal);
        }

        Assert.False(CredentialFile.IsInsideGitWorkingTree(Path.GetDirectoryName(result.CredentialFile)!));
    }

    [Fact]
    public async Task Each_password_signs_in_once_as_a_one_time_password_and_must_then_be_replaced()
    {
        using var db = new TempDatabase();
        var credentials = ReadCredentials((await Init(db)).CredentialFile!);
        var services = ServicesFor(db);

        var signedIn = await services.Authentication.SignInAsync("demo.qc", credentials["demo.qc"], Token);

        Assert.Equal(SignInStatus.Succeeded, signedIn.Status);
        Assert.True(signedIn.Principal!.MustChangePassword);
        Assert.Equal(DepartmentCodes.Qc, signedIn.Principal.DepartmentCode);
        Assert.Equal(SignInStatus.Failed, (await services.Authentication.SignInAsync("demo.qc", credentials["demo.planning"], Token)).Status);

        const string Own = "My-Own-Pass-Phrase-1";
        Assert.Equal(AccountStatus.Succeeded, (await services.Accounts.ChangeOwnPasswordAsync(signedIn.Principal, credentials["demo.qc"], Own, Token)).Status);
        Assert.Equal(SignInStatus.Failed, (await services.Authentication.SignInAsync("demo.qc", credentials["demo.qc"], Token)).Status);
        Assert.False((await services.Authentication.SignInAsync("demo.qc", Own, Token)).Principal!.MustChangePassword);
    }

    [Fact]
    public async Task Initialising_an_initialised_demonstration_database_creates_no_accounts_and_no_new_file()
    {
        using var db = new TempDatabase();
        await Init(db);
        var users = db.Query("SELECT u.Id || u.Username || hex(c.PasswordHash) FROM Users u JOIN UserCredentials c ON c.UserId = u.Id ORDER BY u.Id");
        var unusedPath = db.NextCredentialPath();

        var second = await DemoDatabase.InitializeAsync(db.Path, Dates, unusedPath, SecurityFixture.FastPolicy, Token);

        Assert.Equal(0, second.AccountsCreated);
        Assert.Null(second.CredentialFile);
        Assert.False(File.Exists(unusedPath));
        Assert.Equal(users, db.Query("SELECT u.Id || u.Username || hex(c.PasswordHash) FROM Users u JOIN UserCredentials c ON c.UserId = u.Id ORDER BY u.Id"));
    }

    [Fact]
    public async Task A_reset_creates_new_accounts_with_new_random_passwords()
    {
        using var db = new TempDatabase();
        var first = ReadCredentials((await Init(db)).CredentialFile!);
        var services = ServicesFor(db);
        await services.Authentication.SignInAsync("demo.admin", first["demo.admin"], Token);

        var result = await Reset(db);
        var second = ReadCredentials(result.CredentialFile!);

        Assert.Equal(7, result.AccountsCreated);
        Assert.Equal(first.Keys.OrderBy(k => k, StringComparer.Ordinal), second.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(first.Keys, user => Assert.NotEqual(first[user], second[user]));
        Assert.Equal(SignInStatus.Failed, (await services.Authentication.SignInAsync("demo.admin", first["demo.admin"], Token)).Status);
        Assert.Equal(SignInStatus.Succeeded, (await services.Authentication.SignInAsync("demo.admin", second["demo.admin"], Token)).Status);
        Assert.Equal(7, Count(db, "Users"));
    }

    private static int Count(TempDatabase db, string table) => int.Parse(db.Query("SELECT COUNT(*) FROM " + table)[0], System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Every_created_by_and_approved_by_value_of_the_sample_data_refers_to_an_existing_user()
    {
        using var db = new TempDatabase();
        await Init(db);

        Assert.Equal(new[] { "0" }, db.Query("SELECT COUNT(*) FROM Customers WHERE CreatedByUserId NOT IN (SELECT Id FROM Users)"));
        Assert.Equal(new[] { "0" }, db.Query("SELECT COUNT(*) FROM Requests WHERE CreatedByUserId NOT IN (SELECT Id FROM Users)"));
        Assert.Equal(new[] { "0" }, db.Query("SELECT COUNT(*) FROM RequestItems WHERE CreatedByUserId NOT IN (SELECT Id FROM Users)"));
        Assert.Equal(new[] { "0" }, db.Query("SELECT COUNT(*) FROM QcApprovals WHERE ApprovedByUserId NOT IN (SELECT Id FROM Users)"));

        // The Central user "created" the records and the QC user "approved" the stages; the placeholder value 1 is not used by accident.
        Assert.Equal(new[] { "demo.central" }, db.Query("SELECT DISTINCT u.Username FROM Customers c JOIN Users u ON u.Id = c.CreatedByUserId"));
        Assert.Equal(new[] { "demo.central" }, db.Query("SELECT DISTINCT u.Username FROM Requests c JOIN Users u ON u.Id = c.CreatedByUserId"));
        Assert.Equal(new[] { "demo.qc" }, db.Query("SELECT DISTINCT u.Username FROM QcApprovals a JOIN Users u ON u.Id = a.ApprovedByUserId"));
        Assert.Equal(new[] { "demo.admin" }, db.Query("SELECT Username FROM Users WHERE Id = 1"));
    }

    [Fact]
    public async Task A_credential_file_inside_the_repository_is_refused_before_anything_is_created()
    {
        using var db = new TempDatabase();
        var inside = Path.Combine(SolutionLayout.Root, "tests", "never-written.credentials.txt");

        await Assert.ThrowsAsync<CredentialFileException>(() => DemoDatabase.InitializeAsync(db.Path, Dates, inside, SecurityFixture.FastPolicy, Token));

        Assert.False(File.Exists(inside));
        Assert.Equal(DemoDatabaseState.Missing, DemoDatabase.Inspect(db.Path));
    }

    [Fact]
    public async Task A_reset_with_an_existing_credential_file_is_refused_and_the_database_is_untouched()
    {
        using var db = new TempDatabase();
        await Init(db);
        var before = await File.ReadAllBytesAsync(db.Path, Token);
        var existing = db.NextCredentialPath();
        await File.WriteAllTextAsync(existing, "someone else's file", Token);

        await Assert.ThrowsAsync<CredentialFileException>(() => DemoDatabase.ResetAsync(db.Path, Dates, existing, SecurityFixture.FastPolicy, Token));

        Assert.Equal(before, await File.ReadAllBytesAsync(db.Path, Token));
        Assert.Equal("someone else's file", await File.ReadAllTextAsync(existing, Token));
    }

    [Fact]
    public async Task A_marked_database_with_sample_data_from_before_the_accounts_existed_is_refused_and_must_be_reset()
    {
        using var db = new TempDatabase();

        // The state before sign-in existed: only the initial migration is applied and a sample record names a creator that was never a user.
        await using (var context = db.NewContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(context.Database.GetMigrations().First(), Token);
        }

        db.Execute("PRAGMA application_id = " + DemoDatabase.ApplicationId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        db.Execute("INSERT INTO Customers (Name, IsActive, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, Version) VALUES ('Older sample customer', 1, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15', 1)");
        var bytesBefore = await File.ReadAllBytesAsync(db.Path, Token);

        var refusal = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(() => Init(db));

        Assert.Contains("demo-reset", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(db.Path, Token));
        Assert.DoesNotContain(db.CredentialFiles, f => File.Exists(f));
    }

    [Fact]
    public async Task A_marked_but_empty_database_is_provisioned_and_seeded()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        db.Execute("PRAGMA application_id = " + DemoDatabase.ApplicationId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var result = await Init(db);

        Assert.Equal(7, result.AccountsCreated);
        Assert.Equal(3, Count(db, "Customers"));
        Assert.True(File.Exists(result.CredentialFile));
    }

    [Fact]
    public void Generated_one_time_passwords_are_long_random_and_valid_under_the_password_policy()
    {
        var generated = Enumerable.Range(0, 50).Select(_ => DemoAccountProvisioner.GeneratePassword()).ToList();

        // Booleans only, so a failure message never prints a generated password.
        Assert.True(generated.All(p => PasswordRules.Validate(p, AuthenticationPolicy.Default) == PasswordPolicyError.None));
        Assert.True(generated.All(p => p.Length == 20));
        Assert.Equal(generated.Count, generated.Distinct(StringComparer.Ordinal).Count());
    }
}
