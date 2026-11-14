using ERP.MES.Domain.Reporting;

namespace ERP.MES.Domain.Tests.Reporting;

// Reopening an approved job report creates a new revision and leaves the original as it was (owner decision B2, Decision Log #142).
public class JobReportRevisionTests
{
    private static readonly DateTime T = new(2026, 11, 14, 8, 0, 0, DateTimeKind.Utc);

    private static DailyJobReport Approved()
    {
        var report = new DailyJobReport(5, 3, new DateOnly(2026, 11, 10), 2, 1, 11, T);
        report.ReplaceLines([new JobReportLine(1, 9, 100, 200, 1, T, T.AddHours(4), 30, "خرابی", 10, null)]);
        report.Submit(11, T.AddHours(5), new Dictionary<int, JobLineFlags>());
        report.Approve(12, T.AddHours(6), planningFallback: false);
        return report;
    }

    [Fact]
    public void Reopening_returns_a_draft_amendment_with_a_copy_of_the_lines_the_reason_and_the_actor_and_leaves_the_original_approved()
    {
        var original = Approved();
        var minutesBefore = original.Lines.Sum(l => l.ProductiveMinutes);

        var revision = original.Reopen(21, "  زمان پایان اشتباه بود  ", T.AddDays(1));

        Assert.Equal((JobReportStatus.Draft, 1, 5L, "زمان پایان اشتباه بود", 21L), (revision.Status, revision.AmendmentNo, revision.PreviousRevisionReportId!.Value, revision.AmendmentReason, revision.AmendmentOpenedByUserId!.Value));
        Assert.True(revision.IsAmendment);
        Assert.Equal((original.OperatorId, original.WorkDate, original.ProductionUnitId, original.ShiftTemplateId), (revision.OperatorId, revision.WorkDate, revision.ProductionUnitId, revision.ShiftTemplateId));
        Assert.Equal(minutesBefore, revision.Lines.Sum(l => l.ProductiveMinutes));
        Assert.All(revision.Lines, l => Assert.Equal(JobLineFlags.None, l.Flags)); // flags are computed again at submission
        Assert.Contains(revision.Events, e => e.Kind == JobReportEventKind.RevisionOpened && e.UserId == 21 && e.Reason == "زمان پایان اشتباه بود");

        // The original keeps its approval, lines and reviewer; only one event is appended.
        Assert.Equal((JobReportStatus.Approved, 12L, 1), (original.Status, original.ReviewedByUserId!.Value, original.Lines.Count));
        Assert.Equal(minutesBefore, original.Lines.Sum(l => l.ProductiveMinutes));
        Assert.False(original.IsAmendment);
        Assert.Equal(JobReportEventKind.Reopened, original.Events[^1].Kind);
        Assert.Equal(21L, original.Events[^1].UserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Reopening_without_a_reason_is_refused_and_changes_nothing(string? reason)
    {
        var original = Approved();
        var events = original.Events.Count;
        Assert.Throws<ArgumentException>(() => original.Reopen(21, reason, T.AddDays(1)));
        Assert.Throws<ArgumentException>(() => original.Reopen(21, new string('x', 301), T.AddDays(1)));
        Assert.Equal(events, original.Events.Count);
    }

    [Fact]
    public void Only_an_approved_report_can_be_reopened_and_an_amendment_is_edited_like_any_draft()
    {
        var draft = new DailyJobReport(6, 3, new DateOnly(2026, 11, 10), 2, 1, 11, T);
        Assert.Throws<InvalidOperationException>(() => draft.Reopen(21, "x", T));

        var amendment = Approved().Reopen(21, "x", T.AddDays(1));
        amendment.ReplaceLines([new JobReportLine(1, 9, 100, 200, 1, T, T.AddHours(3), 0, null, 8, null)]);
        Assert.Equal(180, amendment.Lines.Sum(l => l.ProductiveMinutes));
        amendment.Submit(11, T.AddDays(1), new Dictionary<int, JobLineFlags>());
        amendment.Approve(12, T.AddDays(1), planningFallback: false);
        Assert.Equal(JobReportStatus.Approved, amendment.Status);

        // An amendment of an approved amendment is amendment number 2.
        Assert.Equal(2, amendment.Reopen(21, "y", T.AddDays(2)).AmendmentNo);
    }
}
