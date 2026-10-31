using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Documents;

/// <summary>Kinds of stored document. Only the request PDF exists so far (Decision Log #35, #54, #87); other kinds are added with their workflows.</summary>
public enum DocumentType
{
    RequestPdf = 1,
    ContractPdf = 2,
    ItemDrawing = 3,
}

/// <summary>Why a file was refused as a request PDF.</summary>
public enum PdfError
{
    None = 0,
    Empty,
    TooLarge,
    NotPdf,
    FileNameInvalid,
}

/// <summary>
/// A stored binary (Domain and Database Specification section 20). The bytes are stored ONCE here and are attached to records only through link rows
/// (Decision Log #35: no single document foreign key; one binary may be linked to several records). The content hash is a SHA-256 integrity value; it is not
/// used for de-duplication, which is not decided (Decision Log #35). The Domain holds no cryptography: the caller (Application) computes the hash and the Domain only
/// checks its shape.
/// </summary>
public sealed class StoredDocument
{
    /// <summary>The PDF signature must appear within the first bytes of the file (the PDF specification allows a short preamble).</summary>
    private const int SignatureSearchBytes = 1024;

    private static readonly byte[] Signature = "%PDF-"u8.ToArray();

    private StoredDocument()
    {
        FileName = string.Empty;
        ContentHash = string.Empty;
        Content = [];
    }

    private StoredDocument(DocumentType type, string fileName, byte[] content, string contentHash, long uploadedByUserId, DateTime uploadedAtUtc)
    {
        Type = type;
        FileName = fileName;
        Content = content;
        ContentLength = content.Length;
        ContentHash = contentHash;
        UploadedByUserId = uploadedByUserId;
        UploadedAtUtc = uploadedAtUtc;
    }

    public long Id { get; }

    public DocumentType Type { get; }

    /// <summary>The original file name without any folder part.</summary>
    public string FileName { get; }

    public long ContentLength { get; }

    /// <summary>SHA-256 of the content, upper-case hexadecimal.</summary>
    public string ContentHash { get; }

    public byte[] Content { get; }

    public long UploadedByUserId { get; }

    public DateTime UploadedAtUtc { get; }

    /// <summary>
    /// Validates and creates a request PDF: PDF only (signature check, not just the extension), not empty, at most <paramref name="maxBytes"/> (a file of exactly
    /// <paramref name="maxBytes"/> is accepted, one byte more is refused - Decision Log #54), and a usable file name.
    /// </summary>
    public static bool TryCreateRequestPdf(
        string? fileName, byte[]? content, string contentHash, long maxBytes, long uploadedByUserId, DateTime uploadedAtUtc, out StoredDocument? document, out PdfError error) =>
        TryCreatePdf(DocumentType.RequestPdf, fileName, content, contentHash, maxBytes, uploadedByUserId, uploadedAtUtc, out document, out error);

    /// <summary>The same checks for a contract PDF (Decision Log #85, #35).</summary>
    public static bool TryCreateContractPdf(
        string? fileName, byte[]? content, string contentHash, long maxBytes, long uploadedByUserId, DateTime uploadedAtUtc, out StoredDocument? document, out PdfError error) =>
        TryCreatePdf(DocumentType.ContractPdf, fileName, content, contentHash, maxBytes, uploadedByUserId, uploadedAtUtc, out document, out error);

    /// <summary>The same checks for the technical drawing of an item (Decision Log #125 Q14): PDF only, at most <paramref name="maxBytes"/> (20 MiB by default).</summary>
    public static bool TryCreateItemDrawing(
        string? fileName, byte[]? content, string contentHash, long maxBytes, long uploadedByUserId, DateTime uploadedAtUtc, out StoredDocument? document, out PdfError error) =>
        TryCreatePdf(DocumentType.ItemDrawing, fileName, content, contentHash, maxBytes, uploadedByUserId, uploadedAtUtc, out document, out error);

    private static bool TryCreatePdf(
        DocumentType type, string? fileName, byte[]? content, string contentHash, long maxBytes, long uploadedByUserId, DateTime uploadedAtUtc, out StoredDocument? document, out PdfError error)
    {
        document = null;
        if (contentHash is null || contentHash.Length != 64 || !contentHash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("The content hash must be 64 hexadecimal characters (SHA-256).", nameof(contentHash));
        }

        var name = CleanFileName(fileName);
        if (name is null)
        {
            error = PdfError.FileNameInvalid;
            return false;
        }

        if (content is null || content.Length == 0)
        {
            error = PdfError.Empty;
            return false;
        }

        if (content.LongLength > maxBytes)
        {
            error = PdfError.TooLarge;
            return false;
        }

        if (!HasPdfSignature(content))
        {
            error = PdfError.NotPdf;
            return false;
        }

        document = new StoredDocument(type, name, content, contentHash.ToUpperInvariant(), uploadedByUserId, Customer.RequireUtc(uploadedAtUtc, nameof(uploadedAtUtc)));
        error = PdfError.None;
        return true;
    }

    private static string? CleanFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        // Only the file name is kept, whatever folder separators the caller's system used.
        var name = fileName.Trim();
        var cut = name.LastIndexOfAny(['\\', '/']);
        if (cut >= 0)
        {
            name = name[(cut + 1)..];
        }

        return name.Length is > 0 and <= 255 && name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && name.Length > ".pdf".Length ? name : null;
    }

    private static bool HasPdfSignature(byte[] content) =>
        content.AsSpan(0, Math.Min(content.Length, SignatureSearchBytes)).IndexOf(Signature) >= 0;
}
