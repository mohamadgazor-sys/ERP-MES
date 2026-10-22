using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The request screen's lifecycle actions against the real Application services (no window is opened): which buttons each user is offered, the reasons, the history rows.
public class RequestLifecycleViewModelTests
{
    private const string DateText = "۱۴۰۵/۰۷/۲۰";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional\n%%EOF\n");

    /// <summary>Central creates a request, attaches a PDF and submits it; returns the request id.</summary>
    private static async Task<long> SubmittedAsync(WorkflowHarness h)
    {
        var customer = (await h.Workflow.Customers.CreateAsync(h.Central, "مشتری نمونه (فرضی)", Token)).Value!.Value.Id;
        var central = h.RequestsFor(h.Central);
        await central.CreateAsync(customer, "P-1", WorkType.Make, DateText, null, null, Token);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await central.AttachPdfAsync("request.pdf", Pdf(), Token));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await central.SubmitAsync(Token));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        return central.Requests.Single().RequestId;
    }

    private static async Task<RequestsViewModel> OpenAsync(WorkflowHarness h, Principal user, long requestId)
    {
        var vm = h.RequestsFor(user);
        await vm.LoadAsync(Token);
        await vm.SelectAsync(vm.Requests.Single(r => r.RequestId == requestId), Token);
        return vm;
    }

    [Fact]
    public async Task Only_the_manager_is_offered_approve_and_reject_and_only_while_the_request_waits_for_management()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await SubmittedAsync(h);

        var central = await OpenAsync(h, h.Central, id);
        var staff = await OpenAsync(h, h.Management, id);
        var planning = await OpenAsync(h, h.Planning, id);
        var manager = await OpenAsync(h, h.Manager, id);

        Assert.False(central.HasWorkflowActions);
        Assert.False(staff.CanApprove);
        Assert.False(planning.HasWorkflowActions);
        Assert.True(manager.CanApprove);
        Assert.True(manager.CanReject);
        Assert.True(manager.HasWorkflowActions);
        Assert.False(manager.CanForwardToDrawing);
        Assert.False(await staff.ApproveAsync(Token));
        Assert.Equal(Messages.NotAuthorized, staff.Message);

        Assert.True(await manager.ApproveAsync(Token));

        Assert.Equal("درخواست تأیید شد و به برنامه‌ریزی می‌رسد.", manager.Message);
        Assert.Equal(Messages.StatusText(RequestStatus.MgmtApprovedAtPlanning), manager.Requests.Single().StatusText);
        Assert.False(manager.CanApprove);
        Assert.False(manager.HasWorkflowActions);
        Assert.False(await manager.ApproveAsync(Token));
        Assert.Equal(Messages.InvalidState, manager.Message);
    }

    [Fact]
    public async Task Rejecting_needs_a_reason_the_history_shows_it_and_planning_or_management_can_reopen()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await SubmittedAsync(h);
        var manager = await OpenAsync(h, h.Manager, id);

        Assert.False(await manager.RejectAsync("  ", Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.ReasonRequired, ERP.MES.Domain.Calendar.JalaliDateError.None), manager.Message);
        Assert.True(manager.CanReject);

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await manager.RejectAsync("خارج از دامنهٔ محصولات ما", Token));
        Assert.Equal(Messages.StatusText(RequestStatus.MgmtRejectedAtPlanning), manager.Requests.Single().StatusText);
        Assert.True(manager.HasHistory);
        var rejection = manager.History.Last();
        Assert.Equal(Messages.ActionText(RequestAction.Reject), rejection.ActionText);
        Assert.Equal(Messages.DepartmentText(DepartmentCodes.Management), rejection.DepartmentText);
        Assert.Equal("خارج از دامنهٔ محصولات ما", rejection.NoteText);
        Assert.Contains(Messages.StatusText(RequestStatus.MgmtRejectedAtPlanning), rejection.StatusChangeText, StringComparison.Ordinal);

        var planning = await OpenAsync(h, h.Planning, id);
        Assert.True(planning.CanReopen);
        Assert.False(planning.CanApprove);
        Assert.False(await planning.ReopenAsync("", Token));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await planning.ReopenAsync("مشتری دامنه را تأیید کرد", Token));
        Assert.Equal(Messages.StatusText(RequestStatus.SentToManagement), planning.Requests.Single().StatusText);
        Assert.Equal(3, planning.History.Count);
    }

    [Fact]
    public async Task Planning_forwards_drawing_returns_its_result_with_an_outcome_and_planning_can_send_it_back()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await SubmittedAsync(h);
        var manager = await OpenAsync(h, h.Manager, id);
        Assert.True(await manager.ApproveAsync(Token));
        h.Clock.Advance(TimeSpan.FromMinutes(1));

        var planning = await OpenAsync(h, h.Planning, id);
        Assert.True(planning.CanForwardToDrawing);
        Assert.False(planning.CanRecordDrawingResult);
        Assert.True(await planning.ForwardToDrawingAsync(Token));
        Assert.Equal(Messages.StatusText(RequestStatus.UnderDrawingReview), planning.Requests.Single().StatusText);
        h.Clock.Advance(TimeSpan.FromMinutes(1));

        var drawing = await OpenAsync(h, h.Drawing, id);
        Assert.True(drawing.CanRecordDrawingResult);
        Assert.False(drawing.CanForwardToDrawing);
        Assert.Equal(5, drawing.DrawingOutcomes.Count);
        Assert.False(await drawing.RecordDrawingResultAsync(DrawingOutcome.Rejected, null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.ReasonRequired, ERP.MES.Domain.Calendar.JalaliDateError.None), drawing.Message);
        Assert.False(await drawing.RecordDrawingResultAsync((DrawingOutcome)0, "x", Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.DrawingOutcomeInvalid, ERP.MES.Domain.Calendar.JalaliDateError.None), drawing.Message);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await drawing.RecordDrawingResultAsync(DrawingOutcome.MoreInformationNeeded, "اندازهٔ فلنج مشخص نیست", Token));
        Assert.False(drawing.HasWorkflowActions);
        var result = drawing.History.Last();
        Assert.Equal(Messages.OutcomeText(DrawingOutcome.MoreInformationNeeded), result.OutcomeText);
        Assert.Equal("اندازهٔ فلنج مشخص نیست", result.NoteText);
        Assert.Equal(Messages.DepartmentText(DepartmentCodes.Drawing), result.DepartmentText);

        planning = await OpenAsync(h, h.Planning, id);
        Assert.True(planning.CanSendBackToDrawing);
        Assert.False(await planning.SendBackToDrawingAsync(null, Token));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await planning.SendBackToDrawingAsync("اندازه از مشتری گرفته شد", Token));
        Assert.Equal(Messages.StatusText(RequestStatus.UnderDrawingReview), planning.Requests.Single().StatusText);
        Assert.Equal(new[] { RequestAction.Submit, RequestAction.Approve, RequestAction.ForwardToDrawing, RequestAction.RecordDrawingResult, RequestAction.SendBackToDrawing }
            .Select(Messages.ActionText).ToArray(), planning.History.Select(r => r.ActionText).ToArray());
    }

    [Fact]
    public async Task A_screen_that_is_out_of_date_gets_a_conflict_and_shows_the_current_state()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await SubmittedAsync(h);
        var first = await OpenAsync(h, h.Manager, id);
        var second = await OpenAsync(h, h.Manager, id);
        h.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(await first.ApproveAsync(Token));
        var secondTry = await second.RejectAsync("too late", Token);

        Assert.False(secondTry);
        Assert.Equal(Messages.Conflict, second.Message);
        Assert.Equal(Messages.StatusText(RequestStatus.MgmtApprovedAtPlanning), second.Requests.Single().StatusText);
        Assert.False(second.CanApprove);
    }

    [Fact]
    public async Task Without_a_selected_request_every_action_says_so_and_clearing_the_selection_clears_the_history()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await SubmittedAsync(h);
        var manager = await OpenAsync(h, h.Manager, id);
        Assert.True(manager.HasHistory);

        await manager.SelectAsync(null, Token);

        Assert.False(manager.HasHistory);
        Assert.True(manager.HasNoHistory);
        Assert.False(manager.HasWorkflowActions);
        Assert.False(await manager.ApproveAsync(Token));
        Assert.Equal(Messages.NothingSelected, manager.Message);
    }
}
