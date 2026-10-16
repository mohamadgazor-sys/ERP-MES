using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;

namespace ERP.MES.Infrastructure.Tests;

// Administrator recovery (Decision Log #47; Milestone 1 plan 5.7, acceptance tests 5.8): one-time token, new different password, no backdoor.
public class RecoveryTests
{
    private const string AlphaName = "admin.alpha";
    private const string AlphaPassword = "Alpha-Pass-Phrase-3";
    private const string BetaName = "admin.beta";
    private const string BetaPassword = "Beta-Pass-Phrase-4";
    private const string NewPassword = "Brand-New-Pass-Phrase-5";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed record Scene(SecurityFixture Fixture, Principal Alpha, Principal Beta);

    // Two administrators; alpha is the one whose access is lost.
    private static async Task<Scene> TwoAdministratorsAsync()
    {
        var fixture = await SecurityFixture.CreateAsync();
        var alpha = await fixture.BootstrapAdminAsync(AlphaName, AlphaPassword, Token);
        await fixture.CreateUserAsync(alpha, BetaName, BetaPassword, DepartmentCodes.System, RoleCodes.Admin, Token);
        var beta = await fixture.SignInAsync(BetaName, BetaPassword, Token);
        return new Scene(fixture, alpha, beta);
    }

    private static string Row(SecurityFixture fixture, string sql) => fixture.Db.Query(sql).Single();

    [Fact]
    public async Task Recovery_needs_an_initialised_system_a_reason_and_an_existing_administrator_account()
    {
        using var fresh = await SecurityFixture.CreateAsync();
        Assert.Equal(RecoveryStatus.NotInitialized, (await fresh.Services.Recovery.IssueTokenAsync("anyone", "lost access", Token)).Status);
        Assert.Equal(RecoveryStatus.NotInitialized, (await fresh.Services.Recovery.RedeemAsync("token", NewPassword, Token)).Status);

        var scene = await TwoAdministratorsAsync();
        using var fixture = scene.Fixture;
        await fixture.CreateUserAsync(scene.Alpha, "staff.one", "Staff-Pass-Phrase-2", cancellationToken: Token);

        Assert.Equal(RecoveryStatus.ReasonRequired, (await fixture.Services.Recovery.IssueTokenAsync(AlphaName, "  ", Token)).Status);
        Assert.Equal(RecoveryStatus.TargetNotAnAdministrator, (await fixture.Services.Recovery.IssueTokenAsync("nobody", "lost access", Token)).Status);
        Assert.Equal(RecoveryStatus.TargetNotAnAdministrator, (await fixture.Services.Recovery.IssueTokenAsync("staff.one", "lost access", Token)).Status);
        Assert.False(await fixture.Services.Recovery.IsRecoveryPendingAsync(Token));
    }

    [Fact]
    public async Task A_recovery_token_is_pending_only_while_valid_and_only_its_digest_is_stored()
    {
        var scene = await TwoAdministratorsAsync();
        using var fixture = scene.Fixture;

        var issued = await fixture.Services.Recovery.IssueTokenAsync(AlphaName, "Administrator password forgotten", Token);

        Assert.Equal(RecoveryStatus.Succeeded, issued.Status);
        Assert.True(await fixture.Services.Recovery.IsRecoveryPendingAsync(Token));
        Assert.Equal(32, int.Parse(Row(fixture, "SELECT length(RecoveryTokenHash) FROM SystemSetup"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain(issued.Token!, fixture.AllAuditText(), StringComparison.Ordinal);
        Assert.Contains("Administrator password forgotten", fixture.AllAuditText(), StringComparison.Ordinal);
        Assert.Equal(fixture.Clock.UtcNow.AddHours(24), issued.ExpiresAtUtc);

        fixture.Clock.Advance(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1));
        Assert.False(await fixture.Services.Recovery.IsRecoveryPendingAsync(Token));
    }

    [Fact]
    public async Task The_right_token_and_a_new_password_recover_the_account()
    {
        var scene = await TwoAdministratorsAsync();
        using var fixture = scene.Fixture;
        var accounts = fixture.Services.Accounts;

        // Alpha is locked out by guesses, then deactivated by the other administrator.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await fixture.Services.Authentication.SignInAsync(AlphaName, "wrong " + attempt, Token);
        }

        Assert.Equal(AccountStatus.Succeeded, (await accounts.SetActiveAsync(scene.Beta, scene.Alpha.UserId, false, Token)).Status);
        var issued = await fixture.Services.Recovery.IssueTokenAsync(AlphaName, "Alpha's password was lost", Token);

        // The account is demoted after the token was issued; recovery sets the Administrator role again.
        Assert.Equal(AccountStatus.Succeeded, (await accounts.ChangeRoleAsync(scene.Beta, scene.Alpha.UserId, RoleCodes.Staff, Token)).Status);
        var stampBefore = Row(fixture, "SELECT AdminChangeStamp FROM SystemSetup");

        var result = await fixture.Services.Recovery.RedeemAsync(issued.Token, NewPassword, Token);

        Assert.Equal(RecoveryStatus.Succeeded, result.Status);
        Assert.Equal("admin.alpha|1|ADMIN|0|NULL",
            Row(fixture, "SELECT u.Username || '|' || u.IsActive || '|' || r.Code || '|' || c.FailedLoginCount || '|' || COALESCE(c.LockoutEndUtc, 'NULL') FROM Users u JOIN UserCredentials c ON c.UserId = u.Id JOIN Roles r ON r.Id = u.RoleId WHERE u.Username = 'admin.alpha'"));
        Assert.Equal(new[] { "1" }, fixture.Db.Query("SELECT RecoveryTokenHash IS NULL FROM SystemSetup"));
        Assert.NotEqual(stampBefore, Row(fixture, "SELECT AdminChangeStamp FROM SystemSetup"));
        Assert.False(await fixture.Services.Recovery.IsRecoveryPendingAsync(Token));

        // The old password no longer works; the new one does, and it is not a one-time password.
        Assert.Equal(SignInStatus.Failed, (await fixture.Services.Authentication.SignInAsync(AlphaName, AlphaPassword, Token)).Status);
        var signedIn = await fixture.Services.Authentication.SignInAsync(AlphaName, NewPassword, Token);
        Assert.Equal(SignInStatus.Succeeded, signedIn.Status);
        Assert.False(signedIn.Principal!.MustChangePassword);

        var actions = fixture.AuditActionsInOrder();
        Assert.Contains(AuditActions.RecoveryTokenIssued, actions);
        Assert.Contains(AuditActions.RecoveryCompleted, actions);
    }

    [Fact]
    public async Task The_new_password_must_differ_from_the_old_one_and_the_token_stays_valid()
    {
        var scene = await TwoAdministratorsAsync();
        using var fixture = scene.Fixture;
        var issued = await fixture.Services.Recovery.IssueTokenAsync(AlphaName, "lost access", Token);

        var same = await fixture.Services.Recovery.RedeemAsync(issued.Token, AlphaPassword, Token);

        Assert.Equal(RecoveryStatus.PasswordUnchanged, same.Status);
        Assert.True(await fixture.Services.Recovery.IsRecoveryPendingAsync(Token));
        Assert.Equal(RecoveryStatus.Succeeded, (await fixture.Services.Recovery.RedeemAsync(issued.Token, NewPassword, Token)).Status);
    }

    [Fact]
    public async Task Without_the_token_nothing_is_learned_about_the_old_password()
    {
        var scene = await TwoAdministratorsAsync();
        using var fixture = scene.Fixture;
        await fixture.Services.Recovery.IssueTokenAsync(AlphaName, "lost access", Token);

        // A wrong token never reaches the "same as the old password" check, so it cannot be used to test password guesses.
        var guess = await fixture.Services.Recovery.RedeemAsync("0000", AlphaPassword, Token);

        Assert.Equal(RecoveryStatus.TokenInvalid, guess.Status);
    }

    [Fact]
    public async Task The_password_policy_is_enforced_without_using_up_an_attempt()
    {
        var scene = await TwoAdministratorsAsync();
        using var fixture = scene.Fixture;
        var issued = await fixture.Services.Recovery.IssueTokenAsync(AlphaName, "lost access", Token);

        var result = await fixture.Services.Recovery.RedeemAsync(issued.Token, "short", Token);

        Assert.Equal(RecoveryStatus.PasswordRejected, result.Status);
        Assert.Equal(PasswordPolicyError.TooShort, result.PasswordError);
        foreach (var withSpace in new[] { " sixsix", "six six", "sixsix " })
        {
            var rejected = await fixture.Services.Recovery.RedeemAsync(issued.Token, withSpace, Token);
            Assert.Equal(RecoveryStatus.PasswordRejected, rejected.Status);
            Assert.Equal(PasswordPolicyError.ContainsSpace, rejected.PasswordError);
        }

        Assert.Equal("0", Row(fixture, "SELECT RecoveryFailedAttempts FROM SystemSetup"));

        // Six characters are enough and nothing else is required; the token was not used up by the rejections.
        var accepted = await fixture.Services.Recovery.RedeemAsync(issued.Token, "sixsix", Token);
        Assert.Equal(RecoveryStatus.Succeeded, accepted.Status);
        Assert.Equal(SignInStatus.Succeeded, (await fixture.Services.Authentication.SignInAsync(AlphaName, "sixsix", Token)).Status);
    }

    [Fact]
    public async Task Wrong_tokens_are_counted_and_five_attempts_lock_recovery()
    {
        var scene = await TwoAdministratorsAsync();
        using var fixture = scene.Fixture;
        var issued = await fixture.Services.Recovery.IssueTokenAsync(AlphaName, "lost access", Token);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal(RecoveryStatus.TokenInvalid, (await fixture.Services.Recovery.RedeemAsync("wrong " + attempt, NewPassword, Token)).Status);
        }

        Assert.Equal("4", Row(fixture, "SELECT RecoveryFailedAttempts FROM SystemSetup"));
        Assert.Equal(RecoveryStatus.TokenExpiredOrLocked, (await fixture.Services.Recovery.RedeemAsync("wrong 5", NewPassword, Token)).Status);
        Assert.False(await fixture.Services.Recovery.IsRecoveryPendingAsync(Token));

        // The right token no longer works once the limit was reached.
        Assert.Equal(RecoveryStatus.NoRecoveryPending, (await fixture.Services.Recovery.RedeemAsync(issued.Token, NewPassword, Token)).Status);
        Assert.Equal(SignInStatus.Succeeded, (await fixture.Services.Authentication.SignInAsync(AlphaName, AlphaPassword, Token)).Status);
        Assert.Contains(AuditActions.RecoveryFailed, fixture.AuditActionsInOrder());
        Assert.Contains(AuditActions.RecoveryLocked, fixture.AuditActionsInOrder());
    }

    [Fact]
    public async Task An_expired_recovery_token_is_rejected()
    {
        var scene = await TwoAdministratorsAsync();
        using var fixture = scene.Fixture;
        var issued = await fixture.Services.Recovery.IssueTokenAsync(AlphaName, "lost access", Token);

        fixture.Clock.Advance(TimeSpan.FromHours(25));

        Assert.Equal(RecoveryStatus.TokenExpiredOrLocked, (await fixture.Services.Recovery.RedeemAsync(issued.Token, NewPassword, Token)).Status);
        Assert.Equal(SignInStatus.Succeeded, (await fixture.Services.Authentication.SignInAsync(AlphaName, AlphaPassword, Token)).Status);
    }

    [Fact]
    public async Task Issuing_again_invalidates_the_previous_recovery_token()
    {
        var scene = await TwoAdministratorsAsync();
        using var fixture = scene.Fixture;
        var first = await fixture.Services.Recovery.IssueTokenAsync(AlphaName, "lost access", Token);
        var second = await fixture.Services.Recovery.IssueTokenAsync(AlphaName, "lost access again", Token);

        Assert.Equal(RecoveryStatus.TokenInvalid, (await fixture.Services.Recovery.RedeemAsync(first.Token, NewPassword, Token)).Status);
        Assert.Equal(RecoveryStatus.Succeeded, (await fixture.Services.Recovery.RedeemAsync(second.Token, NewPassword, Token)).Status);
    }

    [Fact]
    public async Task Two_simultaneous_redemptions_of_one_token_have_exactly_one_winner()
    {
        var scene = await TwoAdministratorsAsync();
        using var fixture = scene.Fixture;
        var issued = await fixture.Services.Recovery.IssueTokenAsync(AlphaName, "lost access", Token);

        var results = await Task.WhenAll(
            fixture.Services.Recovery.RedeemAsync(issued.Token, "First-Winner-Pass-6", Token),
            fixture.Services.Recovery.RedeemAsync(issued.Token, "Second-Winner-Pass-7", Token));

        Assert.Equal(1, results.Count(r => r.Status == RecoveryStatus.Succeeded));
        Assert.Equal(1, fixture.AuditActionsInOrder().Count(a => a == AuditActions.RecoveryCompleted));
        var winnerPassword = results[0].Status == RecoveryStatus.Succeeded ? "First-Winner-Pass-6" : "Second-Winner-Pass-7";
        var loserPassword = results[0].Status == RecoveryStatus.Succeeded ? "Second-Winner-Pass-7" : "First-Winner-Pass-6";
        Assert.Equal(SignInStatus.Succeeded, (await fixture.Services.Authentication.SignInAsync(AlphaName, winnerPassword, Token)).Status);
        Assert.Equal(SignInStatus.Failed, (await fixture.Services.Authentication.SignInAsync(AlphaName, loserPassword, Token)).Status);
    }

    [Fact]
    public async Task There_is_no_universal_recovery_credential_and_no_secret_is_audited()
    {
        var scene = await TwoAdministratorsAsync();
        using var fixture = scene.Fixture;
        var issued = await fixture.Services.Recovery.IssueTokenAsync(AlphaName, "lost access", Token);
        await fixture.Services.Recovery.RedeemAsync("a wrong token", NewPassword, Token);
        await fixture.Services.Recovery.RedeemAsync(issued.Token, NewPassword, Token);

        // Neither another token form, a made-up recovery password nor the old token works afterwards.
        Assert.Equal(RecoveryStatus.NoRecoveryPending, (await fixture.Services.Recovery.RedeemAsync(issued.Token, "Another-New-Pass-Phrase-8", Token)).Status);
        var audit = fixture.AllAuditText();
        foreach (var secret in new[] { issued.Token!, issued.Token!.Replace("-", string.Empty, StringComparison.Ordinal), NewPassword, AlphaPassword, BetaPassword, "a wrong token" })
        {
            Assert.DoesNotContain(secret, audit, StringComparison.Ordinal);
        }
    }
}
