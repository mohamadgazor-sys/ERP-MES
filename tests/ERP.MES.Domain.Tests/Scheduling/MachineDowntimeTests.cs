using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Domain.Tests.Scheduling;

// Machine downtime reported by Maintenance/PM and reviewed by Planning (Master Specification section 21, Decision Log #14, #128).
public class MachineDowntimeTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc);

    private static MachineDowntime Downtime(DowntimeReason reason = DowntimeReason.Breakdown, DateTime? start = null, DateTime? end = null, string? note = null) =>
        new(0, 3, 2, reason, start ?? new DateTime(2026, 11, 5, 9, 0, 0), end ?? new DateTime(2026, 11, 5, 11, 30, 0), note, 7, Now);

    [Fact]
    public void A_report_has_a_duration_counts_at_once_and_validates_times_reason_and_note()
    {
        var d = Downtime();
        Assert.Equal(DowntimeStatus.Reported, d.Status);
        Assert.Equal(150, d.DurationMinutes);
        Assert.True(d.Counts);
        Assert.Null(d.Note);
        Assert.Equal(2, d.ProductionUnitId);

        Assert.Throws<ArgumentOutOfRangeException>(() => Downtime(end: new DateTime(2026, 11, 5, 9, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Downtime(end: new DateTime(2026, 11, 5, 8, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Downtime(end: new DateTime(2026, 11, 5, 10, 0, 30))); // whole minutes
        Assert.Throws<ArgumentOutOfRangeException>(() => Downtime(end: new DateTime(2027, 2, 4, 9, 1, 0))); // more than 90 days
        Assert.Throws<ArgumentOutOfRangeException>(() => Downtime((DowntimeReason)99));
        Assert.Throws<ArgumentException>(() => Downtime(DowntimeReason.Other)); // "other" needs a note
        Assert.Throws<ArgumentException>(() => Downtime(note: new string('x', 301)));
        Assert.Equal("لرزش اسپیندل", Downtime(DowntimeReason.Other, note: "  لرزش اسپیندل ").Note);

        // Across midnight and exactly 90 days are allowed.
        Assert.Equal(90 * 1440, Downtime(end: new DateTime(2026, 11, 5, 9, 0, 0).AddDays(90)).DurationMinutes);
        Assert.Equal(DateTimeKind.Unspecified, Downtime(start: new DateTime(2026, 11, 5, 22, 0, 0, DateTimeKind.Local), end: new DateTime(2026, 11, 6, 2, 0, 0)).StartLocal.Kind);
    }

    [Fact]
    public void Planning_accepts_or_rejects_a_report_once_and_a_rejection_needs_a_reason()
    {
        var accepted = Downtime();
        accepted.Accept(null, 5, Now);
        Assert.Equal(DowntimeStatus.Accepted, accepted.Status);
        Assert.Equal(5, accepted.DecidedByUserId);
        Assert.Equal(Now, accepted.DecidedAtUtc);
        Assert.True(accepted.Counts);
        Assert.Throws<InvalidOperationException>(() => accepted.Accept(null, 5, Now));
        Assert.Throws<InvalidOperationException>(() => accepted.Reject("x", 5, Now));

        var rejected = Downtime();
        Assert.Throws<ArgumentException>(() => rejected.Reject(" ", 5, Now));
        Assert.Equal(DowntimeStatus.Reported, rejected.Status);
        rejected.Reject("دستگاه کار می‌کرد", 5, Now);
        Assert.Equal(DowntimeStatus.Rejected, rejected.Status);
        Assert.Equal("دستگاه کار می‌کرد", rejected.DecisionNote);
        Assert.False(rejected.Counts);
        Assert.Throws<InvalidOperationException>(() => rejected.Cancel("x", 7, Now));
        Assert.Throws<InvalidOperationException>(() => rejected.ChangeEnd(new DateTime(2026, 11, 5, 12, 0, 0)));

        Assert.Throws<ArgumentException>(() => Downtime().Accept(null, 5, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    [Fact]
    public void Maintenance_cancels_with_a_reason_and_a_changed_end_returns_an_accepted_report_to_planning()
    {
        var d = Downtime();
        d.Accept("بررسی شد", 5, Now);
        d.ChangeEnd(new DateTime(2026, 11, 5, 14, 0, 0));
        Assert.Equal(300, d.DurationMinutes);
        Assert.Equal(DowntimeStatus.Reported, d.Status);
        Assert.Null(d.DecidedByUserId);
        Assert.Null(d.DecisionNote);
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ChangeEnd(new DateTime(2026, 11, 5, 9, 0, 0)));
        Assert.Equal(300, d.DurationMinutes);

        Assert.Throws<ArgumentException>(() => d.Cancel(null, 7, Now));
        d.Cancel("گزارش اشتباه", 7, Now);
        Assert.Equal(DowntimeStatus.Cancelled, d.Status);
        Assert.Equal(7, d.CancelledByUserId);
        Assert.False(d.Counts);
        Assert.Throws<InvalidOperationException>(() => d.Cancel("x", 7, Now));
        Assert.Throws<InvalidOperationException>(() => d.Accept(null, 5, Now));
    }

    [Fact]
    public void Overlap_is_half_open()
    {
        var d = Downtime();
        Assert.True(d.Overlaps(new DateTime(2026, 11, 5, 11, 0, 0), new DateTime(2026, 11, 5, 12, 0, 0)));
        Assert.False(d.Overlaps(new DateTime(2026, 11, 5, 11, 30, 0), new DateTime(2026, 11, 5, 12, 0, 0)));
        Assert.False(d.Overlaps(new DateTime(2026, 11, 5, 8, 0, 0), new DateTime(2026, 11, 5, 9, 0, 0)));
    }
}
