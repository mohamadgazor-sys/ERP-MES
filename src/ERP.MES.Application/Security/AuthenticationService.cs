namespace ERP.MES.Application.Security;

/// <summary>
/// Sign-in (Decision Log #40, #41, #65; Milestone 1 plan 6). The failed-attempt counter and the lockout are stored with the credential in the database, so they
/// hold across every application instance. Unknown user, wrong password, inactive account and locked account all give the same answer to the caller; only the
/// audit log keeps the distinction. A password is verified in every one of those cases, so the response time does not reveal which one it was, and a locked
/// account is never touched (a lock is not extended by further attempts). A correct password re-hashes the password when the stored algorithm, version or work
/// factor is below the configured baseline.
/// </summary>
public sealed class AuthenticationService : SecurityServiceBase
{
    private static readonly SignInResult Failure = new(SignInStatus.Failed, null);

    private readonly Lazy<PasswordHashRecord> _unknownUserHash;

    public AuthenticationService(ISecurityUnitRunner runner, IPasswordHasher hasher, IClock clock, IAuditContext auditContext, AuthenticationPolicy policy)
        : base(runner, hasher, clock, auditContext, policy)
    {
        // Verified against when the user name is unknown, so the response time does not reveal whether the name exists.
        _unknownUserHash = new Lazy<PasswordHashRecord>(() => Hasher.Hash("unknown-user-timing-equaliser", Policy.Pbkdf2Iterations));
    }

    public Task<SignInResult> SignInAsync(string? username, string? password, CancellationToken cancellationToken = default) =>
        ConflictRetry.RunAsync(() => Runner.RunAsync(async unit =>
        {
            var now = Clock.UtcNow;
            var account = await unit.FindAccountByUsernameAsync(username, cancellationToken);

            if (account is null || string.IsNullOrEmpty(password))
            {
                if (!string.IsNullOrEmpty(password))
                {
                    Hasher.Verify(password, _unknownUserHash.Value);
                }

                // The attempted name is not recorded: people sometimes type their password into the name field.
                unit.AddAudit(Audit(AuditActions.LoginFailed, null, "User", null, "Sign-in refused: unknown user or empty credentials."));
                await unit.SaveAsync(cancellationToken);
                return Failure;
            }

            var user = account.User;
            var userKey = Key(user.Id);
            var passwordIsCorrect = Hasher.Verify(password, account.PasswordRecord);

            if (account.IsLockedOut(now))
            {
                // Verified above for the timing; nothing about the credential changes, so the lock is not extended.
                unit.AddAudit(Audit(AuditActions.LoginFailed, user.Id, "User", userKey, "Sign-in refused: the account is locked."));
                await unit.SaveAsync(cancellationToken);
                return Failure;
            }

            if (!passwordIsCorrect)
            {
                var lockedNow = account.RegisterFailedLogin(now, Policy);
                unit.AddAudit(Audit(AuditActions.LoginFailed, user.Id, "User", userKey, "Sign-in refused: wrong password."));
                if (lockedNow)
                {
                    unit.AddAudit(Audit(AuditActions.AccountLocked, user.Id, "User", userKey, "The account was locked after too many failed attempts."));
                }

                await unit.SaveAsync(cancellationToken);
                return Failure;
            }

            if (!user.IsActive)
            {
                unit.AddAudit(Audit(AuditActions.LoginFailed, user.Id, "User", userKey, "Sign-in refused: the account is inactive."));
                await unit.SaveAsync(cancellationToken);
                return Failure;
            }

            account.RecordSuccessfulLogin(now);
            user.RecordLogin(now);
            unit.AddAudit(Audit(AuditActions.LoginSucceeded, user.Id, "User", userKey, "Signed in."));
            if (Hasher.NeedsUpgrade(account.PasswordRecord, Policy.Pbkdf2Iterations))
            {
                account.UpgradeHash(Hasher.Hash(password, Policy.Pbkdf2Iterations), now);
                unit.AddAudit(Audit(AuditActions.PasswordHashUpgraded, user.Id, "User", userKey, "The password hash was upgraded to the current parameters."));
            }

            await unit.SaveAsync(cancellationToken);
            return new SignInResult(SignInStatus.Succeeded, ToPrincipal(account));
        }, cancellationToken));
}
