namespace ERP.MES.Application.Security;

/// <summary>Why a desktop session was locked (Decision Log #74).</summary>
public enum SessionLockReason
{
    /// <summary>No keyboard or mouse input for the configured time.</summary>
    Inactivity = 1,

    /// <summary>The user chose "Lock now" (or Ctrl+L).</summary>
    Manual,

    /// <summary>Windows itself was locked.</summary>
    WindowsLock,

    /// <summary>The Windows user session was switched away (fast user switching, remote disconnect).</summary>
    UserSessionSwitch,
}

public enum UnlockStatus
{
    /// <summary>The password was correct and the user is still active. <see cref="UnlockResult.Principal"/> holds the user as freshly loaded from the database.</summary>
    Succeeded,

    /// <summary>The password was correct but the user must choose a new password first; normal access stays closed until they have.</summary>
    PasswordChangeRequired,

    /// <summary>The password was wrong (or empty) and the account is not locked out yet; the session stays locked.</summary>
    Failed,

    /// <summary>The session cannot continue: the account is deactivated, locked out (possibly by this very attempt) or no longer exists. The caller returns to sign-in.</summary>
    SessionEnded,
}

/// <summary>The outcome of an unlock attempt. <see cref="Principal"/> is set for <see cref="UnlockStatus.Succeeded"/> and <see cref="UnlockStatus.PasswordChangeRequired"/> only.</summary>
public sealed record UnlockResult(UnlockStatus Status, Principal? Principal);

/// <summary>
/// The security side of the desktop session lifecycle (Decision Log #74). There is no session token, no refresh token and nothing persisted: the session lives in
/// memory in the desktop application. This service records the session events in the append-only audit log and verifies the password of the same user to resume a
/// locked session, with exactly the mechanisms of sign-in: one password verification whatever the outcome, the same failed-attempt counter and lockout, a lock is
/// never extended by further attempts, and the hash is upgraded after a correct password. A successful unlock reloads the user's current status, department and role
/// from the database; the cached <see cref="Principal"/> is never authoritative.
/// </summary>
public sealed class SessionService(
    ISecurityUnitRunner runner, IPasswordHasher hasher, IClock clock, IAuditContext auditContext, AuthenticationPolicy policy)
    : SecurityServiceBase(runner, hasher, clock, auditContext, policy)
{
    public Task RecordSignedOutAsync(Principal user, CancellationToken cancellationToken = default) =>
        RecordAsync(user, AuditActions.SessionSignedOut, "Signed out.", cancellationToken);

    public Task RecordLockedAsync(Principal user, SessionLockReason reason, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown lock reason.");
        }

        return RecordAsync(user, AuditActions.SessionLocked, "Session locked. Reason: " + reason + ".", cancellationToken);
    }

    /// <summary>Another user signs in from the lock screen; the previous user's unsaved drafts were discarded after the user's confirmation.</summary>
    public Task RecordUserSwitchedAsync(Principal user, CancellationToken cancellationToken = default) =>
        RecordAsync(user, AuditActions.SessionUserSwitched, "Another user signed in from the lock screen; the session ended.", cancellationToken);

    /// <summary>The application was closed normally. Callers use this on a best-effort basis: it cannot be written after a crash, a forced termination or a power loss.</summary>
    public Task RecordApplicationSessionEndedAsync(Principal user, CancellationToken cancellationToken = default) =>
        RecordAsync(user, AuditActions.ApplicationSessionEnded, "The application was closed normally.", cancellationToken);

    /// <summary>Resumes a locked session with the same user's password. See the class summary for the rules.</summary>
    public Task<UnlockResult> UnlockAsync(Principal locked, string? password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locked);

        return ConflictRetry.RunAsync(() => Runner.RunAsync(async unit =>
        {
            var now = Clock.UtcNow;
            var account = await unit.FindAccountByUserIdAsync(locked.UserId, cancellationToken);
            if (account is null)
            {
                unit.AddAudit(Audit(AuditActions.LoginFailed, null, "User", null, "Unlock refused: the user no longer exists."));
                await unit.SaveAsync(cancellationToken);
                return new UnlockResult(UnlockStatus.SessionEnded, null);
            }

            var user = account.User;
            var userKey = Key(user.Id);
            if (string.IsNullOrEmpty(password))
            {
                unit.AddAudit(Audit(AuditActions.LoginFailed, user.Id, "User", userKey, "Unlock refused: empty credentials."));
                await unit.SaveAsync(cancellationToken);
                return new UnlockResult(UnlockStatus.Failed, null);
            }

            // Exactly one password verification in every remaining case, so the work done does not depend on the outcome.
            var passwordIsCorrect = Hasher.Verify(password, account.PasswordRecord);

            if (account.IsLockedOut(now))
            {
                // Nothing about the credential changes, so the lock is not extended.
                unit.AddAudit(Audit(AuditActions.LoginFailed, user.Id, "User", userKey, "Unlock refused: the account is locked."));
                await unit.SaveAsync(cancellationToken);
                return new UnlockResult(UnlockStatus.SessionEnded, null);
            }

            if (!user.IsActive)
            {
                unit.AddAudit(Audit(AuditActions.LoginFailed, user.Id, "User", userKey, "Unlock refused: the account is inactive."));
                await unit.SaveAsync(cancellationToken);
                return new UnlockResult(UnlockStatus.SessionEnded, null);
            }

            if (!passwordIsCorrect)
            {
                var lockedNow = account.RegisterFailedLogin(now, Policy);
                unit.AddAudit(Audit(AuditActions.LoginFailed, user.Id, "User", userKey, "Unlock refused: wrong password."));
                if (lockedNow)
                {
                    unit.AddAudit(Audit(AuditActions.AccountLocked, user.Id, "User", userKey, "The account was locked after too many failed attempts."));
                }

                await unit.SaveAsync(cancellationToken);
                return new UnlockResult(lockedNow ? UnlockStatus.SessionEnded : UnlockStatus.Failed, null);
            }

            account.RecordSuccessfulLogin(now);
            var summary = account.MustChangePassword
                ? "Session unlocked; a new password must be chosen before normal access."
                : "Session unlocked.";
            unit.AddAudit(Audit(AuditActions.SessionUnlocked, user.Id, "User", userKey, summary));
            if (Hasher.NeedsUpgrade(account.PasswordRecord, Policy.Pbkdf2Iterations))
            {
                account.UpgradeHash(Hasher.Hash(password, Policy.Pbkdf2Iterations), now);
                unit.AddAudit(Audit(AuditActions.PasswordHashUpgraded, user.Id, "User", userKey, "The password hash was upgraded to the current parameters."));
            }

            await unit.SaveAsync(cancellationToken);
            return new UnlockResult(account.MustChangePassword ? UnlockStatus.PasswordChangeRequired : UnlockStatus.Succeeded, ToPrincipal(account));
        }, cancellationToken));
    }

    private Task RecordAsync(Principal user, string actionCode, string summary, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return Runner.RunAsync(async unit =>
        {
            unit.AddAudit(Audit(actionCode, user.UserId, "User", Key(user.UserId), summary));
            await unit.SaveAsync(cancellationToken);
            return true;
        }, cancellationToken);
    }
}
