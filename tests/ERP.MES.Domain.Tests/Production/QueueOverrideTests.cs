using ERP.MES.Domain.Production;

namespace ERP.MES.Domain.Tests.Production;

public class QueueOverrideTests
{
    private static readonly DateTime T = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_override_needs_a_rank_and_a_reason_and_can_be_cleared_flagged_and_confirmed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new QueueOverride(1, 1, 1, 0, "reason", 2, T));
        Assert.Throws<ArgumentException>(() => new QueueOverride(1, 1, 1, 1, " ", 2, T));
        Assert.Throws<ArgumentException>(() => new QueueOverride(1, 1, 1, 1, new string('x', QueueOverride.MaxReasonLength + 1), 2, T));
        Assert.ThrowsAny<ArgumentException>(() => new QueueOverride(1, 1, 1, 1, "ok", 2, DateTime.SpecifyKind(T, DateTimeKind.Unspecified)));

        var value = new QueueOverride(1, 3, 9, 2, " key customer ", 2, T);
        Assert.True(value.IsActive);
        Assert.Equal(2, value.Rank);
        Assert.Equal("key customer", value.Reason);
        Assert.False(value.NeedsReview);

        value.FlagForReview("Planning changed priority", T.AddHours(1));
        Assert.True(value.NeedsReview);
        Assert.True(value.IsActive);
        value.ConfirmReview();
        Assert.False(value.NeedsReview);
        Assert.Throws<InvalidOperationException>(() => value.ConfirmReview());

        value.FlagForReview("again", T.AddHours(2));
        value.Clear();
        Assert.False(value.IsActive);
        Assert.False(value.NeedsReview);
        Assert.Throws<InvalidOperationException>(() => value.Clear());
        Assert.Throws<InvalidOperationException>(() => value.FlagForReview("x", T));

        // A cleared override can be set again; the review flag does not survive a new operation.
        value.Activate(9, 1, "again", 4, T.AddDays(1));
        Assert.True(value.IsActive);
        Assert.Equal(1, value.Rank);
        Assert.Equal(4, value.SetByUserId);
    }

    [Fact]
    public void A_designation_can_be_withdrawn_and_reinstated_and_keeps_who_did_it()
    {
        var designation = new UserUnitDesignation(5, 9, 2, T);
        Assert.True(designation.IsActive);

        designation.Revoke(3, T.AddDays(1));
        Assert.False(designation.IsActive);
        Assert.Equal(3, designation.RevokedByUserId);
        Assert.Throws<InvalidOperationException>(() => designation.Revoke(3, T.AddDays(2)));

        designation.Reinstate(4, T.AddDays(3));
        Assert.True(designation.IsActive);
        Assert.Equal(4, designation.AssignedByUserId);
        Assert.Null(designation.RevokedAtUtc);
        Assert.Throws<InvalidOperationException>(() => designation.Reinstate(4, T.AddDays(4)));
    }
}
