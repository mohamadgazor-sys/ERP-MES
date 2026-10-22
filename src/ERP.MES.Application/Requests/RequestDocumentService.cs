using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Documents;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

/// <summary>
/// The limits for stored documents. The maximum size of a request PDF is a configuration value (Decision Log #35, #54): 20 MiB = 20,971,520 bytes by default, shown to
/// users as "20 MB". A file of exactly the limit is accepted and one byte more is refused.
/// </summary>
public sealed record DocumentPolicy(long MaxRequestPdfBytes)
{
    public const long DefaultMaxRequestPdfBytes = 20L * 1024 * 1024;

    public static DocumentPolicy Default { get; } = new(DefaultMaxRequestPdfBytes);
}

/// <summary>The SHA-256 integrity hash of a document's content, upper-case hexadecimal. Computed here because the Domain holds no cryptography.</summary>
public static class DocumentHash
{
    public static string Sha256Hex(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
    }
}

/// <summary>The current request PDF: the stored binary with its name and hash.</summary>
public sealed record RequestPdfFile(string FileName, long ContentLength, string ContentHash, byte[] Content);

/// <summary>
/// The request PDF (Decision Log #87). Central attaches it before submitting the request to Management; the stored binary is linked to the request through a link
/// row and a replaced PDF stays as history (Decision Log #35). Attaching needs the Central permission (the same one that creates and submits requests) and a request
/// that is still <c>CREATED</c>; viewing the file needs the read permission for requests. Every command reloads the user from the database (Decision Log #74).
/// </summary>
public sealed class RequestDocumentService(IRequestRepository requests, CurrentAuthorization authorization, IClock clock, IAuditContext auditContext, DocumentPolicy policy)
{
    public async Task<Outcome<Stored<Request>>> AttachRequestPdfAsync(
        Principal? principal, long requestId, long expectedVersion, string? fileName, byte[]? content, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.CreateAndSubmitRequest, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<Request>>.Fail(OperationStatus.NotAuthorized);
        }

        var userId = decision.Current!.UserId;
        var now = clock.UtcNow;
        if (!StoredDocument.TryCreateRequestPdf(fileName, content, content is null ? new string('0', 64) : DocumentHash.Sha256Hex(content), policy.MaxRequestPdfBytes, userId, now, out var document, out var error))
        {
            return Outcome<Stored<Request>>.Invalid(error switch
            {
                PdfError.Empty => ValidationIssue.PdfEmpty,
                PdfError.TooLarge => ValidationIssue.PdfTooLarge,
                PdfError.NotPdf => ValidationIssue.PdfNotPdf,
                _ => ValidationIssue.PdfFileNameInvalid,
            });
        }

        IReadOnlyList<AuditLogEntry> Audit(Request request) =>
        [
            new AuditLogEntry(
                now, userId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, AuditActions.RequestPdfAttached, "Request", requestId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Request {request.RequestNo.Value}: request PDF '{document!.FileName}' attached ({document.ContentLength} bytes, SHA-256 {document.ContentHash})"
                + (request.DocumentLinks.Count > 1 ? "; the earlier PDF is kept as history" : string.Empty)),
        ];

        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var saved = await requests.UpdateRowsAsync(
                    requestId, RowVersionExpectations.ForRequest(expectedVersion), r => r.AttachRequestPdf(document!, userId, now), Audit, cancellationToken);
                return Outcome<Stored<Request>>.Ok(new Stored<Request>(saved.Value, saved.Versions.Request));
            });
        }
        catch (InvalidOperationException)
        {
            // The request is no longer CREATED (for example somebody submitted it meanwhile).
            return Outcome<Stored<Request>>.Fail(OperationStatus.InvalidState);
        }
    }

    /// <summary>The current request PDF with its content, for viewing. Any active user who may read requests may view it.</summary>
    public async Task<Outcome<RequestPdfFile>> GetRequestPdfAsync(Principal? principal, long requestId, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ViewCustomersAndRequests, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<RequestPdfFile>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var document = await requests.FindCurrentRequestPdfAsync(requestId, cancellationToken);
            return document is null
                ? Outcome<RequestPdfFile>.Fail(OperationStatus.NotFound)
                : Outcome<RequestPdfFile>.Ok(new RequestPdfFile(document.FileName, document.ContentLength, document.ContentHash, document.Content));
        });
    }
}
