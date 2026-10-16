using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

internal sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

internal sealed class FixedAuditContext : IAuditContext
{
    public string MachineName => "TEST-PC";

    public string OsUserName => "test.operator";

    public string? DatabaseLogin => null;
}

/// <summary>Delegates to the real hasher and counts verifications, so a test can show that every sign-in outcome does the same amount of password work.</summary>
internal sealed class CountingHasher(IPasswordHasher inner) : IPasswordHasher
{
    public int VerifyCalls { get; private set; }

    public PasswordHashRecord Hash(string password, int workFactor) => inner.Hash(password, workFactor);

    public bool Verify(string password, PasswordHashRecord stored)
    {
        VerifyCalls++;
        return inner.Verify(password, stored);
    }

    public bool NeedsUpgrade(PasswordHashRecord stored, int configuredWorkFactor) => inner.NeedsUpgrade(stored, configuredWorkFactor);
}

/// <summary>
/// A migrated temporary SQLite database with the real security services, a controllable clock and a fixed audit context. The work factor is the documented
/// safety FLOOR so the suite stays fast; the approved baseline (600,000) is covered by the policy and hasher tests, and nothing here changes it.
/// </summary>
internal sealed class SecurityFixture : IDisposable
{
    public const string AdminName = "admin.one";
    public const string AdminPassword = "Admin-Pass-Phrase-1";

    public static AuthenticationPolicy FastPolicy { get; } = new() { Pbkdf2Iterations = AuthenticationPolicy.Pbkdf2IterationsFloor };

    private SecurityFixture(TempDatabase db, AuthenticationPolicy policy)
    {
        Db = db;
        Policy = policy;
        Clock = new FakeClock();
        Services = SecurityServices.Create(db.ContextFactory, policy, Clock, new FixedAuditContext());
    }

    public TempDatabase Db { get; }

    public FakeClock Clock { get; }

    public AuthenticationPolicy Policy { get; }

    public SecurityServices Services { get; private set; }

    public static async Task<SecurityFixture> CreateAsync(AuthenticationPolicy? policy = null)
    {
        var db = new TempDatabase();
        await db.MigrateAsync();
        return new SecurityFixture(db, policy ?? FastPolicy);
    }

    /// <summary>The same database and clock with services built from another policy (for example a higher work factor after an upgrade of the baseline).</summary>
    public void UsePolicy(AuthenticationPolicy policy) =>
        Services = SecurityServices.Create(Db.ContextFactory, policy, Clock, new FixedAuditContext());

    public void UseHasher(IPasswordHasher hasher) =>
        Services = SecurityServices.Create(Db.ContextFactory, Policy, Clock, new FixedAuditContext(), hasher);

    /// <summary>Runs the real bootstrap path: issue a token, redeem it, return the first administrator.</summary>
    public async Task<Principal> BootstrapAdminAsync(string username = AdminName, string password = AdminPassword, CancellationToken cancellationToken = default)
    {
        var issued = await Services.Bootstrap.IssueTokenAsync(cancellationToken: cancellationToken);
        Assert.True(issued.Issued);
        var result = await Services.Bootstrap.CompleteAsync(issued.Token, username, "Test Administrator", password, cancellationToken);
        Assert.Equal(BootstrapStatus.Succeeded, result.Status);
        return await SignInAsync(username, password, cancellationToken);
    }

    public async Task<Principal> SignInAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var result = await Services.Authentication.SignInAsync(username, password, cancellationToken);
        Assert.Equal(SignInStatus.Succeeded, result.Status);
        return result.Principal!;
    }

    /// <summary>Creates a user through the real administrator use case and returns the new id. The initial password is one-time (the user must change it).</summary>
    public async Task<long> CreateUserAsync(
        Principal admin, string username, string password, string department = DepartmentCodes.Planning, string role = RoleCodes.Staff, CancellationToken cancellationToken = default)
    {
        var result = await Services.Accounts.CreateUserAsync(admin, username, "User " + username, department, role, password, cancellationToken);
        Assert.Equal(AccountStatus.Succeeded, result.Status);
        return result.UserId!.Value;
    }

    public List<string> AuditActionsInOrder() => Db.Query("SELECT ActionCode FROM AuditLog ORDER BY Id");

    /// <summary>Every audit summary and target, joined, for checks that no secret was written to the audit log.</summary>
    public string AllAuditText() => string.Join("\n", Db.Query("SELECT ActionCode || ' ' || TargetType || ' ' || COALESCE(TargetId, '') || ' ' || Summary FROM AuditLog"));

    public void Dispose() => Db.Dispose();
}
