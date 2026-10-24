using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;
using ERP.MES.Application.Production;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// The operational per-unit production queues (Decision Log #78, #99) on a real SQLite file with the real authorization and repositories.
public class UnitQueueTests
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

    private static string Key(UnitQueueEntry e) => e.ItemNo + "/" + e.StageNo;

    [Fact]
    public async Task A_unit_queue_lists_released_stages_of_that_unit_by_category_priority_position_and_first_come_first_served()
    {
        using var w = await StartAsync();
        var milling = await AddUnitAsync(w, "MILLING");
        var turning = await AddUnitAsync(w, "TURNING");
        await MaterialAsync(w);
        var a = await ItemAsync(w, "R-1", "A", [milling], Priority.Normal);
        var b = await ItemAsync(w, "R-2", "B", [milling], Priority.VeryUrgent);
        var c = await ItemAsync(w, "R-3", "C", [milling], Priority.Normal);
        var d = await ItemAsync(w, "R-4", "D", [milling], Priority.Urgent);
        await ItemAsync(w, "R-5", "E", [turning], Priority.VeryUrgent);

        // D is started, so it leads regardless of priority (IN_PROGRESS first).
        Assert.Equal(ValidationIssue.StageUnitNotDesignated, (await w.Work.PerformAsync(w.Production, StageAction.Start, await TargetAsync(w, d.RequestId, d.ItemId, 1), Token)).Issue);
        Assert.True((await w.Designations.AssignAsync(w.Planning, w.Production.UserId, milling, Token)).Succeeded);
        var started = await w.Work.PerformAsync(w.Production, StageAction.Start, await TargetAsync(w, d.RequestId, d.ItemId, 1), Token);
        Assert.True(started.Succeeded, started.Status + " " + started.Issue);

        var queue = await LoadAsync(w, w.Planning, milling);

        Assert.Equal(new[] { "D/1", "B/1", "A/1", "C/1" }, queue.Queue.Select(Key).ToArray());
        Assert.Equal([1, 2, 3, 4], queue.Queue.Select(e => e.Order).ToArray());
        Assert.Equal(QueueCategory.InProgress, queue.Queue[0].Category);
        Assert.Equal(QueueCategory.Waiting, queue.Queue[1].Category);
        Assert.All(queue.Queue.Skip(1), e => Assert.True(e.IsActionable));
        Assert.All(queue.Queue, e => Assert.Null(e.PlannedEndUtc));
        Assert.Empty(queue.Pending);

        // Equal priority (A before C) is first come, first served: A became available first.
        Assert.True(queue.Queue[2].AvailableSinceUtc < queue.Queue[3].AvailableSinceUtc);

        // The other unit's queue holds only its own stage.
        Assert.Equal(["E/1"], (await LoadAsync(w, w.Planning, turning)).Queue.Select(Key).ToArray());
    }

    [Fact]
    public async Task Planning_positions_reorder_equal_priority_work_persist_are_audited_and_do_not_touch_priority()
    {
        using var w = await StartAsync();
        var milling = await AddUnitAsync(w, "MILLING");
        await MaterialAsync(w);
        var a = await ItemAsync(w, "R-1", "A", [milling]);
        var b = await ItemAsync(w, "R-2", "B", [milling]);
        var c = await ItemAsync(w, "R-3", "C", [milling]);
        var queue = await LoadAsync(w, w.Planning, milling);
        Assert.Equal(new[] { "A/1", "B/1", "C/1" }, queue.Queue.Select(Key).ToArray());

        var byItem = queue.Queue.ToDictionary(e => e.ItemNo);
        var ordered = new[] { byItem["C"], byItem["A"], byItem["B"] }.Select(e => e.ToRowRef()).ToList();
        var result = await w.Queue.SetPlanningOrderAsync(w.Planning, milling, ordered, Token);

        Assert.True(result.Succeeded, result.Status + " " + result.Issue);
        var after = await LoadAsync(w, w.Planning, milling);
        Assert.Equal(new[] { "C/1", "A/1", "B/1" }, after.Queue.Select(Key).ToArray());
        Assert.Equal(new int?[] { 1, 2, 3 }, after.Queue.Select(e => e.PlanningPosition).ToArray());
        Assert.All(after.Queue, e => Assert.Equal(Priority.Normal, e.Priority));
        Assert.Equal(3, Count(w, "AuditLog WHERE ActionCode = 'QueuePositionsSet'"));

        // A restarted application sees the same order (persisted state, not screen state).
        var restarted = WorkflowServices.Create(w.Fixture.Db.ContextFactory, w.Fixture.Services.Authorization, PersistenceTestData.Dates, w.Fixture.Clock);
        Assert.Equal(new[] { "C/1", "A/1", "B/1" }, (await restarted.UnitQueue.LoadAsync(w.Planning, milling, Token)).Value!.Queue.Select(Key).ToArray());

        // The same stale view (row versions from before the reorder) is refused and changes nothing.
        var stale = await w.Queue.SetPlanningOrderAsync(w.Planning, milling, ordered, Token);
        Assert.Equal(OperationStatus.ConcurrencyConflict, stale.Status);
        Assert.Equal(new[] { "C/1", "A/1", "B/1" }, (await LoadAsync(w, w.Planning, milling)).Queue.Select(Key).ToArray());
    }

    [Fact]
    public async Task Ordering_commands_validate_their_rows_and_save_nothing_when_one_row_is_invalid()
    {
        using var w = await StartAsync();
        var milling = await AddUnitAsync(w, "MILLING");
        var turning = await AddUnitAsync(w, "TURNING");
        await MaterialAsync(w);
        await ItemAsync(w, "R-1", "A", [milling]);
        await ItemAsync(w, "R-2", "B", [turning]);
        await ItemAsync(w, "R-3", "C", [milling], release: false);
        var millingQueue = await LoadAsync(w, w.Planning, milling);
        var turningQueue = await LoadAsync(w, w.Planning, turning);
        var good = millingQueue.Queue.Single().ToRowRef();

        Assert.Equal(ValidationIssue.QueueRowsInvalid, (await w.Queue.SetPlanningOrderAsync(w.Planning, milling, [], Token)).Issue);
        Assert.Equal(ValidationIssue.QueueRowsInvalid, (await w.Queue.SetPlanningOrderAsync(w.Planning, milling, [good, good], Token)).Issue);

        // A row of another unit, and a stage that is not released, are refused; the valid row listed with them is not saved either.
        Assert.Equal(ValidationIssue.QueueRowsInvalid, (await w.Queue.SetPlanningOrderAsync(w.Planning, milling, [good, turningQueue.Queue.Single().ToRowRef()], Token)).Issue);
        var pending = millingQueue.Pending.Single();
        Assert.Equal(QueueBlock.NotReleased, pending.Block);
        Assert.Equal(ValidationIssue.QueueRowsInvalid, (await w.Queue.SetPlanningOrderAsync(w.Planning, milling, [good, pending.ToRowRef()], Token)).Issue);
        Assert.Null((await LoadAsync(w, w.Planning, milling)).Queue.Single().PlanningPosition);
        Assert.Equal(0, Count(w, "AuditLog WHERE ActionCode = 'QueuePositionsSet'"));
        Assert.Equal(OperationStatus.NotFound, (await w.Queue.LoadAsync(w.Planning, 987654, Token)).Status);
    }

    [Fact]
    public async Task Pending_work_is_listed_with_the_reason_it_is_not_actionable_and_a_later_stage_waits_for_the_previous_qc()
    {
        using var w = await StartAsync();
        var milling = await AddUnitAsync(w, "MILLING");
        var turning = await AddUnitAsync(w, "TURNING");
        await MaterialAsync(w);
        var a = await ItemAsync(w, "R-1", "A", [milling, turning]);
        await ItemAsync(w, "R-2", "B", [milling], release: false);

        var queue = await LoadAsync(w, w.Planning, milling);
        Assert.Equal(["A/1"], queue.Queue.Select(Key).ToArray());
        Assert.Equal(QueueBlock.NotReleased, Assert.Single(queue.Pending).Block);

        var next = await LoadAsync(w, w.Planning, turning);
        Assert.Empty(next.Queue);
        var waiting = Assert.Single(next.Pending);
        Assert.Equal(QueueBlock.WaitingPreviousQc, waiting.Block);
        Assert.False(waiting.IsActionable);
        Assert.Equal(2, waiting.StageNo);

        // The later stage cannot start, even by a designated Production user, before QC released the previous one.
        await w.Designations.AssignAsync(w.Planning, w.Production.UserId, turning);
        var early = await w.Work.PerformAsync(w.Production, StageAction.Start, await TargetAsync(w, a.RequestId, a.ItemId, 2), Token);
        Assert.False(early.Succeeded);

        // Run stage 1 to QC approval; stage 2 then enters the turning queue as actionable work, and the completed stage is counted, not listed.
        await w.Designations.AssignAsync(w.Planning, w.Production.UserId, milling);
        Assert.True((await w.Work.PerformAsync(w.Production, StageAction.Start, await TargetAsync(w, a.RequestId, a.ItemId, 1), Token)).Succeeded);
        Assert.True((await w.Work.PerformAsync(w.Production, StageAction.SubmitForQc, await TargetAsync(w, a.RequestId, a.ItemId, 1), Token)).Succeeded);
        Assert.Equal(QueueBlock.WaitingQc, Assert.Single((await LoadAsync(w, w.Planning, milling)).Pending, e => e.ItemNo == "A").Block);
        // A stage planned in a unit is decided by quantity (10 pieces here).
        Assert.True((await w.Services.QcInspection.RecordInspectionAsync(w.Qc, await TargetAsync(w, a.RequestId, a.ItemId, 1), [new ERP.MES.Domain.Requests.InspectionEntry(ERP.MES.Domain.Requests.InspectionDisposition.Accepted, 10, null)], Token)).Succeeded);

        var millingAfter = await LoadAsync(w, w.Planning, milling);
        Assert.Equal(1, millingAfter.CompletedStageCount);
        Assert.DoesNotContain(millingAfter.Queue.Concat(millingAfter.Pending), e => e.ItemNo == "A");
        var turningAfter = await LoadAsync(w, w.Planning, turning);
        var released = Assert.Single(turningAfter.Queue);
        Assert.Equal("A/2", Key(released));
        Assert.True(released.IsActionable);
        Assert.NotNull(released.AvailableSinceUtc);
    }

    [Fact]
    public async Task Access_is_by_permission_and_for_production_by_unit_designation_and_default_deny_holds()
    {
        using var w = await StartAsync();
        var milling = await AddUnitAsync(w, "MILLING");
        var turning = await AddUnitAsync(w, "TURNING");
        await MaterialAsync(w);
        await ItemAsync(w, "R-1", "A", [milling]);

        foreach (var user in new Principal?[] { w.Drawing, w.Central, w.Qc, w.Warehouse, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Queue.LoadAsync(user, milling, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Queue.GetOverviewAsync(user, Token)).Status);
        }

        // Production without a designation sees no unit at all; with one, only that unit.
        Assert.Empty((await w.Queue.GetOverviewAsync(w.Production, Token)).Value!.Units);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Queue.LoadAsync(w.Production, milling, Token)).Status);
        Assert.True((await w.Designations.AssignAsync(w.Planning, w.Production.UserId, milling, Token)).Succeeded);
        Assert.Equal(["MILLING"], (await w.Queue.GetOverviewAsync(w.Production, Token)).Value!.Units.Select(u => u.Code).ToArray());
        Assert.True((await w.Queue.LoadAsync(w.Production, milling, Token)).Succeeded);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Queue.LoadAsync(w.Production, turning, Token)).Status);

        // Management and Planning see every unit.
        Assert.Equal(2, (await w.Queue.GetOverviewAsync(w.Manager, Token)).Value!.Units.Count);
        Assert.Equal(2, (await w.Queue.GetOverviewAsync(w.Planning, Token)).Value!.Units.Count);

        // Withdrawing the designation takes the access away at the next call.
        var row = (await w.Designations.LoadAsync(w.Planning, Token)).Value!.Rows.Single();
        Assert.True((await w.Designations.RevokeAsync(w.Planning, row.DesignationId, row.Version, Token)).Succeeded);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Queue.LoadAsync(w.Production, milling, Token)).Status);
    }

    [Fact]
    public async Task Only_planning_may_order_only_management_may_override_and_only_planning_or_the_designated_unit_may_stop()
    {
        using var w = await StartAsync();
        var milling = await AddUnitAsync(w, "MILLING");
        await MaterialAsync(w);
        await ItemAsync(w, "R-1", "A", [milling]);
        var row = (await LoadAsync(w, w.Planning, milling)).Queue.Single().ToRowRef();

        foreach (var user in new Principal?[] { w.Manager, w.ManagementStaff, w.Drawing, w.Central, w.Qc, w.Warehouse, w.Production, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Queue.SetPlanningOrderAsync(user, milling, [row], Token)).Status);
        }

        foreach (var user in new Principal?[] { w.Planning, w.Drawing, w.Central, w.Qc, w.Warehouse, w.Production, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Queue.OverrideAsync(user, milling, "reason", [row], Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Queue.ClearOverridesAsync(user, milling, "reason", [row with { ExpectedOverrideVersion = 1 }], Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Queue.ConfirmOverrideReviewAsync(user, milling, [row with { ExpectedOverrideVersion = 1 }], Token)).Status);
        }

        foreach (var user in new Principal?[] { w.Manager, w.Drawing, w.Central, w.Qc, w.Warehouse, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Queue.StopStageAsync(user, row, "problem", Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Designations.AssignAsync(user, w.Production.UserId, milling, Token)).Status);
        }

        // Production without a designation to the stage's unit cannot stop it.
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Queue.StopStageAsync(w.Production, row, "problem", Token)).Status);
        Assert.Null((await LoadAsync(w, w.Planning, milling)).Queue.Single().StopReason);
        Assert.Equal(0, Count(w, "QueueOverrides") + Count(w, "QueueOverrideHistory") + Count(w, "AuditLog WHERE ActionCode IN ('QueuePositionsSet','StageStopped')"));
    }

    [Fact]
    public async Task A_management_override_goes_above_planning_order_inside_its_category_persists_and_records_who_when_why_and_old_and_new_order()
    {
        using var w = await StartAsync();
        var milling = await AddUnitAsync(w, "MILLING");
        await MaterialAsync(w);
        await ItemAsync(w, "R-1", "A", [milling], Priority.VeryUrgent);
        await ItemAsync(w, "R-2", "B", [milling], Priority.Normal);
        await ItemAsync(w, "R-3", "C", [milling], Priority.Normal);
        var queue = await LoadAsync(w, w.Planning, milling);
        Assert.Equal(new[] { "A/1", "B/1", "C/1" }, queue.Queue.Select(Key).ToArray());
        var byItem = queue.Queue.ToDictionary(e => e.ItemNo);

        Assert.Equal(ValidationIssue.QueueReasonRequired, (await w.Queue.OverrideAsync(w.Manager, milling, "  ", [byItem["C"].ToRowRef()], Token)).Issue);
        Tick(w);
        var result = await w.Queue.OverrideAsync(w.Manager, milling, "مشتری کلیدی", [byItem["C"].ToRowRef(), byItem["B"].ToRowRef()], Token);

        Assert.True(result.Succeeded, result.Status + " " + result.Issue);
        var after = await LoadAsync(w, w.Manager, milling);
        Assert.Equal(new[] { "C/1", "B/1", "A/1" }, after.Queue.Select(Key).ToArray());
        Assert.Equal(new int?[] { 1, 2, null }, after.Queue.Select(e => e.OverrideRank).ToArray());
        Assert.Equal(Priority.VeryUrgent, after.Queue.Single(e => e.ItemNo == "A").Priority);
        Assert.Null(after.Queue.Single(e => e.ItemNo == "A").PlanningPosition);
        Assert.Equal("مشتری کلیدی", after.Queue[0].OverrideReason);

        var history = await w.Services.UnitQueueStore.ListOverrideHistoryAsync(milling, Token);
        Assert.Equal(2, history.Count);
        Assert.All(history, h => Assert.Equal(w.Manager.UserId, h.UserId));
        Assert.All(history, h => Assert.Equal("مشتری کلیدی", h.Reason));
        Assert.Single(history.Select(h => h.OperationId).Distinct());
        var c = history.Single(h => h.RequestItemId == byItem["C"].ItemId);
        Assert.Equal((null, 1, 3, 1), (c.OldRank, c.NewRank, c.OldOrder, c.NewOrder));
        Assert.Equal(2, Count(w, "AuditLog WHERE ActionCode = 'QueueOverrideSet'"));

        // Persisted: a restarted application sees the same overridden order.
        var restarted = WorkflowServices.Create(w.Fixture.Db.ContextFactory, w.Fixture.Services.Authorization, PersistenceTestData.Dates, w.Fixture.Clock);
        Assert.Equal(new[] { "C/1", "B/1", "A/1" }, (await restarted.UnitQueue.LoadAsync(w.Planning, milling, Token)).Value!.Queue.Select(Key).ToArray());

        // A second operation puts A first; C and B keep their relative order after it, each moved row is recorded.
        Tick(w);
        var again = (await LoadAsync(w, w.Manager, milling)).Queue.ToDictionary(e => e.ItemNo);
        Assert.True((await w.Queue.OverrideAsync(w.Manager, milling, "اولویت تولید", [again["A"].ToRowRef()], Token)).Succeeded);
        var third = await LoadAsync(w, w.Manager, milling);
        Assert.Equal(new[] { "A/1", "C/1", "B/1" }, third.Queue.Select(Key).ToArray());
        Assert.Equal(new int?[] { 1, 2, 3 }, third.Queue.Select(e => e.OverrideRank).ToArray());

        // Clearing returns a stage to Planning's order (A first by priority; then B, C by FIFO).
        Tick(w);
        var rows = third.Queue.Select(e => e.ToRowRef()).ToList();
        Assert.Equal(ValidationIssue.QueueRowsInvalid, (await w.Queue.ClearOverridesAsync(w.Manager, milling, "x", [byItem["B"].ToRowRef()], Token)).Issue);
        var cleared = await w.Queue.ClearOverridesAsync(w.Manager, milling, "انجام شد", rows, Token);
        Assert.True(cleared.Succeeded, cleared.Status + " " + cleared.Issue);
        var final = await LoadAsync(w, w.Manager, milling);
        Assert.Equal(new[] { "A/1", "B/1", "C/1" }, final.Queue.Select(Key).ToArray());
        Assert.All(final.Queue, e => Assert.Null(e.OverrideRank));
        Assert.Equal(3, Count(w, "AuditLog WHERE ActionCode = 'QueueOverrideCleared'"));
    }

    [Fact]
    public async Task A_stale_override_view_is_refused_and_a_failing_row_saves_nothing_of_the_operation()
    {
        using var w = await StartAsync();
        var milling = await AddUnitAsync(w, "MILLING");
        await MaterialAsync(w);
        await ItemAsync(w, "R-1", "A", [milling]);
        await ItemAsync(w, "R-2", "B", [milling]);
        var queue = await LoadAsync(w, w.Manager, milling);
        var byItem = queue.Queue.ToDictionary(e => e.ItemNo);
        var rowA = byItem["A"].ToRowRef();
        var rowB = byItem["B"].ToRowRef();

        // Another manager acts first on B.
        Assert.True((await w.Queue.OverrideAsync(w.Manager, milling, "اول", [rowB], Token)).Succeeded);

        // The first user still holds the old view of B (no override then): refused, and A is not overridden either.
        var stale = await w.Queue.OverrideAsync(w.Manager, milling, "دوم", [rowA, rowB], Token);
        Assert.Equal(OperationStatus.ConcurrencyConflict, stale.Status);
        Assert.Equal(1, Count(w, "QueueOverrides"));

        // A row whose stage was changed by someone else meanwhile is refused too.
        Assert.True((await w.Queue.SetPlanningOrderAsync(w.Planning, milling, [rowA], Token)).Succeeded);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Queue.OverrideAsync(w.Manager, milling, "سوم", [rowA], Token)).Status);
        Assert.Equal(1, Count(w, "QueueOverrides"));
    }

    [Fact]
    public async Task A_planning_priority_change_keeps_the_override_and_flags_it_for_management_review_until_management_confirms()
    {
        using var w = await StartAsync();
        var milling = await AddUnitAsync(w, "MILLING");
        await MaterialAsync(w);
        await ItemAsync(w, "R-1", "A", [milling], Priority.VeryUrgent);
        await ItemAsync(w, "R-2", "B", [milling], Priority.Normal);
        var byItem = (await LoadAsync(w, w.Manager, milling)).Queue.ToDictionary(e => e.ItemNo);
        Assert.True((await w.Queue.OverrideAsync(w.Manager, milling, "ضروری", [byItem["B"].ToRowRef()], Token)).Succeeded);
        var overridden = (await LoadAsync(w, w.Manager, milling)).Queue.ToDictionary(e => e.ItemNo);
        Assert.False(overridden["B"].OverrideNeedsReview);

        // Planning raises B's priority: the override stays in force, both facts are kept, and Management is asked to review.
        var changed = await w.Queue.ChangePriorityAsync(w.Planning, overridden["B"].RequestId, overridden["B"].ItemId, Priority.Urgent, overridden["B"].ItemVersion, Token);
        Assert.True(changed.Succeeded, changed.Status + " " + changed.Issue);
        Assert.Equal(1, changed.Value);
        var flagged = await LoadAsync(w, w.Manager, milling);
        var b = flagged.Queue.Single(e => e.ItemNo == "B");
        Assert.Equal(Priority.Urgent, b.Priority);
        Assert.Equal(1, b.OverrideRank);
        Assert.True(b.OverrideNeedsReview);
        Assert.Contains("Planning", b.OverrideReviewNote, StringComparison.Ordinal);
        Assert.Equal("B/1", Key(flagged.Queue[0]));
        Assert.Equal(1, flagged.OverridesNeedingReview);
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'QueueOverrideFlaggedForReview'"));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'ItemPriorityChanged'"));

        // A stale item version is refused.
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Queue.ChangePriorityAsync(w.Planning, b.RequestId, b.ItemId, Priority.Normal, overridden["B"].ItemVersion, Token)).Status);

        // Only Management confirms; the override stays active.
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Queue.ConfirmOverrideReviewAsync(w.Planning, milling, [b.ToRowRef()], Token)).Status);
        Assert.True((await w.Queue.ConfirmOverrideReviewAsync(w.Manager, milling, [b.ToRowRef()], Token)).Succeeded);
        var confirmed = (await LoadAsync(w, w.Manager, milling)).Queue.Single(e => e.ItemNo == "B");
        Assert.False(confirmed.OverrideNeedsReview);
        Assert.Equal(1, confirmed.OverrideRank);

        // Management's own priority change is an informed decision and flags nothing; an unchanged priority writes nothing.
        var again = await w.Queue.ChangePriorityAsync(w.Manager, confirmed.RequestId, confirmed.ItemId, Priority.VeryUrgent, confirmed.ItemVersion, Token);
        Assert.True(again.Succeeded);
        Assert.Equal(0, again.Value);
        Assert.False((await LoadAsync(w, w.Manager, milling)).Queue.Single(e => e.ItemNo == "B").OverrideNeedsReview);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Queue.ChangePriorityAsync(w.Production, confirmed.RequestId, confirmed.ItemId, Priority.Normal, confirmed.ItemVersion, Token)).Status);
    }

    [Fact]
    public async Task A_stopped_stage_is_visible_with_its_reason_cannot_proceed_and_returns_only_when_resumed()
    {
        using var w = await StartAsync();
        var milling = await AddUnitAsync(w, "MILLING");
        await MaterialAsync(w);
        var a = await ItemAsync(w, "R-1", "A", [milling], Priority.Normal);
        await ItemAsync(w, "R-2", "B", [milling], Priority.VeryUrgent);
        await w.Designations.AssignAsync(w.Planning, w.Production.UserId, milling);
        var row = (await LoadAsync(w, w.Planning, milling)).Queue.Single(e => e.ItemNo == "A").ToRowRef();

        Assert.Equal(ValidationIssue.QueueReasonRequired, (await w.Queue.StopStageAsync(w.Production, row, " ", Token)).Issue);
        var stopped = await w.Queue.StopStageAsync(w.Production, row, "خرابی دستگاه", Token);
        Assert.True(stopped.Succeeded, stopped.Status + " " + stopped.Issue);

        var queue = await LoadAsync(w, w.Planning, milling);
        var entry = queue.Queue.Single(e => e.ItemNo == "A");
        Assert.Equal(QueueCategory.Stopped, entry.Category);
        Assert.Equal(QueueBlock.StageStopped, entry.Block);
        Assert.Equal("خرابی دستگاه", entry.StopReason);
        Assert.False(entry.IsActionable);
        Assert.Equal(new[] { "A/1", "B/1" }, queue.Queue.Select(Key).ToArray()); // the approved order puts STOPPED before WAITING, so the problem is at the top

        // It cannot be started while stopped, and stopping twice is refused.
        var start = await w.Work.PerformAsync(w.Production, StageAction.Start, await TargetAsync(w, a.RequestId, a.ItemId, 1), Token);
        Assert.Equal(ValidationIssue.StageStopped, start.Issue);
        Assert.Equal(OperationStatus.InvalidState, (await w.Queue.StopStageAsync(w.Planning, entry.ToRowRef(), "دوباره", Token)).Status);

        // Resume: back to WAITING and actionable; an unrelated unit's Production user could not have done it.
        var resumed = await w.Queue.ResumeStageAsync(w.Planning, entry.ToRowRef(), Token);
        Assert.True(resumed.Succeeded, resumed.Status + " " + resumed.Issue);
        var back = (await LoadAsync(w, w.Planning, milling)).Queue.Single(e => e.ItemNo == "A");
        Assert.Equal(QueueCategory.Waiting, back.Category);
        Assert.True(back.IsActionable);
        Assert.Null(back.StopReason);
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'StageStopped'"));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'StageResumed'"));
    }

    [Fact]
    public async Task Stages_follow_the_gates_and_unit_designation_when_released_or_started()
    {
        using var w = await StartAsync();
        var milling = await AddUnitAsync(w, "MILLING");
        var turning = await AddUnitAsync(w, "TURNING");
        await MaterialAsync(w);
        var a = await ItemAsync(w, "R-1", "A", [milling]);
        var unreleased = await ItemAsync(w, "R-2", "B", [milling], release: false);
        await w.Designations.AssignAsync(w.Planning, w.Production.UserId, turning);

        // Production designated only to another unit cannot start work of this unit.
        var wrongUnit = await w.Work.PerformAsync(w.Production, StageAction.Start, await TargetAsync(w, a.RequestId, a.ItemId, 1), Token);
        Assert.Equal(ValidationIssue.StageUnitNotDesignated, wrongUnit.Issue);

        // The gate regresses (the request goes back to Drawing): the stage cannot be released or started, and the queue says why.
        var current = (await w.Services.Requests.GetAsync(w.Planning, a.RequestId, Token)).Value!;
        var back = await w.Services.Lifecycle.SendBackToDrawingAsync(w.Planning, a.RequestId, current.Request.Version, "نیاز به بازبینی", Token);
        Assert.True(back.Succeeded, back.Status + " " + back.Issue);
        await w.Designations.AssignAsync(w.Planning, w.Production.UserId, milling);
        Assert.Equal(ValidationIssue.StageGateNotSatisfied, (await w.Work.PerformAsync(w.Production, StageAction.Start, await TargetAsync(w, a.RequestId, a.ItemId, 1), Token)).Issue);

        var queue = await LoadAsync(w, w.Planning, milling);
        var entry = queue.Queue.Single(e => e.ItemNo == "A");
        Assert.Equal(QueueBlock.GateNotSatisfied, entry.Block);
        Assert.False(entry.IsActionable);
        Assert.Equal(QueueBlock.NotReleased, Assert.Single(queue.Pending).Block);
        Assert.Equal("B", Assert.Single(queue.Pending).ItemNo);

        // Planning can still release B (its request passed the gates), but not A's sibling after the gate regressed: send B's request back too and the release is refused.
        var other = (await w.Services.Requests.GetAsync(w.Planning, unreleased.RequestId, Token)).Value!;
        Assert.True((await w.Services.Lifecycle.SendBackToDrawingAsync(w.Planning, unreleased.RequestId, other.Request.Version, "نیاز به بازبینی", Token)).Succeeded);
        Assert.Equal(ValidationIssue.StageGateNotSatisfied, (await w.Work.PerformAsync(w.Planning, StageAction.Release, await TargetAsync(w, unreleased.RequestId, unreleased.ItemId, 1), Token)).Issue);
        var blocked = Assert.Single((await LoadAsync(w, w.Planning, milling)).Pending);
        Assert.Equal(QueueBlock.GateNotSatisfied, blocked.Block);
    }
}
