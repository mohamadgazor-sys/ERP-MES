using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Tests.Requests;

// The request-level lifecycle (Decision Log #29, #82): each allowed transition changes the status and adds exactly one history entry; every other status refuses it.
public class RequestWorkflowTests
{
    private static readonly WorkflowActor Manager = new(11, "MANAGEMENT");
    private static readonly WorkflowActor Planner = new(12, "PLANNING");
    private static readonly WorkflowActor Drafter = new(13, "DRAWING");

    private static DateTime At(int minutes) => RequestFixtures.Now.AddMinutes(minutes);

    private static Request InStatus(RequestStatus status)
    {
        var request = RequestFixtures.NewRequest();
        request.SetStatus(status);
        return request;
    }

    [Fact]
    public void The_whole_happy_path_adds_one_history_entry_per_action_in_order()
    {
        var request = RequestFixtures.NewRequest().WithPdf();

        request.SubmitToManagement(RequestFixtures.Actor, At(1));
        request.Approve(Manager, At(2));
        request.ForwardToDrawing(Planner, At(3));
        request.RecordDrawingResult(Drafter, DrawingOutcome.PartiallyAccepted, "  Section B cannot be machined  ", At(4));
        request.SendBackToDrawing(Planner, "Customer supplied a corrected drawing", At(5));
        request.RecordDrawingResult(Drafter, DrawingOutcome.FullyAccepted, null, At(6));

        Assert.Equal(RequestStatus.DrawingReturnedToPlanning, request.Status);
        Assert.Equal(At(6), request.UpdatedAtUtc);
        Assert.Equal(
            new[] { RequestAction.Submit, RequestAction.Approve, RequestAction.ForwardToDrawing, RequestAction.RecordDrawingResult, RequestAction.SendBackToDrawing, RequestAction.RecordDrawingResult },
            request.WorkflowHistory.Select(e => e.Action).ToArray());
        Assert.Equal(
            new[]
            {
                (RequestStatus.Created, RequestStatus.SentToManagement),
                (RequestStatus.SentToManagement, RequestStatus.MgmtApprovedAtPlanning),
                (RequestStatus.MgmtApprovedAtPlanning, RequestStatus.UnderDrawingReview),
                (RequestStatus.UnderDrawingReview, RequestStatus.DrawingReturnedToPlanning),
                (RequestStatus.DrawingReturnedToPlanning, RequestStatus.UnderDrawingReview),
                (RequestStatus.UnderDrawingReview, RequestStatus.DrawingReturnedToPlanning),
            },
            request.WorkflowHistory.Select(e => (e.FromStatus, e.ToStatus)).ToArray());
        Assert.Equal(new[] { 1L, 11L, 12L, 13L, 12L, 13L }, request.WorkflowHistory.Select(e => e.ActionByUserId).ToArray());
        Assert.Equal(new[] { "CENTRAL", "MANAGEMENT", "PLANNING", "DRAWING", "PLANNING", "DRAWING" }, request.WorkflowHistory.Select(e => e.ActionByDepartmentCode).ToArray());
        Assert.Equal(Enumerable.Range(1, 6).Select(At).ToArray(), request.WorkflowHistory.Select(e => e.ActionAtUtc).ToArray());

        var result = request.WorkflowHistory[3];
        Assert.Equal(DrawingOutcome.PartiallyAccepted, result.Outcome);
        Assert.Equal("Section B cannot be machined", result.Note);
        Assert.Null(request.WorkflowHistory[5].Note);
    }

    [Fact]
    public void Reject_and_reopen_keep_their_reasons_and_return_to_management()
    {
        var request = InStatus(RequestStatus.SentToManagement);

        request.Reject(Manager, "  Not our product range  ", At(1));
        Assert.Equal(RequestStatus.MgmtRejectedAtPlanning, request.Status);
        Assert.Equal("Not our product range", request.WorkflowHistory[0].Note);

        request.Reopen(Planner, "Customer confirmed the range", At(2));
        Assert.Equal(RequestStatus.SentToManagement, request.Status);
        Assert.Equal(new[] { RequestAction.Reject, RequestAction.Reopen }, request.WorkflowHistory.Select(e => e.Action).ToArray());
        Assert.Equal("Customer confirmed the range", request.WorkflowHistory[1].Note);
    }

    public static TheoryData<string, RequestStatus[]> Allowed => new()
    {
        { "approve", [RequestStatus.SentToManagement] },
        { "reject", [RequestStatus.SentToManagement] },
        { "reopen", [RequestStatus.MgmtRejectedAtPlanning] },
        { "forward", [RequestStatus.MgmtApprovedAtPlanning] },
        { "result", [RequestStatus.UnderDrawingReview] },
        { "sendback", [RequestStatus.DrawingReturnedToPlanning] },
    };

    private static void Do(Request request, string action, DateTime at)
    {
        switch (action)
        {
            case "approve":
                request.Approve(Manager, at);
                break;
            case "reject":
                request.Reject(Manager, "reason", at);
                break;
            case "reopen":
                request.Reopen(Planner, "reason", at);
                break;
            case "forward":
                request.ForwardToDrawing(Planner, at);
                break;
            case "result":
                request.RecordDrawingResult(Drafter, DrawingOutcome.Rejected, "note", at);
                break;
            default:
                request.SendBackToDrawing(Planner, "reason", at);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(Allowed))]
    public void Every_action_is_refused_in_every_status_except_the_one_it_needs_and_changes_nothing(string action, RequestStatus[] allowed)
    {
        foreach (var status in Enum.GetValues<RequestStatus>().Except(allowed))
        {
            var request = InStatus(status);
            var updated = request.UpdatedAtUtc;

            Assert.Throws<InvalidOperationException>(() => Do(request, action, At(9)));

            Assert.Equal(status, request.Status);
            Assert.Equal(updated, request.UpdatedAtUtc);
            Assert.Empty(request.WorkflowHistory);
        }

        foreach (var status in allowed)
        {
            var request = InStatus(status);
            Do(request, action, At(9));
            Assert.Single(request.WorkflowHistory);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_reason_is_mandatory_for_reject_reopen_and_send_back_and_nothing_changes_without_it(string? reason)
    {
        var sent = InStatus(RequestStatus.SentToManagement);
        var rejected = InStatus(RequestStatus.MgmtRejectedAtPlanning);
        var returned = InStatus(RequestStatus.DrawingReturnedToPlanning);

        Assert.Throws<ArgumentException>(() => sent.Reject(Manager, reason!, At(1)));
        Assert.Throws<ArgumentException>(() => rejected.Reopen(Planner, reason!, At(1)));
        Assert.Throws<ArgumentException>(() => returned.SendBackToDrawing(Planner, reason!, At(1)));

        Assert.Equal(RequestStatus.SentToManagement, sent.Status);
        Assert.Equal(RequestStatus.MgmtRejectedAtPlanning, rejected.Status);
        Assert.Equal(RequestStatus.DrawingReturnedToPlanning, returned.Status);
        Assert.Empty(sent.WorkflowHistory);
        Assert.Empty(rejected.WorkflowHistory);
        Assert.Empty(returned.WorkflowHistory);
    }

    [Fact]
    public void A_reason_is_limited_to_2000_characters()
    {
        var request = InStatus(RequestStatus.SentToManagement);

        Assert.Throws<ArgumentException>(() => request.Reject(Manager, new string('x', 2001), At(1)));
        Assert.Empty(request.WorkflowHistory);

        request.Reject(Manager, new string('x', 2000), At(1));
        Assert.Equal(2000, request.WorkflowHistory[0].Note!.Length);
    }

    [Theory]
    [InlineData(DrawingOutcome.PartiallyAccepted)]
    [InlineData(DrawingOutcome.Rejected)]
    [InlineData(DrawingOutcome.MoreInformationNeeded)]
    [InlineData(DrawingOutcome.ReturnedForCorrection)]
    public void Every_outcome_except_full_acceptance_needs_a_technical_note(DrawingOutcome outcome)
    {
        var request = InStatus(RequestStatus.UnderDrawingReview);

        Assert.Throws<ArgumentException>(() => request.RecordDrawingResult(Drafter, outcome, "  ", At(1)));
        Assert.Equal(RequestStatus.UnderDrawingReview, request.Status);

        request.RecordDrawingResult(Drafter, outcome, "Technical reason", At(1));
        Assert.Equal(outcome, request.WorkflowHistory.Single().Outcome);
    }

    [Fact]
    public void Full_acceptance_needs_no_note_and_an_unknown_outcome_is_refused()
    {
        var request = InStatus(RequestStatus.UnderDrawingReview);

        Assert.Throws<ArgumentOutOfRangeException>(() => request.RecordDrawingResult(Drafter, (DrawingOutcome)99, "x", At(1)));
        Assert.Empty(request.WorkflowHistory);

        request.RecordDrawingResult(Drafter, DrawingOutcome.FullyAccepted, null, At(1));
        Assert.Equal(DrawingOutcome.FullyAccepted, request.WorkflowHistory.Single().Outcome);
        Assert.Null(request.WorkflowHistory.Single().Note);
    }

    [Fact]
    public void Every_action_needs_a_utc_time_and_changes_nothing_when_it_is_not()
    {
        var local = DateTime.SpecifyKind(RequestFixtures.Now, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => InStatus(RequestStatus.SentToManagement).Approve(Manager, local));
        var request = InStatus(RequestStatus.SentToManagement);
        Assert.Throws<ArgumentException>(() => request.Reject(Manager, "r", local));
        Assert.Equal(RequestStatus.SentToManagement, request.Status);
        Assert.Empty(request.WorkflowHistory);
    }

    [Fact]
    public void The_history_is_exposed_read_only_and_an_entry_cannot_be_constructed_outside_the_domain()
    {
        var request = RequestFixtures.NewRequest();

        Assert.IsAssignableFrom<IReadOnlyList<RequestWorkflowEntry>>(request.WorkflowHistory);
        Assert.Null(typeof(RequestWorkflowEntry).GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).FirstOrDefault());
    }
}
