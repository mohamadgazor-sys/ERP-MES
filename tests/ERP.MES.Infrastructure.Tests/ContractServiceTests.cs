using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// The MAKE contract gate (Decision Log #85) on a real SQLite file with the real authorization.
public class ContractServiceTests
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

        public ContractService Contracts => Services.Contracts;

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

    private static async Task<Stored<RequestContract>> RequiredAsync(World w, long requestId)
    {
        var decided = await w.Contracts.DecideRequirementAsync(w.Planning, requestId, ContractRequirement.Required, null, Token);
        Assert.True(decided.Succeeded, decided.Status + " " + decided.Issue);
        Tick(w);
        return decided.Value!;
    }

    /// <summary>A required contract with number, date and PDF, submitted to Management.</summary>
    private static async Task<Stored<RequestContract>> SubmittedAsync(World w, long requestId)
    {
        var draft = await RequiredAsync(w, requestId);
        var saved = (await w.Contracts.SaveDraftAsync(w.Planning, requestId, draft.Version, "CN-100", "1405/07/25", "قرارداد ساخت", null, Token)).Value!;
        Tick(w);
        var withPdf = (await w.Contracts.AttachPdfAsync(w.Planning, requestId, saved.Version, "contract.pdf", Pdf("v1"), Token)).Value!;
        Tick(w);
        var submitted = await w.Contracts.SubmitAsync(w.Planning, requestId, withPdf.Version, Token);
        Assert.True(submitted.Succeeded, submitted.Status + " " + submitted.Issue);
        Tick(w);
        return submitted.Value!;
    }

    private static long Count(World w, string tableAndWhere) => long.Parse(w.Fixture.Db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<DefinitionBlock> BlockAsync(World w, long requestId) => (await w.Contracts.GetReadinessAsync(w.Planning, requestId, Token)).Value!.Block;

    [Fact]
    public async Task Planning_records_that_no_contract_is_required_and_the_make_request_becomes_ready_for_definition_gates()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        Assert.Equal(DefinitionBlock.ContractDecisionMissing, await BlockAsync(w, id));

        var decided = await w.Contracts.DecideRequirementAsync(w.Planning, id, ContractRequirement.NotRequired, "قرارداد لازم نیست", Token);

        Assert.True(decided.Succeeded, decided.Status + " " + decided.Issue);
        Assert.Equal(ContractStatus.NotRequired, decided.Value!.Value.Status);
        Assert.Equal(DefinitionBlock.None, await BlockAsync(w, id));
        Assert.Equal("ContractRequirementDecided", Assert.Single(w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode LIKE 'Contract%'")));
        Assert.Equal(1, Count(w, "ContractHistory"));
        Assert.Equal(0, Count(w, "ContractVersions"));
        Assert.Contains("ItemDefinition", RequestGateReadiness.GatesNotYetEnforced);
    }

    [Fact]
    public async Task A_repair_request_skips_the_contract_and_cannot_be_given_one()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w, WorkType.Repair);

        var decided = await w.Contracts.DecideRequirementAsync(w.Planning, id, ContractRequirement.Required, null, Token);

        Assert.Equal(ValidationIssue.ContractNotApplicable, decided.Issue);
        Assert.Equal(0, Count(w, "Contracts"));
        Assert.Equal(DefinitionBlock.None, await BlockAsync(w, id));
        Assert.DoesNotContain(await w.Contracts.ListAsync(w.Planning, Token) is { Value: { } list } ? list : [], i => i.RequestId == id);
    }

    [Theory]
    [InlineData(DrawingOutcome.Rejected)]
    [InlineData(DrawingOutcome.MoreInformationNeeded)]
    [InlineData(DrawingOutcome.ReturnedForCorrection)]
    public async Task A_contract_decision_cannot_bypass_an_unaccepted_drawing_result(DrawingOutcome outcome)
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w, WorkType.Make, outcome);

        Assert.Equal(ValidationIssue.ContractDrawingNotAccepted, (await w.Contracts.DecideRequirementAsync(w.Planning, id, ContractRequirement.NotRequired, null, Token)).Issue);
        Assert.Equal(DefinitionBlock.DrawingResultNotAccepted, await BlockAsync(w, id));
        Assert.Equal(0, Count(w, "Contracts"));
    }

    [Fact]
    public async Task A_partially_accepted_drawing_result_allows_the_decision()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w, WorkType.Make, DrawingOutcome.PartiallyAccepted);

        Assert.True((await w.Contracts.DecideRequirementAsync(w.Planning, id, ContractRequirement.NotRequired, null, Token)).Succeeded);
    }

    [Fact]
    public async Task A_contract_decision_cannot_bypass_management_approval_or_drawing()
    {
        using var w = await StartAsync();
        var customer = (await w.Services.Customers.CreateAsync(w.Central, "Fictional Customer", Token)).Value!;
        var created = (await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "X-1", WorkType.Make, "1405/07/20", null, null), Token)).Value!;

        Assert.Equal(ValidationIssue.ContractRequestNotReturned, (await w.Contracts.DecideRequirementAsync(w.Planning, created.Value.Id, ContractRequirement.NotRequired, null, Token)).Issue);
        Assert.Equal(DefinitionBlock.RequestNotReturnedFromDrawing, await BlockAsync(w, created.Value.Id));
        Assert.Equal(0, Count(w, "Contracts"));
    }

    [Fact]
    public async Task The_decision_is_made_once_and_a_second_attempt_is_refused()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        await RequiredAsync(w, id);

        Assert.Equal(ValidationIssue.ContractAlreadyDecided, (await w.Contracts.DecideRequirementAsync(w.Planning, id, ContractRequirement.NotRequired, null, Token)).Issue);
        Assert.Equal(1, Count(w, "Contracts"));
    }

    [Fact]
    public async Task A_required_contract_needs_number_date_and_pdf_before_it_can_be_submitted()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var draft = await RequiredAsync(w, id);
        Assert.Equal(DefinitionBlock.ContractInPreparation, await BlockAsync(w, id));

        Assert.Equal(ValidationIssue.ContractNoRequired, (await w.Contracts.SubmitAsync(w.Planning, id, draft.Version, Token)).Issue);
        var named = (await w.Contracts.SaveDraftAsync(w.Planning, id, draft.Version, "CN-1", null, null, null, Token)).Value!;
        Assert.Equal(ValidationIssue.ContractDateRequired, (await w.Contracts.SubmitAsync(w.Planning, id, named.Version, Token)).Issue);
        var dated = (await w.Contracts.SaveDraftAsync(w.Planning, id, named.Version, "CN-1", "1405/07/25", null, null, Token)).Value!;
        Assert.Equal(ValidationIssue.ContractPdfRequired, (await w.Contracts.SubmitAsync(w.Planning, id, dated.Version, Token)).Issue);

        Assert.Equal(ContractStatus.Draft, (await w.Contracts.GetAsync(w.Planning, id, Token)).Value!.Contract!.Value.Status);
        Assert.Equal(0, Count(w, "AuditLog WHERE ActionCode = 'ContractSubmitted'"));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("notpdf")]
    [InlineData("name")]
    [InlineData("big")]
    public async Task An_invalid_contract_pdf_is_refused_and_nothing_is_stored(string kind)
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var draft = await RequiredAsync(w, id);
        var documentsBefore = Count(w, "Documents");

        var (name, content, expected) = kind switch
        {
            "empty" => ("c.pdf", Array.Empty<byte>(), ValidationIssue.PdfEmpty),
            "notpdf" => ("c.pdf", System.Text.Encoding.ASCII.GetBytes("hello"), ValidationIssue.PdfNotPdf),
            "name" => ("c.txt", Pdf(), ValidationIssue.PdfFileNameInvalid),
            _ => ("c.pdf", new byte[(20 * 1024 * 1024) + 1], ValidationIssue.PdfTooLarge),
        };
        var outcome = await w.Contracts.AttachPdfAsync(w.Planning, id, draft.Version, name, content, Token);

        Assert.Equal(expected, outcome.Issue);
        Assert.Equal(documentsBefore, Count(w, "Documents"));
        Assert.Equal(0, Count(w, "ContractDocumentLinks"));
    }

    [Fact]
    public async Task Field_limits_and_dates_are_validated()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var draft = await RequiredAsync(w, id);

        Assert.Equal(ValidationIssue.ContractTextTooLong, (await w.Contracts.SaveDraftAsync(w.Planning, id, draft.Version, new string('n', 51), null, null, null, Token)).Issue);
        Assert.Equal(ValidationIssue.DateInvalid, (await w.Contracts.SaveDraftAsync(w.Planning, id, draft.Version, "CN", "1405/13/40", null, null, Token)).Issue);
        Assert.Equal(ValidationIssue.ReasonTooLong, (await w.Contracts.SaveDraftAsync(w.Planning, id, draft.Version, "CN", null, null, new string('x', 2001), Token)).Issue);
        Assert.Equal(0, Count(w, "AuditLog WHERE ActionCode = 'ContractDraftSaved'"));
    }

    [Fact]
    public async Task Management_approves_and_the_request_passes_the_contract_gate_with_history_and_audit()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var submitted = await SubmittedAsync(w, id);
        Assert.Equal(DefinitionBlock.ContractPendingManagement, await BlockAsync(w, id));

        var approved = await w.Contracts.ApproveAsync(w.ManagementStaff, id, submitted.Version, "تأیید شد", Token);

        Assert.True(approved.Succeeded, approved.Status + " " + approved.Issue);
        var contract = approved.Value!.Value;
        Assert.Equal(ContractStatus.Approved, contract.Status);
        Assert.Equal(ContractDecision.Approved, contract.Versions.Single().Decision);
        Assert.Equal(w.ManagementStaff.UserId, contract.Versions.Single().DecidedByUserId);
        Assert.Equal(DefinitionBlock.None, await BlockAsync(w, id));

        // Planning and Warehouse see the approval (derived from persisted state; no separate notification mechanism exists).
        foreach (var reader in new[] { w.Planning, w.Warehouse })
        {
            var item = (await w.Contracts.ListAsync(reader, Token)).Value!.Single();
            Assert.Equal(ContractStatus.Approved, item.ContractStatus);
        }

        Assert.Equal(
            new[] { "ContractRequirementDecided", "ContractDraftSaved", "ContractPdfAttached", "ContractSubmitted", "ContractApproved" },
            w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode LIKE 'Contract%' ORDER BY Id").ToArray());
        Assert.Equal(5, Count(w, "ContractHistory"));
    }

    [Fact]
    public async Task A_rejection_needs_a_reason_returns_the_contract_to_planning_and_blocks_the_gate()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var submitted = await SubmittedAsync(w, id);

        Assert.Equal(ValidationIssue.ReasonRequired, (await w.Contracts.RejectAsync(w.Manager, id, submitted.Version, "  ", Token)).Issue);
        Assert.Equal(ContractStatus.PendingManagement, (await w.Contracts.GetAsync(w.Planning, id, Token)).Value!.Contract!.Value.Status);

        var rejected = await w.Contracts.RejectAsync(w.Manager, id, submitted.Version, "مبلغ نادرست", Token);

        Assert.True(rejected.Succeeded);
        Assert.Equal(ContractStatus.RejectedAtPlanning, rejected.Value!.Value.Status);
        Assert.Equal("مبلغ نادرست", rejected.Value.Value.Versions.Single().DecisionNote);
        Assert.Equal(DefinitionBlock.ContractRejected, await BlockAsync(w, id));
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'ContractRejected'"));
    }

    [Fact]
    public async Task Revision_creates_a_new_version_and_never_overwrites_the_rejected_one_or_its_pdf_or_decision()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var submitted = await SubmittedAsync(w, id);
        var rejected = (await w.Contracts.RejectAsync(w.Manager, id, submitted.Version, "اصلاح شود", Token)).Value!;
        Tick(w);

        // Only a rejected contract can be revised, and a rejected one cannot be edited or submitted directly.
        Assert.Equal(OperationStatus.InvalidState, (await w.Contracts.SaveDraftAsync(w.Planning, id, rejected.Version, "CN-X", null, null, null, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Contracts.SubmitAsync(w.Planning, id, rejected.Version, Token)).Status);

        var revising = (await w.Contracts.StartRevisionAsync(w.Planning, id, rejected.Version, Token)).Value!;
        Tick(w);
        var edited = (await w.Contracts.SaveDraftAsync(w.Planning, id, revising.Version, "CN-100-R1", "1405/08/01", "قرارداد اصلاح‌شده", null, Token)).Value!;
        Tick(w);
        var replaced = (await w.Contracts.AttachPdfAsync(w.Planning, id, edited.Version, "contract-v2.pdf", Pdf("v2"), Token)).Value!;
        Tick(w);
        var resubmitted = await w.Contracts.SubmitAsync(w.Planning, id, replaced.Version, Token);
        Assert.True(resubmitted.Succeeded, resubmitted.Status + " " + resubmitted.Issue);
        Tick(w);

        var contract = (await w.Contracts.GetAsync(w.Planning, id, Token)).Value!.Contract!.Value;
        Assert.Equal(2, contract.Versions.Count);
        var first = contract.Versions[0];
        var second = contract.Versions[1];
        Assert.Equal(ContractVersionState.Submitted, first.State);
        Assert.Equal("CN-100", first.ContractNo);
        Assert.Equal(ContractDecision.Rejected, first.Decision);
        Assert.Equal("اصلاح شود", first.DecisionNote);
        Assert.Equal(w.Manager.UserId, first.DecidedByUserId);
        Assert.Equal("contract.pdf", Assert.Single(first.DocumentLinks).FileName);
        Assert.True(first.CurrentPdf!.IsCurrent);
        Assert.Equal("CN-100-R1", second.ContractNo);
        Assert.Equal("contract-v2.pdf", second.CurrentPdf!.FileName);
        Assert.Equal(ContractStatus.PendingManagement, contract.Status);

        // The first PDF is still retrievable, unchanged, beside the second.
        var pdf1 = (await w.Contracts.GetPdfAsync(w.Warehouse, id, 1, Token)).Value!;
        var pdf2 = (await w.Contracts.GetPdfAsync(w.Warehouse, id, 2, Token)).Value!;
        Assert.Equal(Pdf("v1"), pdf1.Content);
        Assert.Equal(Pdf("v2"), pdf2.Content);

        var approved = await w.Contracts.ApproveAsync(w.Manager, id, resubmitted.Value!.Version, null, Token);
        Assert.True(approved.Succeeded);
        Assert.Equal(DefinitionBlock.None, await BlockAsync(w, id));
        var final = approved.Value!.Value;
        Assert.Equal(ContractDecision.Rejected, final.Versions[0].Decision);
        Assert.Equal(ContractDecision.Approved, final.Versions[1].Decision);
        Assert.Equal(
            new[] { "DecidedRequired", "DraftSaved", "PdfAttached", "Submitted", "Rejected", "RevisionStarted", "DraftSaved", "PdfAttached", "Submitted", "Approved" },
            final.History.Select(h => h.Action.ToString()).ToArray());
    }

    [Fact]
    public async Task A_revision_keeps_the_previous_pdf_linked_until_it_is_replaced_and_the_binary_is_stored_once()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var submitted = await SubmittedAsync(w, id);
        var rejected = (await w.Contracts.RejectAsync(w.Manager, id, submitted.Version, "x", Token)).Value!;

        var revising = (await w.Contracts.StartRevisionAsync(w.Planning, id, rejected.Version, Token)).Value!;
        var contract = revising.Value;
        var documentsAfterRevision = Count(w, "Documents WHERE Type = 2");

        Assert.Equal(1, documentsAfterRevision);
        Assert.Equal(contract.Versions[0].CurrentPdf!.ContentHash, contract.Versions[1].CurrentPdf!.ContentHash);
        Assert.Equal(contract.Versions[0].CurrentPdf!.DocumentId, contract.Versions[1].CurrentPdf!.DocumentId);
        Assert.Equal(2, Count(w, "ContractDocumentLinks"));
        Assert.True((await w.Contracts.SubmitAsync(w.Planning, id, revising.Version, Token)).Succeeded);
    }

    [Fact]
    public async Task Planning_can_change_the_decision_with_a_reason_until_a_version_is_submitted_and_never_afterwards()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var notRequired = (await w.Contracts.DecideRequirementAsync(w.Planning, id, ContractRequirement.NotRequired, null, Token)).Value!;

        Assert.Equal(ValidationIssue.ReasonRequired, (await w.Contracts.ChangeRequirementAsync(w.Planning, id, notRequired.Version, ContractRequirement.Required, " ", Token)).Issue);
        var required = (await w.Contracts.ChangeRequirementAsync(w.Planning, id, notRequired.Version, ContractRequirement.Required, "مشتری قرارداد خواست", Token)).Value!;
        Assert.Equal(ContractStatus.Draft, required.Value.Status);
        Assert.Equal(DefinitionBlock.ContractInPreparation, await BlockAsync(w, id));

        var back = (await w.Contracts.ChangeRequirementAsync(w.Planning, id, required.Version, ContractRequirement.NotRequired, "اشتباه بود", Token)).Value!;
        Assert.Equal(ContractVersionState.Withdrawn, back.Value.Versions.Single().State);
        Assert.Equal(1, Count(w, "ContractVersions"));
        Assert.Equal(DefinitionBlock.None, await BlockAsync(w, id));

        var again = (await w.Contracts.ChangeRequirementAsync(w.Planning, id, back.Version, ContractRequirement.Required, "دوباره", Token)).Value!;
        Assert.Equal(2, again.Value.Versions.Count);
        var saved = (await w.Contracts.SaveDraftAsync(w.Planning, id, again.Version, "CN", "1405/07/25", null, null, Token)).Value!;
        var pdf = (await w.Contracts.AttachPdfAsync(w.Planning, id, saved.Version, "c.pdf", Pdf(), Token)).Value!;
        var submitted = (await w.Contracts.SubmitAsync(w.Planning, id, pdf.Version, Token)).Value!;

        Assert.Equal(OperationStatus.InvalidState, (await w.Contracts.ChangeRequirementAsync(w.Planning, id, submitted.Version, ContractRequirement.NotRequired, "دیر شد", Token)).Status);
        Assert.Equal(ContractStatus.PendingManagement, (await w.Contracts.GetAsync(w.Planning, id, Token)).Value!.Contract!.Value.Status);
    }

    [Fact]
    public async Task Warehouse_reads_the_contract_and_its_history_but_cannot_change_anything()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var submitted = await SubmittedAsync(w, id);

        var detail = (await w.Contracts.GetAsync(w.Warehouse, id, Token)).Value!;
        Assert.Equal(ContractStatus.PendingManagement, detail.Contract!.Value.Status);
        Assert.NotEmpty(detail.Contract.Value.History);
        Assert.True((await w.Contracts.GetPdfAsync(w.Warehouse, id, 1, Token)).Succeeded);
        Assert.Equal(new ContractCapabilities(true, false, false), await w.Contracts.GetCapabilitiesAsync(w.Warehouse, Token));

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.DecideRequirementAsync(w.Warehouse, id, ContractRequirement.NotRequired, null, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.SaveDraftAsync(w.Warehouse, id, submitted.Version, "X", null, null, null, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.AttachPdfAsync(w.Warehouse, id, submitted.Version, "c.pdf", Pdf(), Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.SubmitAsync(w.Warehouse, id, submitted.Version, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.ApproveAsync(w.Warehouse, id, submitted.Version, null, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.RejectAsync(w.Warehouse, id, submitted.Version, "x", Token)).Status);
        Assert.Equal(ContractStatus.PendingManagement, (await w.Contracts.GetAsync(w.Planning, id, Token)).Value!.Contract!.Value.Status);
    }

    [Fact]
    public async Task Planning_prepares_and_management_decides_and_nobody_else_does_either()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var submitted = await SubmittedAsync(w, id);

        // Management cannot prepare; Planning cannot decide.
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.DecideRequirementAsync(w.Manager, id, ContractRequirement.NotRequired, null, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.SubmitAsync(w.Manager, id, submitted.Version, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.ApproveAsync(w.Planning, id, submitted.Version, null, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.RejectAsync(w.Planning, id, submitted.Version, "x", Token)).Status);

        foreach (var other in new Principal?[] { w.Central, w.Drawing, w.Qc, w.Production, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.ListAsync(other, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.GetAsync(other, id, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.GetPdfAsync(other, id, 1, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.GetReadinessAsync(other, id, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.DecideRequirementAsync(other, id, ContractRequirement.NotRequired, null, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.ApproveAsync(other, id, submitted.Version, null, Token)).Status);
            Assert.Equal(ContractCapabilities.None, await w.Contracts.GetCapabilitiesAsync(other, Token) with { CanView = false });
        }

        Assert.Equal(ContractStatus.PendingManagement, (await w.Contracts.GetAsync(w.Planning, id, Token)).Value!.Contract!.Value.Status);
    }

    [Fact]
    public async Task A_deactivated_user_is_refused_even_with_an_old_principal()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var submitted = await SubmittedAsync(w, id);
        var staleManager = w.ManagementStaff;
        var stalePlanning = w.Planning;

        await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, staleManager.UserId, false, Token);
        await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, stalePlanning.UserId, false, Token);

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.ApproveAsync(staleManager, id, submitted.Version, null, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.GetAsync(staleManager, id, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.DecideRequirementAsync(stalePlanning, id, ContractRequirement.NotRequired, null, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Contracts.ListAsync(stalePlanning, Token)).Status);
        Assert.Equal(ContractStatus.PendingManagement, (await w.Contracts.GetAsync(w.Manager, id, Token)).Value!.Contract!.Value.Status);
        Assert.Equal(0, Count(w, "AuditLog WHERE ActionCode = 'ContractApproved'"));
    }

    [Fact]
    public async Task A_stale_version_conflicts_and_changes_nothing()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var submitted = await SubmittedAsync(w, id);
        var historyBefore = Count(w, "ContractHistory");

        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Contracts.ApproveAsync(w.Manager, id, submitted.Version + 5, null, Token)).Status);
        var rejected = await w.Contracts.RejectAsync(w.Manager, id, submitted.Version, "x", Token);
        Assert.True(rejected.Succeeded);

        // A second person working from the old view cannot also approve: the contract moved on.
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Contracts.ApproveAsync(w.ManagementStaff, id, submitted.Version, null, Token)).Status);
        Assert.Equal(historyBefore + 1, Count(w, "ContractHistory"));
        Assert.Equal(ContractStatus.RejectedAtPlanning, (await w.Contracts.GetAsync(w.Planning, id, Token)).Value!.Contract!.Value.Status);
        Assert.Equal(0, Count(w, "AuditLog WHERE ActionCode = 'ContractApproved'"));
    }

    [Fact]
    public async Task Approving_in_the_wrong_state_is_refused_without_partial_changes()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var draft = await RequiredAsync(w, id);
        var historyBefore = Count(w, "ContractHistory");

        Assert.Equal(OperationStatus.InvalidState, (await w.Contracts.ApproveAsync(w.Manager, id, draft.Version, null, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Contracts.StartRevisionAsync(w.Planning, id, draft.Version, Token)).Status);
        Assert.Equal(OperationStatus.NotFound, (await w.Contracts.ApproveAsync(w.Manager, 99999, 1, null, Token)).Status);
        Assert.Equal(historyBefore, Count(w, "ContractHistory"));
        Assert.Equal(1, Count(w, "ContractVersions"));
    }

    [Fact]
    public async Task A_request_that_moved_on_blocks_further_contract_actions_and_the_gate()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var submitted = await SubmittedAsync(w, id);

        // Planning sends the request back to Drawing while the contract waits for Management.
        var request = (await w.Services.Requests.GetAsync(w.Planning, id, Token)).Value!.Request;
        Assert.True((await w.Services.Lifecycle.SendBackToDrawingAsync(w.Planning, id, request.Version, "اطلاعات بیشتر", Token)).Succeeded);

        Assert.Equal(ValidationIssue.ContractRequestNotReturned, (await w.Contracts.ApproveAsync(w.Manager, id, submitted.Version, null, Token)).Issue);
        Assert.Equal(DefinitionBlock.RequestNotReturnedFromDrawing, await BlockAsync(w, id));
        Assert.Equal(ContractStatus.PendingManagement, (await w.Contracts.GetAsync(w.Planning, id, Token)).Value!.Contract!.Value.Status);
    }

    [Fact]
    public async Task A_contract_action_raises_the_request_version_so_a_stale_request_action_conflicts()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var staleRequest = (await w.Services.Requests.GetAsync(w.Planning, id, Token)).Value!.Request;

        await w.Contracts.DecideRequirementAsync(w.Planning, id, ContractRequirement.NotRequired, null, Token);

        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Services.Lifecycle.SendBackToDrawingAsync(w.Planning, id, staleRequest.Version, "x", Token)).Status);
    }

    [Fact]
    public async Task The_contract_history_is_append_only_and_everything_survives_a_restart()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var submitted = await SubmittedAsync(w, id);
        await w.Contracts.RejectAsync(w.Manager, id, submitted.Version, "x", Token);

        await using (var context = w.Fixture.Db.NewContext())
        {
            var entry = context.ContractHistory.First();
            context.Entry(entry).Property(e => e.Note).CurrentValue = "changed";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Token));
        }

        await using (var context = w.Fixture.Db.NewContext())
        {
            context.ContractHistory.Remove(context.ContractHistory.First());
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Token));
        }

        var restarted = WorkflowServices.Create(w.Fixture.Db.ContextFactory, w.Fixture.Services.Authorization, PersistenceTestData.Dates, w.Fixture.Clock);
        var contract = (await restarted.Contracts.GetAsync(w.Warehouse, id, Token)).Value!.Contract!.Value;
        Assert.Equal(ContractStatus.RejectedAtPlanning, contract.Status);
        Assert.Equal(5, contract.History.Count);
        Assert.Equal("contract.pdf", contract.Versions.Single().CurrentPdf!.FileName);
    }
}
