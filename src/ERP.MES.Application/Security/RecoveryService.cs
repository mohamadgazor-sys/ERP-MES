using ERP.MES.Domain.Identity;

namespace ERP.MES.Application.Security;

public enum RecoveryStatus
{
    Succeeded,

    /// <summary>Recovery is only possible for an initialised system.</summary>
    NotInitialized,

    /// <summary>No recovery token is pending.</summary>
    NoRecoveryPending,

    TokenInvalid,

    /// <summary>The token had expired or the attempt limit was reached; the pending token was withdrawn.</summary>
    TokenExpiredOrLocked,

    PasswordRejected,

    /// <summary>The new password equals the current one; the token stays valid.</summary>
    PasswordUnchanged,

    /// <summary>For issuing: the target account does not exist or is not an Administrator account.</summary>
    TargetNotAnAdministrator,

    ReasonRequired,
}

public sealed record RecoveryResult(RecoveryStatus Status, PasswordPolicyError PasswordError = PasswordPolicyError.None);

public sealed record IssuedRecovery(RecoveryStatus Status, string? Token, DateTime? ExpiresAtUtc);

/// <summary>
/// Administrator recovery (Decision Log #47; Milestone 1 plan 5.7). If all Administrator access is lost, an authorized operator with database access issues a
/// one-time recovery token for an existing Administrator account through Provisioning. There is no universal recovery password, master key or backdoor.
/// Whoever holds the token sets a NEW password (different from the old one) in the application; that reactivates the account, sets the Administrator role,
/// clears the lockout and erases the token. Everything is audited; neither token nor password is ever logged.
/// </summary>
public sealed class RecoveryService(
    ISecurityUnitRunner runner, IPasswordHasher hasher, IClock clock, IAuditContext auditContext, AuthenticationPolicy policy)
    : SecurityServiceBase(runner, hasher, clock, auditContext, policy)
{
    private sealed record Counted(RecoveryStatus Status, byte[]? Hash, long TargetUserId);

    /// <summary>True only while a valid, unexpired recovery token is pending: the login screen offers "use recovery token" only then.</summary>
    public Task<bool> IsRecoveryPendingAsync(CancellationToken cancellationToken = default) =>
        Runner.RunAsync(async unit => (await unit.GetSetupAsync(cancellationToken)).IsRecoveryPending(Clock.UtcNow, Policy.RecoveryMaxAttempts), cancellationToken);

    public async Task<IssuedRecovery> IssueTokenAsync(string? targetUsername, string? reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return new IssuedRecovery(RecoveryStatus.ReasonRequired, null, null);
        }

        return await ConflictRetry.RunAsync(() => Runner.RunAsync(async unit =>
        {
            var setup = await unit.GetSetupAsync(cancellationToken);
            if (setup.State != SetupState.Initialized)
            {
                return new IssuedRecovery(RecoveryStatus.NotInitialized, null, null);
            }

            var target = await unit.FindAccountByUsernameAsync(targetUsername, cancellationToken);
            if (target is null || target.User.Role.Code != RoleCodes.Admin)
            {
                return new IssuedRecovery(RecoveryStatus.TargetNotAnAdministrator, null, null);
            }

            var (digest, text) = SecretTokens.Generate();
            var expires = Clock.UtcNow.AddHours(Policy.RecoveryTokenLifetimeHours);
            setup.IssueRecoveryToken(digest, expires, target.User.Id);
            unit.AddAudit(Audit(AuditActions.RecoveryTokenIssued, null, "User", Key(target.User.Id), "A recovery token was issued. Reason: " + reason.Trim()));
            await unit.SaveAsync(cancellationToken);
            return new IssuedRecovery(RecoveryStatus.Succeeded, text, expires);
        }, cancellationToken));
    }

    public async Task<RecoveryResult> RedeemAsync(string? token, string? newPassword, CancellationToken cancellationToken = default)
    {
        var passwordError = PasswordRules.Validate(newPassword, Policy);
        if (passwordError != PasswordPolicyError.None)
        {
            return new RecoveryResult(RecoveryStatus.PasswordRejected, passwordError);
        }

        var entered = SecretTokens.DigestOf(token);

        // 1. Count the attempt centrally, in its own short transaction, before the token is checked.
        var counted = await ConflictRetry.RunAsync(() => Runner.RunAsync(async unit =>
        {
            var setup = await unit.GetSetupAsync(cancellationToken);
            if (setup.State != SetupState.Initialized)
            {
                return new Counted(RecoveryStatus.NotInitialized, null, 0);
            }

            if (setup.RecoveryTokenHash is null || setup.RecoveryTargetUserId is not { } targetId)
            {
                return new Counted(RecoveryStatus.NoRecoveryPending, null, 0);
            }

            var attempts = setup.CountRecoveryAttempt();
            var expired = setup.RecoveryTokenExpiresAtUtc is not { } end || end <= Clock.UtcNow;
            if (expired || attempts > Policy.RecoveryMaxAttempts)
            {
                setup.ClearRecoveryToken();
                unit.AddAudit(Audit(AuditActions.RecoveryLocked, null, "SystemSetup", "1", expired ? "The recovery token expired." : "The recovery attempt limit was exceeded."));
                await unit.SaveAsync(cancellationToken);
                return new Counted(RecoveryStatus.TokenExpiredOrLocked, null, 0);
            }

            await unit.SaveAsync(cancellationToken);
            return new Counted(RecoveryStatus.Succeeded, setup.RecoveryTokenHash, targetId);
        }, cancellationToken));

        if (counted.Status != RecoveryStatus.Succeeded)
        {
            return new RecoveryResult(counted.Status);
        }

        // 2. Constant-time comparison of the digests.
        if (!SecretTokens.Matches(counted.Hash, entered))
        {
            return await ConflictRetry.RunAsync(() => Runner.RunAsync(async unit =>
            {
                var setup = await unit.GetSetupAsync(cancellationToken);
                unit.AddAudit(Audit(AuditActions.RecoveryFailed, null, "SystemSetup", "1", "A wrong recovery token was entered."));
                var locked = setup.RecoveryTokenHash is not null && setup.RecoveryFailedAttempts >= Policy.RecoveryMaxAttempts;
                if (locked)
                {
                    setup.ClearRecoveryToken();
                    unit.AddAudit(Audit(AuditActions.RecoveryLocked, null, "SystemSetup", "1", "The recovery attempt limit was reached."));
                }

                await unit.SaveAsync(cancellationToken);
                return new RecoveryResult(locked ? RecoveryStatus.TokenExpiredOrLocked : RecoveryStatus.TokenInvalid);
            }, cancellationToken));
        }

        // 3. The token is valid. The new password must differ from the old one: this is checked only now, so a caller without the token learns nothing about the old password.
        var record = Hasher.Hash(newPassword!, Policy.Pbkdf2Iterations);
        return await ConflictRetry.RunAsync(() => Runner.RunAsync(async unit =>
        {
            var setup = await unit.GetSetupAsync(cancellationToken);
            if (setup.RecoveryTokenHash is null || !SecretTokens.Matches(setup.RecoveryTokenHash, entered) || setup.RecoveryTargetUserId != counted.TargetUserId)
            {
                return new RecoveryResult(RecoveryStatus.TokenInvalid);
            }

            var now = Clock.UtcNow;
            if (setup.RecoveryTokenExpiresAtUtc is not { } expiresAt || expiresAt <= now)
            {
                return new RecoveryResult(RecoveryStatus.TokenExpiredOrLocked);
            }

            var target = await unit.FindAccountByUserIdAsync(counted.TargetUserId, cancellationToken);
            if (target is null)
            {
                return new RecoveryResult(RecoveryStatus.TokenInvalid);
            }

            if (Hasher.Verify(newPassword!, target.PasswordRecord))
            {
                return new RecoveryResult(RecoveryStatus.PasswordUnchanged);
            }

            var administratorRole = await unit.GetRoleAsync(RoleCodes.Admin, cancellationToken);
            target.SetPassword(record, mustChangePassword: false, now);
            target.User.SetActive(true, now);
            target.User.AssignRole(administratorRole, now);
            target.ClearLockout(now);
            setup.ClearRecoveryToken();
            setup.AdvanceAdminChangeStamp();
            unit.AddAudit(Audit(AuditActions.RecoveryCompleted, target.User.Id, "User", Key(target.User.Id), "An administrator account was recovered with a one-time token."));
            await unit.SaveAsync(cancellationToken);
            return new RecoveryResult(RecoveryStatus.Succeeded);
        }, cancellationToken));
    }
}
