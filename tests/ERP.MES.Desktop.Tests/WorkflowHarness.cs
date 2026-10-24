using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Identity;
using ERP.MES.Infrastructure.Persistence;
using ERP.MES.Infrastructure.Security;
using ERP.MES.Infrastructure.Sqlite;

namespace ERP.MES.Desktop.Tests;

internal sealed class HarnessClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

/// <summary>
/// A migrated temporary SQLite database with the real security and workflow services and one signed-in user per department. The screens' view models are created
/// from it; nothing opens a window.
/// </summary>
internal sealed class WorkflowHarness : IDisposable
{
    public const string InitialPassword = "Staff-Pass-Phrase-2";
    public const string OwnPassword = "My-Own-Pass-Phrase-3";

    private static readonly TimeZoneInfo SiteZone = TimeZoneInfo.CreateCustomTimeZone("test-site", TimeSpan.FromMinutes(210), "test-site", "test-site");

    private readonly string _path = Path.Combine(Path.GetTempPath(), "erpmes-workflow-" + Guid.NewGuid().ToString("N") + ".db");

    private WorkflowHarness()
    {
    }

    public HarnessClock Clock { get; } = new();

    public IJalaliDateService Dates { get; } = new JalaliDateService(DigitStyle.Persian, SiteZone);

    public SecurityServices Security { get; private set; } = null!;

    public WorkflowServices Workflow { get; private set; } = null!;

    public Principal Admin { get; private set; } = null!;

    public Principal Central { get; private set; } = null!;

    public Principal Planning { get; private set; } = null!;

    public Principal Drawing { get; private set; } = null!;

    public Principal Qc { get; private set; } = null!;

    public Principal Management { get; private set; } = null!;

    public Principal Production { get; private set; } = null!;

    public Principal Warehouse { get; private set; } = null!;

    /// <summary>A Management user with the Manager role: the only one who may approve or reject a request (Decision Log #30).</summary>
    public Principal Manager { get; private set; } = null!;

    /// <summary>A repository over the harness database, to prepare routes and stage states that no screen creates yet.</summary>
    public EfRequestRepository RequestRepository { get; private set; } = null!;

    public static async Task<WorkflowHarness> CreateAsync(CancellationToken cancellationToken)
    {
        var h = new WorkflowHarness();
        await SqliteDatabase.MigrateAsync(h._path, cancellationToken);
        var factory = SqliteDatabase.CreateContextFactory(h._path);
        h.Security = SecurityServices.Create(factory, new AuthenticationPolicy { Pbkdf2Iterations = AuthenticationPolicy.Pbkdf2IterationsFloor }, h.Clock, hasher: new LowCostHasher());
        h.Workflow = WorkflowServices.Create(factory, h.Security.Authorization, h.Dates, h.Clock);

        var issued = await h.Security.Bootstrap.IssueTokenAsync(cancellationToken: cancellationToken);
        Assert.Equal(BootstrapStatus.Succeeded, (await h.Security.Bootstrap.CompleteAsync(issued.Token, "admin.one", "Administrator", "Admin-Pass-Phrase-1", cancellationToken)).Status);
        h.Admin = (await h.Security.Authentication.SignInAsync("admin.one", "Admin-Pass-Phrase-1", cancellationToken)).Principal!;
        h.Central = await h.UserAsync("central.one", DepartmentCodes.Central, cancellationToken);
        h.Planning = await h.UserAsync("planning.one", DepartmentCodes.Planning, cancellationToken);
        h.Drawing = await h.UserAsync("drawing.one", DepartmentCodes.Drawing, cancellationToken);
        h.Qc = await h.UserAsync("qc.one", DepartmentCodes.Qc, cancellationToken);
        h.Management = await h.UserAsync("management.one", DepartmentCodes.Management, cancellationToken);
        h.Production = await h.UserAsync("production.one", DepartmentCodes.Production, cancellationToken);
        h.Warehouse = await h.UserAsync("warehouse.one", DepartmentCodes.Warehouse, cancellationToken);
        h.Manager = await h.UserAsync("manager.one", DepartmentCodes.Management, cancellationToken, RoleCodes.Manager);
        h.RequestRepository = new EfRequestRepository(factory);
        return h;
    }

    /// <summary>New services over the same file, as after restarting the application.</summary>
    public WorkflowServices Restarted() =>
        WorkflowServices.Create(SqliteDatabase.CreateContextFactory(_path), Security.Authorization, Dates, Clock);

    public CustomersViewModel CustomersFor(Principal user, WorkflowServices? services = null)
    {
        var workflow = services ?? Workflow;
        return new CustomersViewModel(workflow.Customers, workflow.Requests, Dates, () => user);
    }

    public RequestsViewModel RequestsFor(Principal user, WorkflowServices? services = null)
    {
        var workflow = services ?? Workflow;
        return new RequestsViewModel(workflow.Requests, workflow.Documents, workflow.Lifecycle, workflow.Customers, Dates, DigitStyle.Persian, () => Clock.UtcNow, () => user);
    }

    public ProductionQueueViewModel QueueFor(Principal user, ProductionQueueService? service = null) =>
        new(service ?? Workflow.Queue, Dates, DigitStyle.Persian, () => Clock.UtcNow, () => user);

    private async Task<Principal> UserAsync(string name, string department, CancellationToken cancellationToken, string role = RoleCodes.Staff)
    {
        var created = await Security.Accounts.CreateUserAsync(Admin, name, "User " + name, department, role, InitialPassword, cancellationToken);
        Assert.Equal(AccountStatus.Succeeded, created.Status);
        var first = (await Security.Authentication.SignInAsync(name, InitialPassword, cancellationToken)).Principal!;
        Assert.Equal(AccountStatus.Succeeded, (await Security.Accounts.ChangeOwnPasswordAsync(first, InitialPassword, OwnPassword, cancellationToken)).Status);
        return (await Security.Authentication.SignInAsync(name, OwnPassword, cancellationToken)).Principal!;
    }

    public void Dispose()
    {
        foreach (var suffix in new[] { string.Empty, "-journal", "-wal", "-shm" })
        {
            try
            {
                File.Delete(_path + suffix);
            }
            catch (IOException)
            {
                // A leftover temp file does not affect the result.
            }
        }
    }
}

/// <summary>A password hasher with a tiny work factor for the screen tests, which create many users and do not test passwords (the real hasher at the documented floor is covered by the Infrastructure security tests). Production never uses it.</summary>
internal sealed class LowCostHasher : ERP.MES.Application.Security.IPasswordHasher
{
    private const int WorkFactor = 1_000;

    private readonly ERP.MES.Infrastructure.Security.Pbkdf2PasswordHasher _inner = new();

    public ERP.MES.Application.Security.PasswordHashRecord Hash(string password, int workFactor) => _inner.Hash(password, WorkFactor);

    public bool Verify(string password, ERP.MES.Application.Security.PasswordHashRecord stored) => _inner.Verify(password, stored);

    public bool NeedsUpgrade(ERP.MES.Application.Security.PasswordHashRecord stored, int configuredWorkFactor) => false;
}
