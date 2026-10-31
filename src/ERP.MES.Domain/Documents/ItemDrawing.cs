namespace ERP.MES.Domain.Documents;

/// <summary>
/// One revision of the technical drawing of an item (Decision Log #125 Q14): a PDF stored once as a <see cref="StoredDocument"/> and linked to the item by this row. An item has at most one
/// current drawing; replacing it supersedes the current revision and keeps it as history. Rows are never deleted or edited apart from being superseded.
/// </summary>
public sealed class ItemDrawing
{
    private ItemDrawing()
    {
        FileName = string.Empty;
        ContentHash = string.Empty;
    }

    public ItemDrawing(long requestItemId, StoredDocument document, int revision, long uploadedByUserId, DateTime uploadedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (requestItemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestItemId));
        }

        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        if (document.Type != DocumentType.ItemDrawing)
        {
            throw new ArgumentException("The document is not an item drawing.", nameof(document));
        }

        if (uploadedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The time must be UTC.", nameof(uploadedAtUtc));
        }

        RequestItemId = requestItemId;
        Document = document;
        Revision = revision;
        FileName = document.FileName;
        ContentLength = document.ContentLength;
        ContentHash = document.ContentHash;
        UploadedByUserId = uploadedByUserId;
        UploadedAtUtc = uploadedAtUtc;
    }

    public long Id { get; }

    public long RequestItemId { get; }

    /// <summary>The stored binary; not loaded with the list (it can be large) - use the snapshot fields.</summary>
    public StoredDocument? Document { get; }

    public long DocumentId { get; }

    /// <summary>1 for the first drawing of the item, then 2, 3, ... for each replacement.</summary>
    public int Revision { get; }

    public string FileName { get; }

    public long ContentLength { get; }

    public string ContentHash { get; }

    public long UploadedByUserId { get; }

    public DateTime UploadedAtUtc { get; }

    public DateTime? SupersededAtUtc { get; private set; }

    public long? SupersededByUserId { get; private set; }

    public bool IsCurrent => SupersededAtUtc is null;

    public void MarkSuperseded(long byUserId, DateTime atUtc)
    {
        if (SupersededAtUtc is not null)
        {
            throw new InvalidOperationException("The drawing revision is already superseded.");
        }

        if (atUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The time must be UTC.", nameof(atUtc));
        }

        SupersededAtUtc = atUtc;
        SupersededByUserId = byUserId;
    }
}
