using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Tests.Planning;

public class ClarificationTests
{
    private static readonly DateTime At = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly WorkflowActor Drawing = new(1, "DRAWING");
    private static readonly WorkflowActor Planning = new(2, "PLANNING");

    [Theory]
    [InlineData(RequestStatus.DrawingReturnedToPlanning)]
    [InlineData(RequestStatus.MgmtApprovedAtPlanning)]
    [InlineData(RequestStatus.Created)]
    public void A_question_is_asked_only_while_drawing_reviews(RequestStatus status) =>
        Assert.Throws<ClarificationRequestStateException>(() => DrawingClarification.Ask(1, status, "q", Drawing, At));

    [Fact]
    public void The_question_goes_open_answered_closed_and_each_step_happens_once()
    {
        var c = DrawingClarification.Ask(1, RequestStatus.UnderDrawingReview, "  سؤال  ", Drawing, At);
        Assert.Equal("سؤال", c.Question);
        Assert.False(c.IsResolved);
        Assert.Throws<ArgumentException>(() => c.RecordAnswer(" ", Planning, At));

        c.RecordAnswer("جواب", Planning, At);
        Assert.Equal(ClarificationStatus.Answered, c.Status);
        Assert.Throws<InvalidOperationException>(() => c.RecordAnswer("again", Planning, At));
        Assert.Throws<InvalidOperationException>(() => c.Close(null, Planning, At));

        c.Close("ok", Drawing, At);
        Assert.True(c.IsResolved);
        Assert.Equal(1, c.ClosedByUserId);
        Assert.Throws<InvalidOperationException>(() => c.Close(null, Drawing, At));
    }

    [Fact]
    public void An_unanswered_question_can_be_withdrawn_by_the_asker()
    {
        var c = DrawingClarification.Ask(1, RequestStatus.UnderDrawingReview, "q", Drawing, At);

        c.Close(null, Drawing, At);

        Assert.Equal(ClarificationStatus.Closed, c.Status);
        Assert.Null(c.Answer);
        Assert.Throws<InvalidOperationException>(() => c.RecordAnswer("late", Planning, At));
    }
}
