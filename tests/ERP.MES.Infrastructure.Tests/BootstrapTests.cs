using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ERP.MES.Application.Security;

namespace ERP.MES.Infrastructure.Tests;

// One-time administrator bootstrap (Decision Log #39; Milestone 1 plan section 5, acceptance tests 5.8) on a real SQLite database.
public partial class BootstrapTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex("^[0-9a-f]{8}(-[0-9a-f]{8}){7}$")]
    private static partial Regex TokenShape();

    private static string State(SecurityFixture fixture) => fixture.Db.Query("SELECT State FROM SystemSetup").Single();

    private static string Attempts(SecurityFixture fixture) => fixture.Db.Query("SELECT BootstrapFailedAttempts FROM SystemSetup").Single();

    private static Task<BootstrapResult> Complete(SecurityFixture fixture, string? token, string username = "admin.one", string password = SecurityFixture.AdminPassword) =>
        fixture.Services.Bootstrap.CompleteAsync(token, username, "Test Administrator", password, Token);

    [Fact]
    public async Task A_freshly_migrated_database_is_not_provisioned_and_has_no_users_but_has_the_seeded_departments_and_roles()
    {
        using var fixture = await SecurityFixture.CreateAsync();

        Assert.Equal("NotProvisioned", State(fixture));
        Assert.Equal(SetupState.NotProvisioned, await fixture.Services.Bootstrap.GetStateAsync(Token));
        Assert.Empty(fixture.Db.Query("SELECT Id FROM Users"));
        Assert.Equal(DepartmentCodes.All.OrderBy(c => c, StringComparer.Ordinal), fixture.Db.Query("SELECT Code FROM Departments ORDER BY Code"));
        Assert.Equal(RoleCodes.All.OrderBy(c => c, StringComparer.Ordinal), fixture.Db.Query("SELECT Code FROM Roles ORDER BY Code"));
    }

    [Fact]
    public async Task Nobody_can_claim_the_administrator_without_an_issued_token()
    {
        using var fixture = await SecurityFixture.CreateAsync();

        var result = await Complete(fixture, "00000000-00000000-00000000-00000000-00000000-00000000-00000000-00000000");

        Assert.Equal(BootstrapStatus.NotAwaitingBootstrap, result.Status);
        Assert.Equal("NotProvisioned", State(fixture));
        Assert.Empty(fixture.Db.Query("SELECT Id FROM Users"));
    }

    [Fact]
    public async Task A_token_is_random_shown_once_and_only_its_sha256_digest_is_stored()
    {
        using var fixture = await SecurityFixture.CreateAsync();

        var first = await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token);
        var second = await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token);

        Assert.True(first.Issued);
        Assert.Matches(TokenShape(), first.Token!);
        Assert.NotEqual(first.Token, second.Token);
        Assert.Equal("AwaitingBootstrap", State(fixture));

        var tokenBytes = Convert.FromHexString(second.Token!.Replace("-", string.Empty, StringComparison.Ordinal));
        Assert.Equal(32, tokenBytes.Length);
        var stored = fixture.Db.Query("SELECT hex(BootstrapTokenHash) FROM SystemSetup").Single();
        Assert.Equal(Convert.ToHexString(SHA256.HashData(tokenBytes)), stored);
        Assert.NotEqual(Convert.ToHexString(tokenBytes), stored);
        Assert.DoesNotContain(second.Token, fixture.AllAuditText(), StringComparison.Ordinal);
        Assert.Equal(fixture.Clock.UtcNow.AddHours(24), second.ExpiresAtUtc);
    }

    [Fact]
    public async Task The_correct_token_creates_exactly_one_administrator_in_the_system_department_and_closes_the_bootstrap_path()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        var issued = await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token);

        var result = await Complete(fixture, issued.Token);

        Assert.Equal(BootstrapStatus.Succeeded, result.Status);
        Assert.Equal("Initialized", State(fixture));
        Assert.Equal(SetupState.Initialized, await fixture.Services.Bootstrap.GetStateAsync(Token));
        Assert.Equal(new[] { "1" }, fixture.Db.Query("SELECT BootstrapTokenHash IS NULL FROM SystemSetup"));
        Assert.Equal(new[] { "admin.one|SYSTEM|ADMIN|1" },
            fixture.Db.Query("SELECT u.Username || '|' || d.Code || '|' || r.Code || '|' || u.IsActive FROM Users u JOIN Departments d ON d.Id = u.DepartmentId JOIN Roles r ON r.Id = u.RoleId"));

        // The same token cannot be used again, and a new token can no longer be issued.
        Assert.Equal(BootstrapStatus.AlreadyInitialized, (await Complete(fixture, issued.Token, "admin.two")).Status);
        Assert.False((await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token)).Issued);
        Assert.Equal("Initialized", State(fixture));
        Assert.Single(fixture.Db.Query("SELECT Id FROM Users"));
        Assert.Equal(new[] { AuditActions.AdminTokenIssued, AuditActions.BootstrapCompleted }, fixture.AuditActionsInOrder());
    }

    [Fact]
    public async Task The_administrator_password_is_stored_only_as_a_salted_hash_with_its_parameters()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        var issued = await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token);
        await Complete(fixture, issued.Token);

        var row = fixture.Db.Query("SELECT length(PasswordHash) || '|' || length(PasswordSalt) || '|' || PasswordAlgorithm || '|' || PasswordHashVersion || '|' || PasswordWorkFactor || '|' || MustChangePassword FROM Users").Single();
        Assert.Equal("32|16|PBKDF2-HMAC-SHA256|1|310000|0", row);
        Assert.DoesNotContain(SecurityFixture.AdminPassword, fixture.AllAuditText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_token_is_rejected_and_counted_in_the_database()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token);

        var result = await Complete(fixture, "ffffffff-ffffffff-ffffffff-ffffffff-ffffffff-ffffffff-ffffffff-ffffffff");
        var garbage = await Complete(fixture, "not a token");

        Assert.Equal(BootstrapStatus.TokenInvalid, result.Status);
        Assert.Equal(BootstrapStatus.TokenInvalid, garbage.Status);
        Assert.Equal("2", Attempts(fixture));
        Assert.Equal("AwaitingBootstrap", State(fixture));
        Assert.Empty(fixture.Db.Query("SELECT Id FROM Users"));
        Assert.Equal(2, fixture.AuditActionsInOrder().Count(a => a == AuditActions.BootstrapFailed));
    }

    [Fact]
    public async Task Five_attempts_are_allowed_so_the_fifth_may_still_succeed()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        var issued = await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal(BootstrapStatus.TokenInvalid, (await Complete(fixture, "wrong token " + attempt)).Status);
        }

        Assert.Equal(BootstrapStatus.Succeeded, (await Complete(fixture, issued.Token)).Status);
    }

    [Fact]
    public async Task Reaching_the_attempt_limit_locks_the_bootstrap_and_even_the_right_token_no_longer_works()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        var issued = await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal(BootstrapStatus.TokenInvalid, (await Complete(fixture, "wrong token " + attempt)).Status);
        }

        Assert.Equal(BootstrapStatus.TokenExpiredOrLocked, (await Complete(fixture, "wrong token 5")).Status);
        Assert.Equal("NotProvisioned", State(fixture));
        Assert.Equal(new[] { "1" }, fixture.Db.Query("SELECT BootstrapTokenHash IS NULL FROM SystemSetup"));

        Assert.Equal(BootstrapStatus.NotAwaitingBootstrap, (await Complete(fixture, issued.Token)).Status);
        Assert.Empty(fixture.Db.Query("SELECT Id FROM Users"));
        Assert.Contains(AuditActions.BootstrapLocked, fixture.AuditActionsInOrder());

        // IT can issue a new token afterwards.
        var again = await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token);
        Assert.Equal(BootstrapStatus.Succeeded, (await Complete(fixture, again.Token)).Status);
    }

    [Fact]
    public async Task An_expired_token_is_rejected_and_withdrawn()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        var issued = await fixture.Services.Bootstrap.IssueTokenAsync(TimeSpan.FromHours(1), Token);

        fixture.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        var result = await Complete(fixture, issued.Token);

        Assert.Equal(BootstrapStatus.TokenExpiredOrLocked, result.Status);
        Assert.Equal("NotProvisioned", State(fixture));
        Assert.Empty(fixture.Db.Query("SELECT Id FROM Users"));
    }

    [Fact]
    public async Task Issuing_again_invalidates_the_previous_token_and_resets_the_attempt_counter()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        var first = await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token);
        await Complete(fixture, "a wrong token");
        Assert.Equal("1", Attempts(fixture));

        var second = await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token);
        Assert.Equal("0", Attempts(fixture));

        Assert.Equal(BootstrapStatus.TokenInvalid, (await Complete(fixture, first.Token)).Status);
        Assert.Equal(BootstrapStatus.Succeeded, (await Complete(fixture, second.Token)).Status);
    }

    [Fact]
    public async Task Two_simultaneous_claims_with_a_valid_token_produce_exactly_one_administrator()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        var issued = await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token);

        var results = await Task.WhenAll(
            Complete(fixture, issued.Token, "admin.alpha", "Alpha-Pass-Phrase-3"),
            Complete(fixture, issued.Token, "admin.beta", "Beta-Pass-Phrase-4"));

        Assert.Equal(1, results.Count(r => r.Status == BootstrapStatus.Succeeded));
        Assert.Equal(1, results.Count(r => r.Status == BootstrapStatus.AlreadyInitialized));
        Assert.Single(fixture.Db.Query("SELECT Id FROM Users"));
        Assert.Equal("Initialized", State(fixture));
        Assert.Equal(1, fixture.AuditActionsInOrder().Count(a => a == AuditActions.BootstrapCompleted));
    }

    [Fact]
    public async Task The_password_policy_is_enforced_without_using_up_an_attempt()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        var issued = await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token);

        var tooShort = await Complete(fixture, issued.Token, password: "short-pass1");
        var empty = await Complete(fixture, issued.Token, password: string.Empty);

        Assert.Equal(BootstrapStatus.PasswordRejected, tooShort.Status);
        Assert.Equal(PasswordPolicyError.TooShort, tooShort.PasswordError);
        Assert.Equal(PasswordPolicyError.Empty, empty.PasswordError);
        Assert.Equal("0", Attempts(fixture));
        Assert.Equal("AwaitingBootstrap", State(fixture));

        // Exactly twelve characters is enough.
        Assert.Equal(BootstrapStatus.Succeeded, (await Complete(fixture, issued.Token, password: "twelve-chars")).Status);
    }

    [Fact]
    public async Task An_invalid_user_name_or_display_name_is_rejected_without_using_up_an_attempt()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        var issued = await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token);

        Assert.Equal(UsernameError.Empty, (await Complete(fixture, issued.Token, username: string.Empty)).UsernameError);
        Assert.Equal(UsernameError.WhitespaceOrControlCharacter, (await Complete(fixture, issued.Token, username: "admin one")).UsernameError);
        Assert.Equal(UsernameError.TooLong, (await Complete(fixture, issued.Token, username: new string('a', 65))).UsernameError);
        Assert.Equal(BootstrapStatus.DisplayNameRejected, (await fixture.Services.Bootstrap.CompleteAsync(issued.Token, "admin.one", "  ", SecurityFixture.AdminPassword, Token)).Status);
        Assert.Equal("0", Attempts(fixture));
    }

    [Fact]
    public async Task The_token_lifetime_must_be_positive_and_within_the_maximum()
    {
        using var fixture = await SecurityFixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Services.Bootstrap.IssueTokenAsync(TimeSpan.Zero, Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Services.Bootstrap.IssueTokenAsync(TimeSpan.FromDays(400), Token));
        Assert.Equal("NotProvisioned", State(fixture));
    }

    [Fact]
    public async Task Neither_the_token_nor_the_password_appears_in_the_audit_log()
    {
        using var fixture = await SecurityFixture.CreateAsync();
        var issued = await fixture.Services.Bootstrap.IssueTokenAsync(cancellationToken: Token);
        await Complete(fixture, "a wrong token with typed text");
        await Complete(fixture, issued.Token);

        var audit = fixture.AllAuditText();
        Assert.DoesNotContain(issued.Token!, audit, StringComparison.Ordinal);
        Assert.DoesNotContain(issued.Token!.Replace("-", string.Empty, StringComparison.Ordinal), audit, StringComparison.Ordinal);
        Assert.DoesNotContain("a wrong token with typed text", audit, StringComparison.Ordinal);
        Assert.DoesNotContain(SecurityFixture.AdminPassword, audit, StringComparison.Ordinal);
        Assert.Empty(fixture.Db.Query("SELECT Id FROM AuditLog WHERE MachineName <> 'TEST-PC'"));
    }
}
