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

// Quantity-level QC: accepted / rework / scrap per base unit, the shortfall decision and the quantity carried to the next stage (Decision Log #92, #95 D2/D3, #101) on a real SQLite file with the real authorization.
public class QcInspectionTests
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

        public QcInspectionService Qci => Services.QcInspection;

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
    private static async Task<(long RequestId, long ItemId)> ItemAsync(World w, string requestNo, string itemNo, long[] stageUnits, Priority priority = Priority.Normal, bool release = true, bool pairs = false)
    {
        var (id, version) = await ReadyAsync(w, WorkType.Make, requestNo);
        var (piece0, pair0) = await TypesAsync(w);
        var piece = pairs ? pair0 : piece0;
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


    private static async Task<(long RequestId, long ItemId)> SubmittedAsync(World w, string requestNo = "Q-1", int stages = 2)
    {
        var milling = await AddUnitAsync(w, "MILLING");
        var turning = stages > 1 ? await AddUnitAsync(w, "TURNING") : milling;
        await MaterialAsync(w);
        var item = await ItemAsync(w, requestNo, "A", stages > 1 ? [milling, turning] : [milling], pairs: true);
        Assert.True((await w.Designations.AssignAsync(w.Planning, w.Production.UserId, milling, Token)).Succeeded);
        if (stages > 1)
        {
            Assert.True((await w.Designations.AssignAsync(w.Planning, w.Production.UserId, turning, Token)).Succeeded);
        }

        Assert.True((await w.Work.PerformAsync(w.Production, StageAction.Start, await TargetAsync(w, item.RequestId, item.ItemId, 1), Token)).Succeeded);
        Assert.True((await w.Work.PerformAsync(w.Production, StageAction.SubmitForQc, await TargetAsync(w, item.RequestId, item.ItemId, 1), Token)).Succeeded);
        return item;
    }

    private static InspectionEntry Accept(long units) => new(InspectionDisposition.Accepted, units, null);

    private static InspectionEntry Rework(long units) => new(InspectionDisposition.Rework, units, "ابعاد خارج از تلرانس");

    private static InspectionEntry Scrap(long units) => new(InspectionDisposition.Scrap, units, "ترک");

    private static async Task<ItemStage> StageAsync(World w, long requestId, int stageNo) =>
        (await w.Services.Requests.GetAsync(w.Qc, requestId, Token)).Value!.Request.Value.Items.Single().Stages.Single(s => s.StageNo == stageNo);

    [Fact]
    public async Task A_pair_item_is_inspected_in_base_units_and_the_item_keeps_the_quantity_types_base_units()
    {
        using var w = await StartAsync();
        var item = await SubmittedAsync(w);

        var loaded = (await w.Services.Requests.GetAsync(w.Qc, item.RequestId, Token)).Value!.Request.Value.Items.Single();
        Assert.Equal(2, loaded.BaseUnitsPerUnit);
        var stage = loaded.Stages[0];
        Assert.Equal((20L, 20L, 0L), (stage.InputBaseUnits, stage.AwaitingQcBaseUnits, stage.AcceptedBaseUnits)); // 10 pairs = 20 pieces
        Assert.Equal(ItemStageStatus.WaitingQc, stage.Status);
    }

    [Fact]
    public async Task Qc_alone_records_decisions_and_planning_management_and_qc_alone_read_the_worklist()
    {
        using var w = await StartAsync();
        var item = await SubmittedAsync(w);
        var target = await TargetAsync(w, item.RequestId, item.ItemId, 1);

        foreach (var user in new Principal?[] { w.Planning, w.Manager, w.Production, w.Drawing, w.Central, w.Warehouse, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Qci.RecordInspectionAsync(user, target, [Accept(20)], Token)).Status);
        }

        foreach (var user in new Principal?[] { w.Drawing, w.Central, w.Warehouse, w.Production, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Qci.LoadAsync(user, Token)).Status);
        }

        foreach (var user in new[] { w.Qc, w.Planning, w.Manager })
        {
            var worklist = (await w.Qci.LoadAsync(user, Token)).Value!;
            var entry = Assert.Single(worklist.Entries);
            Assert.Equal((20L, 0L), (entry.AwaitingQcBaseUnits, entry.AcceptedBaseUnits));
        }

        Assert.True((await w.Qci.LoadAsync(w.Qc, Token)).Value!.Capabilities.CanInspect);
        Assert.False((await w.Qci.LoadAsync(w.Planning, Token)).Value!.Capabilities.CanInspect);
        Assert.Equal(0, Count(w, "QcInspectionLines"));
    }

    [Fact]
    public async Task Accepting_everything_releases_the_next_stage_with_exactly_the_accepted_base_units_and_audits_it()
    {
        using var w = await StartAsync();
        var item = await SubmittedAsync(w);

        var result = await w.Qci.RecordInspectionAsync(w.Qc, await TargetAsync(w, item.RequestId, item.ItemId, 1), [Accept(20)], Token);

        Assert.True(result.Succeeded, result.Status + " " + result.Issue);
        Assert.True(result.Value!.Completed);
        Assert.Equal(ItemStageStatus.QcApproved, (await StageAsync(w, item.RequestId, 1)).Status);
        var next = await StageAsync(w, item.RequestId, 2);
        Assert.Equal((ItemStageStatus.Waiting, 20L), (next.Status, next.InputBaseUnits));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'QcInspectionRecorded'"));
        Assert.Equal(1, Count(w, "QcInspectionLines"));
        Assert.Equal(10m, (await w.Services.Requests.GetAsync(w.Qc, item.RequestId, Token)).Value!.Request.Value.Items.Single().QcApprovals.Single().ApprovedQuantity);
    }

    [Fact]
    public async Task Rework_scrap_and_the_shortfall_decision_follow_the_rules_and_the_next_stage_receives_only_what_was_accepted()
    {
        using var w = await StartAsync();
        var item = await SubmittedAsync(w);

        // 12 accepted, 5 to rework, 3 scrap: the stage goes back to production for the 5 reworked units.
        var first = await w.Qci.RecordInspectionAsync(w.Qc, await TargetAsync(w, item.RequestId, item.ItemId, 1), [Accept(12), Rework(5), Scrap(3)], Token);
        Assert.True(first.Succeeded, first.Status + " " + first.Issue);
        Assert.Equal(ItemStageStatus.InProgress, first.Value!.NewStatus);
        var stage = await StageAsync(w, item.RequestId, 1);
        Assert.Equal((12L, 5L, 3L, 0L), (stage.AcceptedBaseUnits, stage.ReworkBaseUnits, stage.ScrapBaseUnits, stage.AwaitingQcBaseUnits));
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, (await StageAsync(w, item.RequestId, 2)).Status);

        // The worklist and the unit queue show the rework; production resubmits only the 5 units.
        Assert.Equal(5, (await w.Qci.LoadAsync(w.Qc, Token)).Value!.Entries.Single().ReworkBaseUnits);
        Assert.Equal(5, (await w.Queue.LoadAsync(w.Planning, UnitId(w, "MILLING"), Token)).Value!.Queue.Single().ReworkBaseUnits);
        Assert.True((await w.Work.PerformAsync(w.Production, StageAction.SubmitForQc, await TargetAsync(w, item.RequestId, item.ItemId, 1), Token)).Succeeded);
        Assert.Equal(5, (await StageAsync(w, item.RequestId, 1)).AwaitingQcBaseUnits);

        // Cannot decide more than is awaiting.
        Assert.Equal(ValidationIssue.InspectionExceedsAwaiting, (await w.Qci.RecordInspectionAsync(w.Qc, await TargetAsync(w, item.RequestId, item.ItemId, 1), [Accept(6)], Token)).Issue);

        // The reworked units pass: scrap remains, so the stage waits for the shortfall decision.
        var second = await w.Qci.RecordInspectionAsync(w.Qc, await TargetAsync(w, item.RequestId, item.ItemId, 1), [Accept(5)], Token);
        Assert.Equal(ItemStageStatus.WaitingShortfallDecision, second.Value!.NewStatus);
        Assert.Equal(17, (await StageAsync(w, item.RequestId, 1)).AcceptedBaseUnits);
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, (await StageAsync(w, item.RequestId, 2)).Status);
        var turning = await w.Queue.LoadAsync(w.Planning, UnitId(w, "TURNING"), Token);
        Assert.Equal(QueueBlock.WaitingPreviousQc, Assert.Single(turning.Value!.Pending).Block);

        // Each decision has its own permission: Management accepts, Planning records a replacement order.
        var target = await TargetAsync(w, item.RequestId, item.ItemId, 1);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Qci.DecideShortfallAsync(w.Planning, target, ShortfallKind.ManagementAccepted, "x", Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Qci.DecideShortfallAsync(w.Manager, target, ShortfallKind.ReplacementOrdered, "x", Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Qci.DecideShortfallAsync(w.Qc, target, ShortfallKind.ManagementAccepted, "x", Token)).Status);
        Assert.Equal(ValidationIssue.ShortfallNoteRequired, (await w.Qci.DecideShortfallAsync(w.Manager, target, ShortfallKind.ManagementAccepted, " ", Token)).Issue);
        Assert.Equal(ItemStageStatus.WaitingShortfallDecision, (await StageAsync(w, item.RequestId, 1)).Status);

        var decided = await w.Qci.DecideShortfallAsync(w.Manager, target, ShortfallKind.ManagementAccepted, "مشتری کسری ۳ قطعه را پذیرفت", Token);
        Assert.True(decided.Succeeded, decided.Status + " " + decided.Issue);
        Assert.Equal(3, decided.Value!.ShortfallBaseUnits);
        Assert.Equal(ItemStageStatus.QcApproved, (await StageAsync(w, item.RequestId, 1)).Status);
        var next = await StageAsync(w, item.RequestId, 2);
        Assert.Equal((ItemStageStatus.Waiting, 17L), (next.Status, next.InputBaseUnits));

        // History is kept: every line and the decision, with who decided.
        var history = (await w.Qci.HistoryAsync(w.Planning, item.ItemId, 1, Token)).Value!;
        Assert.Equal(new[] { 12L, 5L, 3L, 5L }, history.Lines.Select(l => l.BaseUnits).ToArray());
        Assert.All(history.Lines, l => Assert.Equal(w.Qc.UserId, l.DecidedByUserId));
        Assert.Equal(w.Manager.UserId, Assert.Single(history.Decisions).DecidedByUserId);
        Assert.Equal(2, Count(w, "AuditLog WHERE ActionCode = 'QcInspectionRecorded'"));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'ShortfallDecided'"));
        Assert.Equal(8.5m, (await w.Services.Requests.GetAsync(w.Qc, item.RequestId, Token)).Value!.Request.Value.Items.Single().QcApprovals.Single().ApprovedQuantity); // 17 base units = 8.5 pairs
    }

    [Fact]
    public async Task Invalid_decisions_stale_views_and_the_whole_quantity_shortcut_are_refused_without_saving_anything()
    {
        using var w = await StartAsync();
        var item = await SubmittedAsync(w);
        var target = await TargetAsync(w, item.RequestId, item.ItemId, 1);

        Assert.Equal(ValidationIssue.InspectionEntriesInvalid, (await w.Qci.RecordInspectionAsync(w.Qc, target, [], Token)).Issue);
        Assert.Equal(ValidationIssue.InspectionEntriesInvalid, (await w.Qci.RecordInspectionAsync(w.Qc, target, [Accept(0)], Token)).Issue);
        Assert.Equal(ValidationIssue.InspectionEntriesInvalid, (await w.Qci.RecordInspectionAsync(w.Qc, target, [new InspectionEntry(InspectionDisposition.Scrap, 1, " ")], Token)).Issue);
        Assert.Equal(ValidationIssue.InspectionExceedsAwaiting, (await w.Qci.RecordInspectionAsync(w.Qc, target, [Accept(15), Scrap(6)], Token)).Issue);
        Assert.Equal(ValidationIssue.VersionsMissing, (await w.Qci.RecordInspectionAsync(w.Qc, target with { StageVersions = new Dictionary<int, long>() }, [Accept(1)], Token)).Issue);

        // The demonstration's whole-quantity approval is not available for a stage planned in a unit.
        Assert.Equal(ValidationIssue.UseQuantityInspection, (await w.Work.PerformAsync(w.Qc, StageAction.ApproveWholeQuantity, target, Token)).Issue);

        // A second QC user working from the same stale view is refused.
        Assert.True((await w.Qci.RecordInspectionAsync(w.Qc, target, [Accept(10)], Token)).Succeeded);
        var stale = await w.Qci.RecordInspectionAsync(w.Qc, target, [Accept(10)], Token);
        Assert.Equal(OperationStatus.ConcurrencyConflict, stale.Status);
        var stage = await StageAsync(w, item.RequestId, 1);
        Assert.Equal((10L, 10L), (stage.AcceptedBaseUnits, stage.AwaitingQcBaseUnits));
        Assert.Equal(1, Count(w, "QcInspectionLines"));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'QcInspectionRecorded'"));

        // A shortfall decision needs a stage that is waiting for one.
        var current = await TargetAsync(w, item.RequestId, item.ItemId, 1);
        Assert.Equal(OperationStatus.InvalidState, (await w.Qci.DecideShortfallAsync(w.Manager, current, ShortfallKind.ManagementAccepted, "no shortfall exists", Token)).Status);
    }
}
