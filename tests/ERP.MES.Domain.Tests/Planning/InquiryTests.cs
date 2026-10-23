using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Tests.Planning;

public class InquiryTests
{
    private static readonly DateTime At = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly WorkflowActor Drawing = new(1, "DRAWING");
    private static readonly WorkflowActor Warehouse = new(2, "WAREHOUSE");

    private static RawMaterialInquiry Draft() => RawMaterialInquiry.Create(10, 1, RequestStatus.UnderDrawingReview, null, Drawing, At);

    private static void Line(RawMaterialInquiry i) => i.AddLine("ST37", "میلگرد", 30m, 6000m, null, 5m, 3, null, null, Drawing, At);

    [Theory]
    [InlineData(RequestStatus.Created)]
    [InlineData(RequestStatus.SentToManagement)]
    [InlineData(RequestStatus.MgmtApprovedAtPlanning)]
    [InlineData(RequestStatus.Cancelled)]
    public void An_inquiry_needs_the_request_in_drawing_review_or_returned(RequestStatus status) =>
        Assert.Throws<InquiryRequestStateException>(() => RawMaterialInquiry.Create(1, 1, status, null, Drawing, At));

    [Fact]
    public void The_status_follows_the_answers_and_a_changed_answer_keeps_the_old_one()
    {
        var inquiry = Draft();
        Line(inquiry);
        Line(inquiry);
        inquiry.Send(RequestStatus.UnderDrawingReview, Drawing, At);

        inquiry.Answer(1, InquiryAnswer.No, "x", Warehouse, At);
        Assert.Equal(InquiryStatus.PartiallyAnswered, inquiry.Status);
        inquiry.Answer(2, InquiryAnswer.Yes, null, Warehouse, At);
        Assert.Equal(InquiryStatus.Answered, inquiry.Status);
        inquiry.Answer(1, InquiryAnswer.Yes, null, Warehouse, At);

        Assert.Equal(2, inquiry.Lines[0].Responses.Count);
        Assert.Equal(InquiryAnswer.Yes, inquiry.Lines[0].CurrentResponse!.Answer);
        Assert.Equal(InquiryStatus.Answered, inquiry.Status);
    }

    [Fact]
    public void Line_numbers_are_never_reused_after_a_removal()
    {
        var inquiry = Draft();
        Line(inquiry);
        inquiry.RemoveLine(1, Drawing, At);
        Line(inquiry);

        Assert.Equal(2, inquiry.Lines.Single().LineNo);
    }

    [Fact]
    public void Lines_change_only_in_a_draft_and_cancel_needs_a_reason()
    {
        var inquiry = Draft();
        Assert.Throws<InquiryNoLinesException>(() => inquiry.Send(RequestStatus.UnderDrawingReview, Drawing, At));
        Line(inquiry);
        inquiry.Send(RequestStatus.DrawingReturnedToPlanning, Drawing, At);

        Assert.Throws<InvalidOperationException>(() => Line(inquiry));
        Assert.Throws<InvalidOperationException>(() => inquiry.RemoveLine(1, Drawing, At));
        Assert.Throws<ArgumentException>(() => inquiry.Cancel(" ", Drawing, At));
        inquiry.Cancel("خطا", Drawing, At);
        Assert.Throws<InvalidOperationException>(() => inquiry.Answer(1, InquiryAnswer.Yes, null, Warehouse, At));
        Assert.Throws<InvalidOperationException>(() => inquiry.Cancel("again", Drawing, At));
    }

    [Fact]
    public void Line_values_are_validated()
    {
        var inquiry = Draft();

        Assert.Throws<ArgumentException>(() => inquiry.AddLine(" ", "x", null, null, null, 1m, 3, null, null, Drawing, At));
        Assert.Throws<ArgumentException>(() => inquiry.AddLine("g", new string('f', 101), null, null, null, 1m, 3, null, null, Drawing, At));
        Assert.Throws<ArgumentOutOfRangeException>(() => inquiry.AddLine("g", "f", 0m, null, null, 1m, 3, null, null, Drawing, At));
        Assert.Throws<ArgumentOutOfRangeException>(() => inquiry.AddLine("g", "f", null, null, null, 0m, 3, null, null, Drawing, At));
        Assert.Empty(inquiry.Lines);
        inquiry.AddLine("g", "f", null, null, "10x20x30", 1m, 3, null, null, Drawing, At);
        Assert.Null(inquiry.Lines.Single().DiameterMm);
    }
}
