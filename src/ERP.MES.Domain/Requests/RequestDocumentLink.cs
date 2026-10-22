using ERP.MES.Domain.Documents;

namespace ERP.MES.Domain.Requests;

/// <summary>
/// The association of a stored document with a request (Decision Log #35: link rows only). The link keeps a snapshot of the file's name, size and hash so a request
/// can show its PDF without loading the binary. A link is never deleted: replacing the PDF marks the old link as superseded and adds a new one, so the history stays.
/// </summary>
public sealed class RequestDocumentLink
{
    private RequestDocumentLink()
    {
        FileName = string.Empty;
        ContentHash = string.Empty;
    }

    internal RequestDocumentLink(StoredDocument document, long linkedByUserId, DateTime linkedAtUtc)
    {
        Document = document;
        Type = document.Type;
        FileName = document.FileName;
        ContentLength = document.ContentLength;
        ContentHash = document.ContentHash;
        LinkedByUserId = linkedByUserId;
        LinkedAtUtc = linkedAtUtc;
    }

    public long Id { get; }

    public DocumentType Type { get; }

    /// <summary>The stored binary. Set when the link is created; it is NOT loaded together with the request (the binary can be large) - use the snapshot fields below.</summary>
    public StoredDocument? Document { get; }

    public long DocumentId { get; }

    public string FileName { get; }

    public long ContentLength { get; }

    public string ContentHash { get; }

    public long LinkedByUserId { get; }

    public DateTime LinkedAtUtc { get; }

    /// <summary>Set when a newer document replaced this one; null for the current link.</summary>
    public DateTime? SupersededAtUtc { get; private set; }

    public bool IsCurrent => SupersededAtUtc is null;

    internal void Supersede(DateTime atUtc) => SupersededAtUtc = atUtc;
}
