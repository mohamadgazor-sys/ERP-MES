using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// Raw-material availability inquiries (Decision Log #86, #88) on a real SQLite file with the real authorization.
public class InquiryServiceTests
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

    [Fact]
    public async Task Drawing_creates_fills_and_sends_an_inquiry_and_warehouse_answers_each_line_with_history_and_audit()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var sent = await SentAsync(w, id);
        Assert.Equal(InquiryStatus.Sent, sent.Value.Status);
        Assert.Equal(1, sent.Value.RoundNo);

        var first = await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, sent.Version, 1, InquiryAnswer.Yes, null, Token);
        Assert.True(first.Succeeded, first.Status + " " + first.Issue);
        Assert.Equal(InquiryStatus.PartiallyAnswered, first.Value!.Value.Status);
        Tick(w);
        var second = await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, first.Value.Version, 2, InquiryAnswer.No, "فقط ۵ کیلو موجود است", Token);

        Assert.Equal(InquiryStatus.Answered, second.Value!.Value.Status);
        var lines = second.Value.Value.Lines;
        Assert.Equal(InquiryAnswer.Yes, lines[0].CurrentResponse!.Answer);
        Assert.Equal(InquiryAnswer.No, lines[1].CurrentResponse!.Answer);
        Assert.Equal("فقط ۵ کیلو موجود است", lines[1].CurrentResponse!.Note);
        Assert.Equal(w.Warehouse.UserId, lines[1].CurrentResponse!.RespondedByUserId);

        // The inquiry changes no request status and decides nothing.
        Assert.Equal(RequestStatus.DrawingReturnedToPlanning, (await w.Services.Requests.GetAsync(w.Planning, id, Token)).Value!.Request.Value.Status);
        Assert.Equal(
            new[] { "InquiryCreated", "InquiryLineAdded", "InquiryLineAdded", "InquirySent", "InquiryAnswered", "InquiryAnswered" },
            w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode LIKE 'Inquiry%' ORDER BY Id").ToArray());
    }

    [Fact]
    public async Task A_changed_answer_is_a_new_row_and_the_earlier_one_is_kept()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var sent = await SentAsync(w, id, 1);
        var no = (await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, sent.Version, 1, InquiryAnswer.No, "نیست", Token)).Value!;
        Tick(w);

        var yes = (await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, no.Version, 1, InquiryAnswer.Yes, "رسید", Token)).Value!;

        var line = yes.Value.Lines.Single();
        Assert.Equal(2, line.Responses.Count);
        Assert.Equal(InquiryAnswer.No, line.Responses[0].Answer);
        Assert.Equal(InquiryAnswer.Yes, line.CurrentResponse!.Answer);
        Assert.Equal(2, Count(w, "RawMaterialInquiryResponses"));
        Assert.Equal(new[] { "Answered", "AnswerChanged" }, yes.Value.History.Where(h => h.Answer is not null).Select(h => h.Action.ToString()).ToArray());
    }

    [Fact]
    public async Task Inquiries_are_only_possible_while_drawing_reviews_or_after_it_returned_the_request()
    {
        using var w = await StartAsync();
        var customer = (await w.Services.Customers.CreateAsync(w.Central, "Fictional Customer", Token)).Value!;
        var created = (await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "X-1", WorkType.Make, "1405/07/20", null, null), Token)).Value!;

        Assert.Equal(ValidationIssue.InquiryRequestState, (await w.Inquiries.CreateAsync(w.Drawing, created.Value.Id, null, Token)).Issue);
        Assert.Equal(0, Count(w, "RawMaterialInquiries"));
        Assert.Equal(OperationStatus.NotFound, (await w.Inquiries.CreateAsync(w.Drawing, 99999, null, Token)).Status);
    }

    [Fact]
    public async Task A_repair_request_can_also_have_an_inquiry()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w, WorkType.Repair);

        Assert.True((await w.Inquiries.CreateAsync(w.Drawing, id, null, Token)).Succeeded);
    }

    [Fact]
    public async Task Rounds_count_up_per_request_and_a_cancelled_round_is_replaced_by_a_new_one()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var sent = await SentAsync(w, id, 1);

        Assert.Equal(ValidationIssue.ReasonRequired, (await w.Inquiries.CancelAsync(w.Drawing, sent.Value.Id, sent.Version, " ", Token)).Issue);
        var cancelled = (await w.Inquiries.CancelAsync(w.Drawing, sent.Value.Id, sent.Version, "مشخصات اصلاح شد", Token)).Value!;
        Assert.Equal(InquiryStatus.Cancelled, cancelled.Value.Status);
        Tick(w);
        var next = (await w.Inquiries.CreateAsync(w.Drawing, id, null, Token)).Value!;

        Assert.Equal(2, next.Value.RoundNo);
        Assert.Single((await w.Inquiries.GetAsync(w.Drawing, sent.Value.Id, Token)).Value!.Inquiry.Value.Lines);
        Assert.Equal(OperationStatus.InvalidState, (await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, cancelled.Version, 1, InquiryAnswer.Yes, null, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Inquiries.CancelAsync(w.Drawing, sent.Value.Id, cancelled.Version, "again", Token)).Status);
    }

    [Fact]
    public async Task Lines_are_validated_and_only_a_draft_can_change_and_an_empty_draft_cannot_be_sent()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var draft = (await w.Inquiries.CreateAsync(w.Drawing, id, null, Token)).Value!;
        var unit = await KgUnitAsync(w);

        Assert.Equal(ValidationIssue.InquiryNoLines, (await w.Inquiries.SendAsync(w.Drawing, draft.Value.Id, draft.Version, Token)).Issue);
        Assert.Equal(ValidationIssue.InquiryLineInvalid, (await w.Inquiries.AddLineAsync(w.Drawing, draft.Value.Id, draft.Version, Line(grade: " ", unitId: unit), Token)).Issue);
        Assert.Equal(ValidationIssue.InquiryLineInvalid, (await w.Inquiries.AddLineAsync(w.Drawing, draft.Value.Id, draft.Version, Line(form: new string('f', 101), unitId: unit), Token)).Issue);
        Assert.Equal(ValidationIssue.InquiryLineInvalid, (await w.Inquiries.AddLineAsync(w.Drawing, draft.Value.Id, draft.Version, Line(unitId: unit) with { DiameterMm = 0m }, Token)).Issue);
        Assert.Equal(ValidationIssue.QuantityInvalid, (await w.Inquiries.AddLineAsync(w.Drawing, draft.Value.Id, draft.Version, Line(unitId: unit) with { RequiredQuantity = 0m }, Token)).Issue);
        Assert.Equal(ValidationIssue.UnitNotFound, (await w.Inquiries.AddLineAsync(w.Drawing, draft.Value.Id, draft.Version, Line(unitId: 987654), Token)).Issue);
        Assert.Equal(0, Count(w, "RawMaterialInquiryLines"));

        // Dimensions may be left empty (not applicable).
        var plate = (await w.Inquiries.AddLineAsync(w.Drawing, draft.Value.Id, draft.Version, Line(unitId: unit) with { DiameterMm = null, LengthMm = null, OtherDimension = "۱۰×۲۰۰×۳۰۰" }, Token)).Value!;
        var removed = (await w.Inquiries.RemoveLineAsync(w.Drawing, draft.Value.Id, plate.Version, 1, Token)).Value!;
        Assert.Empty(removed.Value.Lines);
        Assert.Equal(0, Count(w, "RawMaterialInquiryLines"));

        var again = (await w.Inquiries.AddLineAsync(w.Drawing, draft.Value.Id, removed.Version, Line(unitId: unit), Token)).Value!;
        Assert.Equal(2, again.Value.Lines.Single().LineNo);
        var sent = (await w.Inquiries.SendAsync(w.Drawing, draft.Value.Id, again.Version, Token)).Value!;
        Assert.Equal(OperationStatus.InvalidState, (await w.Inquiries.AddLineAsync(w.Drawing, draft.Value.Id, sent.Version, Line(unitId: unit), Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Inquiries.RemoveLineAsync(w.Drawing, draft.Value.Id, sent.Version, 2, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Inquiries.SendAsync(w.Drawing, draft.Value.Id, sent.Version, Token)).Status);
    }

    [Fact]
    public async Task Closing_needs_every_line_answered_and_a_draft_cannot_be_answered()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var draft = await DraftWithLinesAsync(w, id, 1);
        Assert.Equal(OperationStatus.InvalidState, (await w.Inquiries.AnswerAsync(w.Warehouse, draft.Value.Id, draft.Version, 1, InquiryAnswer.Yes, null, Token)).Status);

        var sent = (await w.Inquiries.SendAsync(w.Drawing, draft.Value.Id, draft.Version, Token)).Value!;
        Assert.Equal(OperationStatus.InvalidState, (await w.Inquiries.CloseAsync(w.Drawing, sent.Value.Id, sent.Version, null, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, sent.Version, 9, InquiryAnswer.Yes, null, Token)).Status);
        var answered = (await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, sent.Version, 1, InquiryAnswer.No, null, Token)).Value!;

        var closed = await w.Inquiries.CloseAsync(w.Drawing, sent.Value.Id, answered.Version, "بررسی شد", Token);
        Assert.Equal(InquiryStatus.Closed, closed.Value!.Value.Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, closed.Value.Version, 1, InquiryAnswer.Yes, null, Token)).Status);
        Assert.Equal(RequestStatus.DrawingReturnedToPlanning, (await w.Services.Requests.GetAsync(w.Planning, id, Token)).Value!.Request.Value.Status);
    }

    [Fact]
    public async Task Only_drawing_manages_and_only_warehouse_answers_and_only_those_two_read()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var sent = await SentAsync(w, id);

        foreach (var user in new Principal?[] { w.Warehouse, w.Planning, w.Manager, w.Qc, w.Central, w.Production, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Inquiries.CreateAsync(user, id, null, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Inquiries.AddLineAsync(user, sent.Value.Id, sent.Version, Line(), Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Inquiries.SendAsync(user, sent.Value.Id, sent.Version, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Inquiries.CancelAsync(user, sent.Value.Id, sent.Version, "x", Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Inquiries.CloseAsync(user, sent.Value.Id, sent.Version, null, Token)).Status);
        }

        foreach (var user in new Principal?[] { w.Drawing, w.Planning, w.Manager, w.Qc, w.Central, w.Production, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Inquiries.AnswerAsync(user, sent.Value.Id, sent.Version, 1, InquiryAnswer.Yes, null, Token)).Status);
        }

        // Planning and Management read inquiries (owner decision D8, #95) but cannot edit, send, cancel, close or answer them (checked above).
        foreach (var reader in new[] { w.Drawing, w.Warehouse, w.Planning, w.Manager })
        {
            Assert.True((await w.Inquiries.ListRequestsAsync(reader, Token)).Succeeded);
            Assert.True((await w.Inquiries.GetAsync(reader, sent.Value.Id, Token)).Succeeded);
            Assert.True((await w.Inquiries.ListForRequestAsync(reader, id, Token)).Succeeded);
        }

        foreach (var other in new Principal?[] { w.Qc, w.Central, w.Production, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Inquiries.ListRequestsAsync(other, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Inquiries.GetAsync(other, sent.Value.Id, Token)).Status);
            Assert.Equal(InquiryCapabilities.None, await w.Inquiries.GetCapabilitiesAsync(other, Token));
        }

        Assert.Equal(new InquiryCapabilities(true, true, false, false), await w.Inquiries.GetCapabilitiesAsync(w.Drawing, Token));
        Assert.Equal(new InquiryCapabilities(true, false, true, false), await w.Inquiries.GetCapabilitiesAsync(w.Warehouse, Token));
        Assert.Equal(new InquiryCapabilities(true, false, false, true), await w.Inquiries.GetCapabilitiesAsync(w.Planning, Token));
        Assert.Equal(new InquiryCapabilities(true, false, false, false), await w.Inquiries.GetCapabilitiesAsync(w.Manager, Token));
        Assert.Equal(0, Count(w, "RawMaterialInquiryResponses"));
    }

    [Fact]
    public async Task A_deactivated_user_is_refused_even_with_an_old_principal()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var sent = await SentAsync(w, id, 1);
        await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Warehouse.UserId, false, Token);
        await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Drawing.UserId, false, Token);

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, sent.Version, 1, InquiryAnswer.Yes, null, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Inquiries.CancelAsync(w.Drawing, sent.Value.Id, sent.Version, "x", Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Inquiries.GetAsync(w.Warehouse, sent.Value.Id, Token)).Status);
        Assert.Equal(0, Count(w, "RawMaterialInquiryResponses"));
    }

    [Fact]
    public async Task A_stale_version_conflicts_and_changes_nothing()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var sent = await SentAsync(w, id, 2);
        var historyBefore = Count(w, "RawMaterialInquiryHistory");

        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, sent.Version + 3, 1, InquiryAnswer.Yes, null, Token)).Status);
        Assert.True((await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, sent.Version, 1, InquiryAnswer.Yes, null, Token)).Succeeded);

        // A second Warehouse view that still holds the old version cannot answer the other line.
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, sent.Version, 2, InquiryAnswer.No, null, Token)).Status);
        Assert.Equal(OperationStatus.NotFound, (await w.Inquiries.AnswerAsync(w.Warehouse, 99999, 1, 1, InquiryAnswer.Yes, null, Token)).Status);
        Assert.Equal(historyBefore + 1, Count(w, "RawMaterialInquiryHistory"));
        Assert.Equal(1, Count(w, "RawMaterialInquiryResponses"));
    }

    [Fact]
    public async Task Answers_and_history_are_append_only_and_everything_survives_a_restart()
    {
        using var w = await StartAsync();
        var id = await ReturnedAsync(w);
        var sent = await SentAsync(w, id, 1);
        await w.Inquiries.AnswerAsync(w.Warehouse, sent.Value.Id, sent.Version, 1, InquiryAnswer.No, "x", Token);

        await using (var context = w.Fixture.Db.NewContext())
        {
            var response = context.RawMaterialInquiryResponses.First();
            context.Entry(response).Property(r => r.Note).CurrentValue = "changed";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Token));
        }

        await using (var context = w.Fixture.Db.NewContext())
        {
            context.RawMaterialInquiryHistory.Remove(context.RawMaterialInquiryHistory.First());
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Token));
        }

        var restarted = WorkflowServices.Create(w.Fixture.Db.ContextFactory, w.Fixture.Services.Authorization, PersistenceTestData.Dates, w.Fixture.Clock);
        var detail = (await restarted.Inquiries.GetAsync(w.Warehouse, sent.Value.Id, Token)).Value!;
        Assert.Equal(InquiryStatus.Answered, detail.Inquiry.Value.Status);
        Assert.Equal("x", detail.Inquiry.Value.Lines.Single().CurrentResponse!.Note);
        Assert.Equal(4, detail.Inquiry.Value.History.Count);
        var list = (await restarted.Inquiries.ListRequestsAsync(w.Drawing, Token)).Value!.Single();
        Assert.Equal(InquiryStatus.Answered, list.LatestStatus);
    }
}
