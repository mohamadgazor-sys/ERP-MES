namespace ERP.MES.Application.Security;

/// <summary>
/// Sign-in (Decision Log #40, #41; Milestone 1 plan 6). The failed-attempt counter and the lockout are stored with the user in the database, so they hold
/// across every application instance. Unknown user, wrong password and inactive account give the same answer. A correct password re-hashes the password
/// when the stored algorithm, version or work factor is below the configured baseline.
/// </summary>
public sealed class AuthenticationService : SecurityServiceBase
{
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
            var user = string.IsNullOrEmpty(username) ? null : await unit.FindUserByUsernameAsync(username, cancellationToken);

            if (user is null || string.IsNullOrEmpty(password))
            {
                if (!string.IsNullOrEmpty(password))
                {
                    Hasher.Verify(password, _unknownUserHash.Value);
                }

                // The attempted name is not recorded: people sometimes type their password into the name field.
                unit.AddAudit(Audit(AuditActions.LoginFailed, null, "User", null, "Sign-in refused: unknown user or empty credentials."));
                await unit.SaveAsync(cancellationToken);
                return new SignInResult(SignInStatus.InvalidCredentials, null);
            }

            if (user.IsLockedOut(now))
            {
                unit.AddAudit(Audit(AuditActions.LoginFailed, user.Id, "User", user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "Sign-in refused: the account is locked."));
                await unit.SaveAsync(cancellationToken);
                return new SignInResult(SignInStatus.LockedOut, null);
            }

            var passwordIsCorrect = Hasher.Verify(password, user.PasswordRecord);
            var userKey = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!passwordIsCorrect)
            {
                var lockedNow = user.RegisterFailedLogin(now, Policy);
                unit.AddAudit(Audit(AuditActions.LoginFailed, user.Id, "User", userKey, "Sign-in refused: wrong password."));
                if (lockedNow)
                {
                    unit.AddAudit(Audit(AuditActions.AccountLocked, user.Id, "User", userKey, "The account was locked after too many failed attempts."));
                }

                await unit.SaveAsync(cancellationToken);
                return new SignInResult(lockedNow ? SignInStatus.LockedOut : SignInStatus.InvalidCredentials, null);
            }

            if (!user.IsActive)
            {
                unit.AddAudit(Audit(AuditActions.LoginFailed, user.Id, "User", userKey, "Sign-in refused: the account is inactive."));
                await unit.SaveAsync(cancellationToken);
                return new SignInResult(SignInStatus.InvalidCredentials, null);
            }

            user.RecordSuccessfulLogin(now);
            unit.AddAudit(Audit(AuditActions.LoginSucceeded, user.Id, "User", userKey, "Signed in."));
            if (Hasher.NeedsUpgrade(user.PasswordRecord, Policy.Pbkdf2Iterations))
            {
                user.UpgradeHash(Hasher.Hash(password, Policy.Pbkdf2Iterations), now);
                unit.AddAudit(Audit(AuditActions.PasswordHashUpgraded, user.Id, "User", userKey, "The password hash was upgraded to the current parameters."));
            }

            await unit.SaveAsync(cancellationToken);
            return new SignInResult(SignInStatus.Succeeded, ToPrincipal(user));
        }, cancellationToken));
}
