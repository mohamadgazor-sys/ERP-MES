using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// The request lifecycle after submission (Decision Log #29, #82) on a real SQLite file with the real authorization: who may do each action is decided by the Application from
// the database at the time of the call; every action changes the status, adds one history entry and one audit entry in one transaction, and is refused in any other status.
public class RequestLifecycleTests
{
    private const string InitialPassword = "Staff-Pass-Phrase-2";
    private const string OwnPassword = "My-Own-Pass-Phrase-3";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class World : IDisposable
    {
        public required SecurityFixture Fixture { get; init; }

        public required Principal Central { get; init; }

        public required Principal Manager { get; init; }

        public required Principal ManagementStaff { get; init; }

        public required Principal Planning { get; init; }

        public required Principal Drawing { get; init; }

        public required Principal Qc { get; init; }

        public required Principal Production { get; init; }

        public required WorkflowServices Services { get; init; }

        public Principal[] Everyone => [Central, Manager, ManagementStaff, Planning, Drawing, Qc, Production];

        public RequestWorkflowService Lifecycle => Services.Lifecycle;

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
            Central = await UserAsync(fixture, admin, "central.one", DepartmentCodes.Central, RoleCodes.Staff),
            Manager = await UserAsync(fixture, admin, "manager.one", DepartmentCodes.Management, RoleCodes.Manager),
            ManagementStaff = await UserAsync(fixture, admin, "management.staff", DepartmentCodes.Management, RoleCodes.Staff),
            Planning = await UserAsync(fixture, admin, "planning.one", DepartmentCodes.Planning, RoleCodes.Staff),
            Drawing = await UserAsync(fixture, admin, "drawing.one", DepartmentCodes.Drawing, RoleCodes.Staff),
            Qc = await UserAsync(fixture, admin, "qc.one", DepartmentCodes.Qc, RoleCodes.Staff),
            Production = await UserAsync(fixture, admin, "production.one", DepartmentCodes.Production, RoleCodes.Staff),
            Services = WorkflowServices.Create(fixture.Db.ContextFactory, fixture.Services.Authorization, PersistenceTestData.Dates, fixture.Clock),
        };
    }

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional\n%%EOF\n");

    private static void Tick(World w) => w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));

    /// <summary>A MAKE request that Central created, attached the PDF to and submitted: status SENT_TO_MANAGEMENT.</summary>
    private static async Task<Stored<Request>> SubmittedAsync(World w, string requestNo = "P-1")
    {
        var customer = (await w.Services.Customers.CreateAsync(w.Central, "Fictional Customer " + requestNo, Token)).Value!;
        var created = await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, requestNo, WorkType.Make, "1405/07/20", null, null), Token);
        Tick(w);
        var withPdf = await w.Services.Documents.AttachRequestPdfAsync(w.Central, created.Value!.Value.Id, created.Value.Version, "a.pdf", Pdf(), Token);
        Tick(w);
        var submitted = await w.Services.Requests.SubmitToManagementAsync(w.Central, withPdf.Value!.Value.Id, withPdf.Value.Version, Token);
        Assert.True(submitted.Succeeded, submitted.Status + " " + submitted.Issue);
        Tick(w);
        return submitted.Value!;
    }

    private static async Task<Stored<Request>> ApprovedAsync(World w)
    {
        var sent = await SubmittedAsync(w);
        var approved = await w.Lifecycle.ApproveAsync(w.Manager, sent.Value.Id, sent.Version, Token);
        Assert.True(approved.Succeeded);
        Tick(w);
        return approved.Value!;
    }

    private static async Task<Request> LoadAsync(World w, long id) => (await w.Services.Requests.GetAsync(w.Qc, id, Token)).Value!.Request.Value;

    private static long Count(World w, string tableAndWhere) => long.Parse(w.Fixture.Db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task The_whole_lifecycle_runs_with_the_right_department_at_each_step_and_is_stored_audited_and_survives_a_restart()
    {
        using var w = await StartAsync();
        var sent = await SubmittedAsync(w);

        var approved = (await w.Lifecycle.ApproveAsync(w.Manager, sent.Value.Id, sent.Version, Token)).Value!;
        Tick(w);
        var forwarded = (await w.Lifecycle.ForwardToDrawingAsync(w.Planning, sent.Value.Id, approved.Version, Token)).Value!;
        Tick(w);
        var returned = await w.Lifecycle.RecordDrawingResultAsync(w.Drawing, sent.Value.Id, forwarded.Version, DrawingOutcome.MoreInformationNeeded, "Dimension of the flange is missing", Token);
        Assert.True(returned.Succeeded, returned.Status + " " + returned.Issue);
        Tick(w);
        var backAgain = await w.Lifecycle.SendBackToDrawingAsync(w.Planning, sent.Value.Id, returned.Value!.Version, "Customer sent the missing dimension", Token);
        Assert.True(backAgain.Succeeded);
        Tick(w);
        var done = await w.Lifecycle.RecordDrawingResultAsync(w.Drawing, sent.Value.Id, backAgain.Value!.Version, DrawingOutcome.FullyAccepted, null, Token);
        Assert.True(done.Succeeded);

        var restarted = WorkflowServices.Create(w.Fixture.Db.ContextFactory, w.Fixture.Services.Authorization, PersistenceTestData.Dates, w.Fixture.Clock);
        var request = (await restarted.Requests.GetAsync(w.Qc, sent.Value.Id, Token)).Value!.Request.Value;
        Assert.Equal(RequestStatus.DrawingReturnedToPlanning, request.Status);
        Assert.Equal(
            new[] { RequestAction.Submit, RequestAction.Approve, RequestAction.ForwardToDrawing, RequestAction.RecordDrawingResult, RequestAction.SendBackToDrawing, RequestAction.RecordDrawingResult },
            request.WorkflowHistory.Select(e => e.Action).ToArray());
        Assert.Equal(new[] { "CENTRAL", "MANAGEMENT", "PLANNING", "DRAWING", "PLANNING", "DRAWING" }, request.WorkflowHistory.Select(e => e.ActionByDepartmentCode).ToArray());
        Assert.Equal(new[] { w.Central.UserId, w.Manager.UserId, w.Planning.UserId, w.Drawing.UserId, w.Planning.UserId, w.Drawing.UserId }, request.WorkflowHistory.Select(e => e.ActionByUserId).ToArray());
        Assert.Equal(DrawingOutcome.MoreInformationNeeded, request.WorkflowHistory[3].Outcome);
        Assert.Equal("Dimension of the flange is missing", request.WorkflowHistory[3].Note);
        Assert.Equal(DrawingOutcome.FullyAccepted, request.WorkflowHistory[5].Outcome);

        Assert.Equal(
            new[] { "RequestSubmitted", "RequestApproved", "RequestForwardedToDrawing", "RequestDrawingResultRecorded", "RequestSentBackToDrawing", "RequestDrawingResultRecorded" },
            w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode LIKE 'Request%' AND ActionCode <> 'RequestPdfAttached' ORDER BY Id").ToArray());
        Assert.Equal(6, Count(w, "RequestWorkflowEntries"));
    }

    [Fact]
    public async Task Rejecting_needs_a_reason_and_a_rejected_request_can_be_reopened_by_planning_or_management_with_a_reason()
    {
        using var w = await StartAsync();
        var sent = await SubmittedAsync(w);

        foreach (var reason in new string?[] { null, "", "   " })
        {
            var refused = await w.Lifecycle.RejectAsync(w.Manager, sent.Value.Id, sent.Version, reason, Token);
            Assert.Equal(OperationStatus.ValidationFailed, refused.Status);
            Assert.Equal(ValidationIssue.ReasonRequired, refused.Issue);
        }

        Assert.Equal(ValidationIssue.ReasonTooLong, (await w.Lifecycle.RejectAsync(w.Manager, sent.Value.Id, sent.Version, new string('x', 2001), Token)).Issue);
        Assert.Equal(RequestStatus.SentToManagement, (await LoadAsync(w, sent.Value.Id)).Status);
        Assert.Equal(1, Count(w, "RequestWorkflowEntries"));

        var rejected = await w.Lifecycle.RejectAsync(w.Manager, sent.Value.Id, sent.Version, "  Outside our range  ", Token);
        Assert.True(rejected.Succeeded);
        Assert.Equal(RequestStatus.MgmtRejectedAtPlanning, rejected.Value!.Value.Status);
        Tick(w);

        Assert.Equal(ValidationIssue.ReasonRequired, (await w.Lifecycle.ReopenAsync(w.Planning, sent.Value.Id, rejected.Value.Version, " ", Token)).Issue);
        foreach (var user in new[] { w.Central, w.Drawing, w.Qc, w.Production })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Lifecycle.ReopenAsync(user, sent.Value.Id, rejected.Value.Version, "again", Token)).Status);
        }

        var reopened = await w.Lifecycle.ReopenAsync(w.Planning, sent.Value.Id, rejected.Value.Version, "Customer confirmed the range", Token);
        Assert.True(reopened.Succeeded);
        Assert.Equal(RequestStatus.SentToManagement, reopened.Value!.Value.Status);
        Tick(w);

        // Management (any role in that department) may reopen too; here the request is rejected again first.
        var rejectedAgain = (await w.Lifecycle.RejectAsync(w.Manager, sent.Value.Id, reopened.Value.Version, "Still outside", Token)).Value!;
        Tick(w);
        Assert.True((await w.Lifecycle.ReopenAsync(w.ManagementStaff, sent.Value.Id, rejectedAgain.Version, "Management reconsiders", Token)).Succeeded);

        var history = (await LoadAsync(w, sent.Value.Id)).WorkflowHistory;
        Assert.Equal(new[] { RequestAction.Submit, RequestAction.Reject, RequestAction.Reopen, RequestAction.Reject, RequestAction.Reopen }, history.Select(e => e.Action).ToArray());
        Assert.Equal("Outside our range", history[1].Note);
        Assert.Equal("Customer confirmed the range", history[2].Note);
    }

    [Fact]
    public async Task Only_the_manager_role_in_management_approves_or_rejects()
    {
        using var w = await StartAsync();
        var sent = await SubmittedAsync(w);

        foreach (var user in new[] { w.Central, w.ManagementStaff, w.Planning, w.Drawing, w.Qc, w.Production })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Lifecycle.ApproveAsync(user, sent.Value.Id, sent.Version, Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Lifecycle.RejectAsync(user, sent.Value.Id, sent.Version, "reason", Token)).Status);
        }

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Lifecycle.ApproveAsync(null, sent.Value.Id, sent.Version, Token)).Status);
        Assert.Equal(RequestStatus.SentToManagement, (await LoadAsync(w, sent.Value.Id)).Status);
        Assert.Equal(1, Count(w, "RequestWorkflowEntries"));
        Assert.True((await w.Lifecycle.ApproveAsync(w.Manager, sent.Value.Id, sent.Version, Token)).Succeeded);
    }

    [Fact]
    public async Task Only_planning_forwards_and_sends_back_and_only_drawing_returns_its_result()
    {
        using var w = await StartAsync();
        var approved = await ApprovedAsync(w);

        foreach (var user in w.Everyone.Where(u => u.UserId != w.Planning.UserId))
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Lifecycle.ForwardToDrawingAsync(user, approved.Value.Id, approved.Version, Token)).Status);
        }

        var forwarded = (await w.Lifecycle.ForwardToDrawingAsync(w.Planning, approved.Value.Id, approved.Version, Token)).Value!;
        Tick(w);
        foreach (var user in w.Everyone.Where(u => u.UserId != w.Drawing.UserId))
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Lifecycle.RecordDrawingResultAsync(user, approved.Value.Id, forwarded.Version, DrawingOutcome.FullyAccepted, null, Token)).Status);
        }

        var returned = (await w.Lifecycle.RecordDrawingResultAsync(w.Drawing, approved.Value.Id, forwarded.Version, DrawingOutcome.Rejected, "Cannot be machined", Token)).Value!;
        Tick(w);
        foreach (var user in w.Everyone.Where(u => u.UserId != w.Planning.UserId))
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Lifecycle.SendBackToDrawingAsync(user, approved.Value.Id, returned.Version, "reason", Token)).Status);
        }

        Assert.Equal(RequestStatus.DrawingReturnedToPlanning, (await LoadAsync(w, approved.Value.Id)).Status);
        Assert.Equal(4, Count(w, "RequestWorkflowEntries"));
    }

    [Fact]
    public async Task The_drawing_result_needs_a_valid_outcome_and_a_note_unless_fully_accepted()
    {
        using var w = await StartAsync();
        var approved = await ApprovedAsync(w);
        var forwarded = (await w.Lifecycle.ForwardToDrawingAsync(w.Planning, approved.Value.Id, approved.Version, Token)).Value!;
        Tick(w);

        Assert.Equal(ValidationIssue.DrawingOutcomeInvalid, (await w.Lifecycle.RecordDrawingResultAsync(w.Drawing, approved.Value.Id, forwarded.Version, (DrawingOutcome)0, "x", Token)).Issue);
        foreach (var outcome in new[] { DrawingOutcome.PartiallyAccepted, DrawingOutcome.Rejected, DrawingOutcome.MoreInformationNeeded, DrawingOutcome.ReturnedForCorrection })
        {
            Assert.Equal(ValidationIssue.ReasonRequired, (await w.Lifecycle.RecordDrawingResultAsync(w.Drawing, approved.Value.Id, forwarded.Version, outcome, "  ", Token)).Issue);
        }

        Assert.Equal(RequestStatus.UnderDrawingReview, (await LoadAsync(w, approved.Value.Id)).Status);
        Assert.True((await w.Lifecycle.RecordDrawingResultAsync(w.Drawing, approved.Value.Id, forwarded.Version, DrawingOutcome.FullyAccepted, null, Token)).Succeeded);
    }

    [Fact]
    public async Task An_action_in_the_wrong_status_is_refused_and_writes_nothing()
    {
        using var w = await StartAsync();
        var sent = await SubmittedAsync(w);
        var entriesBefore = Count(w, "RequestWorkflowEntries");
        var auditBefore = Count(w, "AuditLog");

        // The request is SENT_TO_MANAGEMENT: only approve/reject apply.
        Assert.Equal(OperationStatus.InvalidState, (await w.Lifecycle.ForwardToDrawingAsync(w.Planning, sent.Value.Id, sent.Version, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Lifecycle.RecordDrawingResultAsync(w.Drawing, sent.Value.Id, sent.Version, DrawingOutcome.FullyAccepted, null, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Lifecycle.ReopenAsync(w.Planning, sent.Value.Id, sent.Version, "reason", Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Lifecycle.SendBackToDrawingAsync(w.Planning, sent.Value.Id, sent.Version, "reason", Token)).Status);

        // A decided request cannot be decided again.
        var approved = (await w.Lifecycle.ApproveAsync(w.Manager, sent.Value.Id, sent.Version, Token)).Value!;
        Tick(w);
        Assert.Equal(OperationStatus.InvalidState, (await w.Lifecycle.ApproveAsync(w.Manager, sent.Value.Id, approved.Version, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Lifecycle.RejectAsync(w.Manager, sent.Value.Id, approved.Version, "late", Token)).Status);

        Assert.Equal(RequestStatus.MgmtApprovedAtPlanning, (await LoadAsync(w, sent.Value.Id)).Status);
        Assert.Equal(entriesBefore + 1, Count(w, "RequestWorkflowEntries"));
        Assert.Equal(auditBefore + 1, Count(w, "AuditLog"));
    }

    [Fact]
    public async Task A_stale_version_is_a_conflict_an_unknown_request_is_not_found_and_two_decisions_cannot_both_win()
    {
        using var w = await StartAsync();
        var sent = await SubmittedAsync(w);

        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Lifecycle.ApproveAsync(w.Manager, sent.Value.Id, sent.Version + 9, Token)).Status);
        Assert.Equal(OperationStatus.NotFound, (await w.Lifecycle.ApproveAsync(w.Manager, 4242, 1, Token)).Status);

        // Two managers opened the request at the same version: the first decision wins, the second is told the request changed.
        var first = await w.Lifecycle.ApproveAsync(w.Manager, sent.Value.Id, sent.Version, Token);
        var second = await w.Lifecycle.RejectAsync(w.Manager, sent.Value.Id, sent.Version, "too late", Token);

        Assert.True(first.Succeeded);
        Assert.Equal(OperationStatus.ConcurrencyConflict, second.Status);
        Assert.Equal(RequestStatus.MgmtApprovedAtPlanning, (await LoadAsync(w, sent.Value.Id)).Status);
        Assert.Equal(2, Count(w, "RequestWorkflowEntries"));
    }

    [Fact]
    public async Task A_deactivated_or_unknown_user_is_denied_even_with_a_stale_principal()
    {
        using var w = await StartAsync();
        var sent = await SubmittedAsync(w);

        await w.Fixture.Services.Accounts.SetActiveAsync(await w.Fixture.SignInAsync("admin.one", "Admin-Pass-Phrase-1", Token), w.Manager.UserId, false, Token);

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Lifecycle.ApproveAsync(w.Manager, sent.Value.Id, sent.Version, Token)).Status);
        Assert.Equal(RequestActionCapabilities.None, await w.Lifecycle.GetCapabilitiesAsync(w.Manager, Token));
        Assert.Equal(RequestActionCapabilities.None, await w.Lifecycle.GetCapabilitiesAsync(null, Token));
        Assert.Equal(RequestStatus.SentToManagement, (await LoadAsync(w, sent.Value.Id)).Status);
    }

    [Fact]
    public async Task The_capabilities_follow_department_and_role()
    {
        using var w = await StartAsync();

        Assert.Equal(new RequestActionCapabilities(false, false, false, false), await w.Lifecycle.GetCapabilitiesAsync(w.Central, Token));
        Assert.Equal(new RequestActionCapabilities(true, true, false, false), await w.Lifecycle.GetCapabilitiesAsync(w.Manager, Token));
        Assert.Equal(new RequestActionCapabilities(false, true, false, false), await w.Lifecycle.GetCapabilitiesAsync(w.ManagementStaff, Token));
        Assert.Equal(new RequestActionCapabilities(false, true, true, false), await w.Lifecycle.GetCapabilitiesAsync(w.Planning, Token));
        Assert.Equal(new RequestActionCapabilities(false, false, false, true), await w.Lifecycle.GetCapabilitiesAsync(w.Drawing, Token));
        Assert.Equal(RequestActionCapabilities.None, await w.Lifecycle.GetCapabilitiesAsync(w.Qc, Token));
        Assert.Equal(RequestActionCapabilities.None, await w.Lifecycle.GetCapabilitiesAsync(w.Production, Token));
    }

    [Fact]
    public async Task The_history_table_is_append_only_and_references_users_and_requests()
    {
        using var w = await StartAsync();
        var sent = await SubmittedAsync(w);
        Assert.True((await w.Lifecycle.ApproveAsync(w.Manager, sent.Value.Id, sent.Version, Token)).Succeeded);

        // The domain exposes no way to change an entry, and the persistence layer refuses to save a changed or deleted one.
        await using var context = w.Fixture.Db.NewContext();
        var request = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include(context.Requests, r => r.WorkflowHistory), r => r.Id == sent.Value.Id, Token);
        context.RequestWorkflowEntries.Remove(request.WorkflowHistory[0]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(Token));

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => w.Fixture.Db.Execute("DELETE FROM Requests"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => w.Fixture.Db.Execute("DELETE FROM Users"));
        Assert.Empty(w.Fixture.Db.Query("PRAGMA foreign_key_check"));
    }
}
