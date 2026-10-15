using ERP.MES.Application.Security;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// Sign-in, lockout and hash upgrade (Decision Log #40, #41; Milestone 1 plan 6) on a real SQLite database with the real services.
public class AuthenticationTests
{
    private const string StaffName = "staff.one";
    private const string StaffPassword = "Staff-Pass-Phrase-2";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(SecurityFixture Fixture, Principal Admin)> WithStaffAsync()
    {
        var fixture = await SecurityFixture.CreateAsync();
        var admin = await fixture.BootstrapAdminAsync(cancellationToken: Token);
        await fixture.CreateUserAsync(admin, StaffName, StaffPassword, cancellationToken: Token);
        return (fixture, admin);
    }

    [Fact]
    public async Task A_correct_password_signs_in_and_records_the_login()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        var admin = await fixture.BootstrapAdminAsync(cancellationToken: Token);

        Assert.Equal(SecurityFixture.AdminName, admin.Username);
        Assert.Equal(DepartmentCodes.System, admin.DepartmentCode);
        Assert.Equal(RoleCodes.Admin, admin.RoleCode);
        Assert.False(admin.MustChangePassword);
        Assert.NotEqual("NULL", fixture.Db.Query("SELECT COALESCE(LastLoginAtUtc, 'NULL') FROM Users").Single());
        Assert.Contains(AuditActions.LoginSucceeded, fixture.AuditActionsInOrder());
    }

    [Fact]
    public async Task Unknown_user_wrong_password_and_inactive_account_all_get_the_same_answer()
    {
        var (fixture, admin) = await WithStaffAsync();
        using var _ = fixture;
        var inactiveId = await fixture.CreateUserAsync(admin, "staff.gone", StaffPassword, cancellationToken: Token);
        Assert.Equal(AccountStatus.Succeeded, (await fixture.Services.Accounts.SetActiveAsync(admin, inactiveId, false, Token)).Status);

        var unknown = await fixture.Services.Authentication.SignInAsync("nobody", StaffPassword, Token);
        var wrong = await fixture.Services.Authentication.SignInAsync(StaffName, "not the password", Token);
        var inactive = await fixture.Services.Authentication.SignInAsync("staff.gone", StaffPassword, Token);

        Assert.All(new[] { unknown, wrong, inactive }, result =>
        {
            Assert.Equal(SignInStatus.InvalidCredentials, result.Status);
            Assert.Null(result.Principal);
        });
    }

    [Fact]
    public async Task The_user_name_is_compared_exactly()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        await fixture.BootstrapAdminAsync(cancellationToken: Token);

        Assert.Equal(SignInStatus.InvalidCredentials, (await fixture.Services.Authentication.SignInAsync("Admin.One", SecurityFixture.AdminPassword, Token)).Status);
        Assert.Equal(SignInStatus.InvalidCredentials, (await fixture.Services.Authentication.SignInAsync(" admin.one", SecurityFixture.AdminPassword, Token)).Status);
        Assert.Equal(SignInStatus.Succeeded, (await fixture.Services.Authentication.SignInAsync("admin.one", SecurityFixture.AdminPassword, Token)).Status);
    }

    [Theory]
    [InlineData(null, "x")]
    [InlineData("", "x")]
    [InlineData("admin.one", null)]
    [InlineData("admin.one", "")]
    public async Task Empty_credentials_are_refused_and_audited_without_a_name(string? username, string? password)
    {
        using var fixture = await SecurityFixture.CreateAsync();
        await fixture.BootstrapAdminAsync(cancellationToken: Token);

        var result = await fixture.Services.Authentication.SignInAsync(username, password, Token);

        Assert.Equal(SignInStatus.InvalidCredentials, result.Status);
        Assert.Contains(AuditActions.LoginFailed, fixture.AuditActionsInOrder());
    }

    [Fact]
    public async Task Five_failed_attempts_lock_the_account_for_five_minutes_and_the_lock_then_ends_by_itself()
    {
        var (fixture, _) = await WithStaffAsync();
        using var _ = fixture;

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal(SignInStatus.InvalidCredentials, (await fixture.Services.Authentication.SignInAsync(StaffName, "wrong guess " + attempt, Token)).Status);
        }

        // The fifth failure locks the account.
        Assert.Equal(SignInStatus.LockedOut, (await fixture.Services.Authentication.SignInAsync(StaffName, "wrong guess 5", Token)).Status);
        Assert.Equal(new[] { "0" }, fixture.Db.Query("SELECT FailedLoginCount FROM Users WHERE Username = 'staff.one'"));

        // While locked even the correct password is refused, and the lock does not extend.
        Assert.Equal(SignInStatus.LockedOut, (await fixture.Services.Authentication.SignInAsync(StaffName, StaffPassword, Token)).Status);
        fixture.Clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Assert.Equal(SignInStatus.LockedOut, (await fixture.Services.Authentication.SignInAsync(StaffName, StaffPassword, Token)).Status);

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var afterLock = await fixture.Services.Authentication.SignInAsync(StaffName, StaffPassword, Token);
        Assert.Equal(SignInStatus.Succeeded, afterLock.Status);
        Assert.Equal(new[] { "NULL" }, fixture.Db.Query("SELECT COALESCE(LockoutEndUtc, 'NULL') FROM Users WHERE Username = 'staff.one'"));
        Assert.Contains(AuditActions.AccountLocked, fixture.AuditActionsInOrder());
    }

    [Fact]
    public async Task A_successful_sign_in_resets_the_failed_attempt_counter()
    {
        var (fixture, _) = await WithStaffAsync();
        using var _ = fixture;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await fixture.Services.Authentication.SignInAsync(StaffName, "wrong", Token);
        }

        Assert.Equal(SignInStatus.Succeeded, (await fixture.Services.Authentication.SignInAsync(StaffName, StaffPassword, Token)).Status);

        // Four more failures are still below the limit because the counter started again.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Assert.Equal(SignInStatus.InvalidCredentials, (await fixture.Services.Authentication.SignInAsync(StaffName, "wrong", Token)).Status);
        }

        Assert.Equal(SignInStatus.LockedOut, (await fixture.Services.Authentication.SignInAsync(StaffName, "wrong", Token)).Status);
    }

    [Fact]
    public async Task The_failed_attempt_counter_is_stored_in_the_database_and_shared_by_every_instance()
    {
        var (fixture, _) = await WithStaffAsync();
        using var _ = fixture;
        var other = SecurityServices.Create(fixture.Db.ContextFactory, fixture.Policy, fixture.Clock, new FixedAuditContext());

        await fixture.Services.Authentication.SignInAsync(StaffName, "wrong", Token);
        await other.Authentication.SignInAsync(StaffName, "wrong", Token);
        await fixture.Services.Authentication.SignInAsync(StaffName, "wrong", Token);
        await other.Authentication.SignInAsync(StaffName, "wrong", Token);

        // The fifth failure, from either instance, locks the account for both.
        Assert.Equal(SignInStatus.LockedOut, (await fixture.Services.Authentication.SignInAsync(StaffName, "wrong", Token)).Status);
        Assert.Equal(SignInStatus.LockedOut, (await other.Authentication.SignInAsync(StaffName, StaffPassword, Token)).Status);
    }

    [Fact]
    public async Task Failures_are_counted_per_user()
    {
        var (fixture, admin) = await WithStaffAsync();
        using var _ = fixture;
        await fixture.CreateUserAsync(admin, "staff.two", StaffPassword, cancellationToken: Token);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await fixture.Services.Authentication.SignInAsync(StaffName, "wrong", Token);
        }

        Assert.Equal(SignInStatus.Succeeded, (await fixture.Services.Authentication.SignInAsync("staff.two", StaffPassword, Token)).Status);
        Assert.Equal(SignInStatus.Succeeded, (await fixture.Services.Authentication.SignInAsync(StaffName, StaffPassword, Token)).Status);
    }

    [Fact]
    public async Task Simultaneous_failed_attempts_are_all_counted()
    {
        var (fixture, _) = await WithStaffAsync();
        using var _ = fixture;

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            fixture.Services.Authentication.SignInAsync(StaffName, "wrong " + i, Token)));

        // No update is lost: exactly the fifth counted failure locks the account.
        Assert.Equal(1, results.Count(r => r.Status == SignInStatus.LockedOut));
        Assert.Equal(4, results.Count(r => r.Status == SignInStatus.InvalidCredentials));
        Assert.NotEqual("NULL", fixture.Db.Query("SELECT COALESCE(LockoutEndUtc, 'NULL') FROM Users WHERE Username = 'staff.one'").Single());
    }

    [Fact]
    public async Task A_hash_below_the_configured_work_factor_is_upgraded_after_a_successful_sign_in_only()
    {
        var (fixture, _) = await WithStaffAsync();
        using var _ = fixture;
        Assert.Equal(new[] { "310000" }, fixture.Db.Query("SELECT PasswordWorkFactor FROM Users WHERE Username = 'staff.one'"));

        fixture.UsePolicy(new AuthenticationPolicy { Pbkdf2Iterations = 320_000 });

        // A failed attempt upgrades nothing.
        await fixture.Services.Authentication.SignInAsync(StaffName, "wrong", Token);
        Assert.Equal(new[] { "310000" }, fixture.Db.Query("SELECT PasswordWorkFactor FROM Users WHERE Username = 'staff.one'"));

        Assert.Equal(SignInStatus.Succeeded, (await fixture.Services.Authentication.SignInAsync(StaffName, StaffPassword, Token)).Status);
        Assert.Equal(new[] { "320000" }, fixture.Db.Query("SELECT PasswordWorkFactor FROM Users WHERE Username = 'staff.one'"));
        Assert.Contains(AuditActions.PasswordHashUpgraded, fixture.AuditActionsInOrder());

        // The upgraded hash still verifies, and its parameters are the ones stored with it.
        Assert.Equal(SignInStatus.Succeeded, (await fixture.Services.Authentication.SignInAsync(StaffName, StaffPassword, Token)).Status);
        Assert.Equal(new[] { "PBKDF2-HMAC-SHA256|1" }, fixture.Db.Query("SELECT PasswordAlgorithm || '|' || PasswordHashVersion FROM Users WHERE Username = 'staff.one'"));
    }

    [Fact]
    public async Task Security_events_are_audited_without_secrets_or_the_attempted_name()
    {
        var (fixture, _) = await WithStaffAsync();
        using var _ = fixture;

        await fixture.Services.Authentication.SignInAsync(StaffName, "Wrong-Guess-Password-9", Token);
        await fixture.Services.Authentication.SignInAsync("someone.else", "Another-Wrong-Guess-7", Token);
        await fixture.Services.Authentication.SignInAsync(StaffName, StaffPassword, Token);

        var audit = fixture.AllAuditText();
        Assert.Contains(AuditActions.LoginFailed, audit, StringComparison.Ordinal);
        Assert.Contains(AuditActions.LoginSucceeded, audit, StringComparison.Ordinal);
        foreach (var secret in new[] { "Wrong-Guess-Password-9", "Another-Wrong-Guess-7", StaffPassword, SecurityFixture.AdminPassword, "someone.else" })
        {
            Assert.DoesNotContain(secret, audit, StringComparison.Ordinal);
        }

        // Machine and operating-system user are recorded with every entry.
        Assert.Empty(fixture.Db.Query("SELECT Id FROM AuditLog WHERE MachineName <> 'TEST-PC' OR OsUserName <> 'test.operator'"));
    }

    [Fact]
    public async Task The_services_refuse_a_policy_below_the_safety_floor()
    {
        using var fixture = await SecurityFixture.CreateAsync();

        var tooWeak = new AuthenticationPolicy { Pbkdf2Iterations = 1000 };

        Assert.Throws<InvalidAuthenticationPolicyException>(() => SecurityServices.Create(fixture.Db.ContextFactory, tooWeak));
    }
}
