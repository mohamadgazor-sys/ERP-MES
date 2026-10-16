using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// The security side of the desktop session lifecycle (Decision Log #74): audit events, unlock with the same user's password, and the reload of the user at unlock.
// Real SQLite database, real services; no password ever appears in an audit entry.
public class SessionServiceTests
{
    private const string UserName = "session.user";
    private const string InitialPassword = "Staff-Pass-Phrase-2";
    private const string OwnPassword = "My-Own-Pass-Phrase-3";

    private const string CredentialOf = "SELECT {0} FROM UserCredentials c JOIN Users u ON u.Id = c.UserId WHERE u.Username = '" + UserName + "'";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed record Scene(SecurityFixture Fixture, Principal Admin, Principal User, long UserId);

    // A user who has already chosen their own password, so MustChangePassword is false.
    private static async Task<Scene> StartAsync(string department = DepartmentCodes.Planning, string role = RoleCodes.Staff, SecurityFixture? fixture = null)
    {
        fixture ??= await SecurityFixture.CreateAsync();
        var admin = await fixture.BootstrapAdminAsync(cancellationToken: Token);
        var id = await fixture.CreateUserAsync(admin, UserName, InitialPassword, department, role, Token);
        var first = await fixture.SignInAsync(UserName, InitialPassword, Token);
        Assert.True(first.MustChangePassword);
        Assert.Equal(AccountStatus.Succeeded, (await fixture.Services.Accounts.ChangeOwnPasswordAsync(first, InitialPassword, OwnPassword, Token)).Status);
        var user = await fixture.SignInAsync(UserName, OwnPassword, Token);
        Assert.False(user.MustChangePassword);
        return new Scene(fixture, admin, user, id);
    }

    private static string Credential(Scene scene, string column) => scene.Fixture.Db.Query(string.Format(System.Globalization.CultureInfo.InvariantCulture, CredentialOf, column)).Single();

    private static List<string> SessionAudit(SecurityFixture fixture) =>
        fixture.Db.Query("SELECT ActionCode || '|' || COALESCE(UserId, 'NULL') || '|' || Summary FROM AuditLog WHERE ActionCode LIKE 'Session%' OR ActionCode = 'ApplicationSessionEnded' ORDER BY Id");

    // ---- audit events ----

    [Theory]
    [InlineData(SessionLockReason.Inactivity, "Inactivity")]
    [InlineData(SessionLockReason.Manual, "Manual")]
    [InlineData(SessionLockReason.WindowsLock, "WindowsLock")]
    [InlineData(SessionLockReason.UserSessionSwitch, "UserSessionSwitch")]
    public async Task Locking_is_audited_with_its_reason(SessionLockReason reason, string expected)
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;

        await scene.Fixture.Services.Sessions.RecordLockedAsync(scene.User, reason, Token);

        var entry = Assert.Single(SessionAudit(scene.Fixture));
        Assert.StartsWith(AuditActions.SessionLocked + "|" + scene.UserId + "|", entry, StringComparison.Ordinal);
        Assert.Contains("Reason: " + expected + ".", entry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_lock_reason_is_refused()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => scene.Fixture.Services.Sessions.RecordLockedAsync(scene.User, (SessionLockReason)99, Token));
        Assert.Empty(SessionAudit(scene.Fixture));
    }

    [Fact]
    public async Task Sign_out_user_switch_and_clean_shutdown_are_three_distinct_audit_events()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        var sessions = scene.Fixture.Services.Sessions;

        await sessions.RecordSignedOutAsync(scene.User, Token);
        await sessions.RecordUserSwitchedAsync(scene.User, Token);
        await sessions.RecordApplicationSessionEndedAsync(scene.User, Token);

        var actions = SessionAudit(scene.Fixture).Select(e => e.Split('|')[0]).ToArray();
        Assert.Equal(new[] { AuditActions.SessionSignedOut, AuditActions.SessionUserSwitched, AuditActions.ApplicationSessionEnded }, actions);
        Assert.Equal(3, actions.Distinct().Count());
        Assert.Empty(scene.Fixture.Db.Query("SELECT Id FROM AuditLog WHERE ActionCode LIKE 'Session%' AND (MachineName <> 'TEST-PC' OR OsUserName <> 'test.operator')"));
    }

    [Fact]
    public async Task Session_audit_entries_never_contain_a_password_hash_or_token()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        var sessions = scene.Fixture.Services.Sessions;

        await sessions.RecordLockedAsync(scene.User, SessionLockReason.Manual, Token);
        await sessions.UnlockAsync(scene.User, "Wrong-Guess-Password-9", Token);
        await sessions.UnlockAsync(scene.User, OwnPassword, Token);
        await sessions.RecordSignedOutAsync(scene.User, Token);
        await sessions.RecordApplicationSessionEndedAsync(scene.User, Token);

        var audit = scene.Fixture.AllAuditText();
        foreach (var secret in new[] { "Wrong-Guess-Password-9", OwnPassword, InitialPassword, SecurityFixture.AdminPassword })
        {
            Assert.DoesNotContain(secret, audit, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(Credential(scene, "hex(c.PasswordHash)"), audit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Nothing_is_audited_or_stored_just_because_a_session_exists()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;

        // No session, token or login table exists: the session is in memory only, so nothing can survive a restart.
        var tables = scene.Fixture.Db.Query("SELECT name FROM sqlite_master WHERE type = 'table'");
        Assert.DoesNotContain(tables, t => t.Contains("session", StringComparison.OrdinalIgnoreCase) || t.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(SessionAudit(scene.Fixture));
    }

    // ---- unlock ----

    [Fact]
    public async Task A_correct_password_unlocks_and_returns_the_user_as_loaded_from_the_database_now()
    {
        var scene = await StartAsync(DepartmentCodes.Management, RoleCodes.Manager);
        using var _ = scene.Fixture;
        Assert.Equal(RoleCodes.Manager, scene.User.RoleCode);

        // Meanwhile the role and the department change in the database.
        Assert.Equal(AccountStatus.Succeeded, (await scene.Fixture.Services.Accounts.ChangeRoleAsync(scene.Admin, scene.UserId, RoleCodes.Staff, Token)).Status);
        scene.Fixture.Db.Execute("UPDATE Users SET DepartmentId = (SELECT Id FROM Departments WHERE Code = '" + DepartmentCodes.Qc + "') WHERE Username = '" + UserName + "'");

        var result = await scene.Fixture.Services.Sessions.UnlockAsync(scene.User, OwnPassword, Token);

        Assert.Equal(UnlockStatus.Succeeded, result.Status);
        Assert.Equal(RoleCodes.Staff, result.Principal!.RoleCode);
        Assert.Equal(DepartmentCodes.Qc, result.Principal.DepartmentCode);
        Assert.Equal(scene.UserId, result.Principal.UserId);
        Assert.False(result.Principal.MustChangePassword);
        Assert.Equal(AuditActions.SessionUnlocked, SessionAudit(scene.Fixture).Single().Split('|')[0]);
        Assert.Contains(AuditActions.UserRoleChanged, scene.Fixture.AuditActionsInOrder());
    }

    [Fact]
    public async Task A_correct_unlock_clears_earlier_failed_attempts()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        var sessions = scene.Fixture.Services.Sessions;

        Assert.Equal(UnlockStatus.Failed, (await sessions.UnlockAsync(scene.User, "wrong-one", Token)).Status);
        Assert.Equal(UnlockStatus.Failed, (await sessions.UnlockAsync(scene.User, "wrong-two", Token)).Status);
        Assert.Equal("2", Credential(scene, "c.FailedLoginCount"));

        Assert.Equal(UnlockStatus.Succeeded, (await sessions.UnlockAsync(scene.User, OwnPassword, Token)).Status);
        Assert.Equal("0", Credential(scene, "c.FailedLoginCount"));
    }

    [Fact]
    public async Task A_wrong_password_keeps_the_session_locked_and_is_counted_like_a_failed_sign_in()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;

        var result = await scene.Fixture.Services.Sessions.UnlockAsync(scene.User, "wrong-password", Token);

        Assert.Equal(UnlockStatus.Failed, result.Status);
        Assert.Null(result.Principal);
        Assert.Equal("1", Credential(scene, "c.FailedLoginCount"));
        Assert.Contains(AuditActions.LoginFailed, scene.Fixture.AuditActionsInOrder());
        Assert.Empty(SessionAudit(scene.Fixture));
    }

    [Fact]
    public async Task An_empty_password_is_refused_without_counting_and_without_a_password_check()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        var counting = new CountingHasher(new Pbkdf2PasswordHasher());
        scene.Fixture.UseHasher(counting);

        Assert.Equal(UnlockStatus.Failed, (await scene.Fixture.Services.Sessions.UnlockAsync(scene.User, string.Empty, Token)).Status);
        Assert.Equal(UnlockStatus.Failed, (await scene.Fixture.Services.Sessions.UnlockAsync(scene.User, null, Token)).Status);

        Assert.Equal("0", Credential(scene, "c.FailedLoginCount"));
        Assert.Equal(0, counting.VerifyCalls);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_trigger_the_existing_lockout_and_end_the_session()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        var sessions = scene.Fixture.Services.Sessions;
        var attempts = scene.Fixture.Policy.MaxFailedAttempts;

        for (var i = 1; i < attempts; i++)
        {
            Assert.Equal(UnlockStatus.Failed, (await sessions.UnlockAsync(scene.User, "wrong-" + i, Token)).Status);
        }

        // The last allowed attempt locks the account, and the session cannot continue.
        Assert.Equal(UnlockStatus.SessionEnded, (await sessions.UnlockAsync(scene.User, "wrong-last", Token)).Status);
        Assert.Contains(AuditActions.AccountLocked, scene.Fixture.AuditActionsInOrder());
        var lockout = Credential(scene, "COALESCE(c.LockoutEndUtc, 'NULL')");
        Assert.NotEqual("NULL", lockout);

        // Not even the right password gets in while the account is locked, and further attempts do not extend the lock.
        scene.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(UnlockStatus.SessionEnded, (await sessions.UnlockAsync(scene.User, OwnPassword, Token)).Status);
        Assert.Equal(UnlockStatus.SessionEnded, (await sessions.UnlockAsync(scene.User, "wrong-again", Token)).Status);
        Assert.Equal(lockout, Credential(scene, "COALESCE(c.LockoutEndUtc, 'NULL')"));
        Assert.Equal(SignInStatus.Failed, (await scene.Fixture.Services.Authentication.SignInAsync(UserName, OwnPassword, Token)).Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_deactivated_account_ends_the_session_whatever_the_password_and_counts_nothing(bool correctPassword)
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        Assert.Equal(AccountStatus.Succeeded, (await scene.Fixture.Services.Accounts.SetActiveAsync(scene.Admin, scene.UserId, false, Token)).Status);

        var result = await scene.Fixture.Services.Sessions.UnlockAsync(scene.User, correctPassword ? OwnPassword : "wrong-password", Token);

        Assert.Equal(UnlockStatus.SessionEnded, result.Status);
        Assert.Null(result.Principal);
        Assert.Equal("0", Credential(scene, "c.FailedLoginCount"));
        Assert.DoesNotContain(AuditActions.SessionUnlocked, scene.Fixture.AuditActionsInOrder());
    }

    [Fact]
    public async Task A_user_who_no_longer_exists_ends_the_session()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;

        var result = await scene.Fixture.Services.Sessions.UnlockAsync(scene.User with { UserId = 987_654 }, OwnPassword, Token);

        Assert.Equal(UnlockStatus.SessionEnded, result.Status);
    }

    [Fact]
    public async Task A_mandatory_password_change_is_reported_before_normal_access_is_restored()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        Assert.Equal(AccountStatus.Succeeded, (await scene.Fixture.Services.Accounts.ResetPasswordAsync(scene.Admin, scene.UserId, InitialPassword, Token)).Status);

        var result = await scene.Fixture.Services.Sessions.UnlockAsync(scene.User, InitialPassword, Token);

        Assert.Equal(UnlockStatus.PasswordChangeRequired, result.Status);
        Assert.True(result.Principal!.MustChangePassword);
        Assert.Contains("password must be chosen", SessionAudit(scene.Fixture).Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_unlock_outcome_does_the_same_amount_of_password_work()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        var counting = new CountingHasher(new Pbkdf2PasswordHasher());
        scene.Fixture.UseHasher(counting);
        var sessions = scene.Fixture.Services.Sessions;

        async Task<int> VerificationsAsync(string password)
        {
            var before = counting.VerifyCalls;
            await sessions.UnlockAsync(scene.User, password, Token);
            return counting.VerifyCalls - before;
        }

        Assert.Equal(1, await VerificationsAsync("wrong-password"));
        Assert.Equal(1, await VerificationsAsync(OwnPassword));

        // A deactivated account and a locked-out account are verified once as well, so the time does not reveal which case it was.
        await scene.Fixture.Services.Accounts.SetActiveAsync(scene.Admin, scene.UserId, false, Token);
        Assert.Equal(1, await VerificationsAsync(OwnPassword));
        await scene.Fixture.Services.Accounts.SetActiveAsync(scene.Admin, scene.UserId, true, Token);
        for (var i = 0; i < scene.Fixture.Policy.MaxFailedAttempts; i++)
        {
            await sessions.UnlockAsync(scene.User, "wrong-" + i, Token);
        }

        Assert.Equal(1, await VerificationsAsync(OwnPassword));
    }

    [Fact]
    public async Task A_hash_below_the_configured_work_factor_is_upgraded_after_a_correct_unlock_only()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        Assert.Equal("310000", Credential(scene, "c.PasswordWorkFactor"));
        scene.Fixture.UsePolicy(new AuthenticationPolicy { Pbkdf2Iterations = 320_000 });

        await scene.Fixture.Services.Sessions.UnlockAsync(scene.User, "wrong-password", Token);
        Assert.Equal("310000", Credential(scene, "c.PasswordWorkFactor"));

        Assert.Equal(UnlockStatus.Succeeded, (await scene.Fixture.Services.Sessions.UnlockAsync(scene.User, OwnPassword, Token)).Status);
        Assert.Equal("320000", Credential(scene, "c.PasswordWorkFactor"));
        Assert.Contains(AuditActions.PasswordHashUpgraded, scene.Fixture.AuditActionsInOrder());
    }

    [Fact]
    public async Task Another_users_password_does_not_unlock_the_session()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;

        // The administrator's password is correct for the administrator, but the locked session belongs to the other user.
        var result = await scene.Fixture.Services.Sessions.UnlockAsync(scene.User, SecurityFixture.AdminPassword, Token);

        Assert.Equal(UnlockStatus.Failed, result.Status);
    }
}
