using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;

namespace ERP.MES.Infrastructure.Tests;

// Account management: Admin-only, reloaded from the database, never deleting, and the last active administrator is protected (Decision Log #47; plan 5.7).
public class AccountAdministrationTests
{
    private const string StaffPassword = "Staff-Pass-Phrase-2";
    private const string OtherPassword = "Other-Pass-Phrase-9";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string Row(SecurityFixture fixture, string sql) => fixture.Db.Query(sql).Single();

    private static async Task<(SecurityFixture Fixture, Principal Admin)> StartAsync()
    {
        var fixture = await SecurityFixture.CreateAsync();
        return (fixture, await fixture.BootstrapAdminAsync(cancellationToken: Token));
    }

    [Fact]
    public async Task An_administrator_creates_an_account_with_a_one_time_password()
    {
        var (fixture, admin) = await StartAsync();
        using var _ = fixture;

        var id = await fixture.CreateUserAsync(admin, "planner.one", StaffPassword, DepartmentCodes.Planning, RoleCodes.Staff, Token);

        Assert.Equal("planner.one|PLANNING|STAFF|1|1",
            Row(fixture, "SELECT u.Username || '|' || d.Code || '|' || r.Code || '|' || u.IsActive || '|' || c.MustChangePassword FROM Users u JOIN UserCredentials c ON c.UserId = u.Id JOIN Departments d ON d.Id = u.DepartmentId JOIN Roles r ON r.Id = u.RoleId WHERE u.Username = 'planner.one'"));
        var signedIn = await fixture.Services.Authentication.SignInAsync("planner.one", StaffPassword, Token);
        Assert.Equal(id, signedIn.Principal!.UserId);
        Assert.True(signedIn.Principal.MustChangePassword);
        Assert.Contains(AuditActions.UserCreated, fixture.AuditActionsInOrder());
    }

    [Fact]
    public async Task Only_an_administrator_may_administer_accounts_even_in_the_system_department_or_with_a_manager_role()
    {
        var (fixture, admin) = await StartAsync();
        using var _ = fixture;
        await fixture.CreateUserAsync(admin, "manager.one", StaffPassword, DepartmentCodes.Management, RoleCodes.Manager, Token);
        await fixture.CreateUserAsync(admin, "system.staff", StaffPassword, DepartmentCodes.System, RoleCodes.Staff, Token);
        var manager = await fixture.SignInAsync("manager.one", StaffPassword, Token);
        var systemStaff = await fixture.SignInAsync("system.staff", StaffPassword, Token);

        foreach (var actor in new[] { manager, systemStaff })
        {
            Assert.Equal(AccountStatus.NotAuthorized, (await fixture.Services.Accounts.CreateUserAsync(actor, "x.user", "X", DepartmentCodes.Qc, RoleCodes.Staff, StaffPassword, Token)).Status);
            Assert.Equal(AccountStatus.NotAuthorized, (await fixture.Services.Accounts.SetActiveAsync(actor, admin.UserId, false, Token)).Status);
            Assert.Equal(AccountStatus.NotAuthorized, (await fixture.Services.Accounts.ChangeRoleAsync(actor, admin.UserId, RoleCodes.Staff, Token)).Status);
            Assert.Equal(AccountStatus.NotAuthorized, (await fixture.Services.Accounts.ResetPasswordAsync(actor, admin.UserId, OtherPassword, Token)).Status);
            Assert.Equal(AccountStatus.NotAuthorized, (await fixture.Services.Accounts.UnlockAsync(actor, admin.UserId, Token)).Status);
        }

        Assert.Equal(3, fixture.Db.Query("SELECT Id FROM Users").Count);
    }

    [Fact]
    public async Task The_acting_user_is_reloaded_so_a_stale_or_forged_principal_grants_nothing()
    {
        var (fixture, admin) = await StartAsync();
        using var _ = fixture;
        var secondId = await fixture.CreateUserAsync(admin, "admin.two", StaffPassword, DepartmentCodes.System, RoleCodes.Admin, Token);
        var second = await fixture.SignInAsync("admin.two", StaffPassword, Token);

        // The second administrator is deactivated; their old principal still says "ADMIN".
        Assert.Equal(AccountStatus.Succeeded, (await fixture.Services.Accounts.SetActiveAsync(admin, secondId, false, Token)).Status);
        Assert.Equal(RoleCodes.Admin, second.RoleCode);
        Assert.Equal(AccountStatus.NotAuthorized, (await fixture.Services.Accounts.CreateUserAsync(second, "x.user", "X", DepartmentCodes.Qc, RoleCodes.Staff, StaffPassword, Token)).Status);

        // A forged principal for a staff user who claims to be an administrator.
        var staffId = await fixture.CreateUserAsync(admin, "staff.one", StaffPassword, cancellationToken: Token);
        var forged = new Principal(staffId, "staff.one", "Staff", DepartmentCodes.System, RoleCodes.Admin, false);
        Assert.Equal(AccountStatus.NotAuthorized, (await fixture.Services.Accounts.SetActiveAsync(forged, admin.UserId, false, Token)).Status);
    }

    [Fact]
    public async Task Invalid_input_is_refused_and_user_names_are_unique_ignoring_case()
    {
        var (fixture, admin) = await StartAsync();
        using var _ = fixture;
        var accounts = fixture.Services.Accounts;
        await fixture.CreateUserAsync(admin, "Alice", StaffPassword, cancellationToken: Token);

        Assert.Equal(AccountStatus.UsernameTaken, (await accounts.CreateUserAsync(admin, "Alice", "A", DepartmentCodes.Qc, RoleCodes.Staff, StaffPassword, Token)).Status);

        // User names are unique ignoring case (Decision Log #62): a name that differs only in case is the same name.
        Assert.Equal(AccountStatus.UsernameTaken, (await accounts.CreateUserAsync(admin, "alice", "A", DepartmentCodes.Qc, RoleCodes.Staff, StaffPassword, Token)).Status);
        Assert.Equal(AccountStatus.UsernameTaken, (await accounts.CreateUserAsync(admin, "ALICE", "A", DepartmentCodes.Qc, RoleCodes.Staff, StaffPassword, Token)).Status);
        Assert.Equal(AccountStatus.Succeeded, (await accounts.CreateUserAsync(admin, "Bob", "B", DepartmentCodes.Qc, RoleCodes.Staff, StaffPassword, Token)).Status);
        Assert.Equal("Bob", Row(fixture, "SELECT Username FROM Users WHERE UsernameKey = 'BOB'"));
        Assert.Equal(UsernameError.Empty, (await accounts.CreateUserAsync(admin, "", "A", DepartmentCodes.Qc, RoleCodes.Staff, StaffPassword, Token)).UsernameError);
        Assert.Equal(UsernameError.LeadingOrTrailingWhitespace, (await accounts.CreateUserAsync(admin, " bob", "B", DepartmentCodes.Qc, RoleCodes.Staff, StaffPassword, Token)).UsernameError);
        Assert.Equal(AccountStatus.DisplayNameRejected, (await accounts.CreateUserAsync(admin, "bob", " ", DepartmentCodes.Qc, RoleCodes.Staff, StaffPassword, Token)).Status);
        Assert.Equal(AccountStatus.PasswordRejected, (await accounts.CreateUserAsync(admin, "bob", "B", DepartmentCodes.Qc, RoleCodes.Staff, "short", Token)).Status);
        foreach (var withSpace in new[] { " sixsix", "six six", "sixsix " })
        {
            var rejected = await accounts.CreateUserAsync(admin, "bob", "B", DepartmentCodes.Qc, RoleCodes.Staff, withSpace, Token);
            Assert.Equal(AccountStatus.PasswordRejected, rejected.Status);
            Assert.Equal(PasswordPolicyError.ContainsSpace, rejected.PasswordError);
        }

        Assert.Equal(AccountStatus.InvalidDepartmentOrRole, (await accounts.CreateUserAsync(admin, "bob", "B", "NOPE", RoleCodes.Staff, StaffPassword, Token)).Status);
        Assert.Equal(AccountStatus.InvalidDepartmentOrRole, (await accounts.CreateUserAsync(admin, "bob", "B", DepartmentCodes.Qc, "KING", StaffPassword, Token)).Status);
        Assert.Equal(3, fixture.Db.Query("SELECT Id FROM Users").Count);
    }

    [Fact]
    public async Task The_last_active_administrator_cannot_be_deactivated_or_demoted()
    {
        var (fixture, admin) = await StartAsync();
        using var _ = fixture;

        var deactivate = await fixture.Services.Accounts.SetActiveAsync(admin, admin.UserId, false, Token);
        var demote = await fixture.Services.Accounts.ChangeRoleAsync(admin, admin.UserId, RoleCodes.Staff, Token);

        Assert.Equal(AccountStatus.LastAdministrator, deactivate.Status);
        Assert.Equal(AccountStatus.LastAdministrator, demote.Status);
        Assert.Equal("1|ADMIN", Row(fixture, "SELECT u.IsActive || '|' || r.Code FROM Users u JOIN Roles r ON r.Id = u.RoleId"));
        Assert.Equal(2, fixture.AuditActionsInOrder().Count(a => a == AuditActions.LastAdminProtectionRefused));
    }

    [Fact]
    public async Task With_a_second_administrator_one_may_be_removed_and_then_the_remaining_one_is_protected()
    {
        var (fixture, admin) = await StartAsync();
        using var _ = fixture;
        var secondId = await fixture.CreateUserAsync(admin, "admin.two", StaffPassword, DepartmentCodes.System, RoleCodes.Admin, Token);

        Assert.Equal(AccountStatus.Succeeded, (await fixture.Services.Accounts.ChangeRoleAsync(admin, secondId, RoleCodes.Staff, Token)).Status);
        Assert.Equal(AccountStatus.LastAdministrator, (await fixture.Services.Accounts.SetActiveAsync(admin, admin.UserId, false, Token)).Status);

        // Re-promoting and then removing the first is possible again, and a non-administrator may be deactivated freely.
        Assert.Equal(AccountStatus.Succeeded, (await fixture.Services.Accounts.ChangeRoleAsync(admin, secondId, RoleCodes.Admin, Token)).Status);
        Assert.Equal(AccountStatus.Succeeded, (await fixture.Services.Accounts.SetActiveAsync(admin, secondId, false, Token)).Status);
        Assert.Equal(AccountStatus.Succeeded, (await fixture.Services.Accounts.SetActiveAsync(admin, secondId, true, Token)).Status);
        var stafferId = await fixture.CreateUserAsync(admin, "staff.one", StaffPassword, cancellationToken: Token);
        Assert.Equal(AccountStatus.Succeeded, (await fixture.Services.Accounts.SetActiveAsync(admin, stafferId, false, Token)).Status);
    }

    [Fact]
    public async Task Two_simultaneous_removals_can_never_leave_the_system_without_an_active_administrator()
    {
        for (var round = 0; round < 3; round++)
        {
            var (fixture, admin) = await StartAsync();
            using var _ = fixture;
            var secondId = await fixture.CreateUserAsync(admin, "admin.two", StaffPassword, DepartmentCodes.System, RoleCodes.Admin, Token);

            // The first administrator deactivates the second while demoting themself at the same time: each passes the check alone, both cannot succeed.
            var results = await Task.WhenAll(
                fixture.Services.Accounts.SetActiveAsync(admin, secondId, false, Token),
                fixture.Services.Accounts.ChangeRoleAsync(admin, admin.UserId, RoleCodes.Staff, Token));

            Assert.Equal(1, results.Count(r => r.Status == AccountStatus.Succeeded));
            Assert.All(results.Where(r => r.Status != AccountStatus.Succeeded), r => Assert.Contains(r.Status, new[] { AccountStatus.LastAdministrator, AccountStatus.NotAuthorized }));
            Assert.True(int.Parse(Row(fixture, "SELECT COUNT(*) FROM Users u JOIN Roles r ON r.Id = u.RoleId WHERE u.IsActive = 1 AND r.Code = 'ADMIN'"), System.Globalization.CultureInfo.InvariantCulture) >= 1);
        }
    }

    [Fact]
    public async Task An_administrator_resets_a_password_unlocks_an_account_and_the_user_must_choose_their_own()
    {
        var (fixture, admin) = await StartAsync();
        using var _ = fixture;
        var staffId = await fixture.CreateUserAsync(admin, "staff.one", StaffPassword, cancellationToken: Token);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await fixture.Services.Authentication.SignInAsync("staff.one", "wrong " + attempt, Token);
        }

        Assert.Equal(SignInStatus.Failed, (await fixture.Services.Authentication.SignInAsync("staff.one", StaffPassword, Token)).Status);
        Assert.Equal(AccountStatus.PasswordRejected, (await fixture.Services.Accounts.ResetPasswordAsync(admin, staffId, "short", Token)).Status);
        foreach (var withSpace in new[] { " sixsix", "six six", "sixsix " })
        {
            var rejected = await fixture.Services.Accounts.ResetPasswordAsync(admin, staffId, withSpace, Token);
            Assert.Equal(AccountStatus.PasswordRejected, rejected.Status);
            Assert.Equal(PasswordPolicyError.ContainsSpace, rejected.PasswordError);
        }

        Assert.Equal(AccountStatus.Succeeded, (await fixture.Services.Accounts.ResetPasswordAsync(admin, staffId, OtherPassword, Token)).Status);

        Assert.Equal(SignInStatus.Failed, (await fixture.Services.Authentication.SignInAsync("staff.one", StaffPassword, Token)).Status);
        var signedIn = await fixture.Services.Authentication.SignInAsync("staff.one", OtherPassword, Token);
        Assert.Equal(SignInStatus.Succeeded, signedIn.Status);
        Assert.True(signedIn.Principal!.MustChangePassword);

        // Unlock without a password change.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await fixture.Services.Authentication.SignInAsync("staff.one", "wrong " + attempt, Token);
        }

        Assert.Equal(SignInStatus.Failed, (await fixture.Services.Authentication.SignInAsync("staff.one", OtherPassword, Token)).Status);
        Assert.Equal(AccountStatus.Succeeded, (await fixture.Services.Accounts.UnlockAsync(admin, staffId, Token)).Status);
        Assert.Equal(SignInStatus.Succeeded, (await fixture.Services.Authentication.SignInAsync("staff.one", OtherPassword, Token)).Status);
        Assert.Equal(AccountStatus.NotFound, (await fixture.Services.Accounts.UnlockAsync(admin, 999_999, Token)).Status);
    }

    [Fact]
    public async Task A_user_changes_their_own_password_and_a_wrong_current_password_counts_as_a_failed_sign_in()
    {
        var (fixture, admin) = await StartAsync();
        using var _ = fixture;
        await fixture.CreateUserAsync(admin, "staff.one", StaffPassword, cancellationToken: Token);
        var user = await fixture.SignInAsync("staff.one", StaffPassword, Token);
        var accounts = fixture.Services.Accounts;

        Assert.Equal(AccountStatus.PasswordRejected, (await accounts.ChangeOwnPasswordAsync(user, StaffPassword, "short", Token)).Status);
        foreach (var withSpace in new[] { " sixsix", "six six", "sixsix " })
        {
            var rejected = await accounts.ChangeOwnPasswordAsync(user, StaffPassword, withSpace, Token);
            Assert.Equal(AccountStatus.PasswordRejected, rejected.Status);
            Assert.Equal(PasswordPolicyError.ContainsSpace, rejected.PasswordError);
        }

        Assert.Equal(AccountStatus.PasswordUnchanged, (await accounts.ChangeOwnPasswordAsync(user, StaffPassword, StaffPassword, Token)).Status);
        Assert.Equal(AccountStatus.WrongCurrentPassword, (await accounts.ChangeOwnPasswordAsync(user, "not my password", OtherPassword, Token)).Status);
        Assert.Equal("1", Row(fixture, "SELECT c.FailedLoginCount FROM UserCredentials c JOIN Users u ON u.Id = c.UserId WHERE u.Username = 'staff.one'"));

        Assert.Equal(AccountStatus.Succeeded, (await accounts.ChangeOwnPasswordAsync(user, StaffPassword, OtherPassword, Token)).Status);
        var after = await fixture.Services.Authentication.SignInAsync("staff.one", OtherPassword, Token);
        Assert.False(after.Principal!.MustChangePassword);
        Assert.Equal(SignInStatus.Failed, (await fixture.Services.Authentication.SignInAsync("staff.one", StaffPassword, Token)).Status);
        Assert.Contains(AuditActions.PasswordChanged, fixture.AuditActionsInOrder());

        // Five wrong current passwords lock the account, so the change form cannot be used to guess the password.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await accounts.ChangeOwnPasswordAsync(user, "wrong " + attempt, StaffPassword, Token);
        }

        Assert.Equal(AccountStatus.WrongCurrentPassword, (await accounts.ChangeOwnPasswordAsync(user, "wrong 5", StaffPassword, Token)).Status);
    }

    [Fact]
    public async Task Users_are_never_deleted_and_no_secret_is_audited()
    {
        var (fixture, admin) = await StartAsync();
        using var _ = fixture;
        var staffId = await fixture.CreateUserAsync(admin, "staff.one", StaffPassword, cancellationToken: Token);
        await fixture.Services.Accounts.ResetPasswordAsync(admin, staffId, OtherPassword, Token);
        await fixture.Services.Accounts.SetActiveAsync(admin, staffId, false, Token);

        Assert.Equal(2, fixture.Db.Query("SELECT Id FROM Users").Count);

        // The audit log refers to the users, so a user who acted cannot be removed from the table either.
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => fixture.Db.Execute("DELETE FROM Users WHERE Username = 'admin.one'"));

        var audit = fixture.AllAuditText();
        foreach (var secret in new[] { StaffPassword, OtherPassword, SecurityFixture.AdminPassword })
        {
            Assert.DoesNotContain(secret, audit, StringComparison.Ordinal);
        }
    }
}
