using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;
using ERP.MES.Application.Production;
using ERP.MES.Application.Scheduling;
using ERP.MES.Domain.Scheduling;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// The Planning calendar, shifts, capacity, worker schedule and the planned-end forecast (Decision Log #13, #95 D5, #100) on a real SQLite file with the real authorization.
public class SchedulingServiceTests
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

    [Fact]
    public async Task The_regular_week_is_seeded_saturday_to_thursday_and_planning_alone_can_change_it_with_audit_and_versions()
    {
        using var w = await StartAsync();
        var view = (await w.Calendar.LoadAsync(w.Planning, Token)).Value!;
        Assert.Equal(7, view.Configuration.Weekly.Count);
        Assert.False(view.Configuration.Weekly.Single(d => d.Value.Day == DayOfWeek.Friday).Value.IsWorkday);
        Assert.Equal(6, view.Configuration.Weekly.Count(d => d.Value.IsWorkday));
        Assert.True(view.Capabilities.CanMaintain);

        var friday = view.Configuration.Weekly.Single(d => d.Value.Day == DayOfWeek.Friday);
        foreach (var user in new Principal?[] { w.Manager, w.Production, w.Drawing, w.Central, w.Qc, w.Warehouse, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Calendar.SetWeekdayAsync(user, DayOfWeek.Friday, friday.Version, true, Token)).Status);
        }

        Assert.True((await w.Calendar.SetWeekdayAsync(w.Planning, DayOfWeek.Friday, friday.Version, true, Token)).Succeeded);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Calendar.SetWeekdayAsync(w.Planning, DayOfWeek.Friday, friday.Version, false, Token)).Status);
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'WeeklyWorkdayChanged'"));

        // Reading is open to Planning, Management and Production only.
        Assert.True((await w.Calendar.LoadAsync(w.Manager, Token)).Succeeded);
        Assert.True((await w.Calendar.LoadAsync(w.Production, Token)).Succeeded);
        Assert.False((await w.Calendar.LoadAsync(w.Production, Token)).Value!.Capabilities.CanMaintain);
        foreach (var user in new Principal?[] { w.Drawing, w.Central, w.Qc, w.Warehouse, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Calendar.LoadAsync(user, Token)).Status);
        }
    }

    [Fact]
    public async Task Shift_templates_are_data_with_unique_codes_valid_times_and_retirement_instead_of_deletion()
    {
        using var w = await StartAsync();
        var day = await TemplateAsync(w, "D", "08:00", "17:00", "12:00", "13:00");
        await TemplateAsync(w, "C", "22:00", "06:00");

        Assert.Equal(ValidationIssue.ShiftCodeDuplicate, (await w.Calendar.AddTemplateAsync(w.Planning, "D", "x", new TimeOnly(1, 0), new TimeOnly(2, 0), null, null, Token)).Issue);
        Assert.Equal(ValidationIssue.ShiftTimesInvalid, (await w.Calendar.AddTemplateAsync(w.Planning, "E", "x", new TimeOnly(8, 0), new TimeOnly(8, 0), null, null, Token)).Issue);
        Assert.Equal(ValidationIssue.ShiftTimesInvalid, (await w.Calendar.AddTemplateAsync(w.Planning, "F", "x", new TimeOnly(8, 0), new TimeOnly(16, 0), new TimeOnly(18, 0), new TimeOnly(19, 0), Token)).Issue);
        foreach (var user in new Principal?[] { w.Manager, w.Production, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Calendar.AddTemplateAsync(user, "Z", "x", new TimeOnly(1, 0), new TimeOnly(2, 0), null, null, Token)).Status);
        }

        var config = (await w.Calendar.LoadAsync(w.Planning, Token)).Value!.Configuration;
        var stored = config.Templates.Single(t => t.Value.Id == day);
        Assert.Equal(480, stored.Value.WorkingMinutes);
        Assert.True(config.Templates.Single(t => t.Value.Code == "C").Value.CrossesMidnight);

        var changed = await w.Calendar.UpdateTemplateAsync(w.Planning, day, stored.Version, "روزکار جدید", new TimeOnly(7, 30), new TimeOnly(16, 0), null, null, Token);
        Assert.True(changed.Succeeded);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Calendar.UpdateTemplateAsync(w.Planning, day, stored.Version, "x", new TimeOnly(7, 30), new TimeOnly(16, 0), null, null, Token)).Status);
        Assert.True((await w.Calendar.SetTemplateActiveAsync(w.Planning, day, changed.Value!.Version, false, Token)).Succeeded);
        Assert.Equal(2, Count(w, "ShiftTemplates"));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'ShiftTemplateAdded' AND TargetId = 'D'"));
    }

    [Fact]
    public async Task Unit_shifts_need_an_explicit_overlap_marking_and_a_valid_capacity()
    {
        using var w = await StartAsync();
        var unit = await AddUnitAsync(w, "MILLING");
        var a = await TemplateAsync(w, "A", "06:00", "14:00");
        var b = await TemplateAsync(w, "B", "14:00", "22:00");
        var d = await TemplateAsync(w, "D", "08:00", "17:00", "12:00", "13:00");

        await RunUnitShiftAsync(w, unit, a, 3, 1);
        await RunUnitShiftAsync(w, unit, b, 3, 1); // touches A at 14:00 but does not overlap
        Assert.Equal(ValidationIssue.UnitShiftExists, (await w.Calendar.AddUnitShiftAsync(w.Planning, unit, a, 2, 1, false, Token)).Issue);
        Assert.Equal(ValidationIssue.CapacityInvalid, (await w.Calendar.AddUnitShiftAsync(w.Planning, unit, d, 0, 0, true, Token)).Issue);
        Assert.Equal(ValidationIssue.CapacityInvalid, (await w.Calendar.AddUnitShiftAsync(w.Planning, unit, d, 2, 5, true, Token)).Issue);

        // D overlaps A and B: refused until Planning marks the overlap explicitly, then it has its own capacity.
        Assert.Equal(ValidationIssue.UnitShiftOverlapNotMarked, (await w.Calendar.AddUnitShiftAsync(w.Planning, unit, d, 2, 1, false, Token)).Issue);
        var marked = await w.Calendar.AddUnitShiftAsync(w.Planning, unit, d, 2, 1, true, Token);
        Assert.True(marked.Succeeded, marked.Status + " " + marked.Issue);
        Assert.True(marked.Value!.Value.OverlapAllowed);
        Assert.Equal(ValidationIssue.ProductionUnitInvalid, (await w.Calendar.AddUnitShiftAsync(w.Planning, 987654, d, 2, 1, true, Token)).Issue);

        // Unmarking the overlap on update is refused too.
        Assert.Equal(ValidationIssue.UnitShiftOverlapNotMarked, (await w.Calendar.UpdateUnitShiftAsync(w.Planning, marked.Value.Value.Id, marked.Value.Version, 2, 1, false, Token)).Issue);
        Assert.Equal(3, Count(w, "AuditLog WHERE ActionCode = 'UnitShiftAdded'"));
        foreach (var user in new Principal?[] { w.Manager, w.Production, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Calendar.AddUnitShiftAsync(user, unit, d, 2, 1, true, Token)).Status);
        }
    }

    [Fact]
    public async Task Exceptions_need_a_reason_are_not_duplicated_and_report_how_many_planned_rows_they_invalidate()
    {
        using var w = await StartAsync();
        var unit = await AddUnitAsync(w, "MILLING");
        var d = await TemplateAsync(w, "D", "08:00", "17:00", "12:00", "13:00");
        await RunUnitShiftAsync(w, unit, d);
        var op = await OperatorAsync(w, "OP-1");
        Assert.True((await w.Schedule.ScheduleAsync(w.Planning, op, Saturday, unit, d, null, null, null, Token)).Succeeded);

        Assert.Equal(ValidationIssue.CalendarReasonRequired, (await w.Calendar.AddExceptionAsync(w.Planning, Saturday, CalendarExceptionKind.Off, null, null, " ", Token)).Issue);
        var holiday = await w.Calendar.AddExceptionAsync(w.Planning, Saturday, CalendarExceptionKind.Off, null, null, "تعطیل رسمی", Token);
        Assert.True(holiday.Succeeded, holiday.Status + " " + holiday.Issue);
        Assert.Equal(1, holiday.Value);
        Assert.Equal(ValidationIssue.CalendarExceptionExists, (await w.Calendar.AddExceptionAsync(w.Planning, Saturday, CalendarExceptionKind.Off, null, null, "دوباره", Token)).Issue);
        Assert.Equal(ValidationIssue.ProductionUnitInvalid, (await w.Calendar.AddExceptionAsync(w.Planning, Saturday, CalendarExceptionKind.Off, 987654, null, "x", Token)).Issue);

        // The planned row is NOT cancelled; it is flagged for Planning.
        var rows = (await w.Schedule.LoadAsync(w.Planning, Saturday, Saturday, Token)).Value!.Rows;
        Assert.Equal(WorkerScheduleStatus.Planned, rows.Single().Row.Value.Status);
        Assert.Equal(ScheduleFlags.DayNowOff, rows.Single().Flags);

        // A new row on the off day is refused; cancelling the exception makes the row valid again.
        var other = await OperatorAsync(w, "OP-2");
        Assert.Equal(ValidationIssue.ScheduleDayOff, (await w.Schedule.ScheduleAsync(w.Planning, other, Saturday, unit, d, null, null, null, Token)).Issue);
        var config = (await w.Calendar.LoadAsync(w.Planning, Token)).Value!.Configuration;
        var exception = config.Exceptions.Single();
        Assert.True((await w.Calendar.CancelExceptionAsync(w.Planning, exception.Value.Id, exception.Version, Token)).Succeeded);
        Assert.Equal(ScheduleFlags.None, (await w.Schedule.LoadAsync(w.Planning, Saturday, Saturday, Token)).Value!.Rows.Single().Flags);
        Assert.Equal(2, Count(w, "AuditLog WHERE ActionCode LIKE 'CalendarException%'"));
    }

    [Fact]
    public async Task Worker_scheduling_enforces_the_d5_rules_unit_shift_day_absence_overlap_capacity_and_machine_exclusivity()
    {
        using var w = await StartAsync();
        var milling = await AddUnitAsync(w, "MILLING");
        var turning = await AddUnitAsync(w, "TURNING");
        var a = await TemplateAsync(w, "A", "06:00", "14:00");
        var d = await TemplateAsync(w, "D", "08:00", "17:00", "12:00", "13:00");
        var night = await TemplateAsync(w, "C", "22:00", "06:00");
        var early = await TemplateAsync(w, "E", "04:00", "08:00");
        await RunUnitShiftAsync(w, milling, a, 2, 1);
        await RunUnitShiftAsync(w, milling, d, 1, 1, overlap: true);
        await RunUnitShiftAsync(w, turning, a, 3, 1);
        await RunUnitShiftAsync(w, turning, night, 3, 1);
        await RunUnitShiftAsync(w, turning, early, 3, 1, overlap: true);
        var machine = (await w.Services.PlanningMaster.AddMachineAsync(w.Planning, "FCC-06", "فرز", milling, Token)).Value!.Value.Id;
        var o1 = await OperatorAsync(w, "O1");
        var o2 = await OperatorAsync(w, "O2");
        var o3 = await OperatorAsync(w, "O3");

        // Not configured / inactive operator / unknown.
        Assert.Equal(ValidationIssue.ScheduleUnitShiftNotConfigured, (await w.Schedule.ScheduleAsync(w.Planning, o1, Saturday, milling, night, null, null, null, Token)).Issue);
        Assert.Equal(ValidationIssue.OperatorInvalid, (await w.Schedule.ScheduleAsync(w.Planning, 987654, Saturday, milling, a, null, null, null, Token)).Issue);
        Assert.Equal(ValidationIssue.ScheduleDayOff, (await w.Schedule.ScheduleAsync(w.Planning, o1, Friday, milling, a, null, null, null, Token)).Issue);

        // A worker is never planned in two overlapping occurrences on the same day (A 06-14 and D 08-17), also not in another unit.
        Assert.True((await w.Schedule.ScheduleAsync(w.Planning, o1, Saturday, milling, a, null, null, null, Token)).Succeeded);
        Assert.Equal(ValidationIssue.ScheduleWorkerOverlap, (await w.Schedule.ScheduleAsync(w.Planning, o1, Saturday, milling, d, null, null, null, Token)).Issue);
        Assert.Equal(ValidationIssue.ScheduleWorkerOverlap, (await w.Schedule.ScheduleAsync(w.Planning, o1, Saturday, turning, a, null, null, null, Token)).Issue);

        // Maximum capacity of the unit-shift is a hard limit (milling A max 2).
        Assert.True((await w.Schedule.ScheduleAsync(w.Planning, o2, Saturday, milling, a, null, null, null, Token)).Succeeded);
        Assert.Equal(ValidationIssue.ScheduleCapacityExceeded, (await w.Schedule.ScheduleAsync(w.Planning, o3, Saturday, milling, a, null, null, null, Token)).Issue);
        // ...but the overlapping D shift has its own capacity (1).
        Assert.True((await w.Schedule.ScheduleAsync(w.Planning, o3, Saturday, milling, d, null, null, null, Token)).Succeeded);

        // Absence blocks.
        var off = await w.Schedule.AddOffDayAsync(w.Planning, o1, Saturday.AddDays(2), Saturday.AddDays(3), OffDayKind.Sick, "گواهی", Token);
        Assert.True(off.Succeeded, off.Status + " " + off.Issue);
        Assert.Equal(ValidationIssue.ScheduleOperatorAbsent, (await w.Schedule.ScheduleAsync(w.Planning, o1, Saturday.AddDays(2), milling, a, null, null, null, Token)).Issue);

        // A shift that crosses midnight is compared with the next day: C on Saturday 22:00-06:00 overlaps E on Sunday 04:00-08:00 for the same worker.
        Assert.True((await w.Schedule.ScheduleAsync(w.Planning, o2, Saturday.AddDays(3), turning, night, null, null, null, Token)).Succeeded);
        Assert.Equal(ValidationIssue.ScheduleWorkerOverlap, (await w.Schedule.ScheduleAsync(w.Planning, o2, Saturday.AddDays(4), turning, early, null, null, null, Token)).Issue);
        // ...while the next regular shift A at 06:00 only touches the end of C.
        Assert.True((await w.Schedule.ScheduleAsync(w.Planning, o2, Saturday.AddDays(4), turning, a, null, null, null, Token)).Succeeded);

        // Machines: unit membership, one operator at a time, explicit handover overlap with a stated length.
        var other = (await w.Services.PlanningMaster.AddMachineAsync(w.Planning, "TCC-12", "تراش", turning, Token)).Value!.Value.Id;
        Assert.Equal(ValidationIssue.MachineInvalid, (await w.Schedule.ScheduleAsync(w.Planning, o1, Saturday.AddDays(8), milling, a, other, null, null, Token)).Issue);
        var monday = Saturday.AddDays(2);
        Assert.True((await w.Schedule.ScheduleAsync(w.Planning, o2, monday, milling, a, machine, null, null, Token)).Succeeded);
        Assert.Equal(ValidationIssue.ScheduleMachineBusy, (await w.Schedule.ScheduleAsync(w.Planning, o3, monday, milling, d, machine, null, null, Token)).Issue);
        Assert.Equal(ValidationIssue.HandoverInvalid, (await w.Schedule.ScheduleAsync(w.Planning, o3, monday, milling, d, machine, 30, "تحویل", Token)).Issue); // overlap A/D is 6 hours
        Assert.Equal(ValidationIssue.HandoverInvalid, (await w.Schedule.ScheduleAsync(w.Planning, o3, monday, milling, d, null, 30, "تحویل", Token)).Issue);
        Assert.True((await w.Schedule.ScheduleAsync(w.Planning, o3, monday, milling, d, machine, 480, "تحویل شیفت", Token)).Succeeded);

        // Only Planning schedules.
        foreach (var user in new Principal?[] { w.Manager, w.Production, w.Drawing, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Schedule.ScheduleAsync(user, o1, Saturday.AddDays(9), milling, a, null, null, null, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Schedule.AddOffDayAsync(user, o1, Saturday, Saturday, OffDayKind.Leave, null, Token)).Status);
        }
    }

    [Fact]
    public async Task Cancelling_a_schedule_row_needs_a_reason_and_a_current_version_and_frees_the_capacity()
    {
        using var w = await StartAsync();
        var unit = await AddUnitAsync(w, "MILLING");
        var a = await TemplateAsync(w, "A", "06:00", "14:00");
        await RunUnitShiftAsync(w, unit, a, 1, 0);
        var o1 = await OperatorAsync(w, "O1");
        var o2 = await OperatorAsync(w, "O2");
        var first = (await w.Schedule.ScheduleAsync(w.Planning, o1, Saturday, unit, a, null, null, null, Token)).Value!;
        Assert.Equal(ValidationIssue.ScheduleCapacityExceeded, (await w.Schedule.ScheduleAsync(w.Planning, o2, Saturday, unit, a, null, null, null, Token)).Issue);

        Assert.Equal(ValidationIssue.CalendarReasonRequired, (await w.Schedule.CancelScheduleAsync(w.Planning, first.Value.Id, first.Version, " ", Token)).Issue);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Schedule.CancelScheduleAsync(w.Planning, first.Value.Id, first.Version + 5, "غیبت", Token)).Status);
        Assert.True((await w.Schedule.CancelScheduleAsync(w.Planning, first.Value.Id, first.Version, "غیبت", Token)).Succeeded);
        Assert.Equal(OperationStatus.InvalidState, (await w.Schedule.CancelScheduleAsync(w.Planning, first.Value.Id, first.Version + 1, "دوباره", Token)).Status);
        Assert.True((await w.Schedule.ScheduleAsync(w.Planning, o2, Saturday, unit, a, null, null, null, Token)).Succeeded);
        Assert.Equal(2, Count(w, "WorkerSchedules WHERE Status = 1") + Count(w, "WorkerSchedules WHERE Status = 2"));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'WorkerScheduleCancelled'"));

        // Off-days are cancelled, never deleted, and an absence of an unknown operator is refused.
        var absent = await w.Schedule.AddOffDayAsync(w.Planning, o2, Saturday, Saturday.AddDays(1), OffDayKind.Leave, null, Token);
        Assert.True(absent.Succeeded);
        Assert.Equal(1, absent.Value); // the planned row of o2 on Saturday is now invalid, flagged not cancelled
        Assert.Equal(ScheduleFlags.OperatorAbsent, (await w.Schedule.LoadAsync(w.Planning, Saturday, Saturday, Token)).Value!.Rows.Single(r => r.Row.Value.IsPlanned).Flags);
        Assert.Equal(ValidationIssue.OperatorInvalid, (await w.Schedule.AddOffDayAsync(w.Planning, 987654, Saturday, Saturday, OffDayKind.Leave, null, Token)).Issue);
        Assert.Equal(ValidationIssue.EndBeforeStart, (await w.Schedule.AddOffDayAsync(w.Planning, o2, Saturday.AddDays(3), Saturday, OffDayKind.Leave, null, Token)).Issue);
        var offDay = (await w.Schedule.LoadAsync(w.Planning, Saturday, Saturday, Token)).Value!.OffDays.Single();
        Assert.True((await w.Schedule.CancelOffDayAsync(w.Planning, offDay.Value.Id, offDay.Version, Token)).Succeeded);
        Assert.Equal(1, Count(w, "OperatorOffDays"));
    }

    [Fact]
    public async Task The_planned_end_forecast_walks_the_working_calendar_in_queue_order()
    {
        using var w = await StartAsync();
        var milling = await AddUnitAsync(w, "MILLING");
        var turning = await AddUnitAsync(w, "TURNING");
        await MaterialAsync(w);
        var d = await TemplateAsync(w, "D", "08:00", "17:00", "12:00", "13:00");
        await RunUnitShiftAsync(w, milling, d);
        await ItemAsync(w, "R-1", "A", [milling]);
        await ItemAsync(w, "R-2", "B", [milling], Priority.Normal);
        await ItemAsync(w, "R-3", "C", [turning]);

        // Saturday 07:00 (display zone = UTC in the tests). Each stage is 2 min/piece x 10 = 20 minutes of work.
        w.Fixture.Clock.UtcNow = new DateTime(2026, 10, 24, 7, 0, 0, DateTimeKind.Utc);
        var queue = await LoadAsync(w, w.Planning, milling);
        Assert.Equal(new DateTime(2026, 10, 24, 8, 20, 0, DateTimeKind.Utc), queue.Queue[0].PlannedEndUtc);
        Assert.Equal(new DateTime(2026, 10, 24, 8, 40, 0, DateTimeKind.Utc), queue.Queue[1].PlannedEndUtc);

        // Late Thursday: the work continues on Saturday because Friday is off.
        w.Fixture.Clock.UtcNow = new DateTime(2026, 10, 22, 16, 50, 0, DateTimeKind.Utc);
        var late = await LoadAsync(w, w.Planning, milling);
        Assert.Equal(new DateTime(2026, 10, 24, 8, 10, 0, DateTimeKind.Utc), late.Queue[0].PlannedEndUtc);

        // A unit with no shifts gets no forecast; a stopped entry gets none and does not delay the others.
        Assert.Null((await LoadAsync(w, w.Planning, turning)).Queue.Single().PlannedEndUtc);
        w.Fixture.Clock.UtcNow = new DateTime(2026, 10, 24, 7, 0, 0, DateTimeKind.Utc);
        var first = (await LoadAsync(w, w.Planning, milling)).Queue[0];
        Assert.True((await w.Queue.StopStageAsync(w.Planning, first.ToRowRef(), "خرابی", Token)).Succeeded);
        var afterStop = await LoadAsync(w, w.Planning, milling);
        Assert.Null(afterStop.Queue.Single(e => e.Block == QueueBlock.StageStopped).PlannedEndUtc);
        Assert.Equal(new DateTime(2026, 10, 24, 8, 20, 0, DateTimeKind.Utc), afterStop.Queue.Single(e => e.Block == QueueBlock.None).PlannedEndUtc);

        // Making Saturday a company holiday moves the forecast to Sunday.
        Assert.True((await w.Calendar.AddExceptionAsync(w.Planning, Saturday, CalendarExceptionKind.Off, null, null, "تعطیل", Token)).Succeeded);
        Assert.Equal(new DateTime(2026, 10, 25, 8, 20, 0, DateTimeKind.Utc), (await LoadAsync(w, w.Planning, milling)).Queue.Single(e => e.Block == QueueBlock.None).PlannedEndUtc);
    }
}
