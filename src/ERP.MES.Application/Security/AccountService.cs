using ERP.MES.Application.Persistence;
using ERP.MES.Domain.Identity;

namespace ERP.MES.Application.Security;

public enum AccountStatus
{
    Succeeded,
    NotAuthorized,
    NotFound,
    UsernameTaken,
    UsernameRejected,
    DisplayNameRejected,
    PasswordRejected,
    InvalidDepartmentOrRole,

    /// <summary>The change would deactivate or demote the last active administrator (Decision Log #47).</summary>
    LastAdministrator,

    /// <summary>The current password given while changing a password was wrong or the account is locked; the two are not distinguished (Decision Log #65).</summary>
    WrongCurrentPassword,

    PasswordUnchanged,
}

public sealed record AccountResult(
    AccountStatus Status,
    long? UserId = null,
    PasswordPolicyError PasswordError = PasswordPolicyError.None,
    UsernameError UsernameError = UsernameError.None);

/// <summary>
/// Account management (Domain and Database Specification 15.2: only the Admin authorization administers users). Every administrative method reloads the acting
/// user from the database and checks that they are an active Administrator, so a stale or forged <see cref="Principal"/> grants nothing. Users are never deleted.
/// User names are unique ignoring case (Decision Log #62). The last active Administrator can neither be deactivated nor demoted: the check and the change run in
/// one transaction that also advances the setup row's <see cref="SystemSetup.AdminChangeStamp"/>, so of two concurrent removals exactly one succeeds
/// (Milestone 1 plan 5.7).
/// </summary>
public sealed class AccountService(
    ISecurityUnitRunner runner, IPasswordHasher hasher, IClock clock, IAuditContext auditContext, AuthenticationPolicy policy)
    : SecurityServiceBase(runner, hasher, clock, auditContext, policy)
{
    /// <summary>Creates an account with a one-time initial password: the user has to choose their own at the first sign-in.</summary>
    public async Task<AccountResult> CreateUserAsync(
        Principal actor, string? username, string? displayName, string departmentCode, string roleCode, string? initialPassword, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var usernameError = Username.Validate(username);
        if (usernameError != UsernameError.None)
        {
            return new AccountResult(AccountStatus.UsernameRejected, UsernameError: usernameError);
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return new AccountResult(AccountStatus.DisplayNameRejected);
        }

        var passwordError = PasswordRules.Validate(initialPassword, Policy);
        if (passwordError != PasswordPolicyError.None)
        {
            return new AccountResult(AccountStatus.PasswordRejected, PasswordError: passwordError);
        }

        if (!DepartmentCodes.All.Contains(departmentCode, StringComparer.Ordinal) || !RoleCodes.All.Contains(roleCode, StringComparer.Ordinal))
        {
            return new AccountResult(AccountStatus.InvalidDepartmentOrRole);
        }

        var record = Hasher.Hash(initialPassword!, Policy.Pbkdf2Iterations);
        try
        {
            return await ConflictRetry.RunAsync(() => Runner.RunAsync(async unit =>
            {
                var acting = await unit.FindAccountByUserIdAsync(actor.UserId, cancellationToken);
                if (!AuthorizationRules.IsAllowed(acting?.User, Permission.AdministerUsers))
                {
                    return new AccountResult(AccountStatus.NotAuthorized);
                }

                // Case-insensitive: "Ali" and "ali" are the same name.
                if (await unit.FindAccountByUsernameAsync(username, cancellationToken) is not null)
                {
                    return new AccountResult(AccountStatus.UsernameTaken);
                }

                var department = await unit.GetDepartmentAsync(departmentCode, cancellationToken);
                var role = await unit.GetRoleAsync(roleCode, cancellationToken);
                var user = new User(0, Username.Create(username), displayName!, department, role, Clock.UtcNow);
                unit.AddAccount(new UserCredential(user, record, mustChangePassword: true, Clock.UtcNow));
                unit.AddAudit(Audit(AuditActions.UserCreated, acting!.User.Id, "User", username, "Account created in " + departmentCode + " with role " + roleCode + "."));
                await unit.SaveAsync(cancellationToken);
                return new AccountResult(AccountStatus.Succeeded, user.Id);
            }, cancellationToken));
        }
        catch (PersistenceException exception) when (exception is not ConcurrencyConflictException)
        {
            // A simultaneous creation of the same name is stopped by the unique index on the canonical key.
            var taken = await Runner.RunAsync(async unit => await unit.FindAccountByUsernameAsync(username, cancellationToken) is not null, cancellationToken);
            if (!taken)
            {
                throw;
            }

            return new AccountResult(AccountStatus.UsernameTaken);
        }
    }

    public Task<AccountResult> SetActiveAsync(Principal actor, long userId, bool isActive, CancellationToken cancellationToken = default) =>
        ChangeAdministeredUserAsync(
            actor,
            userId,
            removesAdministrator: account => !isActive && account.User.IsActiveAdministrator,
            apply: async (unit, account, now, actorId) =>
            {
                account.User.SetActive(isActive, now);
                unit.AddAudit(Audit(isActive ? AuditActions.UserActivated : AuditActions.UserDeactivated, actorId, "User", Key(account.User.Id), isActive ? "Account activated." : "Account deactivated."));
                await Task.CompletedTask;
            },
            cancellationToken);

    public async Task<AccountResult> ChangeRoleAsync(Principal actor, long userId, string roleCode, CancellationToken cancellationToken = default)
    {
        if (!RoleCodes.All.Contains(roleCode, StringComparer.Ordinal))
        {
            return new AccountResult(AccountStatus.InvalidDepartmentOrRole);
        }

        return await ChangeAdministeredUserAsync(
            actor,
            userId,
            removesAdministrator: account => account.User.IsActiveAdministrator && roleCode != RoleCodes.Admin,
            apply: async (unit, account, now, actorId) =>
            {
                account.User.AssignRole(await unit.GetRoleAsync(roleCode, cancellationToken), now);
                unit.AddAudit(Audit(AuditActions.UserRoleChanged, actorId, "User", Key(account.User.Id), "Role changed to " + roleCode + "."));
            },
            cancellationToken);
    }

    /// <summary>The Administrator resets a password (there is no self-service reset); the user must choose their own at the next sign-in.</summary>
    public async Task<AccountResult> ResetPasswordAsync(Principal actor, long userId, string? newPassword, CancellationToken cancellationToken = default)
    {
        var passwordError = PasswordRules.Validate(newPassword, Policy);
        if (passwordError != PasswordPolicyError.None)
        {
            return new AccountResult(AccountStatus.PasswordRejected, PasswordError: passwordError);
        }

        var record = Hasher.Hash(newPassword!, Policy.Pbkdf2Iterations);
        return await ChangeAdministeredUserAsync(
            actor,
            userId,
            removesAdministrator: _ => false,
            apply: async (unit, account, now, actorId) =>
            {
                account.SetPassword(record, mustChangePassword: true, now);
                account.ClearLockout(now);
                unit.AddAudit(Audit(AuditActions.PasswordReset, actorId, "User", Key(account.User.Id), "The password was reset by an administrator."));
                await Task.CompletedTask;
            },
            cancellationToken);
    }

    public Task<AccountResult> UnlockAsync(Principal actor, long userId, CancellationToken cancellationToken = default) =>
        ChangeAdministeredUserAsync(
            actor,
            userId,
            removesAdministrator: _ => false,
            apply: async (unit, account, now, actorId) =>
            {
                account.ClearLockout(now);
                unit.AddAudit(Audit(AuditActions.AccountUnlocked, actorId, "User", Key(account.User.Id), "The account was unlocked by an administrator."));
                await Task.CompletedTask;
            },
            cancellationToken);

    /// <summary>
    /// A signed-in user chooses their own password. A wrong current password counts as a failed sign-in, so the form cannot be used to guess the password; a locked
    /// account gets the same answer as a wrong password (and its lock is not extended).
    /// </summary>
    public async Task<AccountResult> ChangeOwnPasswordAsync(Principal user, string? currentPassword, string? newPassword, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var passwordError = PasswordRules.Validate(newPassword, Policy);
        if (passwordError != PasswordPolicyError.None)
        {
            return new AccountResult(AccountStatus.PasswordRejected, PasswordError: passwordError);
        }

        if (string.IsNullOrEmpty(currentPassword))
        {
            return new AccountResult(AccountStatus.WrongCurrentPassword);
        }

        return await ConflictRetry.RunAsync(() => Runner.RunAsync(async unit =>
        {
            var account = await unit.FindAccountByUserIdAsync(user.UserId, cancellationToken);
            if (account is not { User.IsActive: true })
            {
                return new AccountResult(AccountStatus.NotAuthorized);
            }

            var now = Clock.UtcNow;
            var userKey = Key(account.User.Id);
            var currentIsCorrect = Hasher.Verify(currentPassword, account.PasswordRecord);
            if (account.IsLockedOut(now))
            {
                unit.AddAudit(Audit(AuditActions.LoginFailed, account.User.Id, "User", userKey, "Password change refused: the account is locked."));
                await unit.SaveAsync(cancellationToken);
                return new AccountResult(AccountStatus.WrongCurrentPassword);
            }

            if (!currentIsCorrect)
            {
                var lockedNow = account.RegisterFailedLogin(now, Policy);
                unit.AddAudit(Audit(AuditActions.LoginFailed, account.User.Id, "User", userKey, "Wrong current password while changing the password."));
                if (lockedNow)
                {
                    unit.AddAudit(Audit(AuditActions.AccountLocked, account.User.Id, "User", userKey, "The account was locked after too many failed attempts."));
                }

                await unit.SaveAsync(cancellationToken);
                return new AccountResult(AccountStatus.WrongCurrentPassword);
            }

            if (Hasher.Verify(newPassword!, account.PasswordRecord))
            {
                return new AccountResult(AccountStatus.PasswordUnchanged);
            }

            account.SetPassword(Hasher.Hash(newPassword!, Policy.Pbkdf2Iterations), mustChangePassword: false, now);
            unit.AddAudit(Audit(AuditActions.PasswordChanged, account.User.Id, "User", userKey, "The user changed their own password."));
            await unit.SaveAsync(cancellationToken);
            return new AccountResult(AccountStatus.Succeeded, account.User.Id);
        }, cancellationToken));
    }

    // Shared shape of an administrator's change to one account: authorize against the database, apply the last-administrator rule, change, audit, save.
    private async Task<AccountResult> ChangeAdministeredUserAsync(
        Principal actor,
        long userId,
        Func<UserCredential, bool> removesAdministrator,
        Func<ISecurityUnit, UserCredential, DateTime, long, Task> apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        return await ConflictRetry.RunAsync(() => Runner.RunAsync(async unit =>
        {
            var acting = await unit.FindAccountByUserIdAsync(actor.UserId, cancellationToken);
            if (!AuthorizationRules.IsAllowed(acting?.User, Permission.AdministerUsers))
            {
                return new AccountResult(AccountStatus.NotAuthorized);
            }

            var target = await unit.FindAccountByUserIdAsync(userId, cancellationToken);
            if (target is null)
            {
                return new AccountResult(AccountStatus.NotFound);
            }

            // Loaded (and so tracked) before the check: saving advances its version, which makes a concurrent removal fail and be re-evaluated.
            var setup = await unit.GetSetupAsync(cancellationToken);
            if (removesAdministrator(target))
            {
                if (await unit.CountActiveAdministratorsAsync(cancellationToken) <= 1)
                {
                    unit.AddAudit(Audit(AuditActions.LastAdminProtectionRefused, acting!.User.Id, "User", Key(target.User.Id), "Refused: the change would remove the last active administrator."));
                    await unit.SaveAsync(cancellationToken);
                    return new AccountResult(AccountStatus.LastAdministrator, target.User.Id);
                }

                setup.AdvanceAdminChangeStamp();
            }

            var now = Clock.UtcNow;
            await apply(unit, target, now, acting!.User.Id);
            await unit.SaveAsync(cancellationToken);
            return new AccountResult(AccountStatus.Succeeded, target.User.Id);
        }, cancellationToken));
    }
}
