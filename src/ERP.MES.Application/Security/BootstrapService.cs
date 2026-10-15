namespace ERP.MES.Application.Security;

public enum BootstrapStatus
{
    Succeeded,

    /// <summary>The first administrator already exists; the bootstrap path is permanently closed.</summary>
    AlreadyInitialized,

    /// <summary>No bootstrap token is pending (the system has not been initialised, or the token was withdrawn).</summary>
    NotAwaitingBootstrap,

    TokenInvalid,

    /// <summary>The token had expired or the attempt limit was reached; the pending token was withdrawn and IT has to issue a new one.</summary>
    TokenExpiredOrLocked,

    UsernameRejected,
    DisplayNameRejected,
    PasswordRejected,
}

public sealed record IssuedToken(bool Issued, string? Token, DateTime? ExpiresAtUtc);

public sealed record BootstrapResult(BootstrapStatus Status, PasswordPolicyError PasswordError = PasswordPolicyError.None, UsernameError UsernameError = UsernameError.None);

/// <summary>
/// Secure one-time creation of the first administrator (Decision Log #39; Milestone 1 plan section 5). No credential exists in the repository, the application never
/// creates an administrator on its own, a token is issued out-of-band by an operator, only its digest is stored, attempts are counted centrally in the database
/// before the token is checked, and the claim is one conditional update of the setup row so exactly one of two simultaneous claims wins.
/// </summary>
public sealed class BootstrapService(
    ISecurityUnitRunner runner, IPasswordHasher hasher, IClock clock, IAuditContext auditContext, AuthenticationPolicy policy)
    : SecurityServiceBase(runner, hasher, clock, auditContext, policy)
{
    private sealed record Counted(BootstrapStatus Status, byte[]? Hash);

    public Task<SetupState> GetStateAsync(CancellationToken cancellationToken = default) =>
        Runner.RunAsync(async unit => (await unit.GetSetupAsync(cancellationToken)).State, cancellationToken);

    /// <summary>
    /// Issues (or re-issues) the bootstrap token. Re-issuing replaces the digest, the expiry and the attempt counter, so the previous token stops working.
    /// Refused once the system is initialised. The plaintext token is returned once, to the operator; it is never stored or logged.
    /// </summary>
    public Task<IssuedToken> IssueTokenAsync(TimeSpan? lifetime = null, CancellationToken cancellationToken = default)
    {
        var validFor = lifetime ?? TimeSpan.FromHours(Policy.BootstrapTokenLifetimeHours);
        if (validFor <= TimeSpan.Zero || validFor > TimeSpan.FromHours(AuthenticationPolicy.MaximumTokenLifetimeHours))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "The token lifetime must be positive and within the permitted maximum.");
        }

        return ConflictRetry.RunAsync(() => Runner.RunAsync(async unit =>
        {
            var setup = await unit.GetSetupAsync(cancellationToken);
            if (setup.State == SetupState.Initialized)
            {
                return new IssuedToken(false, null, null);
            }

            var (digest, text) = SecretTokens.Generate();
            var expires = Clock.UtcNow.Add(validFor);
            setup.IssueBootstrapToken(digest, expires);
            unit.AddAudit(Audit(AuditActions.AdminTokenIssued, null, "SystemSetup", "1", "A bootstrap token was issued; it expires at " + expires.ToString("u", System.Globalization.CultureInfo.InvariantCulture) + "."));
            await unit.SaveAsync(cancellationToken);
            return new IssuedToken(true, text, expires);
        }, cancellationToken));
    }

    public async Task<BootstrapResult> CompleteAsync(
        string? token, string? username, string? displayName, string? password, CancellationToken cancellationToken = default)
    {
        // Input that is wrong in itself is refused without using up an attempt and without saying anything about the token.
        var usernameError = UsernameRules.Validate(username);
        if (usernameError != UsernameError.None)
        {
            return new BootstrapResult(BootstrapStatus.UsernameRejected, UsernameError: usernameError);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return new BootstrapResult(BootstrapStatus.DisplayNameRejected);
        }

        var passwordError = PasswordRules.Validate(password, Policy);
        if (passwordError != PasswordPolicyError.None)
        {
            return new BootstrapResult(BootstrapStatus.PasswordRejected, passwordError);
        }

        var entered = SecretTokens.DigestOf(token);

        // 1. Count the attempt in its own short transaction BEFORE the token is checked, so failed guesses persist and the limit holds for every client.
        var counted = await ConflictRetry.RunAsync(() => Runner.RunAsync(async unit =>
        {
            var setup = await unit.GetSetupAsync(cancellationToken);
            if (setup.State == SetupState.Initialized)
            {
                return new Counted(BootstrapStatus.AlreadyInitialized, null);
            }

            if (setup.State != SetupState.AwaitingBootstrap)
            {
                return new Counted(BootstrapStatus.NotAwaitingBootstrap, null);
            }

            var attempts = setup.CountBootstrapAttempt();
            var expired = setup.BootstrapTokenExpiresAtUtc is not { } end || end <= Clock.UtcNow;
            if (expired || attempts > Policy.BootstrapMaxAttempts)
            {
                setup.LockBootstrap();
                unit.AddAudit(Audit(AuditActions.BootstrapLocked, null, "SystemSetup", "1", expired ? "The bootstrap token expired." : "The bootstrap attempt limit was exceeded."));
                await unit.SaveAsync(cancellationToken);
                return new Counted(BootstrapStatus.TokenExpiredOrLocked, null);
            }

            await unit.SaveAsync(cancellationToken);
            return new Counted(BootstrapStatus.Succeeded, setup.BootstrapTokenHash);
        }, cancellationToken));

        if (counted.Status != BootstrapStatus.Succeeded)
        {
            return new BootstrapResult(counted.Status);
        }

        // 2. Compare the digests in constant time.
        if (!SecretTokens.Matches(counted.Hash, entered))
        {
            return await ConflictRetry.RunAsync(() => Runner.RunAsync(async unit =>
            {
                var setup = await unit.GetSetupAsync(cancellationToken);
                unit.AddAudit(Audit(AuditActions.BootstrapFailed, null, "SystemSetup", "1", "A wrong bootstrap token was entered."));
                var locked = setup.State == SetupState.AwaitingBootstrap && setup.BootstrapFailedAttempts >= Policy.BootstrapMaxAttempts;
                if (locked)
                {
                    setup.LockBootstrap();
                    unit.AddAudit(Audit(AuditActions.BootstrapLocked, null, "SystemSetup", "1", "The bootstrap attempt limit was reached."));
                }

                await unit.SaveAsync(cancellationToken);
                return new BootstrapResult(locked ? BootstrapStatus.TokenExpiredOrLocked : BootstrapStatus.TokenInvalid);
            }, cancellationToken));
        }

        // 3. Claim: one transaction that closes the setup row (conditional on the version read) and inserts the administrator.
        var record = Hasher.Hash(password!, Policy.Pbkdf2Iterations);
        return await ConflictRetry.RunAsync(() => Runner.RunAsync(async unit =>
        {
            var setup = await unit.GetSetupAsync(cancellationToken);
            if (setup.State == SetupState.Initialized)
            {
                return new BootstrapResult(BootstrapStatus.AlreadyInitialized);
            }

            if (setup.State != SetupState.AwaitingBootstrap || !SecretTokens.Matches(setup.BootstrapTokenHash, entered))
            {
                return new BootstrapResult(BootstrapStatus.TokenInvalid);
            }

            var now = Clock.UtcNow;
            if (setup.BootstrapTokenExpiresAtUtc is not { } expiresAt || expiresAt <= now)
            {
                return new BootstrapResult(BootstrapStatus.TokenExpiredOrLocked);
            }

            var department = await unit.GetDepartmentAsync(DepartmentCodes.System, cancellationToken);
            var role = await unit.GetRoleAsync(RoleCodes.Admin, cancellationToken);
            setup.CompleteBootstrap(now);
            unit.AddUser(new UserAccount(username!, displayName!.Trim(), department, role, record, mustChangePassword: false, now));
            unit.AddAudit(Audit(AuditActions.BootstrapCompleted, null, "User", username, "The first administrator was created."));
            await unit.SaveAsync(cancellationToken);
            return new BootstrapResult(BootstrapStatus.Succeeded);
        }, cancellationToken));
    }
}
