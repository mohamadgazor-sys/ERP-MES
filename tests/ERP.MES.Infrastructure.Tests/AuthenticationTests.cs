using ERP.MES.Application.Security;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// Sign-in, lockout and hash upgrade (Decision Log #40, #41, #62, #65) on a real SQLite database with the real services.
public class AuthenticationTests
{
    private const string StaffName = "staff.one";
    private const string StaffPassword = "Staff-Pass-Phrase-2";

    private const string LockoutOf = "SELECT COALESCE(c.LockoutEndUtc, 'NULL') FROM UserCredentials c JOIN Users u ON u.Id = c.UserId WHERE u.Username = 'staff.one'";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(SecurityFixture Fixture, Principal Admin)> WithStaffAsync()
    {
        var fixture = await SecurityFixture.CreateAsync();
        var admin = await fixture.BootstrapAdminAsync(cancellationToken: Token);
        await fixture.CreateUserAsync(admin, StaffName, StaffPassword, cancellationToken: Token);
        return (fixture, admin);
    }

    private static async Task<SignInStatus> StatusAsync(SecurityFixture fixture, string? username, string? password) =>
        (await fixture.Services.Authentication.SignInAsync(username, password, Token)).Status;

    [Fact]
    public async Task A_correct_password_signs_in_and_records_the_login()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        var admin = await fixture.BootstrapAdminAsync(cancellationToken: Token);

        Assert.Equal(SecurityFixture.AdminName, admin.Username);
        Assert.Equal(DepartmentCodesForTests.System, admin.DepartmentCode);
        Assert.Equal(RoleCodesForTests.Admin, admin.RoleCode);
        Assert.False(admin.MustChangePassword);
        Assert.NotEqual("NULL", fixture.Db.Query("SELECT COALESCE(LastLoginAtUtc, 'NULL') FROM Users").Single());
        Assert.Contains(AuditActions.LoginSucceeded, fixture.AuditActionsInOrder());
    }

    [Fact]
    public async Task Unknown_user_wrong_password_inactive_account_and_locked_account_all_get_the_same_answer()
    {
        var (fixture, admin) = await WithStaffAsync();
        using var _ = fixture;
        var inactiveId = await fixture.CreateUserAsync(admin, "staff.gone", StaffPassword, cancellationToken: Token);
        Assert.Equal(AccountStatus.Succeeded, (await fixture.Services.Accounts.SetActiveAsync(admin, inactiveId, false, Token)).Status);
        await fixture.CreateUserAsync(admin, "staff.locked", StaffPassword, cancellationToken: Token);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await fixture.Services.Authentication.SignInAsync("staff.locked", "wrong " + attempt, Token);
        }

        var results = new[]
        {
            await fixture.Services.Authentication.SignInAsync("nobody", StaffPassword, Token),
            await fixture.Services.Authentication.SignInAsync(StaffName, "not the password", Token),
            await fixture.Services.Authentication.SignInAsync("staff.gone", StaffPassword, Token),
            await fixture.Services.Authentication.SignInAsync("staff.locked", StaffPassword, Token),
            await fixture.Services.Authentication.SignInAsync("staff.locked", "not the password", Token),
        };

        // The caller cannot tell the cases apart: the same status and no principal. Only the audit log keeps the distinction.
        Assert.All(results, result =>
        {
            Assert.Equal(SignInStatus.Failed, result.Status);
            Assert.Null(result.Principal);
        });
        var audit = fixture.AllAuditText();
        Assert.Contains("the account is locked", audit, StringComparison.Ordinal);
        Assert.Contains("the account is inactive", audit, StringComparison.Ordinal);
        Assert.Contains("wrong password", audit, StringComparison.Ordinal);
        Assert.Contains("unknown user", audit, StringComparison.Ordinal);
        Assert.Contains(AuditActions.AccountLocked, fixture.AuditActionsInOrder());
    }

    [Fact]
    public async Task Every_failure_does_the_same_password_work_so_timing_does_not_reveal_which_one_it_was()
    {
        var (fixture, admin) = await WithStaffAsync();
        using var _ = fixture;
        var inactiveId = await fixture.CreateUserAsync(admin, "staff.gone", StaffPassword, cancellationToken: Token);
        await fixture.Services.Accounts.SetActiveAsync(admin, inactiveId, false, Token);
        await fixture.CreateUserAsync(admin, "staff.locked", StaffPassword, cancellationToken: Token);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await fixture.Services.Authentication.SignInAsync("staff.locked", "wrong " + attempt, Token);
        }

        var counting = new CountingHasher(new Pbkdf2PasswordHasher());
        fixture.UseHasher(counting);

        async Task<int> VerificationsFor(string username, string password)
        {
            var before = counting.VerifyCalls;
            await fixture.Services.Authentication.SignInAsync(username, password, Token);
            return counting.VerifyCalls - before;
        }

        // Exactly one password verification in every case (unknown, wrong, inactive, locked, and success).
        Assert.Equal(1, await VerificationsFor("nobody", StaffPassword));
        Assert.Equal(1, await VerificationsFor(StaffName, "not the password"));
        Assert.Equal(1, await VerificationsFor("staff.gone", StaffPassword));
        Assert.Equal(1, await VerificationsFor("staff.locked", StaffPassword));
        Assert.Equal(1, await VerificationsFor("staff.locked", "not the password"));
        Assert.Equal(1, await VerificationsFor(StaffName, StaffPassword));
    }

    [Fact]
    public async Task The_user_name_is_matched_ignoring_case_but_never_trimmed()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        await fixture.BootstrapAdminAsync(cancellationToken: Token);

        Assert.Equal(SignInStatus.Succeeded, await StatusAsync(fixture, "Admin.One", SecurityFixture.AdminPassword));
        Assert.Equal(SignInStatus.Succeeded, await StatusAsync(fixture, "ADMIN.ONE", SecurityFixture.AdminPassword));
        Assert.Equal(SignInStatus.Succeeded, await StatusAsync(fixture, "admin.one", SecurityFixture.AdminPassword));

        // White space is rejected, not removed: these can never be a user's name.
        Assert.Equal(SignInStatus.Failed, await StatusAsync(fixture, " admin.one", SecurityFixture.AdminPassword));
        Assert.Equal(SignInStatus.Failed, await StatusAsync(fixture, "admin.one ", SecurityFixture.AdminPassword));
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

        Assert.Equal(SignInStatus.Failed, await StatusAsync(fixture, username, password));
        Assert.Contains(AuditActions.LoginFailed, fixture.AuditActionsInOrder());
    }

    [Fact]
    public async Task Five_failed_attempts_lock_the_account_for_five_minutes_and_the_lock_then_ends_by_itself()
    {
        var (fixture, _) = await WithStaffAsync();
        using var _ = fixture;

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            Assert.Equal(SignInStatus.Failed, await StatusAsync(fixture, StaffName, "wrong guess " + attempt));
        }

        // The fifth failure locked the account (visible only in the database, not to the caller): the counter restarted and a lock end is set.
        Assert.Equal(new[] { "0" }, fixture.Db.Query("SELECT c.FailedLoginCount FROM UserCredentials c JOIN Users u ON u.Id = c.UserId WHERE u.Username = 'staff.one'"));
        Assert.NotEqual("NULL", fixture.Db.Query(LockoutOf).Single());

        // While locked even the correct password is refused.
        Assert.Equal(SignInStatus.Failed, await StatusAsync(fixture, StaffName, StaffPassword));
        fixture.Clock.Advance(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59));
        Assert.Equal(SignInStatus.Failed, await StatusAsync(fixture, StaffName, StaffPassword));

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(SignInStatus.Succeeded, await StatusAsync(fixture, StaffName, StaffPassword));
        Assert.Equal(new[] { "NULL" }, fixture.Db.Query(LockoutOf));
        Assert.Contains(AuditActions.AccountLocked, fixture.AuditActionsInOrder());
    }

    [Fact]
    public async Task Attempts_during_a_lock_never_extend_it()
    {
        var (fixture, _) = await WithStaffAsync();
        using var _ = fixture;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await StatusAsync(fixture, StaffName, "wrong " + attempt);
        }

        var lockEnd = fixture.Db.Query(LockoutOf).Single();
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));

        await StatusAsync(fixture, StaffName, "wrong again");
        await StatusAsync(fixture, StaffName, StaffPassword);
        await StatusAsync(fixture, StaffName, "and again");

        Assert.Equal(lockEnd, fixture.Db.Query(LockoutOf).Single());
        Assert.Equal(new[] { "0" }, fixture.Db.Query("SELECT c.FailedLoginCount FROM UserCredentials c JOIN Users u ON u.Id = c.UserId WHERE u.Username = 'staff.one'"));

        // Three minutes after the first lock began it ends, exactly as if nothing had been tried meanwhile.
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Equal(SignInStatus.Succeeded, await StatusAsync(fixture, StaffName, StaffPassword));
    }

    [Fact]
    public async Task A_successful_sign_in_resets_the_failed_attempt_counter()
    {
        var (fixture, _) = await WithStaffAsync();
        using var _ = fixture;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await StatusAsync(fixture, StaffName, "wrong");
        }

        Assert.Equal(SignInStatus.Succeeded, await StatusAsync(fixture, StaffName, StaffPassword));

        // Four more failures are still below the limit because the counter started again: the correct password still works.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await StatusAsync(fixture, StaffName, "wrong");
        }

        Assert.Equal(SignInStatus.Succeeded, await StatusAsync(fixture, StaffName, StaffPassword));
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
        await fixture.Services.Authentication.SignInAsync(StaffName, "wrong", Token);

        // The fifth failure, from either instance, locked the account for both.
        Assert.Equal(SignInStatus.Failed, (await fixture.Services.Authentication.SignInAsync(StaffName, StaffPassword, Token)).Status);
        Assert.Equal(SignInStatus.Failed, (await other.Authentication.SignInAsync(StaffName, StaffPassword, Token)).Status);
    }

    [Fact]
    public async Task Failures_are_counted_per_user()
    {
        var (fixture, admin) = await WithStaffAsync();
        using var _ = fixture;
        await fixture.CreateUserAsync(admin, "staff.two", StaffPassword, cancellationToken: Token);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await StatusAsync(fixture, StaffName, "wrong");
        }

        Assert.Equal(SignInStatus.Succeeded, await StatusAsync(fixture, "staff.two", StaffPassword));
        Assert.Equal(SignInStatus.Succeeded, await StatusAsync(fixture, StaffName, StaffPassword));
    }

    [Fact]
    public async Task Simultaneous_failed_attempts_are_all_counted()
    {
        var (fixture, _) = await WithStaffAsync();
        using var _ = fixture;

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            fixture.Services.Authentication.SignInAsync(StaffName, "wrong " + i, Token)));

        // No update is lost: exactly five counted failures lock the account (four would leave it unlocked).
        Assert.All(results, r => Assert.Equal(SignInStatus.Failed, r.Status));
        Assert.NotEqual("NULL", fixture.Db.Query(LockoutOf).Single());
        Assert.Equal(SignInStatus.Failed, await StatusAsync(fixture, StaffName, StaffPassword));
    }

    [Fact]
    public async Task A_hash_below_the_configured_work_factor_is_upgraded_after_a_successful_sign_in_only()
    {
        var (fixture, _) = await WithStaffAsync();
        using var _ = fixture;
        const string WorkFactor = "SELECT c.PasswordWorkFactor FROM UserCredentials c JOIN Users u ON u.Id = c.UserId WHERE u.Username = 'staff.one'";
        Assert.Equal(new[] { "310000" }, fixture.Db.Query(WorkFactor));

        fixture.UsePolicy(new AuthenticationPolicy { Pbkdf2Iterations = 320_000 });

        // A failed attempt upgrades nothing.
        await StatusAsync(fixture, StaffName, "wrong");
        Assert.Equal(new[] { "310000" }, fixture.Db.Query(WorkFactor));

        Assert.Equal(SignInStatus.Succeeded, await StatusAsync(fixture, StaffName, StaffPassword));
        Assert.Equal(new[] { "320000" }, fixture.Db.Query(WorkFactor));
        Assert.Contains(AuditActions.PasswordHashUpgraded, fixture.AuditActionsInOrder());

        // The upgraded hash still verifies, and its parameters are the ones stored with it.
        Assert.Equal(SignInStatus.Succeeded, await StatusAsync(fixture, StaffName, StaffPassword));
        Assert.Equal(new[] { "PBKDF2-HMAC-SHA256|1" }, fixture.Db.Query("SELECT c.PasswordAlgorithm || '|' || c.PasswordHashVersion FROM UserCredentials c JOIN Users u ON u.Id = c.UserId WHERE u.Username = 'staff.one'"));
    }

    [Fact]
    public async Task Security_events_are_audited_without_secrets_or_the_attempted_name()
    {
        var (fixture, _) = await WithStaffAsync();
        using var _ = fixture;

        await StatusAsync(fixture, StaffName, "Wrong-Guess-Password-9");
        await StatusAsync(fixture, "someone.else", "Another-Wrong-Guess-7");
        await StatusAsync(fixture, StaffName, StaffPassword);

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
    public async Task Display_names_never_drive_authorization_only_the_stable_codes_do()
    {
        var (fixture, admin) = await WithStaffAsync();
        using var _ = fixture;
        await fixture.CreateUserAsync(admin, "qc.user", StaffPassword, DepartmentCodesForTests.Qc, RoleCodesForTests.Staff, Token);
        var before = await fixture.SignInAsync("qc.user", StaffPassword, Token);

        // The company renames the departments and roles (Decision Log #30 allows it) - even to the label of another department.
        fixture.Db.Execute("UPDATE Departments SET Name = 'Planning' WHERE Code = 'QC'");
        fixture.Db.Execute("UPDATE Roles SET Name = 'ADMIN' WHERE Code = 'STAFF'");
        var after = await fixture.SignInAsync("qc.user", StaffPassword, Token);

        Assert.Equal(before.DepartmentCode, after.DepartmentCode);
        Assert.Equal(before.RoleCode, after.RoleCode);
        Assert.Equal(DepartmentCodesForTests.Qc, after.DepartmentCode);
        Assert.True(AuthorizationRules.IsAllowed(after, Permission.InspectOrCorrectQc));
        Assert.False(AuthorizationRules.IsAllowed(after, Permission.AdministerUsers));
        Assert.False(AuthorizationRules.IsAllowed(after, Permission.ChangePlanningPriority));

        // And a non-administrator cannot administer accounts however the roles are labelled.
        Assert.Equal(AccountStatus.NotAuthorized, (await fixture.Services.Accounts.SetActiveAsync(after, admin.UserId, false, Token)).Status);
    }

    [Fact]
    public async Task The_services_refuse_a_policy_below_the_safety_floor()
    {
        using var fixture = await SecurityFixture.CreateAsync();

        var tooWeak = new AuthenticationPolicy { Pbkdf2Iterations = 1000 };

        Assert.Throws<InvalidAuthenticationPolicyException>(() => SecurityServices.Create(fixture.Db.ContextFactory, tooWeak));
    }
}

// The stable codes of the Domain, under short names for the assertions above.
internal static class DepartmentCodesForTests
{
    public const string System = ERP.MES.Domain.Identity.DepartmentCodes.System;
    public const string Qc = ERP.MES.Domain.Identity.DepartmentCodes.Qc;
}

internal static class RoleCodesForTests
{
    public const string Admin = ERP.MES.Domain.Identity.RoleCodes.Admin;
    public const string Staff = ERP.MES.Domain.Identity.RoleCodes.Staff;
}
