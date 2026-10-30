using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Domain.Tests.Scheduling;

// The overtime assignment state machine and the limits (Decision Log #95 D5, #103).
public class OvertimeAssignmentTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);

    private static OvertimeAssignment Assignment(DateTime? start = null, DateTime? end = null, string? reason = "سفارش") =>
        new(0, 1, 2, 3, start ?? new DateTime(2026, 10, 24, 17, 30, 0), end ?? new DateTime(2026, 10, 24, 19, 0, 0), reason, 9, Now);

    [Fact]
    public void A_proposal_has_minutes_a_work_date_and_validated_times_and_reason()
    {
        var a = Assignment();
        Assert.Equal(OvertimeStatus.Proposed, a.Status);
        Assert.Equal(90, a.Minutes);
        Assert.Equal(new DateOnly(2026, 10, 24), a.WorkDate);
        Assert.True(a.IsLive);

        Assert.Throws<ArgumentOutOfRangeException>(() => Assignment(end: new DateTime(2026, 10, 24, 17, 30, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Assignment(end: new DateTime(2026, 10, 25, 6, 0, 0))); // more than 12 hours
        Assert.Throws<ArgumentException>(() => Assignment(reason: " "));
        Assert.Throws<ArgumentException>(() => Assignment(reason: new string('x', 301)));

        // Past midnight is allowed and the work date is the start date.
        var late = Assignment(new DateTime(2026, 10, 24, 22, 0, 0), new DateTime(2026, 10, 25, 2, 0, 0));
        Assert.Equal(240, late.Minutes);
        Assert.Equal(new DateOnly(2026, 10, 24), late.WorkDate);
    }

    [Fact]
    public void Planning_approves_within_the_limits_or_sends_to_management_which_then_decides()
    {
        var within = Assignment();
        within.FirstApprove(false, null, 5, Now);
        Assert.Equal(OvertimeStatus.Approved, within.Status);
        Assert.Throws<InvalidOperationException>(() => within.FirstApprove(false, null, 5, Now));
        Assert.Throws<InvalidOperationException>(() => within.ManagementApprove(null, 6, Now));

        var beyond = Assignment();
        beyond.FirstApprove(true, " نیاز ", 5, Now);
        Assert.Equal(OvertimeStatus.AwaitingManagement, beyond.Status);
        Assert.True(beyond.ExceedsLimits);
        Assert.Equal("نیاز", beyond.DecisionNote);
        beyond.ManagementApprove(null, 6, Now);
        Assert.Equal(OvertimeStatus.Approved, beyond.Status);
        Assert.Equal(6, beyond.ManagementUserId);
        Assert.Equal("نیاز", beyond.DecisionNote);
    }

    [Fact]
    public void Rejection_needs_a_reason_from_the_right_decider_and_cancellation_closes_live_assignments_only()
    {
        var proposed = Assignment();
        Assert.Throws<InvalidOperationException>(() => proposed.Reject("x", 6, Now, byManagement: true));
        Assert.Throws<ArgumentException>(() => proposed.Reject(" ", 5, Now, byManagement: false));
        proposed.Reject("دلیل", 5, Now, byManagement: false);
        Assert.Equal(OvertimeStatus.Rejected, proposed.Status);
        Assert.False(proposed.IsLive);
        Assert.Throws<InvalidOperationException>(() => proposed.Cancel("x", Now));

        var waiting = Assignment();
        waiting.FirstApprove(true, null, 5, Now);
        Assert.Throws<InvalidOperationException>(() => waiting.Reject("x", 5, Now, byManagement: false));
        waiting.Reject("خارج از سقف", 6, Now, byManagement: true);
        Assert.Equal(6, waiting.ManagementUserId);

        var approved = Assignment();
        approved.FirstApprove(false, null, 5, Now);
        approved.Cancel("تغییر", Now);
        Assert.Equal(OvertimeStatus.Cancelled, approved.Status);
        Assert.Throws<ArgumentException>(() => Assignment().Cancel(null, Now));
    }

    [Fact]
    public void Overlap_is_exclusive_at_the_edges_and_the_limits_are_validated()
    {
        var a = Assignment();
        Assert.True(a.Overlaps(new DateTime(2026, 10, 24, 18, 0, 0), new DateTime(2026, 10, 24, 20, 0, 0)));
        Assert.False(a.Overlaps(new DateTime(2026, 10, 24, 19, 0, 0), new DateTime(2026, 10, 24, 20, 0, 0)));
        Assert.False(a.Overlaps(new DateTime(2026, 10, 24, 16, 0, 0), new DateTime(2026, 10, 24, 17, 30, 0)));

        var limits = new OvertimeLimits(120, 480, 1, Now);
        Assert.Equal(1, limits.Id);
        Assert.Throws<ArgumentOutOfRangeException>(() => new OvertimeLimits(0, 480, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OvertimeLimits(1441, 5000, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OvertimeLimits(120, 60, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OvertimeLimits(120, 10081, 1, Now));
        limits.Set(60, 300, 2, Now);
        Assert.Equal((60, 300, 2L), (limits.MaxMinutesPerDay, limits.MaxMinutesPerWeek, limits.ChangedByUserId));
    }
}
