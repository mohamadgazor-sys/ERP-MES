using ERP.MES.Domain.Warehouse;

namespace ERP.MES.Domain.Tests.Scheduling;

// The warehouse completion records validate their own input (Decision Log #95 D7, #105).
public class WarehouseCompletionTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_receipt_needs_accepted_units_and_a_short_note_and_keeps_who_and_when()
    {
        var receipt = new WarehouseReceipt(1, 10, " از QC ", 5, Now);
        Assert.Equal((10L, "از QC", 5L, Now), (receipt.ReceivedBaseUnits, receipt.Note, receipt.ReceivedByUserId, receipt.ReceivedAtUtc));
        Assert.Null(new WarehouseReceipt(1, 10, "  ", 5, Now).Note);
        Assert.Throws<ArgumentOutOfRangeException>(() => new WarehouseReceipt(1, 0, null, 5, Now));
        Assert.Throws<ArgumentException>(() => new WarehouseReceipt(1, 1, new string('x', 301), 5, Now));
        Assert.Throws<ArgumentException>(() => new WarehouseReceipt(1, 1, null, 5, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    [Fact]
    public void A_finished_mark_keeps_the_receipt_and_the_approved_price_that_allowed_it()
    {
        var mark = new ItemFinishedSent(1, 7, 9, 5_700_000m, 5, Now);
        Assert.Equal((7L, 9L, 5_700_000m), (mark.ReceiptId, mark.PriceDecisionId, mark.ApprovedPriceRial));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ItemFinishedSent(1, 7, 9, 0m, 5, Now));
    }

    [Fact]
    public void A_dispatch_needs_a_document_reference_of_one_to_a_hundred_characters()
    {
        var dispatch = new CustomerDispatch(1, new DateOnly(2026, 10, 24), " WB-1001 ", null, 5, Now);
        Assert.Equal("WB-1001", dispatch.DocumentReference);
        Assert.Throws<ArgumentException>(() => new CustomerDispatch(1, new DateOnly(2026, 10, 24), " ", null, 5, Now));
        Assert.Throws<ArgumentException>(() => new CustomerDispatch(1, new DateOnly(2026, 10, 24), new string('x', 101), null, 5, Now));
        Assert.Throws<ArgumentException>(() => new CustomerDispatch(1, new DateOnly(2026, 10, 24), "WB", new string('x', 301), 5, Now));
    }
}
