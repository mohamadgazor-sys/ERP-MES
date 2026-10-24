using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Warehouse;

/// <summary>
/// The physical receipt of an item's production output by Warehouse after the last QC (Master Specification 17.2, Decision Log #95 D7, #105). One per item (a duplicate is refused by the Application
/// and by a unique key); append-only. An external stage returning to QC is not a receipt. The quantity is the QC-accepted base units of the last stage at the moment of receipt.
/// </summary>
public sealed class WarehouseReceipt
{
    public const int MaxNoteLength = 300;

    private WarehouseReceipt()
    {
    }

    public WarehouseReceipt(long requestItemId, long receivedBaseUnits, string? note, long receivedByUserId, DateTime receivedAtUtc)
    {
        if (receivedBaseUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(receivedBaseUnits), receivedBaseUnits, "A receipt needs accepted base units.");
        }

        RequestItemId = requestItemId;
        ReceivedBaseUnits = receivedBaseUnits;
        Note = CleanNote(note);
        ReceivedByUserId = receivedByUserId;
        ReceivedAtUtc = Requirements.Utc(receivedAtUtc, nameof(receivedAtUtc));
    }

    public long Id { get; private set; }

    public long RequestItemId { get; private set; }

    public long ReceivedBaseUnits { get; private set; }

    public string? Note { get; private set; }

    public long ReceivedByUserId { get; private set; }

    public DateTime ReceivedAtUtc { get; private set; }

    internal static string? CleanNote(string? note)
    {
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        return text is { Length: > MaxNoteLength } ? throw new ArgumentException("The note is at most 300 characters.", nameof(note)) : text;
    }
}

/// <summary>
/// Warehouse's completion mark of an item (Decision Log #95 D7, #105): allowed only after the item was received and Management approved the price of the item's latest cost snapshot. It keeps the receipt
/// and the price decision that allowed it. One per item; append-only. The item is FINISHED_SENT from this record on.
/// </summary>
public sealed class ItemFinishedSent
{
    private ItemFinishedSent()
    {
    }

    public ItemFinishedSent(long requestItemId, long receiptId, long priceDecisionId, decimal approvedPriceRial, long markedByUserId, DateTime markedAtUtc)
    {
        if (approvedPriceRial <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(approvedPriceRial), approvedPriceRial, "An approved price is positive.");
        }

        RequestItemId = requestItemId;
        ReceiptId = receiptId;
        PriceDecisionId = priceDecisionId;
        ApprovedPriceRial = approvedPriceRial;
        MarkedByUserId = markedByUserId;
        MarkedAtUtc = Requirements.Utc(markedAtUtc, nameof(markedAtUtc));
    }

    public long Id { get; private set; }

    public long RequestItemId { get; private set; }

    public long ReceiptId { get; private set; }

    public long PriceDecisionId { get; private set; }

    /// <summary>The price approved by Management at the time of the mark (Iranian Rial).</summary>
    public decimal ApprovedPriceRial { get; private set; }

    public long MarkedByUserId { get; private set; }

    public DateTime MarkedAtUtc { get; private set; }
}

/// <summary>
/// An optional record that the received item was handed to the customer: the date and a document reference (Decision Log #95 D7, #105). It is for tracking only and not required to close the item. Several
/// may exist for an item (for example partial deliveries); each is append-only.
/// </summary>
public sealed class CustomerDispatch
{
    public const int MaxReferenceLength = 100;

    private CustomerDispatch()
    {
    }

    public CustomerDispatch(long requestItemId, DateOnly dispatchDate, string? documentReference, string? note, long recordedByUserId, DateTime recordedAtUtc)
    {
        var reference = documentReference?.Trim();
        if (string.IsNullOrEmpty(reference) || reference.Length > MaxReferenceLength)
        {
            throw new ArgumentException("A document reference of 1 to 100 characters is required.", nameof(documentReference));
        }

        RequestItemId = requestItemId;
        DispatchDate = dispatchDate;
        DocumentReference = reference;
        Note = WarehouseReceipt.CleanNote(note);
        RecordedByUserId = recordedByUserId;
        RecordedAtUtc = Requirements.Utc(recordedAtUtc, nameof(recordedAtUtc));
    }

    public long Id { get; private set; }

    public long RequestItemId { get; private set; }

    public DateOnly DispatchDate { get; private set; }

    public string DocumentReference { get; private set; } = string.Empty;

    public string? Note { get; private set; }

    public long RecordedByUserId { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }
}
