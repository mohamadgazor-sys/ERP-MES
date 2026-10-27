using ERP.MES.Application.Persistence;
using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Persistence;
using ERP.MES.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.SqlServer.Tests;

// The SQL Server integration pass (Decision Log #117): the provider-neutral model, repositories and use cases against a real SQL Server. The schema here is created from the model with
// EnsureCreated (TEST ONLY); the committed SQL Server migrations are generated on a machine that can reach the provider package (docs/SQL_SERVER_PASS.md).
public class SqlServerPassTests
{
    private const string InitialPassword = "Staff-Pass-Phrase-2";
    private const string OwnPassword = "My-Own-Pass-Phrase-3";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly JalaliDateService Dates = new(DigitStyle.Latin, TimeZoneInfo.Utc);

    private sealed class Clock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 10, 24, 5, 0, 0, DateTimeKind.Utc);

        public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
    }

    private sealed class Audit : IAuditContext
    {
        public string MachineName => "SQL-IT";

        public string OsUserName => "ci";

        public string? DatabaseLogin => null;
    }

    private sealed class FastHasher : IPasswordHasher
    {
        private readonly Pbkdf2PasswordHasher _inner = new();

        public PasswordHashRecord Hash(string password, int workFactor) => _inner.Hash(password, 1_000);

        public bool Verify(string password, PasswordHashRecord stored) => _inner.Verify(password, stored);

        public bool NeedsUpgrade(PasswordHashRecord stored, int configuredWorkFactor) => false;
    }

    private sealed class World
    {
        public required SqlServerFixture Db { get; init; }

        public required IDbContextFactory<ErpMesDbContext> Factory { get; init; }

        public required Clock Clock { get; init; }

        public required SecurityServices Security { get; init; }

        public required WorkflowServices Services { get; init; }

        public required Principal Admin { get; init; }

        public required Dictionary<string, Principal> Users { get; init; }

        public Principal Central => Users["central"];

        public Principal Manager => Users["manager"];

        public Principal Planning => Users["planning"];

        public Principal Drawing => Users["drawing"];

        public Principal Warehouse => Users["warehouse"];

        public Principal Qc => Users["qc"];

        public Principal Operator => Users["operator"];

        public Principal Supervisor => Users["supervisor"];
    }

    private static async Task<World> StartAsync(SqlServerFixture db)
    {
        var factory = SqlServerDatabase.CreateContextFactory(db.ConnectionString);
        await using (var context = await factory.CreateDbContextAsync(Token))
        {
            Assert.True(await context.Database.EnsureCreatedAsync(Token));
        }

        var clock = new Clock();
        var security = SecurityServices.Create(factory, new AuthenticationPolicy { Pbkdf2Iterations = AuthenticationPolicy.Pbkdf2IterationsFloor }, clock, new Audit(), new FastHasher());
        var issued = await security.Bootstrap.IssueTokenAsync(cancellationToken: Token);
        Assert.True(issued.Issued);
        Assert.Equal(BootstrapStatus.Succeeded, (await security.Bootstrap.CompleteAsync(issued.Token, "admin.one", "Administrator", "Admin-Pass-Phrase-1", Token)).Status);
        var admin = (await security.Authentication.SignInAsync("admin.one", "Admin-Pass-Phrase-1", Token)).Principal!;
        var users = new Dictionary<string, Principal>();
        foreach (var (key, department, role) in new[]
                 {
                     ("central", DepartmentCodes.Central, RoleCodes.Staff), ("manager", DepartmentCodes.Management, RoleCodes.Manager), ("planning", DepartmentCodes.Planning, RoleCodes.Staff),
                     ("drawing", DepartmentCodes.Drawing, RoleCodes.Staff), ("warehouse", DepartmentCodes.Warehouse, RoleCodes.Staff), ("qc", DepartmentCodes.Qc, RoleCodes.Staff),
                     ("operator", DepartmentCodes.Production, RoleCodes.Staff), ("supervisor", DepartmentCodes.Production, RoleCodes.Staff),
                 })
        {
            var name = key + ".one";
            Assert.Equal(AccountStatus.Succeeded, (await security.Accounts.CreateUserAsync(admin, name, "User " + name, department, role, InitialPassword, Token)).Status);
            var first = (await security.Authentication.SignInAsync(name, InitialPassword, Token)).Principal!;
            Assert.Equal(AccountStatus.Succeeded, (await security.Accounts.ChangeOwnPasswordAsync(first, InitialPassword, OwnPassword, Token)).Status);
            users[key] = (await security.Authentication.SignInAsync(name, OwnPassword, Token)).Principal!;
        }

        return new World { Db = db, Factory = factory, Clock = clock, Security = security, Services = WorkflowServices.Create(factory, security.Authorization, Dates, clock, new Audit()), Admin = admin, Users = users };
    }

    private static byte[] Pdf(string text) => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% " + text + "\n%%EOF\n");

    private static async Task<long> ReadyRequestAsync(World w, string requestNo)
    {
        var customer = (await w.Services.Customers.CreateAsync(w.Central, "مشتری آزمون SQL Server", Token)).Value!;
        var created = (await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, requestNo, WorkType.Make, "1405/08/01", null, null), Token)).Value!;
        var withPdf = (await w.Services.Documents.AttachRequestPdfAsync(w.Central, created.Value.Id, created.Version, "a.pdf", Pdf("req"), Token)).Value!;
        var sent = (await w.Services.Requests.SubmitToManagementAsync(w.Central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        var approved = (await w.Services.Lifecycle.ApproveAsync(w.Manager, sent.Value.Id, sent.Version, Token)).Value!;
        var forwarded = (await w.Services.Lifecycle.ForwardToDrawingAsync(w.Planning, approved.Value.Id, approved.Version, Token)).Value!;
        Assert.True((await w.Services.Lifecycle.RecordDrawingResultAsync(w.Drawing, forwarded.Value.Id, forwarded.Version, DrawingOutcome.FullyAccepted, null, Token)).Succeeded);
        Assert.True((await w.Services.Contracts.DecideRequirementAsync(w.Planning, created.Value.Id, ContractRequirement.NotRequired, null, Token)).Succeeded);
        return created.Value.Id;
    }

    [Fact]
    public async Task The_model_creates_on_SQL_Server_with_the_seed_rows_and_the_binary_collation_on_the_business_keys()
    {
        await using var db = await SqlServerFixture.CreateAsync();
        _ = await StartAsync(db);

        Assert.True(int.Parse((await db.QueryAsync("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'"))[0], System.Globalization.CultureInfo.InvariantCulture) >= 60);
        Assert.Equal("7", (await db.QueryAsync("SELECT COUNT(*) FROM PauseReasons"))[0]);
        Assert.Equal(ErpMesDbContext.SqlServerBinaryCollation, (await db.QueryAsync("SELECT COLLATION_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Requests' AND COLUMN_NAME = 'RequestNo'"))[0]);
        Assert.Equal(ErpMesDbContext.SqlServerBinaryCollation, (await db.QueryAsync("SELECT COLLATION_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'UsernameKey'"))[0]);
        Assert.Empty(await db.QueryAsync("SELECT COLLATION_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE COLLATION_NAME = 'BINARY'"));
    }

    [Fact]
    public async Task A_request_number_is_compared_case_sensitively_and_persian_text_survives()
    {
        await using var db = await SqlServerFixture.CreateAsync();
        var w = await StartAsync(db);
        var customer = (await w.Services.Customers.CreateAsync(w.Central, "مشتری فارسی ۱۲۳", Token)).Value!;

        var first = await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "A-1", WorkType.Make, "1405/08/01", null, null), Token);
        var lower = await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "a-1", WorkType.Make, "1405/08/01", null, null), Token);
        var duplicate = await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "A-1", WorkType.Make, "1405/08/01", null, null), Token);

        Assert.True(first.Succeeded);
        Assert.True(lower.Succeeded, "'a-1' and 'A-1' are different request numbers (Decision Log #1)");
        Assert.Equal(OperationStatus.DuplicateRequest, duplicate.Status);
        Assert.Equal("مشتری فارسی ۱۲۳", (await w.Services.Customers.ListAsync(w.Central, false, Token)).Value!.Single().Value.Name);
    }

    [Fact]
    public async Task A_work_session_runs_from_dispatch_to_an_approved_machine_cost_on_SQL_Server_and_the_database_refuses_double_booking()
    {
        await using var db = await SqlServerFixture.CreateAsync();
        var w = await StartAsync(db);
        var s = w.Services;
        var milling = (await s.PlanningMaster.AddUnitAsync(w.Planning, "MILLING", "فرزکاری", Token)).Value!.Value.Id;
        var machine = (await s.PlanningMaster.AddMachineAsync(w.Planning, "M1", "ماشین ۱", milling, Token)).Value!.Value.Id;
        Assert.True((await s.PlanningMaster.AddRateAsync(w.Planning, machine, 1_200_000m, "1405/01/01", "نرخ", Token)).Succeeded);
        var material = (await s.Materials.AddMaterialAsync(w.Warehouse, "ST37-R30", "میلگرد", "ST37", "میلگرد", Token)).Value!.Value.Id;
        Assert.True((await s.Materials.AddPriceAsync(w.Warehouse, material, 90_000m, "1405/01/01", "قیمت", Token)).Succeeded);
        var template = (await s.Calendar.AddTemplateAsync(w.Planning, "D", "شیفت", new TimeOnly(8, 0), new TimeOnly(17, 0), new TimeOnly(12, 0), new TimeOnly(13, 0), Token)).Value!.Value.Id;
        Assert.True((await s.Calendar.AddUnitShiftAsync(w.Planning, milling, template, 3, 1, false, Token)).Succeeded);
        foreach (var user in new[] { w.Operator, w.Supervisor })
        {
            Assert.True((await s.UnitDesignations.AssignAsync(w.Planning, user.UserId, milling, Token)).Succeeded);
        }

        var row = (await s.UnitDesignations.LoadAsync(w.Planning, Token)).Value!.Rows.Single(r => r.UserId == w.Supervisor.UserId);
        Assert.True((await s.UnitDesignations.SetSupervisorAsync(w.Planning, row.DesignationId, row.Version, true, Token)).Succeeded);
        var op1 = (await s.Operators.RegisterAsync(w.Planning, "OP1", "علی", w.Operator.UserId, Token)).Value!.Value.Id;
        var op2 = (await s.Operators.RegisterAsync(w.Planning, "OP2", "حسن", null, Token)).Value!.Value.Id;
        foreach (var op in new[] { op1, op2 })
        {
            Assert.True((await s.Operators.AssignAsync(w.Planning, op, machine, "1405/01/01", false, null, Token)).Succeeded);
            Assert.True((await s.WorkerSchedule.ScheduleAsync(w.Planning, op, new DateOnly(2026, 10, 24), milling, template, null, null, null, Token)).Succeeded);
        }

        var requestId = await ReadyRequestAsync(w, "SQL-1");
        var piece = (await s.QcMaster.LoadAsync(w.Qc, Token)).Value!.QuantityTypes.First(t => t.Value.Code == "PIECE").Value.Id;
        var version = (await s.Requests.GetAsync(w.Qc, requestId, Token)).Value!.Request.Version;
        var defined = await s.ItemDefinition.DefineItemAsync(w.Planning, requestId, version, new DefineItemCommand("A", "قطعه", 10m, Priority.Normal, piece, material, 1m, null, null), Token);
        Assert.True(defined.Succeeded, defined.Status + " " + defined.Issue);
        var itemId = defined.Value!.Value.Items.Single().Id;
        var current = (await s.Requests.GetAsync(w.Planning, requestId, Token)).Value!;
        Assert.True((await s.ItemDefinition.DefineStageAsync(w.Planning, requestId, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand("فرز", 2m, milling, null), Token)).Succeeded);

        async Task<StageTarget> TargetAsync()
        {
            var latest = (await s.Requests.GetAsync(w.Planning, requestId, Token)).Value!;
            return new StageTarget(requestId, itemId, 1, latest.Request.Version, latest.Versions.ItemVersion(itemId), latest.Versions.Stages.Where(x => x.Key.ItemId == itemId).ToDictionary(x => x.Key.StageNo, x => x.Value));
        }

        Assert.True((await s.StageWork.PerformAsync(w.Planning, StageAction.Release, await TargetAsync(), Token)).Succeeded);

        // Dispatch and start at 08:00.00 + a fraction of a second (precision), pause 10:00-10:30, finish 12:00.
        w.Clock.UtcNow = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
        var started = await s.WorkSessions.DispatchAsync(w.Supervisor, new DispatchCommand(itemId, 1, machine, op1, null, null, true), Token);
        Assert.True(started.Succeeded, started.Status + " " + started.Issue);
        Assert.Equal(ValidationIssue.SessionMachineBusy, (await s.WorkSessions.DispatchAsync(w.Supervisor, new DispatchCommand(itemId, 1, machine, op2, null, null, true), Token)).Issue);

        // The database itself refuses a second open interval on the same machine, whatever the Application checked.
        await using (var context = await w.Factory.CreateDbContextAsync(Token))
        {
            var rogue = WorkSession.Dispatch(itemId, 1, milling, machine, op2, null, false, null, w.Supervisor.UserId, w.Clock.UtcNow);
            rogue.Start(w.Supervisor.UserId, w.Clock.UtcNow);
            context.WorkSessions.Add(rogue);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(Token));
        }

        w.Clock.UtcNow = new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);
        var reason = (await s.WorkSessions.LoadUnitAsync(w.Supervisor, milling, 7, Token)).Value!.Reasons.Single(r => r.Value.Code == PauseReason.MaterialUnavailable).Value.Id;
        var paused = (await s.WorkSessions.PauseAsync(w.Operator, started.Value!.Value.Id, started.Value.Version, reason, null, Token)).Value!;
        Assert.Equal(ValidationIssue.SubmitBlockedBySessions, (await s.StageWork.PerformAsync(w.Operator, StageAction.SubmitForQc, await TargetAsync(), Token)).Issue);
        w.Clock.UtcNow = new DateTime(2026, 10, 24, 10, 30, 0, DateTimeKind.Utc);
        var resumed = (await s.WorkSessions.ResumeAsync(w.Operator, paused.Value.Id, paused.Version, null, Token)).Value!;
        w.Clock.UtcNow = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234567);
        var finished = (await s.WorkSessions.FinishAsync(w.Supervisor, resumed.Value.Id, resumed.Version, null, Token)).Value!;
        Assert.Equal(TimeSpan.FromMinutes(210), SessionTime.Active(finished.Value, w.Clock.UtcNow));
        Assert.Equal(new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc).AddTicks(1234567), finished.Value.StartedAtUtc);

        Assert.True((await s.WorkSessions.ApproveTimeAsync(w.Planning, finished.Value.Id, finished.Version, Token)).Succeeded);
        Assert.True((await s.StageWork.PerformAsync(w.Operator, StageAction.SubmitForQc, await TargetAsync(), Token)).Succeeded);
        Assert.True((await s.QcInspection.RecordInspectionAsync(w.Qc, await TargetAsync(), [new InspectionEntry(InspectionDisposition.Accepted, 10, null)], Token)).Succeeded);
        var cost = await s.Costing.CalculateAsync(w.Warehouse, itemId, Token);
        Assert.True(cost.Succeeded, cost.Status + " " + cost.Issue);
        Assert.Equal((4_200_000m, 900_000m, 5_100_000m), (cost.Value!.Calculation.Value.MachiningTotalRial, cost.Value.Calculation.Value.MaterialTotalRial, cost.Value.Calculation.Value.TotalRial));

        // A stale row version is refused on SQL Server as on SQLite (application-managed versions).
        var again = await s.WorkSessions.FinishAsync(w.Supervisor, finished.Value.Id, finished.Version - 1, null, Token);
        Assert.NotEqual(OperationStatus.Succeeded, again.Status);
    }
}
