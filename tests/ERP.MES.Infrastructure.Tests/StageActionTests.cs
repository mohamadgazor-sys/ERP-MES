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

// The stage actions release, start and submit for QC on stages planned in production units (Decision Log #79, #99, #109) on a real SQLite file with the real authorization: departments, gates, the QC gate,
// expected row versions and the audit trail written in the same transaction. QC then decides by quantity (QcInspectionTests).
public class StageActionTests
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

        public ERP.MES.Application.Costing.CostingService Costing => Services.Costing;

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
        var fixture = await SecurityFixture.CreateFastAsync();
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
            Services = WorkflowServices.Create(fixture.Db.ContextFactory, fixture.Services.Authorization, PersistenceTestData.Dates, fixture.Clock, new FixedAuditContext()),
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
        var current = (await w.Services.Requests.GetAsync(w.Qc, requestId, Token)).Value!;
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

    private static string[] Snapshot(World w) => w.Fixture.Db.Query(
        "SELECT 'R', Id, Version, UpdatedAtUtc FROM Requests UNION ALL SELECT 'I', Id, Version, UpdatedAtUtc FROM RequestItems "
        + "UNION ALL SELECT 'S', RequestItemId, StageNo, Version || '/' || Status FROM ItemStages "
        + "UNION ALL SELECT 'A', Id, 0, ActionCode FROM AuditLog WHERE ActionCode LIKE 'Stage%' ORDER BY 1, 2, 3, 4").ToArray();

    private static async Task<ItemStage> StageAsync(World w, long requestId, int stageNo) =>
        (await w.Services.Requests.GetAsync(w.Qc, requestId, Token)).Value!.Request.Value.Items.Single(i => i.ItemNo == "A").Stages.Single(s => s.StageNo == stageNo);

    /// <summary>A gate-ready request with item "A" of ten pieces whose route has the given stages (units MILLING, TURNING, ...), the Production user designated to all of them; the first stage is still PLANNED.</summary>
    private static async Task<(long RequestId, long ItemId)> PlannedAsync(World w, int stages = 2)
    {
        var units = new List<long>();
        for (var i = 0; i < stages; i++)
        {
            units.Add(await AddUnitAsync(w, "UNIT" + (i + 1)));
        }

        await MaterialAsync(w);
        foreach (var unit in units)
        {
            Assert.True((await w.Designations.AssignAsync(w.Planning, w.Production.UserId, unit, Token)).Succeeded);
        }

        return await ItemAsync(w, "R-1", "A", units.ToArray(), release: false);
    }

    private static async Task<Outcome<StageActionResult>> ActAsync(World w, Principal? user, StageAction action, (long RequestId, long ItemId) item, int stageNo = 1) =>
        await w.Work.PerformAsync(user, action, await TargetAsync(w, item.RequestId, item.ItemId, stageNo), Token);

    private static async Task<StageActionResult> MustAsync(World w, Principal user, StageAction action, (long RequestId, long ItemId) item, int stageNo = 1)
    {
        var outcome = await ActAsync(w, user, action, item, stageNo);
        Assert.True(outcome.Succeeded, action + ": " + outcome.Status + " " + outcome.Issue);
        Tick(w);
        return outcome.Value!;
    }

    private static async Task AcceptAllAsync(World w, (long RequestId, long ItemId) item, int stageNo = 1)
    {
        var inspected = await w.Qci.RecordInspectionAsync(w.Qc, await TargetAsync(w, item.RequestId, item.ItemId, stageNo), [Accept(10)], Token);
        Assert.True(inspected.Succeeded, inspected.Status + " " + inspected.Issue);
        Tick(w);
    }

    [Fact]
    public async Task The_complete_sequence_release_start_submit_and_quantity_qc_walks_a_two_stage_item_to_completion()
    {
        using var w = await StartAsync();
        WithContract = false;
        var item = await PlannedAsync(w);
        Assert.Equal(ItemStageStatus.Planned, (await StageAsync(w, item.RequestId, 1)).Status);
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, (await StageAsync(w, item.RequestId, 2)).Status);

        Assert.Equal(ItemStageStatus.Waiting, (await MustAsync(w, w.Planning, StageAction.Release, item)).NewStatus);
        Assert.Equal(ItemStageStatus.InProgress, (await MustAsync(w, w.Production, StageAction.Start, item)).NewStatus);
        Assert.Equal(ItemStageStatus.WaitingQc, (await MustAsync(w, w.Production, StageAction.SubmitForQc, item)).NewStatus);

        // The next stage stays locked until QC has approved the preceding one by quantity.
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, (await StageAsync(w, item.RequestId, 2)).Status);
        await AcceptAllAsync(w, item);
        Assert.Equal(ItemStageStatus.QcApproved, (await StageAsync(w, item.RequestId, 1)).Status);
        Assert.Equal(ItemStageStatus.Waiting, (await StageAsync(w, item.RequestId, 2)).Status);

        await MustAsync(w, w.Production, StageAction.Start, item, 2);
        await MustAsync(w, w.Production, StageAction.SubmitForQc, item, 2);
        await AcceptAllAsync(w, item, 2);
        Assert.True((await w.Services.Requests.GetAsync(w.Qc, item.RequestId, Token)).Value!.Request.Value.Items.Single(i => i.ItemNo == "A").IsRouteCompleted);
    }

    [Fact]
    public async Task The_next_stage_cannot_be_started_or_submitted_while_the_preceding_stage_is_not_approved()
    {
        using var w = await StartAsync();
        WithContract = false;
        var item = await PlannedAsync(w);
        await MustAsync(w, w.Planning, StageAction.Release, item);
        await MustAsync(w, w.Production, StageAction.Start, item);
        await MustAsync(w, w.Production, StageAction.SubmitForQc, item);
        var before = Snapshot(w);

        // Stage 2 is named with perfectly current versions and still cannot pass the gate: stage 1 is not approved.
        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Production, StageAction.Start, item, 2)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Production, StageAction.SubmitForQc, item, 2)).Status);
        Assert.Equal(before, Snapshot(w));
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, (await StageAsync(w, item.RequestId, 2)).Status);
    }

    [Fact]
    public async Task Each_action_is_allowed_only_for_its_department_and_a_refusal_changes_and_audits_nothing()
    {
        using var w = await StartAsync();
        WithContract = false;
        var item = await PlannedAsync(w);
        var before = Snapshot(w);

        foreach (var user in new Principal?[] { w.Production, w.Qc, w.Manager, w.Central, w.Drawing, w.Warehouse, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await ActAsync(w, user, StageAction.Release, item)).Status);
        }

        Assert.Equal(before, Snapshot(w));
        await MustAsync(w, w.Planning, StageAction.Release, item);

        before = Snapshot(w);
        foreach (var user in new Principal?[] { w.Planning, w.Qc, w.Manager, w.Central, w.Drawing, w.Warehouse, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await ActAsync(w, user, StageAction.Start, item)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await ActAsync(w, user, StageAction.SubmitForQc, item)).Status);
        }

        Assert.Equal(before, Snapshot(w));
    }

    [Fact]
    public async Task A_deactivated_user_loses_the_permission_at_once_and_a_forged_principal_is_judged_by_the_database()
    {
        using var w = await StartAsync();
        WithContract = false;
        var item = await PlannedAsync(w, 1);
        Assert.Equal(AccountStatus.Succeeded, (await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Planning.UserId, false, Token)).Status);
        var before = Snapshot(w);

        Assert.Equal(OperationStatus.NotAuthorized, (await ActAsync(w, w.Planning, StageAction.Release, item)).Status);
        var forged = new Principal(w.Drawing.UserId, w.Drawing.Username, w.Drawing.DisplayName, DepartmentCodes.Planning, RoleCodes.Staff, false);
        Assert.Equal(OperationStatus.NotAuthorized, (await ActAsync(w, forged, StageAction.Release, item)).Status);
        Assert.Equal(before, Snapshot(w));

        Assert.Equal(AccountStatus.Succeeded, (await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Planning.UserId, true, Token)).Status);
        await MustAsync(w, w.Planning, StageAction.Release, item);
        Assert.Equal(AccountStatus.Succeeded, (await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Production.UserId, false, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await ActAsync(w, w.Production, StageAction.Start, item)).Status);
    }

    [Fact]
    public async Task Invalid_transitions_are_refused_and_change_nothing()
    {
        using var w = await StartAsync();
        WithContract = false;
        var item = await PlannedAsync(w);
        var before = Snapshot(w);

        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Production, StageAction.Start, item)).Status);       // PLANNED: only release is possible
        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Production, StageAction.SubmitForQc, item)).Status);
        Assert.Equal(before, Snapshot(w));

        await MustAsync(w, w.Planning, StageAction.Release, item);
        before = Snapshot(w);
        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Planning, StageAction.Release, item)).Status);       // WAITING: not releasable again, not submittable
        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Production, StageAction.SubmitForQc, item)).Status);
        Assert.Equal(before, Snapshot(w));

        await MustAsync(w, w.Production, StageAction.Start, item);
        before = Snapshot(w);
        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Production, StageAction.Start, item)).Status);       // IN_PROGRESS: not startable again
        Assert.Equal(before, Snapshot(w));
    }

    [Fact]
    public async Task A_repeated_action_on_an_old_view_or_a_stale_request_item_stage_or_previous_stage_version_is_a_conflict_and_changes_nothing()
    {
        using var w = await StartAsync();
        WithContract = false;
        var item = await PlannedAsync(w);
        var stale = await TargetAsync(w, item.RequestId, item.ItemId, 1);

        // Stale item version: Planning changes the priority after the view was loaded.
        await w.Fixture.Db.Requests().UpdateRowsAsync(item.RequestId, RowVersionExpectations.None, r => Assert.True(r.Items.Single().ChangePriority(Priority.VeryUrgent, w.Fixture.Clock.UtcNow)), Token);
        var before = Snapshot(w);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Work.PerformAsync(w.Planning, StageAction.Release, stale, Token)).Status);
        Assert.Equal(before, Snapshot(w));

        // Stale request version: another user adds an item.
        var staleRequest = await TargetAsync(w, item.RequestId, item.ItemId, 1);
        Tick(w);
        await w.Fixture.Db.Requests().UpdateRowsAsync(item.RequestId, RowVersionExpectations.None, r => r.AddItem(0, "2.10", "Shaft B", 1m, w.Planning.UserId, w.Fixture.Clock.UtcNow), Token);
        before = Snapshot(w);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Work.PerformAsync(w.Planning, StageAction.Release, staleRequest, Token)).Status);
        Assert.Equal(before, Snapshot(w));

        // A repeated click on the old view never runs twice: the stage was started by another user.
        await MustAsync(w, w.Planning, StageAction.Release, item);
        var waitingView = await TargetAsync(w, item.RequestId, item.ItemId, 1);
        await MustAsync(w, w.Production, StageAction.Start, item);
        before = Snapshot(w);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Work.PerformAsync(w.Production, StageAction.Start, waitingView, Token)).Status);
        Assert.Equal(before, Snapshot(w));
        Assert.Equal(ItemStageStatus.InProgress, (await StageAsync(w, item.RequestId, 1)).Status);
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'StageStarted'"));

        // A stale PRECEDING stage version rejects the start of the next stage.
        await MustAsync(w, w.Production, StageAction.SubmitForQc, item);
        await AcceptAllAsync(w, item);
        var current = await TargetAsync(w, item.RequestId, item.ItemId, 2);
        before = Snapshot(w);
        var wrongPrevious = current with { StageVersions = new Dictionary<int, long>(current.StageVersions) { [1] = current.StageVersions[1] + 1 } };
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Work.PerformAsync(w.Production, StageAction.Start, wrongPrevious, Token)).Status);
        Assert.Equal(before, Snapshot(w));
        Assert.Equal(ItemStageStatus.Waiting, (await StageAsync(w, item.RequestId, 2)).Status);
    }

    [Fact]
    public async Task Missing_row_versions_unknown_actions_and_vanished_items_are_refused_before_anything_is_changed()
    {
        using var w = await StartAsync();
        WithContract = false;
        var item = await PlannedAsync(w);
        var target = await TargetAsync(w, item.RequestId, item.ItemId, 1);
        var before = Snapshot(w);

        var outcome = await w.Work.PerformAsync(w.Planning, StageAction.Release, target with { StageVersions = new Dictionary<int, long>() }, Token);
        Assert.Equal((OperationStatus.ValidationFailed, ValidationIssue.VersionsMissing), (outcome.Status, outcome.Issue));
        Assert.Equal(OperationStatus.ValidationFailed, (await w.Work.PerformAsync(w.Planning, (StageAction)99, target, Token)).Status);
        Assert.Equal(OperationStatus.ValidationFailed, (await w.Work.PerformAsync(w.Planning, (StageAction)4, target, Token)).Status); // the retired whole-quantity approval
        Assert.Equal(OperationStatus.NotFound, (await w.Work.PerformAsync(w.Planning, StageAction.Release, target with { ItemId = target.ItemId + 999 }, Token)).Status);
        Assert.Equal(OperationStatus.NotFound, (await w.Work.PerformAsync(w.Planning, StageAction.Release, target with { RequestId = target.RequestId + 999 }, Token)).Status);
        Assert.Equal(before, Snapshot(w));
    }

    [Fact]
    public async Task Every_action_writes_one_audit_entry_with_user_target_and_before_after_state_and_refusals_conflicts_and_failures_write_none()
    {
        using var w = await StartAsync();
        WithContract = false;
        var item = await PlannedAsync(w, 1);
        var stale = await TargetAsync(w, item.RequestId, item.ItemId, 1);

        await MustAsync(w, w.Planning, StageAction.Release, item);
        await MustAsync(w, w.Production, StageAction.Start, item);
        await MustAsync(w, w.Production, StageAction.SubmitForQc, item);

        var rows = w.Fixture.Db.Query("SELECT ActionCode, UserId, TargetType, TargetId, MachineName, OsUserName, Summary FROM AuditLog WHERE ActionCode LIKE 'Stage%' ORDER BY Id");
        Assert.Equal(new[] { "StageReleased", "StageStarted", "StageSubmittedForQc" }, rows.Select(r => r.Split('|')[0]).ToArray());
        Assert.Equal(
            new[] { w.Planning.UserId, w.Production.UserId, w.Production.UserId }.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            rows.Select(r => r.Split('|')[1]).ToArray());
        Assert.All(rows, r =>
        {
            var cells = r.Split('|');
            Assert.Equal("ItemStage", cells[2]);
            Assert.Equal(item.ItemId + "/1", cells[3]);
            Assert.Equal("TEST-PC", cells[4]);
        });
        Assert.Contains("PLANNED -> WAITING", rows[0]);
        Assert.Contains("WAITING -> IN_PROGRESS", rows[1]);
        Assert.Contains("IN_PROGRESS -> WAITING_QC", rows[2]);
        Assert.Contains("Request R-1, item A, stage 1", rows[0]);
        var text = string.Join("\n", rows);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);

        // A conflict, an invalid state and a refusal add nothing.
        await w.Work.PerformAsync(w.Production, StageAction.Start, stale, Token);
        await ActAsync(w, w.Production, StageAction.SubmitForQc, item);
        await ActAsync(w, w.Planning, StageAction.Start, item);
        Assert.Equal(3, Count(w, "AuditLog WHERE ActionCode LIKE 'Stage%'"));
    }

    [Fact]
    public async Task Stages_follow_the_unit_designation_and_the_request_gates()
    {
        using var w = await StartAsync();
        WithContract = false;
        var item = await PlannedAsync(w, 1);
        await MustAsync(w, w.Planning, StageAction.Release, item);

        // A Production user who is not designated to the stage's unit cannot start it.
        var designation = (await w.Designations.LoadAsync(w.Planning, Token)).Value!.Rows.Single();
        Assert.True((await w.Designations.RevokeAsync(w.Planning, designation.DesignationId, designation.Version, Token)).Succeeded);
        var refused = await ActAsync(w, w.Production, StageAction.Start, item);
        Assert.Equal((OperationStatus.ValidationFailed, ValidationIssue.StageUnitNotDesignated), (refused.Status, refused.Issue));
        Assert.Equal(ItemStageStatus.Waiting, (await StageAsync(w, item.RequestId, 1)).Status);
    }
}
