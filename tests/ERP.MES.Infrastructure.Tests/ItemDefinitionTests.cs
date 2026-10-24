using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// Item and stage definition by Planning after the gates (Decision Log #86, #95) on a real SQLite file with the real authorization.
public class ItemDefinitionTests
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

    private static DefineItemCommand Make(long typeId, long materialId, string itemNo = "1.10", Priority priority = Priority.Normal) =>
        new(itemNo, "Ring A", 10m, priority, typeId, materialId, 1.2345m, 6000m, 30m);

    private static DefineItemCommand Repair(string itemNo = "2.10", Priority priority = Priority.Normal) => new(itemNo, "Shaft B", 4m, priority, null, null, null, null, null);

    private static long Count(World w, string tableAndWhere) => long.Parse(w.Fixture.Db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Planning_defines_a_make_item_with_its_specification_after_the_gates_and_it_is_stored_audited_and_survives_a_restart()
    {
        using var w = await StartAsync();
        var (id, version) = await ReadyAsync(w);
        var (piece, _) = await TypesAsync(w);
        var material = await MaterialAsync(w);

        var outcome = await w.Items.DefineItemAsync(w.Planning, id, version, Make(piece, material, priority: Priority.Urgent), Token);

        Assert.True(outcome.Succeeded, outcome.Status + " " + outcome.Issue);
        var item = (await WorkflowServices.Create(w.Fixture.Db.ContextFactory, w.Fixture.Services.Authorization, PersistenceTestData.Dates, w.Fixture.Clock).Requests.GetAsync(w.Qc, id, Token)).Value!.Request.Value.Items.Single();
        Assert.Equal("1.10", item.ItemNo);
        Assert.Equal(10m, item.RequestedQuantity);
        Assert.Equal(piece, item.QuantityTypeId);
        Assert.Equal(material, item.MaterialId);
        Assert.Equal(1.2345m, item.WeightKgPerBaseUnit);
        Assert.Equal(6000m, item.LengthMm);
        Assert.Equal(30m, item.DiameterMm);
        Assert.Equal(Priority.Urgent, item.Priority);
        Assert.Equal(w.Planning.UserId, item.CreatedByUserId);
        Assert.Equal("ItemDefined", Assert.Single(w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode = 'ItemDefined'")));
    }

    [Fact]
    public async Task A_repair_item_needs_no_specification_and_no_contract_gate()
    {
        using var w = await StartAsync();
        var (id, version) = await ReadyAsync(w, WorkType.Repair);

        var bare = await w.Items.DefineItemAsync(w.Planning, id, version, Repair("R-1"), Token);
        Assert.True(bare.Succeeded, bare.Status + " " + bare.Issue);

        var (piece, _) = await TypesAsync(w);
        var withSpec = await w.Items.DefineItemAsync(w.Planning, id, bare.Value!.Version, Repair("R-2") with { QuantityTypeId = piece, WeightKgPerBaseUnit = 0.5m }, Token);
        Assert.True(withSpec.Succeeded);
        var items = (await LoadAsync(w, id)).Items;
        Assert.Null(items[0].QuantityTypeId);
        Assert.Equal(piece, items[1].QuantityTypeId);
    }

    [Fact]
    public async Task Drawing_and_everyone_else_cannot_define_items_or_stages_only_planning()
    {
        using var w = await StartAsync();
        var (id, version) = await ReadyAsync(w);
        var (piece, _) = await TypesAsync(w);
        var material = await MaterialAsync(w);
        var repair = await ReadyAsync(w, WorkType.Repair, "D-2");

        foreach (var user in new Principal?[] { w.Drawing, w.Central, w.Manager, w.ManagementStaff, w.Warehouse, w.Qc, w.Production, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Items.DefineItemAsync(user, id, version, Make(piece, material, "X"), Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Items.DefineItemAsync(user, repair.Id, repair.Version, Repair("X"), Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Items.DefineStageAsync(user, id, version, 1, 1, new DefineStageCommand("t", 1m, 1, null), Token)).Status);
            Assert.False((await w.Items.GetCapabilitiesAsync(user, Token)).CanDefine);
        }

        Assert.Equal(new ItemDefinitionCapabilities(true, true), await w.Items.GetCapabilitiesAsync(w.Planning, Token));
        Assert.Empty((await LoadAsync(w, id)).Items);
        Assert.Equal(0, Count(w, "RequestItems"));
    }

    [Fact]
    public async Task The_gates_block_definition_until_they_hold()
    {
        using var w = await StartAsync();
        var (piece, _) = await TypesAsync(w);
        var material = await MaterialAsync(w);

        // Not returned from Drawing yet.
        var customer = (await w.Services.Customers.CreateAsync(w.Central, "Fictional Customer", Token)).Value!;
        var created = (await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "G-0", WorkType.Make, "1405/07/20", null, null), Token)).Value!;
        Assert.Equal(ValidationIssue.ItemGateNotSatisfied, (await w.Items.DefineItemAsync(w.Planning, created.Value.Id, created.Version, Make(piece, material), Token)).Issue);

        // Returned, but the MAKE contract decision is missing.
        var id = await ReturnedAsync(w, WorkType.Make, DrawingOutcome.FullyAccepted, "G-1");
        Assert.Equal(ValidationIssue.ItemGateNotSatisfied, (await w.Items.DefineItemAsync(w.Planning, id, await VersionAsync(w, id), Make(piece, material), Token)).Issue);

        // A required contract that is only a draft or pending still blocks.
        var draft = (await w.Contracts.DecideRequirementAsync(w.Planning, id, ContractRequirement.Required, null, Token)).Value!;
        Assert.Equal(ValidationIssue.ItemGateNotSatisfied, (await w.Items.DefineItemAsync(w.Planning, id, await VersionAsync(w, id), Make(piece, material), Token)).Issue);
        Tick(w);
        var named = (await w.Contracts.SaveDraftAsync(w.Planning, id, draft.Version, "CN-1", "1405/07/25", null, null, Token)).Value!;
        var pdf = (await w.Contracts.AttachPdfAsync(w.Planning, id, named.Version, "c.pdf", Pdf(), Token)).Value!;
        var pending = (await w.Contracts.SubmitAsync(w.Planning, id, pdf.Version, Token)).Value!;
        Assert.Equal(ValidationIssue.ItemGateNotSatisfied, (await w.Items.DefineItemAsync(w.Planning, id, await VersionAsync(w, id), Make(piece, material), Token)).Issue);

        // Approved: now it passes.
        Assert.True((await w.Contracts.ApproveAsync(w.Manager, id, pending.Version, null, Token)).Succeeded);
        var done = await w.Items.DefineItemAsync(w.Planning, id, await VersionAsync(w, id), Make(piece, material), Token);
        Assert.True(done.Succeeded, done.Status + " " + done.Issue);
        Assert.Equal(1, Count(w, "RequestItems"));
    }

    [Fact]
    public async Task A_drawing_result_that_is_not_accepted_blocks_definition_also_for_repair()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w, WorkType.Repair, DrawingOutcome.Rejected, "G-2");

        Assert.Equal(ValidationIssue.ItemGateNotSatisfied, (await w.Items.DefineItemAsync(w.Planning, id, await VersionAsync(w, id), Repair(), Token)).Issue);
        Assert.Equal(0, Count(w, "RequestItems"));
    }

    [Fact]
    public async Task Open_inquiry_lines_a_no_without_disposition_and_an_open_question_block_definition_until_resolved()
    {
        using var w = await StartAsync();
        var (id, _) = await ReadyAsync(w);
        var (piece, _) = await TypesAsync(w);
        var material = await MaterialAsync(w);
        var kg = w.Fixture.Db.ContextFactory.CreateDbContext().UnitsOfMeasure.First(u => u.Code == "KG").Id;
        var draft = (await w.Services.Inquiries.CreateAsync(w.Drawing, id, null, Token)).Value!;
        Assert.True(draft.Value.Status == InquiryStatus.Draft);
        var line = (await w.Services.Inquiries.AddLineAsync(w.Drawing, draft.Value.Id, draft.Version, new InquiryLineInput("ST37", "میلگرد", 30m, 6000m, null, 5m, kg, null, null), Token)).Value!;
        var sent = (await w.Services.Inquiries.SendAsync(w.Drawing, draft.Value.Id, line.Version, Token)).Value!;

        // Sent and unanswered.
        Assert.Equal(ValidationIssue.ItemGateNotSatisfied, (await w.Items.DefineItemAsync(w.Planning, id, await VersionAsync(w, id), Make(piece, material), Token)).Issue);

        // Answered No, no disposition yet.
        var answered = (await w.Services.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, sent.Version, 1, InquiryAnswer.No, "نیست", Token)).Value!;
        Assert.Equal(ValidationIssue.ItemGateNotSatisfied, (await w.Items.DefineItemAsync(w.Planning, id, await VersionAsync(w, id), Make(piece, material), Token)).Issue);

        // Dispositioned: passes.
        Assert.True((await w.Services.Inquiries.RecordDispositionAsync(w.Drawing, sent.Value.Id, answered.Version, 1, DispositionKind.ReturnedUnchanged, "قابل قبول", null, Token)).Succeeded);
        var defined = await w.Items.DefineItemAsync(w.Planning, id, await VersionAsync(w, id), Make(piece, material, "OK"), Token);
        Assert.True(defined.Succeeded, defined.Status + " " + defined.Issue);
    }

    [Fact]
    public async Task A_make_item_needs_type_material_and_weight_and_values_are_validated_exactly()
    {
        using var w = await StartAsync();
        var (id, version) = await ReadyAsync(w);
        var (piece, _) = await TypesAsync(w);
        var material = await MaterialAsync(w);

        Assert.Equal(ValidationIssue.ItemSpecificationIncomplete, (await w.Items.DefineItemAsync(w.Planning, id, version, Make(piece, material) with { QuantityTypeId = null }, Token)).Issue);
        Assert.Equal(ValidationIssue.ItemSpecificationIncomplete, (await w.Items.DefineItemAsync(w.Planning, id, version, Make(piece, material) with { MaterialId = null }, Token)).Issue);
        Assert.Equal(ValidationIssue.ItemSpecificationIncomplete, (await w.Items.DefineItemAsync(w.Planning, id, version, Make(piece, material) with { WeightKgPerBaseUnit = null }, Token)).Issue);
        Assert.Equal(ValidationIssue.WeightInvalid, (await w.Items.DefineItemAsync(w.Planning, id, version, Make(piece, material) with { WeightKgPerBaseUnit = 0m }, Token)).Issue);
        Assert.Equal(ValidationIssue.WeightInvalid, (await w.Items.DefineItemAsync(w.Planning, id, version, Make(piece, material) with { WeightKgPerBaseUnit = 1.23456m }, Token)).Issue);
        Assert.Equal(ValidationIssue.WeightInvalid, (await w.Items.DefineItemAsync(w.Planning, id, version, Make(piece, material) with { WeightKgPerBaseUnit = 100_000_000m }, Token)).Issue);
        Assert.Equal(ValidationIssue.DimensionInvalid, (await w.Items.DefineItemAsync(w.Planning, id, version, Make(piece, material) with { LengthMm = -1m }, Token)).Issue);
        Assert.Equal(ValidationIssue.DimensionInvalid, (await w.Items.DefineItemAsync(w.Planning, id, version, Make(piece, material) with { DiameterMm = 30.123m }, Token)).Issue);
        Assert.Equal(0, Count(w, "RequestItems"));

        // Exactly at the limits is accepted and kept without rounding.
        var edge = await w.Items.DefineItemAsync(w.Planning, id, version, Make(piece, material) with { WeightKgPerBaseUnit = 99_999_999.9999m, LengthMm = 99_999_999.99m, DiameterMm = 0.01m }, Token);
        Assert.True(edge.Succeeded, edge.Status + " " + edge.Issue);
        var stored = (await LoadAsync(w, id)).Items.Single();
        Assert.Equal(99_999_999.9999m, stored.WeightKgPerBaseUnit);
        Assert.Equal(0.01m, stored.DiameterMm);
    }

    [Fact]
    public async Task The_quantity_type_and_material_must_exist_and_be_active_and_come_from_the_masters()
    {
        using var w = await StartAsync();
        var (id, version) = await ReadyAsync(w);
        var (piece, pair) = await TypesAsync(w);
        var material = await MaterialAsync(w);

        Assert.Equal(ValidationIssue.QuantityTypeInvalid, (await w.Items.DefineItemAsync(w.Planning, id, version, Make(987654, material), Token)).Issue);
        Assert.Equal(ValidationIssue.MaterialInvalid, (await w.Items.DefineItemAsync(w.Planning, id, version, Make(piece, 987654), Token)).Issue);

        var types = (await w.Services.QcMaster.LoadAsync(w.Qc, Token)).Value!.QuantityTypes.First(t => t.Value.Id == pair);
        Assert.True((await w.Services.QcMaster.SetQuantityTypeActiveAsync(w.Qc, pair, types.Version, false, Token)).Succeeded);
        Assert.Equal(ValidationIssue.QuantityTypeInvalid, (await w.Items.DefineItemAsync(w.Planning, id, version, Make(pair, material), Token)).Issue);

        var m = (await w.Services.Materials.LoadAsync(w.Warehouse, Token)).Value!.Materials.Single();
        Assert.True((await w.Services.Materials.SetMaterialActiveAsync(w.Warehouse, material, m.Version, false, Token)).Succeeded);
        Assert.Equal(ValidationIssue.MaterialInvalid, (await w.Items.DefineItemAsync(w.Planning, id, version, Make(piece, material), Token)).Issue);
        Assert.Equal(0, Count(w, "RequestItems"));
    }

    [Theory]
    [InlineData(null, "Ring", 1, ValidationIssue.ItemNoRequired)]
    [InlineData("  ", "Ring", 1, ValidationIssue.ItemNoRequired)]
    [InlineData("1.10", null, 1, ValidationIssue.PartNameRequired)]
    [InlineData("1.10", " ", 1, ValidationIssue.PartNameRequired)]
    [InlineData("1.10", "Ring", 0, ValidationIssue.QuantityInvalid)]
    [InlineData("1.10", "Ring", -2, ValidationIssue.QuantityInvalid)]
    public async Task An_invalid_item_is_refused_with_the_reason_and_nothing_is_saved(string? itemNo, string? partName, int quantity, ValidationIssue expected)
    {
        using var w = await StartAsync();
        var (id, version) = await ReadyAsync(w, WorkType.Repair);

        var outcome = await w.Items.DefineItemAsync(w.Planning, id, version, new DefineItemCommand(itemNo, partName, quantity, Priority.Normal, null, null, null, null, null), Token);

        Assert.Equal(OperationStatus.ValidationFailed, outcome.Status);
        Assert.Equal(expected, outcome.Issue);
        Assert.Equal(0, Count(w, "RequestItems"));
    }

    [Fact]
    public async Task Item_numbers_are_free_text_unique_per_request_and_compared_exactly()
    {
        using var w = await StartAsync();
        var first = await ReadyAsync(w, WorkType.Repair, "P-1");
        var second = await ReadyAsync(w, WorkType.Repair, "P-2");

        var one = await w.Items.DefineItemAsync(w.Planning, first.Id, first.Version, Repair("A-1") with { Quantity = 2.0001m }, Token);
        var duplicate = await w.Items.DefineItemAsync(w.Planning, first.Id, one.Value!.Version, Repair("A-1"), Token);
        var otherCase = await w.Items.DefineItemAsync(w.Planning, first.Id, one.Value.Version, Repair("a-1"), Token);
        var freeText = await w.Items.DefineItemAsync(w.Planning, first.Id, otherCase.Value!.Version, Repair("شفت ۱/الف #7"), Token);
        var otherRequest = await w.Items.DefineItemAsync(w.Planning, second.Id, second.Version, Repair("A-1"), Token);

        Assert.Equal(ValidationIssue.ItemNoDuplicate, duplicate.Issue);
        Assert.True(otherCase.Succeeded);
        Assert.True(freeText.Succeeded);
        Assert.True(otherRequest.Succeeded);
        var stored = (await LoadAsync(w, first.Id)).Items;
        Assert.Equal(new[] { "A-1", "a-1", "شفت ۱/الف #7" }, stored.Select(i => i.ItemNo).ToArray());
        Assert.Equal(2.0001m, stored[0].RequestedQuantity);
    }

    [Fact]
    public async Task A_priority_other_than_normal_is_allowed_for_planning_and_an_unknown_one_is_refused()
    {
        using var w = await StartAsync();
        var (id, version) = await ReadyAsync(w, WorkType.Repair);

        var urgent = await w.Items.DefineItemAsync(w.Planning, id, version, Repair("U-1", Priority.VeryUrgent), Token);
        var unknown = await w.Items.DefineItemAsync(w.Planning, id, urgent.Value!.Version, Repair("U-2", (Priority)99), Token);

        Assert.True(urgent.Succeeded);
        Assert.Equal(ValidationIssue.PriorityInvalid, unknown.Issue);
        Assert.Equal(Priority.VeryUrgent, (await LoadAsync(w, id)).Items.Single().Priority);
    }

    [Fact]
    public async Task A_cancelled_request_and_an_unknown_request_accept_no_items()
    {
        using var w = await StartAsync();
        var (id, version) = await ReadyAsync(w, WorkType.Repair);
        w.Fixture.Db.Execute("UPDATE Requests SET Status = 7 WHERE Id = " + id);

        // Cancelled is also not "returned from Drawing", so the gate refuses first; the closed-request rule is still the Domain's.
        var outcome = await w.Items.DefineItemAsync(w.Planning, id, version, Repair(), Token);

        Assert.False(outcome.Succeeded);
        Assert.Equal(ValidationIssue.RequestClosedForItems, outcome.Issue);
        Assert.Equal(OperationStatus.NotFound, (await w.Items.DefineItemAsync(w.Planning, 4242, 1, Repair(), Token)).Status);
        Assert.Equal(0, Count(w, "RequestItems"));
    }

    [Fact]
    public async Task Two_planners_working_from_the_same_view_cannot_overwrite_each_other()
    {
        using var w = await StartAsync();
        var (id, version) = await ReadyAsync(w, WorkType.Repair);
        var second = await UserAsync(w.Fixture, w.Admin, "planning.two", DepartmentCodes.Planning, RoleCodes.Staff);

        var first = await w.Items.DefineItemAsync(w.Planning, id, version, Repair("A"), Token);
        var stale = await w.Items.DefineItemAsync(second, id, version, Repair("B"), Token);

        Assert.True(first.Succeeded);
        Assert.Equal(OperationStatus.ConcurrencyConflict, stale.Status);
        Assert.Equal(new[] { "A" }, (await LoadAsync(w, id)).Items.Select(i => i.ItemNo).ToArray());
        Assert.True((await w.Items.DefineItemAsync(second, id, first.Value!.Version, Repair("B"), Token)).Succeeded);
    }

    [Fact]
    public async Task A_stale_principal_is_refused_after_the_department_or_activity_changed()
    {
        using var w = await StartAsync();
        var (id, version) = await ReadyAsync(w, WorkType.Repair);
        var ok = await w.Items.DefineItemAsync(w.Planning, id, version, Repair("A"), Token);
        Assert.True(ok.Succeeded);

        await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Planning.UserId, false, Token);

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Items.DefineItemAsync(w.Planning, id, ok.Value!.Version, Repair("B"), Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Items.DefineStageAsync(w.Planning, id, ok.Value.Version, 1, 1, new DefineStageCommand("t", 1m, 1, null), Token)).Status);
        Assert.Single((await LoadAsync(w, id)).Items);
    }

    [Fact]
    public async Task Stages_are_planned_in_an_active_unit_and_a_machine_of_that_unit_and_the_gate_applies()
    {
        using var w = await StartAsync();
        var (id, version) = await ReadyAsync(w, WorkType.Repair);
        var item = await w.Items.DefineItemAsync(w.Planning, id, version, Repair("A"), Token);
        var itemId = item.Value!.Value.Items.Single().Id;
        var unit = (await w.Services.PlanningMaster.AddUnitAsync(w.Planning, "MILLING", "فرزکاری", Token)).Value!;
        var other = (await w.Services.PlanningMaster.AddUnitAsync(w.Planning, "TURNING", "تراشکاری", Token)).Value!;
        var machine = (await w.Services.PlanningMaster.AddMachineAsync(w.Planning, "FCC-06", "فرز CNC", unit.Value.Id, Token)).Value!;

        async Task<Outcome<Stored<Request>>> Define(DefineStageCommand command)
        {
            var current = (await w.Services.Requests.GetAsync(w.Planning, id, Token)).Value!;
            return await w.Items.DefineStageAsync(w.Planning, id, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), command, Token);
        }

        Assert.Equal(ValidationIssue.StageTitleRequired, (await Define(new(" ", 5m, unit.Value.Id, null))).Issue);
        Assert.Equal(ValidationIssue.StageMinutesInvalid, (await Define(new("فرزکاری", 0m, unit.Value.Id, null))).Issue);
        Assert.Equal(ValidationIssue.ProductionUnitInvalid, (await Define(new("فرزکاری", 5m, 987654, null))).Issue);
        Assert.Equal(ValidationIssue.MachineInvalid, (await Define(new("فرزکاری", 5m, unit.Value.Id, 987654))).Issue);
        Assert.Equal(ValidationIssue.MachineUnitMismatch, (await Define(new("فرزکاری", 5m, other.Value.Id, machine.Value.Id))).Issue);
        Assert.Equal(ValidationIssue.ItemNotFound, (await w.Items.DefineStageAsync(w.Planning, id, await VersionAsync(w, id), 987654, 1, new DefineStageCommand("t", 1m, unit.Value.Id, null), Token)).Issue);
        Assert.Equal(0, Count(w, "ItemStages"));

        var first = await Define(new("فرزکاری", 5.5m, unit.Value.Id, machine.Value.Id));
        Assert.True(first.Succeeded, first.Status + " " + first.Issue);
        var second = await Define(new("تراشکاری", 3m, other.Value.Id, null));
        Assert.True(second.Succeeded);

        var stages = (await LoadAsync(w, id)).Items.Single().Stages;
        Assert.Equal(new[] { 1, 2 }, stages.Select(s => s.StageNo).ToArray());
        Assert.Equal(new long?[] { unit.Value.Id, other.Value.Id }, stages.Select(s => s.ProductionUnitId).ToArray());
        Assert.Equal(machine.Value.Id, stages[0].MachineId);
        Assert.Null(stages[1].MachineId);
        Assert.Equal(new[] { ItemStageStatus.Planned, ItemStageStatus.WaitingPreviousQc }, stages.Select(s => s.Status).ToArray());
        Assert.Equal(2, Count(w, "AuditLog WHERE ActionCode = 'ItemStageDefined'"));

        // A stale item version conflicts; a retired unit is refused.
        var current2 = (await w.Services.Requests.GetAsync(w.Planning, id, Token)).Value!;
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Items.DefineStageAsync(w.Planning, id, current2.Request.Version, itemId, current2.Versions.ItemVersion(itemId) + 3, new DefineStageCommand("x", 1m, unit.Value.Id, null), Token)).Status);
        var retired = (await w.Services.PlanningMaster.SetUnitActiveAsync(w.Planning, other.Value.Id, other.Version, false, Token)).Value!;
        Assert.Equal(ValidationIssue.ProductionUnitInvalid, (await Define(new("x", 1m, retired.Value.Id, null))).Issue);
    }

    [Fact]
    public async Task Stages_cannot_be_added_once_the_first_stage_was_released_or_while_a_gate_blocks()
    {
        using var w = await StartAsync();
        var (id, version) = await ReadyAsync(w, WorkType.Repair);
        var item = await w.Items.DefineItemAsync(w.Planning, id, version, Repair("A"), Token);
        var itemId = item.Value!.Value.Items.Single().Id;
        var unit = (await w.Services.PlanningMaster.AddUnitAsync(w.Planning, "MILLING", "فرزکاری", Token)).Value!;

        async Task<Outcome<Stored<Request>>> Define(string title)
        {
            var current = (await w.Services.Requests.GetAsync(w.Planning, id, Token)).Value!;
            return await w.Items.DefineStageAsync(w.Planning, id, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand(title, 2m, unit.Value.Id, null), Token);
        }

        Assert.True((await Define("اول")).Succeeded);
        w.Fixture.Db.Execute("UPDATE ItemStages SET Status = 3 WHERE StageNo = 1");
        Assert.Equal(OperationStatus.InvalidState, (await Define("دوم")).Status);
        Assert.Single((await LoadAsync(w, id)).Items.Single().Stages);

        // A new clarification question cannot be asked once the request left Drawing review; an open one from before would block. Simulate by an inquiry left unanswered.
        var kg = w.Fixture.Db.ContextFactory.CreateDbContext().UnitsOfMeasure.First(u => u.Code == "KG").Id;
        var draft = (await w.Services.Inquiries.CreateAsync(w.Drawing, id, null, Token)).Value!;
        var line = (await w.Services.Inquiries.AddLineAsync(w.Drawing, draft.Value.Id, draft.Version, new InquiryLineInput("ST37", "میلگرد", 30m, 6000m, null, 5m, kg, null, null), Token)).Value!;
        Assert.True((await w.Services.Inquiries.SendAsync(w.Drawing, draft.Value.Id, line.Version, Token)).Succeeded);
        Assert.Equal(ValidationIssue.ItemGateNotSatisfied, (await Define("سوم")).Issue);
    }
}
