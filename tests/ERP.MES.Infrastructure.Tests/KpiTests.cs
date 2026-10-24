using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;
using ERP.MES.Application.Production;
using ERP.MES.Application.Reporting;
using ERP.MES.Application.Costing;
using ERP.MES.Domain.Costing;
using ERP.MES.Application.Scheduling;
using ERP.MES.Domain.Reporting;
using ERP.MES.Domain.Scheduling;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// The unit metrics (docs/JOB_REPORTS_KPI_AND_OVERTIME_DESIGN.md section 7; Decision Log #95 D6/D8, #106) on a real SQLite file with the real authorization.
public class KpiTests
{
    private const string InitialPassword = "Staff-Pass-Phrase-2";
    private const string OwnPassword = "My-Own-Pass-Phrase-3";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class World : IDisposable
    {
        public required SecurityFixture Fixture { get; init; }

        public required Principal Admin { get; init; }

        public required Principal Central { get; init; }

        public required Principal Manager { get; init; }

        public required Principal ManagementStaff { get; init; }

        public required Principal Planning { get; init; }

        public required Principal Drawing { get; init; }

        public required Principal Warehouse { get; init; }

        public required Principal Qc { get; init; }

        public required Principal Production { get; init; }

        public required Principal Supervisor { get; init; }

        public required Principal OtherOperator { get; init; }

        public required WorkflowServices Services { get; init; }

        public InquiryService Inquiries => Services.Inquiries;

        public ClarificationService Clarifications => Services.Clarifications;

        public RequestGateService Gates => Services.Gates;

        public ContractService Contracts => Services.Contracts;

        public ItemDefinitionService Items => Services.ItemDefinition;

        public UnitQueueService Queue => Services.UnitQueue;

        public UnitDesignationService Designations => Services.UnitDesignations;

        public StageWorkService Work => Services.StageWork;

        public CalendarService Calendar => Services.Calendar;

        public JobReportService Reports => Services.JobReports;

        public KpiService Kpi => Services.Kpi;

        public QcInspectionService Qci => Services.QcInspection;

        public WorkerScheduleService Schedule => Services.WorkerSchedule;

        public void Dispose() => Fixture.Dispose();
    }

    private static async Task<Principal> UserAsync(SecurityFixture fixture, Principal admin, string name, string department, string role)
    {
        await fixture.CreateUserAsync(admin, name, InitialPassword, department, role, Token);
        var first = await fixture.SignInAsync(name, InitialPassword, Token);
        await fixture.Services.Accounts.ChangeOwnPasswordAsync(first, InitialPassword, OwnPassword, Token);
        return await fixture.SignInAsync(name, OwnPassword, Token);
    }

    private static async Task<World> StartAsync()
    {
        var fixture = await SecurityFixture.CreateAsync();
        var admin = await fixture.BootstrapAdminAsync(cancellationToken: Token);
        return new World
        {
            Fixture = fixture,
            Admin = admin,
            Central = await UserAsync(fixture, admin, "central.one", DepartmentCodes.Central, RoleCodes.Staff),
            Manager = await UserAsync(fixture, admin, "manager.one", DepartmentCodes.Management, RoleCodes.Manager),
            ManagementStaff = await UserAsync(fixture, admin, "management.staff", DepartmentCodes.Management, RoleCodes.Staff),
            Planning = await UserAsync(fixture, admin, "planning.one", DepartmentCodes.Planning, RoleCodes.Staff),
            Drawing = await UserAsync(fixture, admin, "drawing.one", DepartmentCodes.Drawing, RoleCodes.Staff),
            Warehouse = await UserAsync(fixture, admin, "warehouse.one", DepartmentCodes.Warehouse, RoleCodes.Staff),
            Qc = await UserAsync(fixture, admin, "qc.one", DepartmentCodes.Qc, RoleCodes.Staff),
            Production = await UserAsync(fixture, admin, "production.one", DepartmentCodes.Production, RoleCodes.Staff),
            Supervisor = await UserAsync(fixture, admin, "supervisor.one", DepartmentCodes.Production, RoleCodes.Staff),
            OtherOperator = await UserAsync(fixture, admin, "production.two", DepartmentCodes.Production, RoleCodes.Staff),
            Services = WorkflowServices.Create(fixture.Db.ContextFactory, fixture.Services.Authorization, PersistenceTestData.Dates, fixture.Clock),
        };
    }

    private static byte[] Pdf(string text = "fictional") => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% " + text + "\n%%EOF\n");

    private static void Tick(World w) => w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));

    /// <summary>A request taken through Central, Management and Drawing to DRAWING_RETURNED_TO_PLANNING with the given Drawing outcome.</summary>
    private static async Task<long> ReturnedAsync(World w, WorkType type = WorkType.Make, DrawingOutcome outcome = DrawingOutcome.FullyAccepted, string requestNo = "C-1")
    {
        var customer = (await w.Services.Customers.CreateAsync(w.Central, "Fictional Customer " + requestNo, Token)).Value!;
        var created = (await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, requestNo, type, "1405/07/20", null, null), Token)).Value!;
        Tick(w);
        var withPdf = (await w.Services.Documents.AttachRequestPdfAsync(w.Central, created.Value.Id, created.Version, "a.pdf", Pdf(), Token)).Value!;
        Tick(w);
        var sent = (await w.Services.Requests.SubmitToManagementAsync(w.Central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        Tick(w);
        var approved = (await w.Services.Lifecycle.ApproveAsync(w.Manager, sent.Value.Id, sent.Version, Token)).Value!;
        Tick(w);
        var forwarded = (await w.Services.Lifecycle.ForwardToDrawingAsync(w.Planning, approved.Value.Id, approved.Version, Token)).Value!;
        Tick(w);
        var note = outcome == DrawingOutcome.FullyAccepted ? null : "یادداشت فنی";
        var returned = await w.Services.Lifecycle.RecordDrawingResultAsync(w.Drawing, forwarded.Value.Id, forwarded.Version, outcome, note, Token);
        Assert.True(returned.Succeeded, returned.Status + " " + returned.Issue);
        Tick(w);
        return created.Value.Id;
    }

    private static InquiryLineInput Line(string grade = "ST37", string form = "میلگرد", long unitId = 3) =>
        new(grade, form, 30m, 6000m, null, 12m, unitId, "DIN 1013", "برش نخورده");

    private static async Task<long> KgUnitAsync(World w) =>
        (await w.Fixture.Db.ContextFactory.CreateDbContextAsync(Token)) is var c && c is not null
            ? c.UnitsOfMeasure.First(u => u.Code == "KG").Id
            : 0;

    private static async Task<Stored<RawMaterialInquiry>> DraftWithLinesAsync(World w, long requestId, int lines = 2)
    {
        var draft = (await w.Inquiries.CreateAsync(w.Drawing, requestId, "فوری", Token)).Value!;
        var unit = await KgUnitAsync(w);
        for (var i = 1; i <= lines; i++)
        {
            var added = await w.Inquiries.AddLineAsync(w.Drawing, draft.Value.Id, draft.Version, Line("ST3" + i, unitId: unit), Token);
            Assert.True(added.Succeeded, added.Status + " " + added.Issue);
            draft = added.Value!;
            Tick(w);
        }

        return draft;
    }

    private static async Task<Stored<RawMaterialInquiry>> SentAsync(World w, long requestId, int lines = 2)
    {
        var draft = await DraftWithLinesAsync(w, requestId, lines);
        var sent = await w.Inquiries.SendAsync(w.Drawing, draft.Value.Id, draft.Version, Token);
        Assert.True(sent.Succeeded, sent.Status + " " + sent.Issue);
        Tick(w);
        return sent.Value!;
    }

    /// <summary>A request that passed Management and Drawing and has its contract decision (MAKE: not required): the gates hold. Returns the id and the current request version.</summary>
    private static async Task<(long Id, long Version)> ReadyAsync(World w, WorkType type = WorkType.Make, string requestNo = "D-1")
    {
        var id = await ReturnedAsync(w, type, DrawingOutcome.FullyAccepted, requestNo);
        if (type == WorkType.Make)
        {
            var decided = await w.Contracts.DecideRequirementAsync(w.Planning, id, WithContract ? ContractRequirement.Required : ContractRequirement.NotRequired, null, Token);
            Assert.True(decided.Succeeded);
            Tick(w);
            if (WithContract)
            {
                var draft = (await w.Contracts.SaveDraftAsync(w.Planning, id, decided.Value!.Version, "CN-100", "1405/07/25", "قرارداد ساخت", null, Token)).Value!;
                Tick(w);
                var withPdf = (await w.Contracts.AttachPdfAsync(w.Planning, id, draft.Version, "contract.pdf", Pdf("contract"), Token)).Value!;
                Tick(w);
                var submitted = (await w.Contracts.SubmitAsync(w.Planning, id, withPdf.Version, Token)).Value!;
                Tick(w);
                Assert.True((await w.Contracts.ApproveAsync(w.Manager, id, submitted.Version, null, Token)).Succeeded);
                Tick(w);
            }
        }

        return (id, await VersionAsync(w, id));
    }

    private static bool WithContract { get; set; }

    private static async Task<long> VersionAsync(World w, long id) => (await w.Services.Requests.GetAsync(w.Qc, id, Token)).Value!.Request.Version;

    private static async Task<Request> LoadAsync(World w, long id) => (await w.Services.Requests.GetAsync(w.Qc, id, Token)).Value!.Request.Value;

    private static async Task<(long PieceId, long PairId)> TypesAsync(World w)
    {
        var data = (await w.Services.QcMaster.LoadAsync(w.Qc, Token)).Value!;
        return (data.QuantityTypes.First(t => t.Value.Code == "PIECE").Value.Id, data.QuantityTypes.First(t => t.Value.Code == "PAIR").Value.Id);
    }

    private static async Task<long> MaterialAsync(World w, string code = "ST37-R30")
    {
        var outcome = await w.Services.Materials.AddMaterialAsync(w.Warehouse, code, "میلگرد ST37", "ST37", "میلگرد", Token);
        Assert.True(outcome.Succeeded, outcome.Status + " " + outcome.Issue);
        return outcome.Value!.Value.Id;
    }
    private static DefineItemCommand Repair(string itemNo = "2.10", Priority priority = Priority.Normal) => new(itemNo, "Shaft B", 4m, priority, null, null, null, null, null);

    private static long Count(World w, string tableAndWhere) => long.Parse(w.Fixture.Db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);


    private static long UnitId(World w, string code) => long.Parse(w.Fixture.Db.Query($"SELECT Id FROM ProductionUnits WHERE Code = '{code}'").Single(), System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<long> AddUnitAsync(World w, string code)
    {
        var unit = await w.Services.PlanningMaster.AddUnitAsync(w.Planning, code, "واحد " + code, Token);
        Assert.True(unit.Succeeded, unit.Status + " " + unit.Issue);
        return unit.Value!.Value.Id;
    }

    private static async Task<StageTarget> TargetAsync(World w, long requestId, long itemId, int stageNo)
    {
        var current = (await w.Services.Requests.GetAsync(w.Planning, requestId, Token)).Value!;
        return new StageTarget(requestId, itemId, stageNo, current.Request.Version, current.Versions.ItemVersion(itemId), current.Versions.Items.Keys.Contains(itemId)
            ? current.Versions.Stages.Where(s => s.Key.ItemId == itemId).ToDictionary(s => s.Key.StageNo, s => s.Value)
            : new Dictionary<int, long>());
    }

    /// <summary>A request through every gate with one item whose route has the given stages (all planned in the given units), the first stage optionally released.</summary>
    private static async Task<(long RequestId, long ItemId)> ItemAsync(World w, string requestNo, string itemNo, long[] stageUnits, Priority priority = Priority.Normal, bool release = true)
    {
        var (id, version) = await ReadyAsync(w, WorkType.Make, requestNo);
        var (piece, _) = await TypesAsync(w);
        var material = long.Parse(w.Fixture.Db.Query("SELECT Id FROM Materials LIMIT 1").Single(), System.Globalization.CultureInfo.InvariantCulture);
        var defined = await w.Items.DefineItemAsync(w.Planning, id, version, new DefineItemCommand(itemNo, "Part " + itemNo, 10m, priority, piece, material, 1m, null, null), Token);
        Assert.True(defined.Succeeded, defined.Status + " " + defined.Issue);
        var itemId = defined.Value!.Value.Items.Single(i => i.ItemNo == itemNo).Id;
        for (var i = 0; i < stageUnits.Length; i++)
        {
            var current = (await w.Services.Requests.GetAsync(w.Planning, id, Token)).Value!;
            var staged = await w.Items.DefineStageAsync(w.Planning, id, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand("مرحله " + (i + 1), 2m, stageUnits[i], null), Token);
            Assert.True(staged.Succeeded, staged.Status + " " + staged.Issue);
        }

        if (release)
        {
            var released = await w.Work.PerformAsync(w.Planning, StageAction.Release, await TargetAsync(w, id, itemId, 1), Token);
            Assert.True(released.Succeeded, released.Status + " " + released.Issue);
        }

        Tick(w);
        return (id, itemId);
    }

    private static async Task<UnitQueue> LoadAsync(World w, Principal user, long unit)
    {
        var outcome = await w.Queue.LoadAsync(user, unit, Token);
        Assert.True(outcome.Succeeded, outcome.Status + " " + outcome.Issue);
        return outcome.Value!;
    }


    private static readonly DateOnly Saturday = new(2026, 10, 24);
    private static readonly DateOnly Friday = new(2026, 10, 23);

    private static async Task<long> TemplateAsync(World w, string code, string start, string end, string? breakStart = null, string? breakEnd = null)
    {
        var added = await w.Calendar.AddTemplateAsync(w.Planning, code, "شیفت " + code, TimeOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), TimeOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture),
            breakStart is null ? null : TimeOnly.Parse(breakStart, System.Globalization.CultureInfo.InvariantCulture), breakEnd is null ? null : TimeOnly.Parse(breakEnd, System.Globalization.CultureInfo.InvariantCulture), Token);
        Assert.True(added.Succeeded, added.Status + " " + added.Issue);
        return added.Value!.Value.Id;
    }

    private static async Task<long> OperatorAsync(World w, string code)
    {
        var added = await w.Services.Operators.RegisterAsync(w.Planning, code, "اپراتور " + code, null, Token);
        Assert.True(added.Succeeded, added.Status + " " + added.Issue);
        return added.Value!.Value.Id;
    }

    private static async Task<long> RunUnitShiftAsync(World w, long unit, long template, int max = 2, int min = 1, bool overlap = false)
    {
        var added = await w.Calendar.AddUnitShiftAsync(w.Planning, unit, template, max, min, overlap, Token);
        Assert.True(added.Succeeded, added.Status + " " + added.Issue);
        return added.Value!.Value.Id;
    }

    private static readonly DateTime Day = new(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc); // a Saturday; the tests' display zone is UTC

    private static DateTime At(int hour, int minute = 0) => Day.AddHours(hour).AddMinutes(minute);

    private sealed record Setup(long Unit, long Template, long Machine, long RequestId, long ItemId, long OperatorId, long Designation);

    /// <summary>A unit with shift D (08:00-17:00), a machine, an item stage that is running in it, an operator linked to the Production user and planned on the date.</summary>
    private static async Task<Setup> SetupAsync(World w, bool plan = true, bool supervisor = true)
    {
        w.Fixture.Clock.UtcNow = At(18);
        var unit = await AddUnitAsync(w, "MILLING");
        var template = await TemplateAsync(w, "D", "08:00", "17:00", "12:00", "13:00");
        await RunUnitShiftAsync(w, unit, template, 3, 1);
        var machine = (await w.Services.PlanningMaster.AddMachineAsync(w.Planning, "FCC-06", "فرز", unit, Token)).Value!.Value.Id;
        await MaterialAsync(w);
        var item = await ItemAsync(w, "R-1", "A", [unit]);
        var designation = (await w.Designations.AssignAsync(w.Planning, w.Production.UserId, unit, Token)).Value!;
        Assert.True((await w.Designations.AssignAsync(w.Planning, w.Supervisor.UserId, unit, Token)).Succeeded);
        if (supervisor)
        {
            var row = (await w.Designations.LoadAsync(w.Planning, Token)).Value!.Rows.Single(r => r.UserId == w.Supervisor.UserId);
            Assert.True((await w.Designations.SetSupervisorAsync(w.Planning, row.DesignationId, row.Version, true, Token)).Succeeded);
        }

        Assert.True((await w.Work.PerformAsync(w.Production, StageAction.Start, await TargetAsync(w, item.RequestId, item.ItemId, 1), Token)).Succeeded);
        var operatorId = (await w.Services.Operators.RegisterAsync(w.Planning, "OP-1", "علی رضایی", w.Production.UserId, Token)).Value!.Value.Id;
        if (plan)
        {
            Assert.True((await w.Schedule.ScheduleAsync(w.Planning, operatorId, DateOnly.FromDateTime(Day), unit, template, machine, null, null, Token)).Succeeded);
        }

        return new Setup(unit, template, machine, item.RequestId, item.ItemId, operatorId, designation.DesignationId);
    }

    private static JobLineInput Line(Setup s, int fromHour, int toHour, long units = 8, int downtime = 0, string? downtimeReason = null, long? machine = null) =>
        new(s.RequestId, s.ItemId, 1, machine ?? s.Machine, At(fromHour), At(toHour), downtime, downtimeReason, units, null);

    private static async Task<Stored<DailyJobReport>> DraftAsync(World w, Setup s, params JobLineInput[] lines)
    {
        var saved = await w.Reports.SaveDraftAsync(w.Production, null, null, DateOnly.FromDateTime(Day), s.Unit, s.Template, lines, Token);
        Assert.True(saved.Succeeded, saved.Status + " " + saved.Issue);
        return saved.Value!;
    }

    private static async Task<Stored<DailyJobReport>> SubmittedAsync(World w, Setup s, params JobLineInput[] lines)
    {
        var draft = await DraftAsync(w, s, lines);
        var submitted = await w.Reports.SubmitAsync(w.Production, draft.Value.Id, draft.Version, Token);
        Assert.True(submitted.Succeeded, submitted.Status + " " + submitted.Issue);
        return submitted.Value!;
    }


    private static InspectionEntry Accept(long units) => new(InspectionDisposition.Accepted, units, null);

    private static async Task CompleteRouteAsync(World w, Setup s)
    {
        Assert.True((await w.Work.PerformAsync(w.Production, StageAction.SubmitForQc, await TargetAsync(w, s.RequestId, s.ItemId, 1), Token)).Succeeded);
        var inspected = await w.Qci.RecordInspectionAsync(w.Qc, await TargetAsync(w, s.RequestId, s.ItemId, 1), [Accept(10)], Token);
        Assert.True(inspected.Succeeded, inspected.Status + " " + inspected.Issue);
        Tick(w);
    }

    private static readonly DateOnly Day0 = new(2026, 10, 24);

    [Fact]
    public async Task The_metrics_come_from_approved_reports_qc_decisions_and_the_schedule_and_unapproved_reports_are_counted_not_used()
    {
        using var w = await StartAsync();
        WithContract = false;
        var s = await SetupAsync(w); // OP-1 planned on Saturday on shift D (480 working minutes)
        var first = await SubmittedAsync(w, s, Line(s, 8, 12, 10));
        Assert.True((await w.Reports.ReviewAsync(w.Supervisor, first.Value.Id, first.Version, true, null, Token)).Succeeded);

        // A second operator's report stays waiting for approval: it is counted, never used.
        Assert.True((await w.Services.Operators.RegisterAsync(w.Planning, "OP-2", "رضا احمدی", w.OtherOperator.UserId, Token)).Succeeded);
        var waiting = (await w.Reports.SaveDraftAsync(w.OtherOperator, null, null, Day0, s.Unit, s.Template, [Line(s, 13, 15, 2)], Token)).Value!;
        Assert.True((await w.Reports.SubmitAsync(w.OtherOperator, waiting.Value.Id, waiting.Version, Token)).Succeeded);
        await CompleteRouteAsync(w, s);

        var report = (await w.Kpi.ReportAsync(w.Manager, Day0, Day0, Token)).Value!;
        var unit = report.Units.Single(u => u.UnitId == s.Unit);
        Assert.Equal((240, 10L, 0L, 0L), (unit.ProductiveMinutes, unit.AcceptedBaseUnits, unit.ReworkBaseUnits, unit.ScrapBaseUnits));
        Assert.Equal((1m, 0m, 0m), (unit.AcceptanceRate, unit.ReworkRate, unit.ScrapRate));
        Assert.Equal((1, 20m, 240, 12m), (unit.CompletedStages, unit.PlannedMinutes, unit.ActualMinutes, unit.ActualToPlanned));
        Assert.Equal((480, 0.5m), (unit.ScheduledMinutes, unit.Utilization));
        Assert.Equal((1, false), (unit.PendingReports, unit.IsAuthoritative));
        Assert.Equal(1, report.TotalPendingReports);
        var ranked = Assert.Single(report.Operators);
        Assert.Equal(("OP-1", 1, 240, 1), (ranked.Code, ranked.Rank, ranked.ProductiveMinutes, ranked.WorkedDays));

        // Once the waiting report is approved it counts and the unit becomes authoritative.
        Assert.True((await w.Reports.ReviewAsync(w.Supervisor, waiting.Value.Id, (await w.Reports.LoadAsync(w.Planning, Day0, Day0, Token)).Value!.Items.Single(i => i.Report.Value.Id == waiting.Value.Id).Report.Version, true, null, Token)).Succeeded);
        var later = (await w.Kpi.ReportAsync(w.Planning, Day0, Day0, Token)).Value!.Units.Single(u => u.UnitId == s.Unit);
        Assert.Equal((360, 0, true), (later.ProductiveMinutes, later.PendingReports, later.IsAuthoritative));

        // Another day has no figures.
        var empty = (await w.Kpi.ReportAsync(w.Planning, Day0.AddDays(10), Day0.AddDays(12), Token)).Value!.Units.Single(u => u.UnitId == s.Unit);
        Assert.Equal((0, 0L, (decimal?)null), (empty.ProductiveMinutes, empty.AcceptedBaseUnits, empty.AcceptanceRate));
    }

    [Fact]
    public async Task Planning_and_management_read_every_unit_a_supervisor_only_their_own_and_everyone_else_is_refused()
    {
        using var w = await StartAsync();
        WithContract = false;
        var s = await SetupAsync(w);
        var report = await SubmittedAsync(w, s, Line(s, 8, 12, 10));
        Assert.True((await w.Reports.ReviewAsync(w.Supervisor, report.Value.Id, report.Version, true, null, Token)).Succeeded);
        var other = (await w.Services.PlanningMaster.AddUnitAsync(w.Planning, "TURNING", "تراش", Token)).Value!.Value.Id;

        foreach (var user in new[] { w.Planning, w.Manager })
        {
            var all = (await w.Kpi.ReportAsync(user, Day0, Day0, Token)).Value!;
            Assert.Equal(new[] { "MILLING", "TURNING" }, all.Units.Select(u => u.Code).ToArray());
        }

        var supervised = (await w.Kpi.ReportAsync(w.Supervisor, Day0, Day0, Token)).Value!;
        Assert.Equal(new[] { "MILLING" }, supervised.Units.Select(u => u.Code).ToArray());
        Assert.Equal(240, supervised.Units.Single().ProductiveMinutes);
        Assert.DoesNotContain(supervised.Units, u => u.UnitId == other);

        // A Production user who supervises no unit, and every other department, is refused.
        foreach (var user in new Principal?[] { w.Production, w.OtherOperator, w.Qc, w.Drawing, w.Central, w.Warehouse, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Kpi.ReportAsync(user, Day0, Day0, Token)).Status);
        }

        Assert.Equal(ValidationIssue.EndBeforeStart, (await w.Kpi.ReportAsync(w.Planning, Day0, Day0.AddDays(-1), Token)).Issue);
        Assert.Equal(ValidationIssue.EndBeforeStart, (await w.Kpi.ReportAsync(w.Planning, Day0, Day0.AddDays(401), Token)).Issue);
    }
}
