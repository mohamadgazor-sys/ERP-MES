using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;
using ERP.MES.Application.Production;
using ERP.MES.Application.Reporting;
using ERP.MES.Application.Scheduling;
using ERP.MES.Domain.Reporting;
using ERP.MES.Domain.Scheduling;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// Daily job reports, supervisor review and stage corrections (Decision Log #82 R5, #95 D8, #102) on a real SQLite file with the real authorization.
public class JobReportTests
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
            Assert.True((await w.Contracts.DecideRequirementAsync(w.Planning, id, ContractRequirement.NotRequired, null, Token)).Succeeded);
            Tick(w);
        }

        return (id, await VersionAsync(w, id));
    }

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

    [Fact]
    public async Task An_operator_reports_their_own_day_the_supervisor_approves_and_the_history_and_audit_are_kept()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);

        var draft = await DraftAsync(w, s, Line(s, 8, 12, 10), Line(s, 13, 17, 12, 30, "خرابی ابزار"));
        Assert.Equal(JobReportStatus.Draft, draft.Value.Status);
        Assert.Equal(2, draft.Value.Lines.Count);
        Assert.Equal(new[] { 240, 240 }, draft.Value.Lines.Select(l => l.WorkedMinutes).ToArray());
        Assert.Equal(210, draft.Value.Lines[1].ProductiveMinutes);

        var submitted = (await w.Reports.SubmitAsync(w.Production, draft.Value.Id, draft.Version, Token)).Value!;
        Assert.Equal(JobReportStatus.Submitted, submitted.Value.Status);
        Assert.All(submitted.Value.Lines, l => Assert.Equal(JobLineFlags.None, l.Flags)); // inside the shift, planned, planned machine

        var approved = await w.Reports.ReviewAsync(w.Supervisor, submitted.Value.Id, submitted.Version, true, null, Token);
        Assert.True(approved.Succeeded, approved.Status + " " + approved.Issue);
        Assert.Equal(JobReportStatus.Approved, approved.Value!.Value.Status);
        Assert.False(approved.Value.Value.ReviewedByPlanningFallback);
        Assert.Equal(w.Supervisor.UserId, approved.Value.Value.ReviewedByUserId);
        Assert.Equal(new[] { JobReportEventKind.Created, JobReportEventKind.Submitted, JobReportEventKind.Approved }, approved.Value.Value.Events.Select(e => e.Kind).ToArray());
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'JobReportSubmitted'"));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'JobReportApproved'"));

        // An approved report is final: it cannot be edited, resubmitted or reviewed again.
        Assert.Equal(OperationStatus.InvalidState, (await w.Reports.SaveDraftAsync(w.Production, approved.Value.Value.Id, approved.Value.Version, DateOnly.FromDateTime(Day), s.Unit, s.Template, [Line(s, 8, 9)], Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Reports.SubmitAsync(w.Production, approved.Value.Value.Id, approved.Value.Version, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Reports.ReviewAsync(w.Supervisor, approved.Value.Value.Id, approved.Value.Version, true, null, Token)).Status);
    }

    [Fact]
    public async Task Only_a_linked_operator_reports_and_only_for_their_own_name_and_other_departments_are_denied()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);

        foreach (var user in new Principal?[] { w.Planning, w.Manager, w.Drawing, w.Central, w.Qc, w.Warehouse, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Reports.SaveDraftAsync(user, null, null, DateOnly.FromDateTime(Day), s.Unit, s.Template, [Line(s, 8, 9)], Token)).Status);
        }

        // A Production user without an operator record cannot report.
        Assert.Equal(ValidationIssue.JobReportOperatorNotLinked, (await w.Reports.SaveDraftAsync(w.OtherOperator, null, null, DateOnly.FromDateTime(Day), s.Unit, s.Template, [Line(s, 8, 9)], Token)).Issue);

        // Another operator cannot touch or submit the first operator's report.
        var draft = await DraftAsync(w, s, Line(s, 8, 12));
        await w.Services.Operators.RegisterAsync(w.Admin, "OP-2", "رضا احمدی", w.OtherOperator.UserId, Token);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Reports.SaveDraftAsync(w.OtherOperator, draft.Value.Id, draft.Version, DateOnly.FromDateTime(Day), s.Unit, s.Template, [Line(s, 9, 10)], Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Reports.SubmitAsync(w.OtherOperator, draft.Value.Id, draft.Version, Token)).Status);
        Assert.Equal(JobReportStatus.Draft, (await w.Reports.LoadAsync(w.Planning, DateOnly.FromDateTime(Day), DateOnly.FromDateTime(Day), Token)).Value!.Items.Single().Report.Value.Status);
    }

    [Fact]
    public async Task Lines_outside_the_shift_unscheduled_on_another_machine_or_overlapping_another_operator_are_flagged_not_rejected()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w, plan: false);
        var other = (await w.Services.PlanningMaster.AddMachineAsync(w.Admin, "FCC-07", "فرز ۲", s.Unit, Token)).Value!.Value.Id;

        // A second operator already has an approved line on FCC-06 at 09:00-10:00.
        var person2 = (await w.Services.Operators.RegisterAsync(w.Admin, "OP-2", "رضا احمدی", w.OtherOperator.UserId, Token)).Value!.Value.Id;
        Assert.True((await w.Schedule.ScheduleAsync(w.Planning, person2, DateOnly.FromDateTime(Day), s.Unit, s.Template, null, null, null, Token)).Succeeded);
        var theirs = (await w.Reports.SaveDraftAsync(w.OtherOperator, null, null, DateOnly.FromDateTime(Day), s.Unit, s.Template, [Line(s, 9, 10)], Token)).Value!;
        var theirsSubmitted = (await w.Reports.SubmitAsync(w.OtherOperator, theirs.Value.Id, theirs.Version, Token)).Value!;
        Assert.True((await w.Reports.ReviewAsync(w.Supervisor, theirsSubmitted.Value.Id, theirsSubmitted.Version, true, null, Token)).Succeeded);

        var report = await SubmittedAsync(w, s, Line(s, 7, 8), Line(s, 9, 11), Line(s, 14, 15, machine: other));
        var flags = report.Value.Lines.ToDictionary(l => l.LineNo, l => l.Flags);

        Assert.True(flags[1].HasFlag(JobLineFlags.OutsideShift));
        Assert.True(flags[1].HasFlag(JobLineFlags.NotScheduled)); // the operator was not planned this day
        Assert.True(flags[2].HasFlag(JobLineFlags.MachineOverlap));
        Assert.False(flags[2].HasFlag(JobLineFlags.OutsideShift));
        Assert.False(flags[3].HasFlag(JobLineFlags.MachineOverlap));
        Assert.Equal(JobReportStatus.Submitted, report.Value.Status); // flagged, not rejected
        Assert.True(report.Value.HasFlaggedLines);
    }

    [Fact]
    public async Task An_operator_with_an_off_day_is_flagged_on_submission()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        Assert.True((await w.Schedule.AddOffDayAsync(w.Planning, s.OperatorId, DateOnly.FromDateTime(Day), DateOnly.FromDateTime(Day), OffDayKind.Sick, "گواهی", Token)).Succeeded);

        var report = await SubmittedAsync(w, s, Line(s, 8, 12));

        Assert.True(report.Value.Lines.Single().Flags.HasFlag(JobLineFlags.OperatorOffDay));
        Assert.Equal(JobReportStatus.Submitted, report.Value.Status);
    }

    [Fact]
    public async Task A_stage_machine_that_differs_from_the_plan_is_flagged_for_planning_and_the_reviewer()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        var other = (await w.Services.PlanningMaster.AddMachineAsync(w.Admin, "FCC-07", "فرز ۲", s.Unit, Token)).Value!.Value.Id;
        var planned = (await w.Services.PlanningMaster.AddMachineAsync(w.Admin, "FCC-08", "فرز ۳", s.Unit, Token)).Value!.Value.Id;
        var item = await ItemAsync(w, "R-2", "B", [s.Unit]);

        // Stage 1 of item B is planned on no machine; a stage planned on FCC-08 would flag FCC-06 or FCC-07 — define such a stage on a new item.
        var (id, version) = await ReadyAsync(w, WorkType.Make, "R-3");
        var (piece, _) = await TypesAsync(w);
        var material = long.Parse(w.Fixture.Db.Query("SELECT Id FROM Materials LIMIT 1").Single(), System.Globalization.CultureInfo.InvariantCulture);
        var defined = await w.Items.DefineItemAsync(w.Planning, id, version, new DefineItemCommand("C", "Part C", 10m, Priority.Normal, piece, material, 1m, null, null), Token);
        var itemC = defined.Value!.Value.Items.Single().Id;
        var current = (await w.Services.Requests.GetAsync(w.Planning, id, Token)).Value!;
        Assert.True((await w.Items.DefineStageAsync(w.Planning, id, current.Request.Version, itemC, current.Versions.ItemVersion(itemC), new DefineStageCommand("فرز", 2m, s.Unit, planned), Token)).Succeeded);
        Assert.True((await w.Work.PerformAsync(w.Planning, StageAction.Release, await TargetAsync(w, id, itemC, 1), Token)).Succeeded);
        Assert.True((await w.Work.PerformAsync(w.Production, StageAction.Start, await TargetAsync(w, id, itemC, 1), Token)).Succeeded);
        _ = (item, other);

        var report = await SubmittedAsync(w, s, new JobLineInput(id, itemC, 1, s.Machine, At(8), At(10), 0, null, 5, null));

        Assert.True(report.Value.Lines.Single().Flags.HasFlag(JobLineFlags.MachineDiffersFromPlan));
    }

    [Fact]
    public async Task Review_rules_supervisor_first_planning_only_without_one_nobody_reviews_their_own_report_and_a_return_needs_a_reason()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        var report = await SubmittedAsync(w, s, Line(s, 8, 12));

        // Planning may not review while the unit has a supervisor; others and the operator themselves cannot review.
        Assert.Equal(ValidationIssue.JobReportReviewerNotAllowed, (await w.Reports.ReviewAsync(w.Planning, report.Value.Id, report.Version, true, null, Token)).Issue);
        Assert.Equal(ValidationIssue.JobReportReviewerNotAllowed, (await w.Reports.ReviewAsync(w.Production, report.Value.Id, report.Version, true, null, Token)).Issue);
        foreach (var user in new Principal?[] { w.Manager, w.Drawing, w.Central, w.Qc, w.Warehouse, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Reports.ReviewAsync(user, report.Value.Id, report.Version, true, null, Token)).Status);
        }

        // A return needs a reason; the operator corrects the SAME report and resubmits it.
        Assert.Equal(ValidationIssue.JobReportReasonRequired, (await w.Reports.ReviewAsync(w.Supervisor, report.Value.Id, report.Version, false, " ", Token)).Issue);
        var returned = (await w.Reports.ReviewAsync(w.Supervisor, report.Value.Id, report.Version, false, "ساعت پایان اشتباه است", Token)).Value!;
        Assert.Equal((JobReportStatus.Returned, 2, "ساعت پایان اشتباه است"), (returned.Value.Status, returned.Value.RevisionNo, returned.Value.ReturnReason));
        var fixedDraft = (await w.Reports.SaveDraftAsync(w.Production, returned.Value.Id, returned.Version, DateOnly.FromDateTime(Day), s.Unit, s.Template, [Line(s, 8, 11)], Token)).Value!;
        var resubmitted = (await w.Reports.SubmitAsync(w.Production, fixedDraft.Value.Id, fixedDraft.Version, Token)).Value!;
        Assert.Equal(JobReportStatus.Submitted, resubmitted.Value.Status);
        Assert.Equal(180, resubmitted.Value.Lines.Single().WorkedMinutes);
        Assert.Equal(1, Count(w, "DailyJobReports"));

        // A stale version is refused.
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Reports.ReviewAsync(w.Supervisor, report.Value.Id, report.Version, true, null, Token)).Status);
        Assert.True((await w.Reports.ReviewAsync(w.Supervisor, resubmitted.Value.Id, resubmitted.Version, true, null, Token)).Succeeded);
    }

    [Fact]
    public async Task A_unit_without_a_supervisor_is_reviewed_by_planning_and_the_report_is_flagged_as_the_fallback()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w, supervisor: false);
        var report = await SubmittedAsync(w, s, Line(s, 8, 12));

        // The unit has no supervisor: the Production colleague who is not a supervisor cannot review, Planning can.
        Assert.Equal(ValidationIssue.JobReportReviewerNotAllowed, (await w.Reports.ReviewAsync(w.Supervisor, report.Value.Id, report.Version, true, null, Token)).Issue);
        var list = (await w.Reports.LoadAsync(w.Planning, DateOnly.FromDateTime(Day), DateOnly.FromDateTime(Day), Token)).Value!.Items.Single();
        Assert.True(list.CanReview);
        Assert.True(list.ReviewWouldBePlanningFallback);

        var approved = (await w.Reports.ReviewAsync(w.Planning, report.Value.Id, report.Version, true, null, Token)).Value!;
        Assert.True(approved.Value.ReviewedByPlanningFallback);
        Assert.True(approved.Value.Events.Last().PlanningFallback);
    }

    [Fact]
    public async Task Reports_are_visible_to_their_operator_the_supervisor_of_the_unit_planning_and_management_only()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        await SubmittedAsync(w, s, Line(s, 8, 12));
        var range = (DateOnly.FromDateTime(Day), DateOnly.FromDateTime(Day));

        foreach (var user in new[] { w.Production, w.Supervisor, w.Planning, w.Manager })
        {
            Assert.Single((await w.Reports.LoadAsync(user, range.Item1, range.Item2, Token)).Value!.Items);
        }

        // A Production colleague who is neither the operator nor a supervisor sees none; other departments are denied.
        Assert.Empty((await w.Reports.LoadAsync(w.OtherOperator, range.Item1, range.Item2, Token)).Value!.Items);
        foreach (var user in new Principal?[] { w.Drawing, w.Central, w.Qc, w.Warehouse, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Reports.LoadAsync(user, range.Item1, range.Item2, Token)).Status);
        }

        Assert.True((await w.Reports.LoadAsync(w.Production, range.Item1, range.Item2, Token)).Value!.Capabilities.CanSubmit);
        Assert.False((await w.Reports.LoadAsync(w.Manager, range.Item1, range.Item2, Token)).Value!.Capabilities.CanSubmit);
    }

    [Fact]
    public async Task Invalid_reports_are_refused_duplicates_overlaps_future_dates_wrong_machines_and_unreleased_stages()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        var otherUnit = await AddUnitAsync(w, "TURNING");
        var foreignMachine = (await w.Services.PlanningMaster.AddMachineAsync(w.Admin, "TCC-12", "تراش", otherUnit, Token)).Value!.Value.Id;
        var date = DateOnly.FromDateTime(Day);

        Assert.Equal(ValidationIssue.JobReportLineInvalid, (await w.Reports.SaveDraftAsync(w.Production, null, null, date, s.Unit, s.Template, [Line(s, 8, 9, machine: foreignMachine)], Token)).Issue);
        Assert.Equal(ValidationIssue.JobReportLineInvalid, (await w.Reports.SaveDraftAsync(w.Production, null, null, date, s.Unit, s.Template, [Line(s, 9, 8)], Token)).Issue);
        Assert.Equal(ValidationIssue.JobReportLineInvalid, (await w.Reports.SaveDraftAsync(w.Production, null, null, date, s.Unit, s.Template, [Line(s, 8, 9, downtime: 10, downtimeReason: " ")], Token)).Issue);
        Assert.Equal(ValidationIssue.JobReportLineInvalid, (await w.Reports.SaveDraftAsync(w.Production, null, null, date, s.Unit, s.Template, [Line(s, 8, 9, units: -1)], Token)).Issue);
        Assert.Equal(ValidationIssue.JobReportLineInvalid, (await w.Reports.SaveDraftAsync(w.Production, null, null, date, s.Unit, s.Template, [new JobLineInput(s.RequestId, s.ItemId, 7, s.Machine, At(8), At(9), 0, null, 1, null)], Token)).Issue);
        Assert.Equal(ValidationIssue.JobReportLinesOverlap, (await w.Reports.SaveDraftAsync(w.Production, null, null, date, s.Unit, s.Template, [Line(s, 8, 11), Line(s, 10, 12)], Token)).Issue);
        Assert.Equal(ValidationIssue.JobReportDateInvalid, (await w.Reports.SaveDraftAsync(w.Production, null, null, date.AddDays(3), s.Unit, s.Template, [Line(s, 8, 9)], Token)).Issue);
        Assert.Equal(ValidationIssue.JobReportDateInvalid, (await w.Reports.SaveDraftAsync(w.Production, null, null, date, otherUnit, s.Template, [Line(s, 8, 9)], Token)).Issue);
        Assert.Equal(0, Count(w, "DailyJobReports"));

        // One report per operator, date and shift; a report without lines cannot be submitted.
        var first = await DraftAsync(w, s, Line(s, 8, 9));
        Assert.Equal(ValidationIssue.JobReportDuplicate, (await w.Reports.SaveDraftAsync(w.Production, null, null, date, s.Unit, s.Template, [Line(s, 10, 11)], Token)).Issue);
        var emptied = (await w.Reports.SaveDraftAsync(w.Production, first.Value.Id, first.Version, date, s.Unit, s.Template, [], Token)).Value!;
        Assert.Equal(OperationStatus.InvalidState, (await w.Reports.SubmitAsync(w.Production, emptied.Value.Id, emptied.Version, Token)).Status);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Reports.SaveDraftAsync(w.Production, first.Value.Id, first.Version, date, s.Unit, s.Template, [Line(s, 8, 9)], Token)).Status);
    }

    [Fact]
    public async Task Hours_cannot_overlap_between_two_reports_of_the_same_operator()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        var evening = await TemplateAsync(w, "E", "10:00", "18:00");
        await RunUnitShiftAsync(w, s.Unit, evening, 3, 1, overlap: true);
        var date = DateOnly.FromDateTime(Day);

        await SubmittedAsync(w, s, Line(s, 8, 12));
        var second = (await w.Reports.SaveDraftAsync(w.Production, null, null, date, s.Unit, evening, [Line(s, 11, 13)], Token)).Value!;

        Assert.Equal(ValidationIssue.JobReportLinesOverlap, (await w.Reports.SubmitAsync(w.Production, second.Value.Id, second.Version, Token)).Issue);
        Assert.Equal(JobReportStatus.Draft, (await w.Reports.LoadAsync(w.Planning, date, date, Token)).Value!.Items.Single(i => i.Report.Value.Id == second.Value.Id).Report.Value.Status);
    }

    [Fact]
    public async Task Planning_alone_corrects_an_erroneous_start_or_submission_with_a_reason_and_only_before_qc_decided()
    {
        using var w = await StartAsync();
        var s = await SetupAsync(w);
        var target = await TargetAsync(w, s.RequestId, s.ItemId, 1);

        foreach (var user in new Principal?[] { w.Production, w.Manager, w.Qc, w.Drawing, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Work.CorrectAsync(user, StageCorrection.UndoStart, target, "اشتباه", Token)).Status);
        }

        Assert.Equal(ValidationIssue.QueueReasonRequired, (await w.Work.CorrectAsync(w.Planning, StageCorrection.UndoStart, target, " ", Token)).Issue);
        var undone = await w.Work.CorrectAsync(w.Planning, StageCorrection.UndoStart, target, "اپراتور اشتباه مرحله را شروع کرد", Token);
        Assert.True(undone.Succeeded, undone.Status + " " + undone.Issue);
        Assert.Equal(ItemStageStatus.Waiting, undone.Value!.NewStatus);
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'StageCorrected'"));

        // The operator starts and submits again; Planning undoes the submission; once QC decided anything, nothing can be undone.
        Assert.True((await w.Work.PerformAsync(w.Production, StageAction.Start, await TargetAsync(w, s.RequestId, s.ItemId, 1), Token)).Succeeded);
        Assert.True((await w.Work.PerformAsync(w.Production, StageAction.SubmitForQc, await TargetAsync(w, s.RequestId, s.ItemId, 1), Token)).Succeeded);
        var back = await w.Work.CorrectAsync(w.Planning, StageCorrection.UndoSubmit, await TargetAsync(w, s.RequestId, s.ItemId, 1), "ارسال زودهنگام", Token);
        Assert.Equal(ItemStageStatus.InProgress, back.Value!.NewStatus);
        Assert.True((await w.Work.PerformAsync(w.Production, StageAction.SubmitForQc, await TargetAsync(w, s.RequestId, s.ItemId, 1), Token)).Succeeded);
        Assert.True((await w.Services.QcInspection.RecordInspectionAsync(w.Qc, await TargetAsync(w, s.RequestId, s.ItemId, 1), [new InspectionEntry(InspectionDisposition.Accepted, 4, null)], Token)).Succeeded);
        Assert.Equal(OperationStatus.InvalidState, (await w.Work.CorrectAsync(w.Planning, StageCorrection.UndoSubmit, await TargetAsync(w, s.RequestId, s.ItemId, 1), "دیر شد", Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Work.CorrectAsync(w.Planning, StageCorrection.UndoStart, await TargetAsync(w, s.RequestId, s.ItemId, 1), "دیر شد", Token)).Status);
    }
}
