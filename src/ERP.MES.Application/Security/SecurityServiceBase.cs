using ERP.MES.Domain.Identity;

namespace ERP.MES.Application.Security;

/// <summary>Shared plumbing of the security use cases: the collaborators and the construction of audit entries.</summary>
public abstract class SecurityServiceBase
{
    protected SecurityServiceBase(
        ISecurityUnitRunner runner, IPasswordHasher hasher, IClock clock, IAuditContext auditContext, AuthenticationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(auditContext);
        ArgumentNullException.ThrowIfNull(policy);

        var problems = policy.Validate();
        if (problems.Count > 0)
        {
            throw new InvalidAuthenticationPolicyException(problems);
        }

        Runner = runner;
        Hasher = hasher;
        Clock = clock;
        AuditContext = auditContext;
        Policy = policy;
    }

    protected ISecurityUnitRunner Runner { get; }

    protected IPasswordHasher Hasher { get; }

    protected IClock Clock { get; }

    protected IAuditContext AuditContext { get; }

    protected AuthenticationPolicy Policy { get; }

    /// <summary>An audit entry stamped with the current time and the machine and operating-system user. The summary must never contain a token or a password.</summary>
    protected AuditLogEntry Audit(string actionCode, long? userId, string targetType, string? targetId, string summary) =>
        new(Clock.UtcNow, userId, AuditContext.MachineName, AuditContext.OsUserName, AuditContext.DatabaseLogin, actionCode, targetType, targetId, summary);

    protected static Principal ToPrincipal(UserCredential account) =>
        new(account.User.Id, account.User.Username.Value, account.User.DisplayName, account.User.Department.Code, account.User.Role.Code, account.MustChangePassword);

    protected static string Key(long id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
