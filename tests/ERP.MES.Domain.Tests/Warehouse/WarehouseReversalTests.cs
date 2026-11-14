using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Requests;
using ERP.MES.Domain.Warehouse;

namespace ERP.MES.Domain.Tests.Warehouse;

// The Warehouse reversal proposal and its marker (owner decision B9, Decision Log #142): a mandatory reason, one decision, a marker that names its approved reversal and only marks Warehouse records.
public class WarehouseReversalTests
{
    private static readonly DateTime T = new(2026, 11, 14, 8, 0, 0, DateTimeKind.Utc);

    private static WarehouseReversal Proposal(InvalidatedRecordKind kind = InvalidatedRecordKind.FinishedSent, string? reason = "اشتباه") =>
        new(7, kind, 3, ReversalNature.RecordedInError, reason, 11, T);

    [Fact]
    public void A_proposal_keeps_who_what_why_and_when_and_starts_undecided()
    {
        var proposal = Proposal(reason: "  مهر اشتباه  ");

        Assert.Equal((ReversalStatus.Proposed, "مهر اشتباه", 11L, T), (proposal.Status, proposal.ProposalReason, proposal.ProposedByUserId, proposal.ProposedAtUtc));
        Assert.Equal((7L, InvalidatedRecordKind.FinishedSent, 3L), (proposal.RequestItemId, proposal.TargetKind, proposal.TargetId));
        Assert.Null(proposal.DecidedByUserId);
        Assert.Null(proposal.DecidedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_proposal_without_a_reason_is_refused(string? reason) => Assert.Throws<ArgumentException>(() => Proposal(reason: reason));

    [Fact]
    public void A_reason_longer_than_300_characters_a_non_warehouse_record_and_an_unknown_nature_are_refused()
    {
        Assert.Throws<ArgumentException>(() => Proposal(reason: new string('x', 301)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Proposal(InvalidatedRecordKind.CostSnapshot));
        Assert.Throws<ArgumentOutOfRangeException>(() => Proposal(InvalidatedRecordKind.PriceDecision));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WarehouseReversal(7, InvalidatedRecordKind.FinishedSent, 3, (ReversalNature)9, "x", 11, T));
        Assert.Throws<ArgumentException>(() => new WarehouseReversal(7, InvalidatedRecordKind.FinishedSent, 3, ReversalNature.RecordedInError, "x", 11, new DateTime(2026, 11, 14)));
    }

    [Fact]
    public void A_proposal_is_decided_once_an_approval_may_omit_the_reason_and_a_rejection_may_not()
    {
        var approved = Proposal();
        approved.Approve(21, null, T.AddHours(1));
        Assert.Equal((ReversalStatus.Approved, 21L, (string?)null), (approved.Status, approved.DecidedByUserId, approved.DecisionReason));
        Assert.Throws<InvalidOperationException>(() => approved.Reject(21, "x", T.AddHours(2)));
        Assert.Throws<InvalidOperationException>(() => approved.Approve(21, null, T.AddHours(2)));

        var rejected = Proposal();
        Assert.Throws<ArgumentException>(() => rejected.Reject(21, " ", T.AddHours(1)));
        Assert.Equal(ReversalStatus.Proposed, rejected.Status); // the refused rejection decided nothing
        rejected.Reject(21, "سند درست است", T.AddHours(1));
        Assert.Equal((ReversalStatus.Rejected, "سند درست است", T.AddHours(1)), (rejected.Status, rejected.DecisionReason, rejected.DecidedAtUtc));
        Assert.Throws<InvalidOperationException>(() => rejected.Approve(21, null, T.AddHours(2)));
    }

    [Fact]
    public void The_marker_needs_exactly_one_source_and_a_reversal_source_only_marks_the_approved_Warehouse_record_with_the_effect_Reversed()
    {
        var approved = Proposal();
        approved.Approve(21, null, T.AddHours(1));

        var marker = new RecordInvalidation(7, InvalidatedRecordKind.FinishedSent, 3, InvalidationEffect.Reversed, "اشتباه", 21, T.AddHours(1), warehouseReversal: approved);
        Assert.Same(approved, marker.WarehouseReversal);
        Assert.Equal(InvalidationEffect.Reversed, marker.Effect);

        // An undecided or rejected proposal never marks anything.
        Assert.Throws<ArgumentException>(() => new RecordInvalidation(7, InvalidatedRecordKind.FinishedSent, 3, InvalidationEffect.Reversed, "x", 21, T, warehouseReversal: Proposal()));
        // The marker names the record the reversal was approved for, and a different kind is refused.
        Assert.Throws<ArgumentException>(() => new RecordInvalidation(7, InvalidatedRecordKind.WarehouseReceipt, 3, InvalidationEffect.Reversed, "x", 21, T, warehouseReversal: approved));
        // The effect Reversed belongs to an approved reversal alone, and an effect other than Reversed is refused for it.
        Assert.Throws<ArgumentException>(() => new RecordInvalidation(7, InvalidatedRecordKind.CostSnapshot, 3, InvalidationEffect.Reversed, "x", 21, T, jobReportRevisionId: 5));
        Assert.Throws<ArgumentException>(() => new RecordInvalidation(7, InvalidatedRecordKind.FinishedSent, 3, InvalidationEffect.Invalidated, "x", 21, T, warehouseReversal: approved));
        // Exactly one source; no source is refused, and a job-report revision never touches a Warehouse record.
        Assert.Throws<ArgumentException>(() => new RecordInvalidation(7, InvalidatedRecordKind.FinishedSent, 3, InvalidationEffect.Reversed, "x", 21, T));
        Assert.Throws<ArgumentException>(() => new RecordInvalidation(7, InvalidatedRecordKind.FinishedSent, 3, InvalidationEffect.Invalidated, "x", 21, T, jobReportRevisionId: 5));
    }
}
