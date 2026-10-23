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

// Item cost snapshots and Management price approval (docs/COSTING_DESIGN.md; Decision Log #91, #95 D3/D4, #104) on a real SQLite file with the real authorization.
public class CostingTests
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

    [Fact]
    public async Task Warehouse_calculates_an_immutable_snapshot_submits_it_and_management_approves_the_price()
    {
        using var w = await StartAsync();
        var s = await ReadyToCostAsync(w);

        var calculated = await w.Costing.CalculateAsync(w.Warehouse, s.ItemId, Token);
        Assert.True(calculated.Succeeded, calculated.Status + " " + calculated.Issue);
        var calc = calculated.Value!.Calculation.Value;
        Assert.Equal(1, calc.SequenceNo);
        Assert.Equal((10L, 10L), (calc.AcceptedBaseUnits, calc.ConsumedBaseUnits));
        Assert.Equal((4_800_000m, 900_000m, 5_700_000m, 570_000m), (calc.MachiningTotalRial, calc.MaterialTotalRial, calc.TotalRial, calc.UnitCostRial));
        Assert.Equal(new[] { CostLineKind.Machining, CostLineKind.Material }, calc.Lines.Select(l => l.Kind).ToArray());
        var machining = calc.Lines[0];
        Assert.Equal((240, 1_200_000m, new DateOnly(2026, 10, 24)), (machining.Minutes, machining.UnitPriceRial, machining.WorkDate));
        Assert.Equal(10m, calc.Lines[1].WeightKg);
        var decision = calculated.Value.Decision;
        Assert.Equal((PriceDecisionStatus.Draft, PriceBasis.Calculated, 5_700_000m), (decision.Value.Status, decision.Value.Basis, decision.Value.ProposedPriceRial));

        // Management cannot approve a draft; Warehouse submits; the proposal is then approved as it is.
        Assert.Equal(OperationStatus.InvalidState, (await w.Costing.ApproveAsync(w.Manager, decision.Value.Id, decision.Version, null, null, null, Token)).Status);
        var submitted = await w.Costing.SubmitAsync(w.Warehouse, decision.Value.Id, decision.Version, Token);
        Assert.Equal(PriceDecisionStatus.Submitted, submitted.Value!.Value.Status);
        var approved = await w.Costing.ApproveAsync(w.Manager, submitted.Value.Value.Id, submitted.Value.Version, null, null, null, Token);
        Assert.True(approved.Succeeded, approved.Status + " " + approved.Issue);
        Assert.Equal((PriceDecisionStatus.Approved, 5_700_000m, w.Manager.UserId), (approved.Value!.Value.Status, approved.Value.Value.ApprovedPriceRial, approved.Value.Value.ApprovedByUserId));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'ItemCostCalculated'"));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'ItemPriceSubmitted'"));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'ItemPriceApproved'"));

        // An approved decision is final.
        Assert.Equal(OperationStatus.InvalidState, (await w.Costing.ApproveAsync(w.Manager, approved.Value.Value.Id, approved.Value.Version, 6_000_000m, "x", null, Token)).Status);

        // Recalculation adds snapshot 2 (a new rate applies) and supersedes the old decision; history keeps both.
        Assert.True((await w.Services.PlanningMaster.AddRateAsync(w.Planning, s.Machine, 1_500_000m, "1405/08/01", "افزایش", Token)).Succeeded);
        var second = (await w.Costing.CalculateAsync(w.Warehouse, s.ItemId, Token)).Value!;
        Assert.Equal((2, 6_900_000m), (second.Calculation.Value.SequenceNo, second.Calculation.Value.TotalRial));
        var history = (await w.Costing.HistoryAsync(w.Planning, s.ItemId, Token)).Value!;
        Assert.Equal(new[] { 5_700_000m, 6_900_000m }, history.Calculations.Select(c => c.Value.TotalRial).ToArray());
        Assert.Equal(new[] { PriceDecisionStatus.Approved, PriceDecisionStatus.Draft }, history.Decisions.Select(d => d.Value.Status).ToArray());
        var list = (await w.Costing.LoadAsync(w.Manager, Token)).Value!;
        Assert.Equal(PriceDecisionStatus.Draft, Assert.Single(list.Items).LatestDecision!.Value.Status);
    }

    [Fact]
    public async Task A_snapshot_is_refused_with_named_gaps_and_nothing_is_saved()
    {
        using var w = await StartAsync();
        WithContract = false;
        var s = await SetupAsync(w);

        // The route is not complete yet.
        Assert.Equal(ValidationIssue.CostingNotReady, (await w.Costing.CalculateAsync(w.Warehouse, s.ItemId, Token)).Issue);
        await CompleteRouteAsync(w, s);

        // No approved report time: only a submitted report exists, and cost is never defaulted to the plan.
        var report = await SubmittedAsync(w, s, Line(s, 8, 12, 10));
        Assert.Equal(ValidationIssue.CostingNoApprovedTime, (await w.Costing.CalculateAsync(w.Warehouse, s.ItemId, Token)).Issue);
        Assert.True((await w.Reports.ReviewAsync(w.Supervisor, report.Value.Id, report.Version, true, null, Token)).Succeeded);

        // No machine rate and no price yet: every gap is named at once.
        var noRate = await w.Costing.CalculateAsync(w.Warehouse, s.ItemId, Token);
        Assert.Equal(ValidationIssue.CostingRateMissing, noRate.Issue);
        Assert.Equal(new[] { "RATE|FCC-06|2026-10-24", "PRICE|ST37-R30|2026-10-24" }, noRate.WarningList.ToArray());
        Assert.True((await w.Services.PlanningMaster.AddRateAsync(w.Planning, s.Machine, 1_200_000m, "1405/01/01", null, Token)).Succeeded);

        // Only the material price is still missing.
        var noPrice = await w.Costing.CalculateAsync(w.Warehouse, s.ItemId, Token);
        Assert.Equal(ValidationIssue.CostingPriceMissing, noPrice.Issue);
        Assert.Equal("PRICE|ST37-R30|2026-10-24", Assert.Single(noPrice.WarningList));

        Assert.Equal(0, Count(w, "ItemCostCalculations"));
        Assert.Equal(0, Count(w, "ItemPriceDecisions"));
        Assert.Equal(0, Count(w, "AuditLog WHERE ActionCode = 'ItemCostCalculated'"));
        Assert.Equal(OperationStatus.NotFound, (await w.Costing.CalculateAsync(w.Warehouse, 9999, Token)).Status);
    }

    [Fact]
    public async Task Only_the_right_departments_calculate_submit_approve_and_read()
    {
        using var w = await StartAsync();
        var s = await ReadyToCostAsync(w);

        foreach (var user in new Principal?[] { w.Manager, w.Planning, w.Qc, w.Production, w.Drawing, w.Central, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Costing.CalculateAsync(user, s.ItemId, Token)).Status);
        }

        var decision = (await w.Costing.CalculateAsync(w.Warehouse, s.ItemId, Token)).Value!.Decision;
        foreach (var user in new Principal?[] { w.Manager, w.Planning, w.Qc, w.Production, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Costing.SubmitAsync(user, decision.Value.Id, decision.Version, Token)).Status);
        }

        var submitted = (await w.Costing.SubmitAsync(w.Warehouse, decision.Value.Id, decision.Version, Token)).Value!;
        foreach (var user in new Principal?[] { w.Warehouse, w.Planning, w.Qc, w.Production, w.Drawing, w.Central, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Costing.ApproveAsync(user, submitted.Value.Id, submitted.Version, null, null, null, Token)).Status);
        }

        foreach (var user in new[] { w.Warehouse, w.Planning, w.Manager })
        {
            Assert.True((await w.Costing.LoadAsync(user, Token)).Succeeded);
            Assert.True((await w.Costing.HistoryAsync(user, s.ItemId, Token)).Succeeded);
        }

        foreach (var user in new Principal?[] { w.Qc, w.Production, w.Drawing, w.Central, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Costing.LoadAsync(user, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Costing.HistoryAsync(user, s.ItemId, Token)).Status);
        }

        var capabilities = (await w.Costing.LoadAsync(w.Manager, Token)).Value!.Capabilities;
        Assert.True(capabilities.CanApprove && !capabilities.CanCalculate);
        Assert.Equal(PriceDecisionStatus.Submitted, (await w.Costing.LoadAsync(w.Planning, Token)).Value!.Items.Single().LatestDecision!.Value.Status);
    }

    [Fact]
    public async Task Management_may_change_the_price_with_a_reason_the_proposal_is_kept_and_stale_or_invalid_input_changes_nothing()
    {
        using var w = await StartAsync();
        var s = await ReadyToCostAsync(w);
        var decision = (await w.Costing.CalculateAsync(w.Warehouse, s.ItemId, Token)).Value!.Decision;
        var submitted = (await w.Costing.SubmitAsync(w.Warehouse, decision.Value.Id, decision.Version, Token)).Value!;

        // A stale version, then invalid prices and a missing reason: nothing changes.
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Costing.ApproveAsync(w.Manager, submitted.Value.Id, decision.Version, null, null, null, Token)).Status);
        foreach (var price in new decimal?[] { 0m, -5m, 5_800_000.5m, 10_000_000_000_000_001m })
        {
            Assert.Equal(ValidationIssue.ItemPriceInvalid, (await w.Costing.ApproveAsync(w.Manager, submitted.Value.Id, submitted.Version, price, "دلیل", null, Token)).Issue);
        }

        Assert.Equal(ValidationIssue.ItemPriceInvalid, (await w.Costing.ApproveAsync(w.Manager, submitted.Value.Id, submitted.Version, 6_000_000m, "  ", null, Token)).Issue);
        Assert.Equal(PriceDecisionStatus.Submitted, (await w.Costing.HistoryAsync(w.Manager, s.ItemId, Token)).Value!.Decisions.Single().Value.Status);

        var approved = (await w.Costing.ApproveAsync(w.Manager, submitted.Value.Id, submitted.Version, 6_000_000m, "حاشیه سود", null, Token)).Value!;
        Assert.Equal((5_700_000m, 6_000_000m, "حاشیه سود"), (approved.Value.ProposedPriceRial, approved.Value.ApprovedPriceRial, approved.Value.Reason));
        Assert.Equal(OperationStatus.NotFound, (await w.Costing.SubmitAsync(w.Warehouse, 9999, 1, Token)).Status);
    }

    [Fact]
    public async Task An_item_of_an_approved_contract_takes_the_contract_amount_as_the_price_and_the_calculated_cost_is_information()
    {
        using var w = await StartAsync();
        var s = await ReadyToCostAsync(w, contract: true);
        var calculated = (await w.Costing.CalculateAsync(w.Warehouse, s.ItemId, Token)).Value!;
        Assert.Equal(5_700_000m, calculated.Calculation.Value.TotalRial);
        Assert.Equal((PriceBasis.Contract, (decimal?)null), (calculated.Decision.Value.Basis, calculated.Decision.Value.ProposedPriceRial));
        var submitted = (await w.Costing.SubmitAsync(w.Warehouse, calculated.Decision.Value.Id, calculated.Decision.Version, Token)).Value!;

        // The calculation never silently fills the contract price: the amount and the contract reference are mandatory.
        Assert.Equal(ValidationIssue.ItemPriceInvalid, (await w.Costing.ApproveAsync(w.Manager, submitted.Value.Id, submitted.Version, null, null, "CN-100", Token)).Issue);
        Assert.Equal(ValidationIssue.ItemPriceInvalid, (await w.Costing.ApproveAsync(w.Manager, submitted.Value.Id, submitted.Version, 8_000_000m, null, null, Token)).Issue);
        var approved = (await w.Costing.ApproveAsync(w.Manager, submitted.Value.Id, submitted.Version, 8_000_000m, null, "CN-100", Token)).Value!;
        Assert.Equal((8_000_000m, "CN-100"), (approved.Value.ApprovedPriceRial, approved.Value.ContractReference));
        Assert.True((await w.Costing.LoadAsync(w.Manager, Token)).Value!.Items.Single().ContractApproved);
    }
}
