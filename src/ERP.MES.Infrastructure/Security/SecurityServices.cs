using ERP.MES.Application.Security;
using ERP.MES.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Security;

/// <summary>The security use cases wired to the real hasher, the database and the clock. Composition roots (Desktop, Provisioning) and tests create it once per database.</summary>
public sealed record SecurityServices(
    AuthenticationService Authentication,
    BootstrapService Bootstrap,
    RecoveryService Recovery,
    AccountService Accounts,
    AuthenticationPolicy Policy)
{
    public static SecurityServices Create(
        IDbContextFactory<ErpMesDbContext> contexts,
        AuthenticationPolicy? policy = null,
        IClock? clock = null,
        IAuditContext? auditContext = null,
        IPasswordHasher? hasher = null)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        var effectivePolicy = policy ?? AuthenticationPolicy.Default;
        var runner = new EfSecurityUnitRunner(contexts);
        var effectiveHasher = hasher ?? new Pbkdf2PasswordHasher();
        var effectiveClock = clock ?? new SystemClock();
        var effectiveContext = auditContext ?? new EnvironmentAuditContext();

        return new SecurityServices(
            new AuthenticationService(runner, effectiveHasher, effectiveClock, effectiveContext, effectivePolicy),
            new BootstrapService(runner, effectiveHasher, effectiveClock, effectiveContext, effectivePolicy),
            new RecoveryService(runner, effectiveHasher, effectiveClock, effectiveContext, effectivePolicy),
            new AccountService(runner, effectiveHasher, effectiveClock, effectiveContext, effectivePolicy),
            effectivePolicy);
    }
}
