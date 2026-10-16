namespace ERP.MES.Application.Security;

/// <summary>The outcome of a current-authorization check: whether the permission is granted now, and the user as freshly loaded from the database (null when there is no such user).</summary>
public sealed record AuthorizationDecision(bool IsAllowed, Principal? Current);

/// <summary>Raised by <see cref="CurrentAuthorization.RequireAsync"/> when the permission is not granted now.</summary>
public sealed class NotAuthorizedException(Permission permission)
    : InvalidOperationException("The user is not authorized for " + permission + ".")
{
    public Permission Permission { get; } = permission;
}

/// <summary>
/// Decides, from the database and at the time of the call, whether a user may perform an action (Decision Log #74). The desktop <see cref="Principal"/> is a UI
/// snapshot taken at sign-in or unlock; it must not authorize a business operation by itself. Every role-sensitive Application command calls
/// <see cref="RequireAsync"/> (or <see cref="CheckAsync"/>) first, so a user who was deactivated, or whose department or role changed, loses the permission at the
/// next operation instead of at the next sign-in. The rules are the default-deny <see cref="AuthorizationRules"/>; an inactive, unknown or missing user is denied
/// everything. Nothing is written.
/// </summary>
public sealed class CurrentAuthorization(ISecurityUnitRunner runner)
{
    public Task<AuthorizationDecision> CheckAsync(Principal? principal, Permission permission, CancellationToken cancellationToken = default)
    {
        if (principal is null)
        {
            return Task.FromResult(new AuthorizationDecision(false, null));
        }

        return runner.RunAsync(async unit =>
        {
            var account = await unit.FindAccountByUserIdAsync(principal.UserId, cancellationToken);
            if (account is null)
            {
                return new AuthorizationDecision(false, null);
            }

            var user = account.User;
            var current = new Principal(user.Id, user.Username.Value, user.DisplayName, user.Department.Code, user.Role.Code, account.MustChangePassword);

            // A user who still has to choose their own password has no operational access either.
            var allowed = !account.MustChangePassword && AuthorizationRules.IsAllowed(user, permission);
            return new AuthorizationDecision(allowed, current);
        }, cancellationToken);
    }

    /// <summary>Returns the current user when the permission is granted now; otherwise throws <see cref="NotAuthorizedException"/>.</summary>
    public async Task<Principal> RequireAsync(Principal? principal, Permission permission, CancellationToken cancellationToken = default)
    {
        var decision = await CheckAsync(principal, permission, cancellationToken);
        return decision.IsAllowed ? decision.Current! : throw new NotAuthorizedException(permission);
    }
}
