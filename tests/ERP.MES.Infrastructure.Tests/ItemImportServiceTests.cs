using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;
using ERP.MES.Application.Imports;
using ERP.MES.Infrastructure.Imports;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// Excel item import (Decision Log #16, #97) on a real SQLite file with the real authorization, repositories and xlsx reader.
public class ItemImportServiceTests
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
    private static DefineItemCommand Repair(string itemNo = "2.10", Priority priority = Priority.Normal) => new(itemNo, "Shaft B", 4m, priority, null, null, null, null, null);

    private static long Count(World w, string tableAndWhere) => long.Parse(w.Fixture.Db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);

    private static readonly XlsxWorkbookWriter Writer = new();

    private static readonly string[] H = [ItemImportTemplate.WorkType, ItemImportTemplate.ItemNo, ItemImportTemplate.PartName, ItemImportTemplate.Quantity, ItemImportTemplate.QuantityType, ItemImportTemplate.Material, ItemImportTemplate.Weight, ItemImportTemplate.Length, ItemImportTemplate.Diameter, ItemImportTemplate.Priority];

    private static IReadOnlyList<string> MakeRow(string itemNo, string part = "Ring A", string qty = "10", string weight = "1.2345", string priority = "عادی", string material = "ST37-R30") =>
        ["ساخت", itemNo, part, qty, "PIECE", material, weight, "6000", "30", priority];

    private static byte[] Sheet(params IReadOnlyList<string>[] rows) => Writer.Write("آیتم‌ها", H, rows);

    private static async Task<(World W, long Id, long Version)> ReadyWorldAsync(WorkType type = WorkType.Make)
    {
        var w = await StartAsync();
        var (id, version) = await ReadyAsync(w, type);
        await MaterialAsync(w);
        return (w, id, version);
    }

    private static ItemImportService Svc(World w) => w.Services.ItemImport;

    private static async Task<ImportPreview> PreviewOkAsync(World w, long id, byte[] file, string name = "items.xlsx")
    {
        var outcome = await Svc(w).PreviewAsync(w.Planning, id, name, file, Token);
        Assert.True(outcome.Succeeded, outcome.Status + " " + outcome.Issue);
        return outcome.Value!;
    }

    private static async Task<Outcome<ImportCommitResult>> CommitAsync(World w, long id, byte[] file, IReadOnlyList<ImportDecision>? decisions = null, string name = "items.xlsx")
    {
        var preview = await PreviewOkAsync(w, id, file, name);
        return await Svc(w).CommitAsync(w.Planning, id, preview.RequestVersion, name, file, preview.FileHash, decisions ?? [], Token);
    }

    private static readonly string[] HierarchyHeaders = [ItemImportTemplate.CustomerItemRef, ItemImportTemplate.PartName, ItemImportTemplate.Quantity, ItemImportTemplate.QuantityType, ItemImportTemplate.Material, ItemImportTemplate.Weight, ItemImportTemplate.ParentRef, ItemImportTemplate.Assembly];

    private static IReadOnlyList<string> Node(string? customerRef, string part, string parent = "", string assembly = "") =>
        [customerRef ?? string.Empty, part, "10", "PIECE", "ST37-R30", "1.2", parent, assembly];

    private static byte[] HierarchySheet(params IReadOnlyList<string>[] rows) => Writer.Write("آیتم‌ها", HierarchyHeaders, rows);

    [Fact]
    public async Task An_import_builds_the_assembly_hierarchy_in_order_and_numbers_it_from_the_request()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;
        var file = HierarchySheet(Node("ASM", "Assembly", assembly: "بله"), Node("SUB", "Sub-assembly", "ASM", "بله"), Node("P1", "Part one", "SUB"), Node("P2", "Part two", "ASM"));

        var result = await CommitAsync(w, id, file);

        Assert.True(result.Succeeded, result.Status + " " + result.Issue);
        // The database may store a child before a sibling subtree, so the numbers (which sort correctly as text) are compared, not the storage order.
        var items = (await LoadAsync(w, id)).Items.OrderBy(i => i.ItemNo, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "D-1-001", "D-1-001.001", "D-1-001.001.001", "D-1-001.002" }, items.Select(i => i.ItemNo).ToArray());
        Assert.Equal(new[] { true, true, false, false }, items.Select(i => i.IsAssembly).ToArray());
        Assert.Equal(new string?[] { null, "ASM", "SUB", "ASM" }, items.Select(i => i.Parent?.CustomerItemRef).ToArray());
        Assert.Equal(new[] { "ASM", "SUB", "P1", "P2" }, items.Select(i => i.CustomerItemRef).ToArray());
    }

    [Fact]
    public async Task A_parent_that_is_unknown_not_an_assembly_or_too_deep_makes_only_that_row_invalid_with_a_reason()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;
        var file = HierarchySheet(
            Node("PLAIN", "Plain"), Node("A", "x", "PLAIN"), Node("B", "x", "NOWHERE"), Node("C", "x", "LATER"), Node("LATER", "late", assembly: "بله"),
            Node("L1", "l1", assembly: "بله"), Node("L2", "l2", "L1", "بله"), Node("L3", "l3", "L2", "بله"), Node("L4", "l4", "L3", "بله"), Node("L5", "l5", "L4"));

        var preview = await PreviewOkAsync(w, id, file);

        var issues = preview.Rows.ToDictionary(r => r.ItemNo ?? "?", r => r.Messages.Where(m => m.IsError).Select(m => m.Issue).ToArray());
        Assert.Equal(new[] { ImportIssue.ParentNotAssembly }, issues["A"]);
        Assert.Equal(new[] { ImportIssue.ParentRefUnknown }, issues["B"]);
        Assert.Equal(new[] { ImportIssue.ParentRefUnknown }, issues["C"]); // the parent must come before the child
        Assert.Equal(new[] { ImportIssue.ParentTooDeep }, issues["L5"]);
        Assert.Empty(issues["PLAIN"]);
        Assert.False(preview.CanCommit);
    }

    [Fact]
    public async Task Rows_without_a_customer_reference_are_always_new_and_never_matched_to_an_existing_item()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;
        var file = HierarchySheet(Node("KEEP", "Has a reference"), Node(null, "No reference"));

        var preview = await PreviewOkAsync(w, id, file);
        Assert.Contains(preview.Rows[1].Messages, m => m.Issue == ImportIssue.NoCustomerRef && !m.IsError);
        Assert.Equal(ImportRowStatus.New, preview.Rows[1].Status);
        Assert.True((await CommitAsync(w, id, file)).Succeeded);

        // The same file again: the referenced row is unchanged, the row without a reference is added again (an unreliable match is never used).
        var second = await CommitAsync(w, id, file);
        Assert.True(second.Succeeded, second.Status + " " + second.Issue);
        Assert.Equal(1, second.Value!.Created);
        Assert.Equal(1, second.Value.Unchanged);
        Assert.Equal(new[] { "D-1-001", "D-1-002", "D-1-003" }, (await LoadAsync(w, id)).Items.Select(i => i.ItemNo).ToArray());
    }

    [Fact]
    public async Task A_customer_reference_repeated_in_the_file_is_rejected_and_the_two_reference_headers_cannot_be_mixed()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;

        var repeated = await PreviewOkAsync(w, id, HierarchySheet(Node("DUP", "one"), Node("DUP", "two")));
        Assert.Contains(repeated.Rows[1].Messages, m => m.Issue == ImportIssue.ItemNoDuplicate && m.IsError);
        Assert.False(repeated.CanCommit);

        var mixed = Writer.Write("x", [ItemImportTemplate.CustomerItemRef, ItemImportTemplate.ItemNo, ItemImportTemplate.PartName, ItemImportTemplate.Quantity], [["A", "A", "p", "1"]]);
        var preview = await Svc(w).PreviewAsync(w.Planning, id, "mixed.xlsx", mixed, Token);
        Assert.False(preview.Value!.CanCommit);
        Assert.Contains(preview.Value.FileMessages, m => m.Issue == ImportIssue.HeaderDuplicate);
    }

    [Fact]
    public async Task Preview_writes_nothing_and_commit_creates_items_with_specification_import_log_and_audit()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;
        var file = Sheet(MakeRow("1.10", priority: "فوری"), MakeRow("1.20", "Shaft"), MakeRow("007", "Pin", qty: "2.5"));

        var preview = await PreviewOkAsync(w, id, file);
        Assert.Equal(3, preview.NewCount);
        Assert.True(preview.CanCommit);
        Assert.Equal(0, Count(w, "RequestItems"));
        Assert.Equal(0, Count(w, "ItemImports"));

        var result = await CommitAsync(w, id, file);

        Assert.True(result.Succeeded, result.Status + " " + result.Issue);
        Assert.Equal(3, result.Value!.Created);
        var items = (await LoadAsync(w, id)).Items;
        Assert.Equal(["1.10", "1.20", "007"], items.Select(i => i.CustomerItemRef).ToArray());
        Assert.Equal(["D-1-001", "D-1-002", "D-1-003"], items.Select(i => i.ItemNo).ToArray());
        Assert.Equal(Priority.Urgent, items[0].Priority);
        Assert.Equal(1.2345m, items[0].WeightKgPerBaseUnit);
        Assert.Equal(2.5m, items[2].RequestedQuantity);
        Assert.All(items, i => Assert.Equal(w.Planning.UserId, i.CreatedByUserId));
        Assert.Equal(1, Count(w, "ItemImports"));
        Assert.Equal(3, Count(w, "ItemImportRows"));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'ItemImportCommitted'"));
        Assert.Equal(3, Count(w, "AuditLog WHERE ActionCode = 'ItemDefined'"));
    }

    [Fact]
    public async Task Importing_the_same_file_again_changes_nothing_and_logs_nothing()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;
        var file = Sheet(MakeRow("1.10"), MakeRow("1.20"));
        Assert.True((await CommitAsync(w, id, file)).Succeeded);
        var version = await VersionAsync(w, id);
        var audit = Count(w, "AuditLog");

        var again = await CommitAsync(w, id, file);

        Assert.True(again.Succeeded);
        Assert.Equal(new ImportCommitResult(0, 0, 2, 0, null), again.Value);
        Assert.Equal(version, await VersionAsync(w, id));
        Assert.Equal(audit, Count(w, "AuditLog"));
        Assert.Equal(1, Count(w, "ItemImports"));
        Assert.Equal(2, (await LoadAsync(w, id)).Items.Count);
    }

    [Fact]
    public async Task A_changed_existing_item_is_a_conflict_that_needs_an_explicit_choice_and_is_never_overwritten_silently()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;
        Assert.True((await CommitAsync(w, id, Sheet(MakeRow("1.10", "Ring A", "10"), MakeRow("1.20", "Shaft", "4")))).Succeeded);
        var changed = Sheet(MakeRow("1.10", "Ring A2", "12"), MakeRow("1.20", "Shaft", "4"), MakeRow("1.30", "New"));

        var preview = await PreviewOkAsync(w, id, changed);
        Assert.Equal(1, preview.ConflictCount);
        Assert.Equal(1, preview.UnchangedCount);
        Assert.Equal(1, preview.NewCount);
        var conflict = preview.Rows.Single(r => r.Status == ImportRowStatus.Conflict);
        Assert.Equal("1.10", conflict.ItemNo);
        Assert.Equal([ImportField.PartName, ImportField.Quantity], conflict.Changes.Select(c => c.Field).ToArray());
        Assert.Equal(new ImportFieldChange(ImportField.Quantity, "10", "12"), conflict.Changes[1]);

        // No decision -> refused; nothing written.
        var undecided = await Svc(w).CommitAsync(w.Planning, id, preview.RequestVersion, "items.xlsx", changed, preview.FileHash, [], Token);
        Assert.Equal(ValidationIssue.ImportDecisionMissing, undecided.Issue);
        Assert.Equal(2, (await LoadAsync(w, id)).Items.Count);

        // Skip keeps the old values and still creates the new row.
        var skipped = await Svc(w).CommitAsync(w.Planning, id, preview.RequestVersion, "items.xlsx", changed, preview.FileHash, [new ImportDecision(conflict.SheetRowNo, ConflictChoice.Skip)], Token);
        Assert.True(skipped.Succeeded);
        Assert.Equal(new ImportCommitResult(1, 0, 1, 1, null), skipped.Value);
        var items = (await LoadAsync(w, id)).Items;
        Assert.Equal("Ring A", items.Single(i => i.CustomerItemRef == "1.10").PartName);
        Assert.Equal(10m, items.Single(i => i.CustomerItemRef == "1.10").RequestedQuantity);
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task Choosing_update_revises_the_item_and_audits_the_changed_fields()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;
        Assert.True((await CommitAsync(w, id, Sheet(MakeRow("1.10", "Ring A", "10")))).Succeeded);
        var changed = Sheet(MakeRow("1.10", "Ring A2", "12", priority: "خیلی فوری"));
        var preview = await PreviewOkAsync(w, id, changed);
        var row = preview.Rows.Single();

        var result = await Svc(w).CommitAsync(w.Planning, id, preview.RequestVersion, "items.xlsx", changed, preview.FileHash, [new ImportDecision(row.SheetRowNo, ConflictChoice.Update)], Token);

        Assert.True(result.Succeeded, result.Status + " " + result.Issue);
        Assert.Equal(1, result.Value!.Updated);
        var item = Assert.Single((await LoadAsync(w, id)).Items);
        Assert.Equal("Ring A2", item.PartName);
        Assert.Equal(12m, item.RequestedQuantity);
        Assert.Equal(Priority.VeryUrgent, item.Priority);
        Assert.Contains("PartName", Assert.Single(w.Fixture.Db.Query("SELECT Summary FROM AuditLog WHERE ActionCode = 'ItemRevisedByImport'")));
        Assert.Equal(2, Count(w, "ItemImports"));
    }

    [Fact]
    public async Task An_item_that_already_has_a_stage_is_locked_and_can_only_be_skipped()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;
        Assert.True((await CommitAsync(w, id, Sheet(MakeRow("1.10", "Ring A", "10")))).Succeeded);
        var unit = (await w.Services.PlanningMaster.AddUnitAsync(w.Admin, "MILLING", "فرزکاری", Token)).Value!;
        var current = (await w.Services.Requests.GetAsync(w.Planning, id, Token)).Value!;
        var itemId = current.Request.Value.Items.Single().Id;
        Assert.True((await w.Items.DefineStageAsync(w.Planning, id, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand("فرز", 3m, unit.Value.Id, null), Token)).Succeeded);
        var changed = Sheet(MakeRow("1.10", "Other", "99"));
        var preview = await PreviewOkAsync(w, id, changed);
        Assert.Equal(ImportRowStatus.Locked, preview.Rows.Single().Status);

        var update = await Svc(w).CommitAsync(w.Planning, id, preview.RequestVersion, "items.xlsx", changed, preview.FileHash, [new ImportDecision(preview.Rows[0].SheetRowNo, ConflictChoice.Update)], Token);
        Assert.Equal(ValidationIssue.ImportDecisionInvalid, update.Issue);

        var skip = await Svc(w).CommitAsync(w.Planning, id, preview.RequestVersion, "items.xlsx", changed, preview.FileHash, [new ImportDecision(preview.Rows[0].SheetRowNo, ConflictChoice.Skip)], Token);
        Assert.True(skip.Succeeded);
        Assert.Equal(new ImportCommitResult(0, 0, 0, 1, null), skip.Value);
        Assert.Equal("Ring A", (await LoadAsync(w, id)).Items.Single().PartName);
    }

    [Fact]
    public async Task Stage_columns_create_the_route_of_new_items_with_active_units_and_machines_of_that_unit()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;
        var milling = (await w.Services.PlanningMaster.AddUnitAsync(w.Admin, "MILLING", "فرزکاری", Token)).Value!;
        await w.Services.PlanningMaster.AddUnitAsync(w.Admin, "TURNING", "تراشکاری", Token);
        var machine = (await w.Services.PlanningMaster.AddMachineAsync(w.Admin, "FCC-06", "فرز CNC", milling.Value.Id, Token)).Value!;
        var headers = H.Concat([ItemImportTemplate.StageTitle(1), ItemImportTemplate.StageMinutes(1), ItemImportTemplate.StageUnit(1), ItemImportTemplate.StageMachine(1), ItemImportTemplate.StageTitle(2), ItemImportTemplate.StageMinutes(2), ItemImportTemplate.StageUnit(2)]).ToList();
        IReadOnlyList<string> row = [.. MakeRow("1.10"), "فرزکاری", "12.5", "MILLING", "FCC-06", "تراشکاری", "3", "TURNING"];

        var result = await CommitAsync(w, id, Writer.Write("S", headers, [row]));

        Assert.True(result.Succeeded, result.Status + " " + result.Issue);
        var stages = (await LoadAsync(w, id)).Items.Single().Stages;
        Assert.Equal(2, stages.Count);
        Assert.Equal(12.5m, stages[0].PlannedMinutesPerPiece);
        Assert.Equal(milling.Value.Id, stages[0].ProductionUnitId);
        Assert.Equal(machine.Value.Id, stages[0].MachineId);
        Assert.Equal(10m, stages[0].PlannedQuantity);
        Assert.Null(stages[1].MachineId);

        // A machine of another unit is an error on that row.
        IReadOnlyList<string> bad = [.. MakeRow("1.20"), "فرزکاری", "5", "TURNING", "FCC-06", "", "", ""];
        var preview = await PreviewOkAsync(w, id, Writer.Write("S", headers, [bad]));
        Assert.Equal(ImportRowStatus.Invalid, preview.Rows.Single().Status);
        Assert.Contains(preview.Rows.Single().Messages, m => m.Issue == ImportIssue.StageMachineUnitMismatch);
    }

    [Fact]
    public async Task Any_invalid_row_blocks_the_whole_file_and_every_error_carries_its_row_number()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;
        var file = Sheet(
            MakeRow("1.10"),
            MakeRow("1.10", "dup"),
            MakeRow("1.30", qty: "0"),
            MakeRow("1.40", material: "NOPE"),
            MakeRow("1.50", weight: "-1"),
            MakeRow("1.60", priority: "نامعلوم"),
            ["ساخت", "1.70", "", "5", "PIECE", "ST37-R30", "1", "", "", ""]);

        var preview = await PreviewOkAsync(w, id, file);

        Assert.False(preview.CanCommit);
        Assert.Equal(6, preview.InvalidCount);
        Assert.Equal(ImportRowStatus.New, preview.Rows[0].Status);
        var byRow = preview.Rows.ToDictionary(r => r.SheetRowNo);
        Assert.Contains(byRow[3].Messages, m => m.Issue == ImportIssue.ItemNoDuplicate);
        Assert.Contains(byRow[4].Messages, m => m.Issue == ImportIssue.QuantityInvalid);
        Assert.Contains(byRow[5].Messages, m => m.Issue == ImportIssue.MaterialUnknown);
        Assert.Contains(byRow[6].Messages, m => m.Issue == ImportIssue.WeightInvalid);
        Assert.Contains(byRow[7].Messages, m => m.Issue == ImportIssue.PriorityUnknown);
        Assert.Contains(byRow[8].Messages, m => m.Issue == ImportIssue.PartNameMissing);

        var commit = await Svc(w).CommitAsync(w.Planning, id, preview.RequestVersion, "items.xlsx", file, preview.FileHash, [], Token);
        Assert.Equal(ValidationIssue.ImportHasErrors, commit.Issue);
        Assert.Equal(0, Count(w, "RequestItems"));
    }

    [Fact]
    public async Task A_numeric_item_number_cell_wrong_work_type_missing_headers_and_non_xlsx_files_are_rejected()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;

        // WorkType column that does not match the request.
        var wrong = await PreviewOkAsync(w, id, Sheet(["تعمیر", "1", "x", "1", "PIECE", "ST37-R30", "1", "", "", ""]));
        Assert.Contains(wrong.Rows.Single().Messages, m => m.Issue == ImportIssue.WorkTypeMismatch);

        // Required header missing.
        var noHeader = await PreviewOkAsync(w, id, Writer.Write("S", [ItemImportTemplate.ItemNo, ItemImportTemplate.PartName], [["1", "x"]]));
        Assert.Contains(noHeader.FileMessages, m => m.IsError && m.Issue == ImportIssue.HeaderMissing);
        Assert.False(noHeader.CanCommit);

        // Not an xlsx at all.
        var csv = await PreviewOkAsync(w, id, System.Text.Encoding.UTF8.GetBytes("a,b\n1,2"), "items.csv");
        Assert.Contains(csv.FileMessages, m => m.IsError && m.Issue == ImportIssue.FileNotXlsx);

        // Missing / empty file.
        Assert.Equal(ValidationIssue.ImportFileMissing, (await Svc(w).PreviewAsync(w.Planning, id, "a.xlsx", [], Token)).Issue);
        Assert.Equal(ValidationIssue.ImportFileMissing, (await Svc(w).PreviewAsync(w.Planning, id, "", [1], Token)).Issue);
        Assert.Equal(ValidationIssue.ImportFileTooLarge, (await Svc(w).PreviewAsync(w.Planning, id, "a.xlsx", new byte[ItemImportService.MaxFileBytes + 1], Token)).Issue);
        Assert.Equal(OperationStatus.NotFound, (await Svc(w).PreviewAsync(w.Planning, 987654, "a.xlsx", [1], Token)).Status);
    }

    [Fact]
    public async Task Only_planning_may_import_and_the_gates_must_hold()
    {
        var (w, id, version) = await ReadyWorldAsync();
        using var disposeWorld = w;
        var file = Sheet(MakeRow("1.10"));
        var preview = await PreviewOkAsync(w, id, file);

        foreach (var user in new Principal?[] { w.Drawing, w.Central, w.Manager, w.ManagementStaff, w.Warehouse, w.Qc, w.Production, w.Admin, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await Svc(w).PreviewAsync(user, id, "a.xlsx", file, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await Svc(w).CommitAsync(user, id, version, "a.xlsx", file, preview.FileHash, [], Token)).Status);
            Assert.False(await Svc(w).CanImportAsync(user, Token));
        }

        Assert.True(await Svc(w).CanImportAsync(w.Planning, Token));
        Assert.Equal(0, Count(w, "RequestItems"));

        // A request that has not passed Drawing: gate blocks both preview and commit.
        var customer = (await w.Services.Customers.CreateAsync(w.Central, "Other Customer", Token)).Value!;
        var created = (await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "G-9", WorkType.Make, "1405/07/20", null, null), Token)).Value!;
        Assert.Equal(ValidationIssue.ItemGateNotSatisfied, (await Svc(w).PreviewAsync(w.Planning, created.Value.Id, "a.xlsx", file, Token)).Issue);
        Assert.Equal(ValidationIssue.ItemGateNotSatisfied, (await Svc(w).CommitAsync(w.Planning, created.Value.Id, created.Version, "a.xlsx", file, preview.FileHash, [], Token)).Issue);
    }

    [Fact]
    public async Task A_deactivated_planning_user_with_a_stale_principal_is_refused()
    {
        var (w, id, version) = await ReadyWorldAsync();
        using var disposeWorld = w;
        var file = Sheet(MakeRow("1.10"));
        var preview = await PreviewOkAsync(w, id, file);
        await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Planning.UserId, false, Token);

        Assert.Equal(OperationStatus.NotAuthorized, (await Svc(w).CommitAsync(w.Planning, id, version, "a.xlsx", file, preview.FileHash, [], Token)).Status);
        Assert.Equal(0, Count(w, "RequestItems"));
    }

    [Fact]
    public async Task A_stale_request_version_or_a_changed_file_is_refused_without_writing()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;
        var file = Sheet(MakeRow("1.10"));
        var preview = await PreviewOkAsync(w, id, file);

        // The file differs from what was previewed.
        var other = Sheet(MakeRow("1.10", "Different"));
        Assert.Equal(ValidationIssue.ImportFileChanged, (await Svc(w).CommitAsync(w.Planning, id, preview.RequestVersion, "items.xlsx", other, preview.FileHash, [], Token)).Issue);

        // Somebody else changed the request after the preview.
        var (piece, _) = await TypesAsync(w);
        var material = long.Parse(w.Fixture.Db.Query("SELECT Id FROM Materials LIMIT 1").Single(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True((await w.Items.DefineItemAsync(w.Planning, id, preview.RequestVersion, new DefineItemCommand("X-1", "Other", 1m, Priority.Normal, piece, material, 1m, null, null), Token)).Succeeded);
        var stale = await Svc(w).CommitAsync(w.Planning, id, preview.RequestVersion, "items.xlsx", file, preview.FileHash, [], Token);
        Assert.Equal(OperationStatus.ConcurrencyConflict, stale.Status);
        Assert.Single((await LoadAsync(w, id)).Items);
        Assert.Equal(0, Count(w, "ItemImports"));
    }

    [Fact]
    public async Task A_repair_request_imports_rows_without_a_specification_but_with_the_repair_fields()
    {
        var (w, id, _) = await ReadyWorldAsync(WorkType.Repair);
        using var disposeWorld = w;
        string[] header = [ItemImportTemplate.WorkType, ItemImportTemplate.ItemNo, ItemImportTemplate.PartName, ItemImportTemplate.Quantity, ItemImportTemplate.DeliveryDate, ItemImportTemplate.WarehouseCode];
        var incomplete = Writer.Write("S", header, [["تعمیر", "R-1", "Shaft", "3", "", ""]]);
        var preview = await Svc(w).PreviewAsync(w.Planning, id, "r.xlsx", incomplete, Token);
        Assert.Contains(preview.Value!.Rows.Single().Messages, m => m.Issue == ImportIssue.RepairFieldsMissing);

        var file = Writer.Write("S", header, [["تعمیر", "R-1", "Shaft", "3", "1450/01/01", "WH-1"], ["", "R-2", "Gear", "1", "1450/01/02", "WH-2"]]);

        var result = await CommitAsync(w, id, file);

        Assert.True(result.Succeeded, result.Status + " " + result.Issue);
        var items = (await LoadAsync(w, id)).Items;
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Null(i.MaterialId));
        Assert.All(items, i => Assert.NotNull(i.DeliveryDate));
        Assert.Equal(["WH-1", "WH-2"], items.Select(i => i.WarehouseItemCode).Order().ToArray());
    }

    [Fact]
    public async Task Nine_hundred_ninety_nine_rows_import_in_one_transaction_in_reasonable_time_and_the_thousandth_item_is_refused()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;
        var tooMany = await CommitAsync(w, id, Sheet(Enumerable.Range(1, 1000).Select(i => MakeRow("A-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "Part " + i)).ToArray()));
        Assert.Equal(ValidationIssue.ItemSequenceExhausted, tooMany.Issue);
        Assert.Equal(0, Count(w, "RequestItems"));

        var rows = Enumerable.Range(1, 999).Select(i => MakeRow("A-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "Part " + i)).ToArray();
        var file = Sheet(rows);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await CommitAsync(w, id, file);
        watch.Stop();

        Assert.True(result.Succeeded, result.Status + " " + result.Issue);
        Assert.Equal(999, result.Value!.Created);
        Assert.Equal(999, Count(w, "RequestItems"));
        Assert.Equal(999, Count(w, "ItemImportRows"));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(60), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task A_route_can_have_more_than_five_stages_up_to_the_documented_safety_limit_and_a_stage_column_beyond_it_is_an_error()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;
        await w.Services.PlanningMaster.AddUnitAsync(w.Admin, "MILLING", "فرزکاری", Token);
        List<string> headers = [.. H];
        List<string> cells = [.. MakeRow("1.10")];
        for (var k = 1; k <= 12; k++)
        {
            headers.AddRange([ItemImportTemplate.StageTitle(k), ItemImportTemplate.StageMinutes(k), ItemImportTemplate.StageUnit(k)]);
            cells.AddRange(["مرحله " + k, "2", "MILLING"]);
        }

        var result = await CommitAsync(w, id, Writer.Write("S", headers, [cells]));

        Assert.True(result.Succeeded, result.Status + " " + result.Issue);
        var stages = (await LoadAsync(w, id)).Items.Single().Stages;
        Assert.Equal(12, stages.Count);
        Assert.Equal(Enumerable.Range(1, 12).ToArray(), stages.Select(s => s.StageNo).ToArray());

        // Beyond the safety limit the header is an ERROR (the data would otherwise be dropped silently).
        var tooMany = headers.Concat([ItemImportTemplate.StageTitle(ItemImportTemplate.MaxStages + 1)]).ToList();
        var preview = await PreviewOkAsync(w, id, Writer.Write("S", tooMany, [[.. MakeRow("1.20"), .. Enumerable.Repeat("", headers.Count - H.Length), "x"]]));
        Assert.False(preview.CanCommit);
        Assert.Contains(preview.FileMessages, m => m.IsError && m.Issue == ImportIssue.StageHeaderBeyondLimit);
    }

    [Fact]
    public async Task A_failure_while_saving_leaves_no_partial_import_and_the_request_version_unchanged()
    {
        var (w, id, version) = await ReadyWorldAsync();
        using var disposeWorld = w;
        var repository = new ERP.MES.Infrastructure.Persistence.EfItemImportRepository(w.Fixture.Db.ContextFactory);
        var (piece, _) = await TypesAsync(w);
        var material = long.Parse(w.Fixture.Db.Query("SELECT Id FROM Materials LIMIT 1").Single(), System.Globalization.CultureInfo.InvariantCulture);
        var spec = new ItemSpecification(piece, material, 1m, null, null);

        // Items are added to the aggregate, then the audit builder fails: nothing may reach the database.
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.CommitAsync(
            id,
            version,
            r =>
            {
                r.DefineItem(0, "A-1", "x", 1m, spec, w.Planning.UserId, DateTime.UtcNow, Priority.Normal);
                r.DefineItem(0, "A-2", "y", 1m, spec, w.Planning.UserId, DateTime.UtcNow, Priority.Normal);
            },
            r => new ERP.MES.Domain.Imports.ItemImport(id, "f.xlsx", "00", w.Planning.UserId, DateTime.UtcNow, []),
            r => throw new InvalidOperationException("audit failed"),
            Token));

        Assert.Equal(0, Count(w, "RequestItems"));
        Assert.Equal(0, Count(w, "ItemImports"));
        Assert.Equal(version, await VersionAsync(w, id));

        // A failure inside the database save (here: a duplicate item number in the aggregate) also saves nothing.
        await Assert.ThrowsAnyAsync<Exception>(() => repository.CommitAsync(
            id,
            version,
            r =>
            {
                r.DefineItem(0, "B-1", "x", 1m, spec, w.Planning.UserId, DateTime.UtcNow, Priority.Normal);
                r.DefineItem(0, "B-1", "dup", 1m, spec, w.Planning.UserId, DateTime.UtcNow, Priority.Normal);
            },
            r => new ERP.MES.Domain.Imports.ItemImport(id, "f.xlsx", "00", w.Planning.UserId, DateTime.UtcNow, []),
            r => [],
            Token));
        Assert.Equal(0, Count(w, "RequestItems"));
        Assert.Equal(0, Count(w, "ItemImports"));
    }

    [Fact]
    public async Task Numeric_cells_for_quantity_and_weight_are_accepted_but_a_numeric_item_number_and_excess_decimals_are_not()
    {
        var (w, id, _) = await ReadyWorldAsync();
        using var disposeWorld = w;
        var sheet = "<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"
            + "<row r=\"1\">" + string.Concat(H.Select((h, i) => $"<c r=\"{(char)('A' + i)}1\" t=\"inlineStr\"><is><t>{h}</t></is></c>")) + "</row>"
            + "<row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>ساخت</t></is></c><c r=\"B2\" t=\"inlineStr\"><is><t>N-1</t></is></c><c r=\"C2\" t=\"inlineStr\"><is><t>x</t></is></c><c r=\"D2\"><v>12.5</v></c><c r=\"E2\" t=\"inlineStr\"><is><t>PIECE</t></is></c><c r=\"F2\" t=\"inlineStr\"><is><t>ST37-R30</t></is></c><c r=\"G2\"><v>1.2345</v></c></row>"
            + "<row r=\"3\"><c r=\"A3\" t=\"inlineStr\"><is><t>ساخت</t></is></c><c r=\"B3\"><v>10</v></c><c r=\"C3\" t=\"inlineStr\"><is><t>x</t></is></c><c r=\"D3\"><v>1</v></c><c r=\"E3\" t=\"inlineStr\"><is><t>PIECE</t></is></c><c r=\"F3\" t=\"inlineStr\"><is><t>ST37-R30</t></is></c><c r=\"G3\"><v>1</v></c></row>"
            + "<row r=\"4\"><c r=\"A4\" t=\"inlineStr\"><is><t>ساخت</t></is></c><c r=\"B4\" t=\"inlineStr\"><is><t>N-3</t></is></c><c r=\"C4\" t=\"inlineStr\"><is><t>x</t></is></c><c r=\"D4\"><v>1</v></c><c r=\"E4\" t=\"inlineStr\"><is><t>PIECE</t></is></c><c r=\"F4\" t=\"inlineStr\"><is><t>ST37-R30</t></is></c><c r=\"G4\"><v>1.23456789</v></c></row>"
            + "</sheetData></worksheet>";
        var file = ZipOf(sheet);

        var preview = await PreviewOkAsync(w, id, file);

        Assert.Equal(ImportRowStatus.New, preview.Rows[0].Status);
        Assert.Equal(ImportRowStatus.Invalid, preview.Rows[1].Status);
        Assert.Contains(preview.Rows[1].Messages, m => m.Issue == ImportIssue.ItemNoNumeric);
        Assert.Equal(ImportRowStatus.Invalid, preview.Rows[2].Status);
        Assert.Contains(preview.Rows[2].Messages, m => m.Issue == ImportIssue.WeightInvalid);
        Assert.False(preview.CanCommit);
    }

    private static byte[] ZipOf(string sheetXml)
    {
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string content)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new System.Text.UTF8Encoding(false));
                writer.Write(content);
            }

            Add("[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            Add("xl/workbook.xml", "<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"x\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Add("xl/worksheets/sheet1.xml", sheetXml);
        }

        return stream.ToArray();
    }
}
