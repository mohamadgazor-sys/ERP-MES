using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// Owner decision D1 (Decision Log #95): No-answer dispositions, supply routes, clarification questions and the production gate, on a real SQLite file with the real authorization.
public class InquiryDispositionTests
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

    private static long Count(World w, string tableAndWhere) => long.Parse(w.Fixture.Db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A sent inquiry whose line 1 was answered No and line 2 Yes; the inquiry is ANSWERED.</summary>
    private static async Task<Stored<RawMaterialInquiry>> WithNoAnswerAsync(World w, long requestId)
    {
        var sent = await SentAsync(w, requestId, 2);
        var one = (await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, sent.Version, 1, InquiryAnswer.No, "موجود نیست", Token)).Value!;
        Tick(w);
        var two = (await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, one.Version, 2, InquiryAnswer.Yes, null, Token)).Value!;
        Tick(w);
        return two;
    }

    [Fact]
    public async Task A_no_answer_blocks_the_gate_until_drawing_records_a_disposition_and_nothing_rejects_the_request()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        await w.Contracts.DecideRequirementAsync(w.Planning, id, ContractRequirement.NotRequired, null, Token);
        Tick(w);
        var answered = await WithNoAnswerAsync(w, id);

        var before = (await w.Gates.GetAsync(w.Planning, id, Token)).Value!;
        Assert.Equal(DefinitionBlock.None, before.DefinitionBlock);
        Assert.Equal(1, before.PendingDispositions);
        Assert.False(before.Satisfied);
        Assert.Contains(GateBlocker.NoAnswerWithoutDisposition, before.Blockers);

        var disposed = await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, answered.Version, 1, DispositionKind.RecordedAsCondition, "ماده جایگزین مجاز است", null, Token);
        Assert.True(disposed.Succeeded, disposed.Status + " " + disposed.Issue);

        var after = (await w.Gates.GetAsync(w.Planning, id, Token)).Value!;
        Assert.Equal(0, after.PendingDispositions);
        Assert.True(after.Satisfied);
        Assert.Equal(DispositionKind.RecordedAsCondition, disposed.Value!.Value.Lines[0].CurrentDisposition!.Kind);
        Assert.Equal(w.Drawing.UserId, disposed.Value.Value.Lines[0].CurrentDisposition!.DecidedByUserId);
        Assert.Equal(RequestStatus.DrawingReturnedToPlanning, (await w.Services.Requests.GetAsync(w.Planning, id, Token)).Value!.Request.Value.Status);
        Assert.Equal(1, Count(w, "AuditLog WHERE ActionCode = 'InquiryDispositionRecorded'"));
    }

    [Fact]
    public async Task Disposition_rules_note_kind_line_and_roles()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var answered = await WithNoAnswerAsync(w, id);

        Assert.Equal(ValidationIssue.DispositionInvalid, (await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, answered.Version, 1, DispositionKind.ReturnedUnchanged, " ", null, Token)).Issue);
        Assert.Equal(ValidationIssue.DispositionInvalid, (await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, answered.Version, 1, DispositionKind.RevisedInNewRound, null, null, Token)).Issue);
        Assert.Equal(ValidationIssue.DispositionInvalid, (await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, answered.Version, 1, (DispositionKind)99, "x", null, Token)).Issue);

        // Line 2 was answered Yes: nothing to dispose of. Line 9 does not exist.
        Assert.Equal(OperationStatus.InvalidState, (await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, answered.Version, 2, DispositionKind.ReturnedUnchanged, "x", null, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, answered.Version, 9, DispositionKind.ReturnedUnchanged, "x", null, Token)).Status);

        foreach (var user in new Principal?[] { w.Warehouse, w.Planning, w.Manager, w.Qc, w.Central, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Inquiries.RecordDispositionAsync(user, answered.Value.Id, answered.Version, 1, DispositionKind.ReturnedUnchanged, "x", null, Token)).Status);
        }

        var ok = await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, answered.Version, 1, DispositionKind.ReturnedUnchanged, "بدون تغییر", null, Token);
        Assert.True(ok.Succeeded);

        // A second disposition for the same answer is refused: it is already dispositioned.
        Assert.Equal(OperationStatus.InvalidState, (await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, ok.Value!.Version, 1, DispositionKind.RecordedAsCondition, "y", null, Token)).Status);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, answered.Version, 1, DispositionKind.RecordedAsCondition, "y", null, Token)).Status);
        Assert.Equal(1, Count(w, "RawMaterialInquiryDispositions"));
    }

    [Fact]
    public async Task A_revision_names_a_later_round_of_the_same_request_that_is_not_cancelled_and_the_old_answers_are_kept()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var answered = await WithNoAnswerAsync(w, id);
        var otherId = await ReturnedAsync(w, WorkType.Make, DrawingOutcome.FullyAccepted, "C-2");
        var otherRound = (await w.Inquiries.CreateAsync(w.Drawing, otherId, null, Token)).Value!;

        // The same inquiry, another request's round and a missing id are all refused.
        Assert.Equal(ValidationIssue.DispositionInvalid, (await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, answered.Version, 1, DispositionKind.RevisedInNewRound, null, answered.Value.Id, Token)).Issue);
        Assert.Equal(ValidationIssue.DispositionInvalid, (await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, answered.Version, 1, DispositionKind.RevisedInNewRound, null, otherRound.Value.Id, Token)).Issue);
        Assert.Equal(ValidationIssue.DispositionInvalid, (await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, answered.Version, 1, DispositionKind.RevisedInNewRound, null, 987654, Token)).Issue);

        var round2 = (await w.Inquiries.CreateAsync(w.Drawing, id, null, Token)).Value!;
        Assert.Equal(2, round2.Value.RoundNo);
        var disposed = await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, answered.Version, 1, DispositionKind.RevisedInNewRound, null, round2.Value.Id, Token);
        Assert.True(disposed.Succeeded, disposed.Status + " " + disposed.Issue);
        Assert.Equal(round2.Value.Id, disposed.Value!.Value.Lines[0].CurrentDisposition!.NewRoundInquiryId);

        // Round 1 keeps its answers and history, and the cancelled-round path cannot make a No disappear either.
        var round1 = (await w.Inquiries.GetAsync(w.Drawing, answered.Value.Id, Token)).Value!.Inquiry.Value;
        Assert.Equal(InquiryAnswer.No, round1.Lines[0].CurrentResponse!.Answer);
        Assert.Contains(round1.History, h => h.Action == InquiryAction.DispositionRecorded);
    }

    [Fact]
    public async Task Cancelling_an_inquiry_cannot_hide_a_no_answer_from_the_gate()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var answered = await WithNoAnswerAsync(w, id);

        var cancelled = (await w.Inquiries.CancelAsync(w.Drawing, answered.Value.Id, answered.Version, "دور جدید", Token)).Value!;

        Assert.Equal(1, (await w.Gates.GetAsync(w.Planning, id, Token)).Value!.PendingDispositions);
        Assert.True((await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, cancelled.Version, 1, DispositionKind.ReturnedUnchanged, "x", null, Token)).Succeeded);
        Assert.Equal(0, (await w.Gates.GetAsync(w.Planning, id, Token)).Value!.PendingDispositions);
    }

    [Fact]
    public async Task A_changed_answer_makes_the_old_disposition_stale_and_a_yes_needs_none()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var answered = await WithNoAnswerAsync(w, id);
        var disposed = (await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, answered.Version, 1, DispositionKind.ReturnedUnchanged, "x", null, Token)).Value!;
        Tick(w);

        // Warehouse changes its mind: Yes needs no disposition; then No again needs a NEW disposition.
        var yes = (await w.Inquiries.AnswerAsync(w.Warehouse, answered.Value.Id, disposed.Version, 1, InquiryAnswer.Yes, null, Token)).Value!;
        Assert.Equal(0, (await w.Gates.GetAsync(w.Planning, id, Token)).Value!.PendingDispositions);
        Tick(w);
        var no = (await w.Inquiries.AnswerAsync(w.Warehouse, answered.Value.Id, yes.Version, 1, InquiryAnswer.No, "again", Token)).Value!;
        Assert.Equal(1, (await w.Gates.GetAsync(w.Planning, id, Token)).Value!.PendingDispositions);
        Assert.Null(no.Value.Lines[0].CurrentDisposition);
        Assert.Single(no.Value.Lines[0].Dispositions);
    }

    [Fact]
    public async Task Planning_records_the_supply_route_for_a_no_answer_and_nobody_else_can()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var answered = await WithNoAnswerAsync(w, id);

        Assert.Equal(ValidationIssue.SupplyRouteInvalid, (await w.Inquiries.RecordSupplyRouteAsync(w.Planning, answered.Value.Id, answered.Version, 1, " ", true, Token)).Issue);
        Assert.Equal(OperationStatus.InvalidState, (await w.Inquiries.RecordSupplyRouteAsync(w.Planning, answered.Value.Id, answered.Version, 2, "x", true, Token)).Status);
        foreach (var user in new Principal?[] { w.Drawing, w.Warehouse, w.Manager, w.Qc, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Inquiries.RecordSupplyRouteAsync(user, answered.Value.Id, answered.Version, 1, "x", true, Token)).Status);
        }

        var route = await w.Inquiries.RecordSupplyRouteAsync(w.Planning, answered.Value.Id, answered.Version, 1, "خرید از تأمین‌کننده بیرونی", true, Token);

        Assert.True(route.Succeeded, route.Status + " " + route.Issue);
        Assert.True(route.Value!.Value.Lines[0].CurrentSupplyRoute!.ExternalPurchase);
        Assert.Equal(w.Planning.UserId, route.Value.Value.Lines[0].CurrentSupplyRoute!.DecidedByUserId);

        // A route does not replace the Drawing disposition.
        Assert.Equal(1, (await w.Gates.GetAsync(w.Planning, id, Token)).Value!.PendingDispositions);
    }

    [Fact]
    public async Task Unanswered_lines_of_a_sent_inquiry_block_the_gate_and_a_draft_does_not()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        await w.Contracts.DecideRequirementAsync(w.Planning, id, ContractRequirement.NotRequired, null, Token);
        Tick(w);
        var draft = await DraftWithLinesAsync(w, id, 2);
        Assert.True((await w.Gates.GetAsync(w.Planning, id, Token)).Value!.Satisfied);

        var sent = (await w.Inquiries.SendAsync(w.Drawing, draft.Value.Id, draft.Version, Token)).Value!;
        var status = (await w.Gates.GetAsync(w.Planning, id, Token)).Value!;
        Assert.Equal(2, status.UnansweredInquiryLines);
        Assert.Contains(GateBlocker.InquiryLinesUnanswered, status.Blockers);
        Assert.True(status.HasUnresolvedMaterialState);

        var first = (await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, sent.Version, 1, InquiryAnswer.Yes, null, Token)).Value!;
        Assert.Equal(1, (await w.Gates.GetAsync(w.Planning, id, Token)).Value!.UnansweredInquiryLines);
        await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, first.Version, 2, InquiryAnswer.Yes, null, Token);
        Assert.True((await w.Gates.GetAsync(w.Planning, id, Token)).Value!.Satisfied);
    }

    [Fact]
    public async Task Drawing_asks_planning_answers_drawing_closes_and_the_request_stays_under_drawing_review()
    {
        using var w = await StartAsync();
        var customer = (await w.Services.Customers.CreateAsync(w.Central, "Fictional Customer", Token)).Value!;
        var created = (await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "Q-1", WorkType.Make, "1405/07/20", null, null), Token)).Value!;
        var withPdf = (await w.Services.Documents.AttachRequestPdfAsync(w.Central, created.Value.Id, created.Version, "a.pdf", Pdf(), Token)).Value!;
        var sentReq = (await w.Services.Requests.SubmitToManagementAsync(w.Central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        var approved = (await w.Services.Lifecycle.ApproveAsync(w.Manager, sentReq.Value.Id, sentReq.Version, Token)).Value!;
        var forwarded = (await w.Services.Lifecycle.ForwardToDrawingAsync(w.Planning, approved.Value.Id, approved.Version, Token)).Value!;
        var id = forwarded.Value.Id;

        Assert.Equal(ValidationIssue.ClarificationTextInvalid, (await w.Clarifications.AskAsync(w.Drawing, id, " ", Token)).Issue);
        var asked = await w.Clarifications.AskAsync(w.Drawing, id, "قطر قطعه روی نقشه خوانا نیست؛ مقدار درست چند است؟", Token);
        Assert.True(asked.Succeeded, asked.Status + " " + asked.Issue);
        Tick(w);

        // Planning sees the outstanding question; the request stays under Drawing review; the gate reports it.
        var unresolved = (await w.Clarifications.ListUnresolvedAsync(w.Planning, Token)).Value!;
        Assert.Equal("Q-1", Assert.Single(unresolved).RequestNo);
        Assert.Equal(RequestStatus.UnderDrawingReview, (await w.Services.Requests.GetAsync(w.Planning, id, Token)).Value!.Request.Value.Status);
        Assert.Equal(1, (await w.Gates.GetAsync(w.Planning, id, Token)).Value!.UnresolvedClarifications);

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Clarifications.AnswerAsync(w.Drawing, asked.Value!.Value.Id, asked.Value.Version, "x", Token)).Status);
        Assert.Equal(ValidationIssue.ClarificationTextInvalid, (await w.Clarifications.AnswerAsync(w.Planning, asked.Value.Value.Id, asked.Value.Version, "", Token)).Issue);
        var answered = await w.Clarifications.AnswerAsync(w.Planning, asked.Value.Value.Id, asked.Value.Version, "۳۰ میلی‌متر طبق تأیید مشتری", Token);
        Assert.True(answered.Succeeded);
        Assert.Equal(ClarificationStatus.Answered, answered.Value!.Value.Status);
        Tick(w);

        // Only the asking party closes; Planning cannot, and the question stays unresolved for the gate until it is closed.
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Clarifications.CloseAsync(w.Planning, asked.Value.Value.Id, answered.Value.Version, null, Token)).Status);
        Assert.Equal(1, (await w.Gates.GetAsync(w.Planning, id, Token)).Value!.UnresolvedClarifications);
        var closed = await w.Clarifications.CloseAsync(w.Drawing, asked.Value.Value.Id, answered.Value.Version, "ممنون", Token);
        Assert.True(closed.Succeeded);
        Assert.Equal(0, (await w.Gates.GetAsync(w.Planning, id, Token)).Value!.UnresolvedClarifications);
        Assert.Equal(OperationStatus.InvalidState, (await w.Clarifications.CloseAsync(w.Drawing, asked.Value.Value.Id, closed.Value!.Version, null, Token)).Status);
        Assert.Equal(new[] { "ClarificationAsked", "ClarificationAnswered", "ClarificationClosed" }, w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode LIKE 'Clarification%' ORDER BY Id").ToArray());
    }

    [Fact]
    public async Task A_question_can_be_withdrawn_before_it_is_answered_and_only_while_drawing_reviews()
    {
        using var w = await StartAsync();
        var returned = await ReturnedAsync(w);

        // The request already returned from Drawing: a new question cannot be asked.
        Assert.Equal(ValidationIssue.ClarificationRequestState, (await w.Clarifications.AskAsync(w.Drawing, returned, "q", Token)).Issue);
        Assert.Equal(OperationStatus.NotFound, (await w.Clarifications.AskAsync(w.Drawing, 99999, "q", Token)).Status);
        Assert.Equal(0, Count(w, "DrawingClarifications"));
    }

    [Fact]
    public async Task Drawing_may_return_its_result_with_a_question_open_and_the_gate_still_blocks_release()
    {
        using var w = await StartAsync();
        var customer = (await w.Services.Customers.CreateAsync(w.Central, "Fictional Customer", Token)).Value!;
        var created = (await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "Q-2", WorkType.Make, "1405/07/20", null, null), Token)).Value!;
        var withPdf = (await w.Services.Documents.AttachRequestPdfAsync(w.Central, created.Value.Id, created.Version, "a.pdf", Pdf(), Token)).Value!;
        var sentReq = (await w.Services.Requests.SubmitToManagementAsync(w.Central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        var approved = (await w.Services.Lifecycle.ApproveAsync(w.Manager, sentReq.Value.Id, sentReq.Version, Token)).Value!;
        var forwarded = (await w.Services.Lifecycle.ForwardToDrawingAsync(w.Planning, approved.Value.Id, approved.Version, Token)).Value!;
        var id = forwarded.Value.Id;
        await w.Clarifications.AskAsync(w.Drawing, id, "سؤال باز", Token);
        var request = (await w.Services.Requests.GetAsync(w.Planning, id, Token)).Value!.Request;

        var returned = await w.Services.Lifecycle.RecordDrawingResultAsync(w.Drawing, id, request.Version, DrawingOutcome.FullyAccepted, null, Token);

        Assert.True(returned.Succeeded, returned.Status + " " + returned.Issue);
        await w.Contracts.DecideRequirementAsync(w.Planning, id, ContractRequirement.NotRequired, null, Token);
        var status = (await w.Gates.GetAsync(w.Planning, id, Token)).Value!;
        Assert.Equal(DefinitionBlock.None, status.DefinitionBlock);
        Assert.True(status.HasUnresolvedMaterialState);
        Assert.False(status.Satisfied);
        Assert.Equal(new[] { GateBlocker.ClarificationUnresolved }, status.Blockers);
    }

    [Fact]
    public async Task The_gate_is_readable_by_four_departments_only_and_unknown_requests_are_not_found()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);

        foreach (var user in new[] { w.Planning, w.Manager, w.Drawing, w.Warehouse })
        {
            Assert.True((await w.Gates.GetAsync(user, id, Token)).Succeeded);
        }

        foreach (var user in new Principal?[] { w.Central, w.Qc, w.Production, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Gates.GetAsync(user, id, Token)).Status);
        }

        Assert.Equal(OperationStatus.NotFound, (await w.Gates.GetAsync(w.Planning, 99999, Token)).Status);
        Assert.Equal(DefinitionBlock.ContractDecisionMissing, (await w.Gates.GetAsync(w.Planning, id, Token)).Value!.DefinitionBlock);
    }

    [Fact]
    public async Task Stale_versions_and_inactive_users_change_nothing()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var answered = await WithNoAnswerAsync(w, id);

        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Inquiries.RecordSupplyRouteAsync(w.Planning, answered.Value.Id, answered.Version + 4, 1, "x", false, Token)).Status);
        await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Drawing.UserId, false, Token);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Inquiries.RecordDispositionAsync(w.Drawing, answered.Value.Id, answered.Version, 1, DispositionKind.ReturnedUnchanged, "x", null, Token)).Status);
        Assert.Equal(0, Count(w, "RawMaterialInquiryDispositions"));
        Assert.Equal(0, Count(w, "RawMaterialInquirySupplyRoutes"));
    }
}
