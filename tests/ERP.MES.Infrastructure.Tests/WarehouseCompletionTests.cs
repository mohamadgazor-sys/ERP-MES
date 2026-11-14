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
using ERP.MES.Domain.Warehouse;
using ERP.MES.Application.Scheduling;
using ERP.MES.Domain.Reporting;
using ERP.MES.Domain.Scheduling;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// Warehouse receipt, finished/sent and customer dispatch (Master Specification 17.2; Decision Log #95 D7, #105) on a real SQLite file with the real authorization.
public class WarehouseCompletionTests
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
        public required Principal ProductionManager { get; init; }

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

        public ERP.MES.Application.Costing.WarehouseCompletionService Completion => Services.Completion;

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
            ProductionManager = await UserAsync(fixture, admin, "prodmanager.one", DepartmentCodes.ProductionManager, RoleCodes.Manager),
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
        var unit = await w.Services.PlanningMaster.AddUnitAsync(w.Admin, code, "واحد " + code, Token);
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
        var itemId = defined.Value!.Value.Items.Single(i => i.CustomerItemRef == itemNo).Id;
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
        var added = await w.Services.Operators.RegisterAsync(w.Admin, code, "اپراتور " + code, null, Token);
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
        var machine = (await w.Services.PlanningMaster.AddMachineAsync(w.Admin, "FCC-06", "فرز", unit, Token)).Value!.Value.Id;
        await MaterialAsync(w);
        var item = await ItemAsync(w, "R-1", "A", [unit]);
        var designation = (await w.Designations.AssignAsync(w.Admin, w.Production.UserId, unit, Token)).Value!;
        Assert.True((await w.Designations.AssignAsync(w.Admin, w.Supervisor.UserId, unit, Token)).Succeeded);
        if (supervisor)
        {
            var row = (await w.Designations.LoadAsync(w.ProductionManager, Token)).Value!.Rows.Single(r => r.UserId == w.Supervisor.UserId);
            Assert.True((await w.Designations.SetSupervisorAsync(w.ProductionManager, row.DesignationId, row.Version, true, Token)).Succeeded);
        }

        Assert.True((await w.Work.PerformAsync(w.Production, StageAction.Start, await TargetAsync(w, item.RequestId, item.ItemId, 1), Token)).Succeeded);
        var operatorId = (await w.Services.Operators.RegisterAsync(w.Admin, "OP-1", "علی رضایی", w.Production.UserId, Token)).Value!.Value.Id;
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

    private static async Task<(long MachineRate, long PriceId)> RatesAsync(World w, Setup s, decimal rate = 1_200_000m, decimal price = 90_000m)
    {
        var added = await w.Services.PlanningMaster.AddRateAsync(w.Planning, s.Machine, rate, "1405/01/01", "نرخ اولیه", Token);
        Assert.True(added.Succeeded, added.Status + " " + added.Issue);
        var material = (await w.Services.Materials.LoadAsync(w.Warehouse, Token)).Value!.Materials.First().Value.Id;
        var priced = await w.Services.Materials.AddPriceAsync(w.Warehouse, material, price, "1405/01/01", "قیمت اولیه", Token);
        Assert.True(priced.Succeeded, priced.Status + " " + priced.Issue);
        return (added.Value!.Id, priced.Value!.Id);
    }

    /// <summary>The running stage is submitted and QC accepts all 10 base units: the (single-stage) route is complete.</summary>
    private static async Task CompleteRouteAsync(World w, Setup s)
    {
        Assert.True((await w.Work.PerformAsync(w.Production, StageAction.SubmitForQc, await TargetAsync(w, s.RequestId, s.ItemId, 1), Token)).Succeeded);
        var inspected = await w.Qci.RecordInspectionAsync(w.Qc, await TargetAsync(w, s.RequestId, s.ItemId, 1), [Accept(10)], Token);
        Assert.True(inspected.Succeeded, inspected.Status + " " + inspected.Issue);
        Tick(w);
    }

    /// <summary>A submitted and approved report of 08:00-12:00 (240 productive minutes after 0 downtime) on the planned machine.</summary>
    private static async Task ApprovedReportAsync(World w, Setup s)
    {
        var report = await SubmittedAsync(w, s, Line(s, 8, 12, 10));
        Assert.True((await w.Reports.ReviewAsync(w.Supervisor, report.Value.Id, report.Version, true, null, Token)).Succeeded);
    }

    private static async Task<Setup> ReadyToCostAsync(World w, bool contract = false)
    {
        WithContract = contract;
        var s = await SetupAsync(w);
        await ApprovedReportAsync(w, s);
        await RatesAsync(w, s);
        await CompleteRouteAsync(w, s);
        return s;
    }

    private static async Task<(Stored<ItemPriceDecision> Decision, Setup Setup)> ApprovedPriceAsync(World w, Setup s)
    {
        var calculated = (await w.Costing.CalculateAsync(w.Warehouse, s.ItemId, Token)).Value!;
        var submitted = (await w.Costing.SubmitAsync(w.Warehouse, calculated.Decision.Value.Id, calculated.Decision.Version, Token)).Value!;
        var approved = await w.Costing.ApproveAsync(w.Manager, submitted.Value.Id, submitted.Version, null, null, null, Token);
        Assert.True(approved.Succeeded, approved.Status + " " + approved.Issue);
        return (approved.Value!, s);
    }

    [Fact]
    public async Task Warehouse_receives_after_the_last_qc_marks_finished_after_the_price_and_may_record_dispatches_each_as_a_separate_record()
    {
        using var w = await StartAsync();
        var s = await ReadyToCostAsync(w);

        // Nothing is finished by one action: the receipt alone does not finish the item.
        var received = await w.Completion.ReceiveAsync(w.Warehouse, s.ItemId, "تحویل از کنترل کیفیت", Token);
        Assert.True(received.Succeeded, received.Status + " " + received.Issue);
        Assert.Equal((10L, w.Warehouse.UserId), (received.Value!.ReceivedBaseUnits, received.Value.ReceivedByUserId));
        Assert.Equal(ValidationIssue.ReceiptDuplicate, (await w.Completion.ReceiveAsync(w.Warehouse, s.ItemId, null, Token)).Issue);
        Assert.Equal(ValidationIssue.FinishedSentNeedsPrice, (await w.Completion.MarkFinishedSentAsync(w.Warehouse, s.ItemId, Token)).Issue);

        // A price that is only calculated or only submitted is not an approved price.
        var calculated = (await w.Costing.CalculateAsync(w.Warehouse, s.ItemId, Token)).Value!;
        Assert.Equal(ValidationIssue.FinishedSentNeedsPrice, (await w.Completion.MarkFinishedSentAsync(w.Warehouse, s.ItemId, Token)).Issue);
        var submitted = (await w.Costing.SubmitAsync(w.Warehouse, calculated.Decision.Value.Id, calculated.Decision.Version, Token)).Value!;
        Assert.Equal(ValidationIssue.FinishedSentNeedsPrice, (await w.Completion.MarkFinishedSentAsync(w.Warehouse, s.ItemId, Token)).Issue);
        var approved = (await w.Costing.ApproveAsync(w.Manager, submitted.Value.Id, submitted.Version, null, null, null, Token)).Value!;

        var finished = await w.Completion.MarkFinishedSentAsync(w.Warehouse, s.ItemId, Token);
        Assert.True(finished.Succeeded, finished.Status + " " + finished.Issue);
        Assert.Equal((5_700_000m, approved.Value.Id, received.Value.Id), (finished.Value!.ApprovedPriceRial, finished.Value.PriceDecisionId, finished.Value.ReceiptId));
        Assert.Equal(ValidationIssue.FinishedSentDuplicate, (await w.Completion.MarkFinishedSentAsync(w.Warehouse, s.ItemId, Token)).Issue);

        // Dispatch is optional tracking: a Jalali date not in the future and a document reference; several are allowed.
        var dispatch = await w.Completion.RecordDispatchAsync(w.Warehouse, s.ItemId, "1405/08/02", "WB-1001", "بخش اول", Token);
        Assert.True(dispatch.Succeeded, dispatch.Status + " " + dispatch.Issue);
        Assert.Equal((new DateOnly(2026, 10, 24), "WB-1001"), (dispatch.Value!.DispatchDate, dispatch.Value.DocumentReference));
        Assert.True((await w.Completion.RecordDispatchAsync(w.Warehouse, s.ItemId, "1405/08/02", "WB-1002", null, Token)).Succeeded);
        Assert.Equal(ValidationIssue.CompletionInputInvalid, (await w.Completion.RecordDispatchAsync(w.Warehouse, s.ItemId, "1405/08/03", "WB-1003", null, Token)).Issue); // tomorrow
        Assert.Equal(ValidationIssue.CompletionInputInvalid, (await w.Completion.RecordDispatchAsync(w.Warehouse, s.ItemId, "x", "WB-1003", null, Token)).Issue);
        Assert.Equal(ValidationIssue.CompletionInputInvalid, (await w.Completion.RecordDispatchAsync(w.Warehouse, s.ItemId, "1405/08/02", "  ", null, Token)).Issue);

        var item = Assert.Single((await w.Completion.LoadAsync(w.Manager, Token)).Value!.Items);
        Assert.Equal((10L, true, true, 2), (item.AcceptedBaseUnits, item.Receipt is not null, item.FinishedSent is not null, item.Dispatches.Count));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'WarehouseReceiptRecorded'"));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'ItemFinishedSent'"));
        Assert.Equal(2, Count(w, "AuditLog WHERE ActionCode = 'CustomerDispatchRecorded'"));
        Assert.Equal(1, Count(w, "WarehouseReceipts"));
    }

    [Fact]
    public async Task The_steps_are_refused_in_the_wrong_order_and_for_items_that_are_not_complete()
    {
        using var w = await StartAsync();
        WithContract = false;
        var s = await SetupAsync(w);

        // The route is not complete: no receipt, no mark, no dispatch.
        Assert.Equal(ValidationIssue.ReceiptNotReady, (await w.Completion.ReceiveAsync(w.Warehouse, s.ItemId, null, Token)).Issue);
        Assert.Equal(ValidationIssue.ReceiptRequired, (await w.Completion.MarkFinishedSentAsync(w.Warehouse, s.ItemId, Token)).Issue);
        Assert.Equal(ValidationIssue.ReceiptRequired, (await w.Completion.RecordDispatchAsync(w.Warehouse, s.ItemId, "1405/08/02", "WB-1", null, Token)).Issue);
        Assert.Equal(0, Count(w, "WarehouseReceipts"));
        Assert.Equal(OperationStatus.NotFound, (await w.Completion.ReceiveAsync(w.Warehouse, 9999, null, Token)).Status);
        Assert.Empty((await w.Completion.LoadAsync(w.Warehouse, Token)).Value!.Items);

        // A received item with an approved price cannot be finished again after a recalculation until the new price is approved.
        await ApprovedReportAsync(w, s);
        await RatesAsync(w, s);
        await CompleteRouteAsync(w, s);
        await ApprovedPriceAsync(w, s);
        Assert.True((await w.Completion.ReceiveAsync(w.Warehouse, s.ItemId, null, Token)).Succeeded);
        Assert.True((await w.Services.PlanningMaster.AddRateAsync(w.Planning, s.Machine, 1_500_000m, "1405/08/01", "افزایش", Token)).Succeeded);
        Assert.True((await w.Costing.CalculateAsync(w.Warehouse, s.ItemId, Token)).Succeeded);
        Assert.Equal(ValidationIssue.FinishedSentNeedsPrice, (await w.Completion.MarkFinishedSentAsync(w.Warehouse, s.ItemId, Token)).Issue);
        Assert.Equal(0, Count(w, "ItemFinishedSent"));
    }

    // ---- Warehouse reversal (owner decision B9, Decision Log #142): the Warehouse proposes with a reason, Management approves or rejects, the original is never deleted ----

    private static async Task<(Setup S, long ReceiptId, long MarkId, long DispatchId)> FullyDispatchedAsync(World w)
    {
        var s = await ReadyToCostAsync(w);
        var receipt = (await w.Completion.ReceiveAsync(w.Warehouse, s.ItemId, "تحویل", Token)).Value!;
        await ApprovedPriceAsync(w, s);
        var mark = (await w.Completion.MarkFinishedSentAsync(w.Warehouse, s.ItemId, Token)).Value!;
        var dispatch = (await w.Completion.RecordDispatchAsync(w.Warehouse, s.ItemId, "1405/08/02", "WB-9001", null, Token)).Value!;
        return (s, receipt.Id, mark.Id, dispatch.Id);
    }

    [Fact]
    public async Task A_reversal_changes_nothing_until_Management_approves_then_marks_the_original_reversed_keeps_every_row_and_links_proposer_approver_and_reasons()
    {
        using var w = await StartAsync();
        var (s, receiptId, markId, _) = await FullyDispatchedAsync(w);

        // The receipt cannot be reversed while a current finished/sent mark depends on it.
        Assert.Equal(ValidationIssue.ReversalBlockedByFinishedMark,
            (await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId, InvalidatedRecordKind.WarehouseReceipt, receiptId, ReversalNature.RecordedInError, "دریافت اشتباه", Token)).Issue);

        var proposed = await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId, InvalidatedRecordKind.FinishedSent, markId, ReversalNature.RecordedInError, "  مهر اشتباه زده شد  ", Token);
        Assert.True(proposed.Succeeded, proposed.Status + " " + proposed.Issue);
        Assert.Equal((ReversalStatus.Proposed, "مهر اشتباه زده شد", w.Warehouse.UserId), (proposed.Value!.Status, proposed.Value.ProposalReason, proposed.Value.ProposedByUserId));

        // Proposed only: the mark is still current and nothing is marked.
        Assert.Equal(0, Count(w, "RecordInvalidations"));
        var before = Assert.Single((await w.Completion.LoadAsync(w.Manager, Token)).Value!.Items);
        Assert.False(before.NotCurrent);
        Assert.Single(before.Reversals);
        Assert.Equal(ValidationIssue.FinishedSentDuplicate, (await w.Completion.MarkFinishedSentAsync(w.Warehouse, s.ItemId, Token)).Issue);
        Assert.Equal(ValidationIssue.ReversalAlreadyProposed,
            (await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId, InvalidatedRecordKind.FinishedSent, markId, ReversalNature.RecordedInError, "دوباره", Token)).Issue);

        var approved = await w.Completion.DecideReversalAsync(w.Manager, proposed.Value.Id, approve: true, "تأیید شد", Token);
        Assert.True(approved.Succeeded, approved.Status + " " + approved.Issue);
        Assert.Equal((ReversalStatus.Approved, w.Manager.UserId, "تأیید شد"), (approved.Value!.Status, approved.Value.DecidedByUserId, approved.Value.DecisionReason));
        Assert.NotNull(approved.Value.DecidedAtUtc);

        // Everything is kept; the original is only marked, linked to the reversal.
        Assert.Equal((1L, 1L, 1L, 1L), (Count(w, "WarehouseReceipts"), Count(w, "ItemFinishedSent"), Count(w, "CustomerDispatches"), Count(w, "WarehouseReversals")));
        Assert.Equal(1, Count(w, $"RecordInvalidations WHERE Kind = 4 AND TargetId = {markId} AND Effect = 4 AND WarehouseReversalId = {proposed.Value.Id} AND QcCorrectionId IS NULL AND JobReportRevisionId IS NULL"));
        Assert.Equal((1L, 1L, 0L), (Count(w, "AuditLog WHERE ActionCode = 'WarehouseReversalProposed'"), Count(w, "AuditLog WHERE ActionCode = 'WarehouseReversalApproved'"), Count(w, "AuditLog WHERE ActionCode = 'WarehouseReversalRejected'")));
        var after = Assert.Single((await w.Completion.LoadAsync(w.Manager, Token)).Value!.Items);
        Assert.Equal(InvalidationEffect.Reversed, after.FinishedSentInvalidation!.Effect);
        Assert.True(after.NotCurrent);

        // A decided proposal cannot be decided again; the reversed mark is no longer current, so it cannot be reversed again either.
        Assert.Equal(OperationStatus.InvalidState, (await w.Completion.DecideReversalAsync(w.Manager, proposed.Value.Id, approve: false, "x", Token)).Status);
        Assert.Equal(ValidationIssue.ReversalTargetNotCurrent,
            (await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId, InvalidatedRecordKind.FinishedSent, markId, ReversalNature.RecordedInError, "x", Token)).Issue);

        // The mark can now be recorded again (sequence 2), as a real new record; the earlier one stays as history.
        var again = await w.Completion.MarkFinishedSentAsync(w.Warehouse, s.ItemId, Token);
        Assert.True(again.Succeeded, again.Status + " " + again.Issue);
        Assert.Equal(2, again.Value!.Sequence);
        Assert.Equal(2L, Count(w, "ItemFinishedSent"));

        // With the mark reversed again, the receipt itself can be reversed and re-recorded.
        var second = (await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId, InvalidatedRecordKind.FinishedSent, again.Value.Id, ReversalNature.RecordedInError, "دوباره اشتباه", Token)).Value!;
        Assert.True((await w.Completion.DecideReversalAsync(w.Manager, second.Id, true, null, Token)).Succeeded);
        var receiptProposal = (await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId, InvalidatedRecordKind.WarehouseReceipt, receiptId, ReversalNature.PhysicalMovementCorrection, "کالا برگشت داده شد", Token)).Value!;
        Assert.Equal(ReversalNature.PhysicalMovementCorrection, receiptProposal.Nature);
        Assert.True((await w.Completion.DecideReversalAsync(w.Manager, receiptProposal.Id, true, null, Token)).Succeeded);
        var newReceipt = await w.Completion.ReceiveAsync(w.Warehouse, s.ItemId, "دریافت مجدد", Token);
        Assert.True(newReceipt.Succeeded, newReceipt.Status + " " + newReceipt.Issue);
        Assert.Equal(2, newReceipt.Value!.Sequence);
        Assert.Equal(2L, Count(w, "WarehouseReceipts"));
    }

    [Fact]
    public async Task A_reversed_customer_dispatch_stays_listed_as_reversed_and_a_rejected_proposal_leaves_the_record_current()
    {
        using var w = await StartAsync();
        var (s, _, _, dispatchId) = await FullyDispatchedAsync(w);

        var proposal = (await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId, InvalidatedRecordKind.CustomerDispatch, dispatchId, ReversalNature.PhysicalMovementCorrection, "کالا از مشتری برگشت خورد", Token)).Value!;
        // Rejection needs a reason; without it nothing is decided.
        Assert.Equal(ValidationIssue.ReversalInvalid, (await w.Completion.DecideReversalAsync(w.Manager, proposal.Id, approve: false, " ", Token)).Issue);
        var rejected = await w.Completion.DecideReversalAsync(w.Manager, proposal.Id, approve: false, "سند ارسال درست بود", Token);
        Assert.True(rejected.Succeeded, rejected.Status + " " + rejected.Issue);
        Assert.Equal((ReversalStatus.Rejected, "سند ارسال درست بود"), (rejected.Value!.Status, rejected.Value.DecisionReason));
        Assert.Equal(0, Count(w, "RecordInvalidations"));

        // The record is current again, so a new proposal is possible and can be approved.
        var again = (await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId, InvalidatedRecordKind.CustomerDispatch, dispatchId, ReversalNature.PhysicalMovementCorrection, "کالا از مشتری برگشت خورد", Token)).Value!;
        Assert.True((await w.Completion.DecideReversalAsync(w.Manager, again.Id, approve: true, null, Token)).Succeeded);

        var item = Assert.Single((await w.Completion.LoadAsync(w.Manager, Token)).Value!.Items);
        Assert.Equal(1, item.Dispatches.Count);                      // the dispatch row is kept
        Assert.Contains(dispatchId, item.ReversedDispatchIds);        // and shown as reversed
        Assert.DoesNotContain(dispatchId, item.FlaggedDispatchIds);   // which is different from a QC-correction flag
        Assert.False(item.NotCurrent);                                // a reversed dispatch does not make the item not current
        Assert.Equal(2, item.Reversals.Count(r => r.TargetId == dispatchId));
        Assert.Equal(1L, Count(w, "CustomerDispatches"));
    }

    [Fact]
    public async Task Only_the_Warehouse_proposes_only_Management_decides_and_the_reason_target_and_kind_are_validated()
    {
        using var w = await StartAsync();
        var (s, _, _, dispatchId) = await FullyDispatchedAsync(w);

        foreach (var other in new[] { w.Manager, w.ManagementStaff, w.Planning, w.ProductionManager, w.Drawing, w.Qc, w.Production, w.Central, w.Admin })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Completion.ProposeReversalAsync(other, s.ItemId, InvalidatedRecordKind.CustomerDispatch, dispatchId, ReversalNature.RecordedInError, "x", Token)).Status);
        }

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Completion.ProposeReversalAsync(null, s.ItemId, InvalidatedRecordKind.CustomerDispatch, dispatchId, ReversalNature.RecordedInError, "x", Token)).Status);
        Assert.Equal(ValidationIssue.ReversalInvalid, (await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId, InvalidatedRecordKind.CustomerDispatch, dispatchId, ReversalNature.RecordedInError, "  ", Token)).Issue);
        Assert.Equal(ValidationIssue.ReversalInvalid, (await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId, InvalidatedRecordKind.CustomerDispatch, dispatchId, ReversalNature.RecordedInError, new string('x', 301), Token)).Issue);
        Assert.Equal(ValidationIssue.ReversalInvalid, (await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId, InvalidatedRecordKind.CostSnapshot, 1, ReversalNature.RecordedInError, "x", Token)).Issue);
        Assert.Equal(ValidationIssue.ReversalInvalid, (await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId, InvalidatedRecordKind.CustomerDispatch, dispatchId, (ReversalNature)9, "x", Token)).Issue);
        Assert.Equal(ValidationIssue.ReversalTargetMissing, (await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId, InvalidatedRecordKind.CustomerDispatch, dispatchId + 100, ReversalNature.RecordedInError, "x", Token)).Issue);
        Assert.Equal(ValidationIssue.ReversalTargetMissing, (await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId + 100, InvalidatedRecordKind.CustomerDispatch, dispatchId, ReversalNature.RecordedInError, "x", Token)).Issue);
        Assert.Equal(0, Count(w, "WarehouseReversals"));

        var proposal = (await w.Completion.ProposeReversalAsync(w.Warehouse, s.ItemId, InvalidatedRecordKind.CustomerDispatch, dispatchId, ReversalNature.RecordedInError, "اشتباه", Token)).Value!;
        // The Warehouse proposer cannot approve its own proposal; nobody but Management decides.
        foreach (var other in new[] { w.Warehouse, w.Planning, w.ProductionManager, w.Qc, w.Production, w.Central, w.Admin, w.Drawing })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Completion.DecideReversalAsync(other, proposal.Id, true, null, Token)).Status);
        }

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Completion.DecideReversalAsync(null, proposal.Id, true, null, Token)).Status);
        Assert.Equal(ReversalStatus.Proposed, (await w.Completion.LoadAsync(w.Manager, Token)).Value!.Items.Single().Reversals.Single().Status);
        Assert.Equal(0, Count(w, "RecordInvalidations"));

        // Capabilities: the Warehouse proposes, Management decides, Planning only reads.
        var warehouse = (await w.Completion.LoadAsync(w.Warehouse, Token)).Value!.Capabilities;
        var management = (await w.Completion.LoadAsync(w.Manager, Token)).Value!.Capabilities;
        var planning = (await w.Completion.LoadAsync(w.Planning, Token)).Value!.Capabilities;
        Assert.Equal((true, false), (warehouse.CanProposeReversal, warehouse.CanDecideReversal));
        Assert.Equal((false, true), (management.CanProposeReversal, management.CanDecideReversal));
        Assert.Equal((false, false), (planning.CanProposeReversal, planning.CanDecideReversal));
    }

    [Fact]
    public async Task Only_warehouse_records_and_warehouse_planning_and_management_read()
    {
        using var w = await StartAsync();
        var s = await ReadyToCostAsync(w);
        await ApprovedPriceAsync(w, s);

        foreach (var user in new Principal?[] { w.Manager, w.Planning, w.Qc, w.Production, w.Drawing, w.Central, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Completion.ReceiveAsync(user, s.ItemId, null, Token)).Status);
        }

        Assert.True((await w.Completion.ReceiveAsync(w.Warehouse, s.ItemId, null, Token)).Succeeded);
        foreach (var user in new Principal?[] { w.Manager, w.Planning, w.Qc, w.Production, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Completion.MarkFinishedSentAsync(user, s.ItemId, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Completion.RecordDispatchAsync(user, s.ItemId, "1405/08/02", "WB-1", null, Token)).Status);
        }

        foreach (var user in new[] { w.Warehouse, w.Planning, w.Manager })
        {
            Assert.True((await w.Completion.LoadAsync(user, Token)).Succeeded);
        }

        foreach (var user in new Principal?[] { w.Qc, w.Production, w.Drawing, w.Central, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Completion.LoadAsync(user, Token)).Status);
        }

        var capabilities = (await w.Completion.LoadAsync(w.Planning, Token)).Value!.Capabilities;
        Assert.False(capabilities.CanReceive || capabilities.CanMarkFinished || capabilities.CanDispatch);
        Assert.Equal(1, Count(w, "WarehouseReceipts"));
        Assert.Equal(0, Count(w, "ItemFinishedSent"));
    }
}
