using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Documents;

namespace ERP.MES.Application.Requests;

/// <summary>
/// The technical drawing of an item (Decision Log #125 Q14): Drawing uploads and replaces a PDF of at most 20 MiB; an item has one current drawing and keeps earlier revisions as history; every
/// signed-in user may open it, and nothing is loaded until somebody asks for it. A missing drawing never blocks work: <see cref="HasDrawingAsync"/> lets screens and the stage release warn.
/// Each upload is audited with the actor in the same transaction. Every command reloads the user from the database (Decision Log #74).
/// </summary>
public sealed class ItemDrawingService(IItemDrawingRepository drawings, CurrentAuthorization authorization, IClock clock, IAuditContext auditContext, DocumentPolicy policy)
{
    public async Task<bool> CanManageAsync(Principal? principal, CancellationToken cancellationToken = default) =>
        (await authorization.CheckAsync(principal, Permission.ManageItemDrawings, cancellationToken)).IsAllowed;

    /// <summary>Uploads the first drawing of the item, or replaces the current one with a new revision.</summary>
    public async Task<Outcome<ItemDrawingInfo>> UploadAsync(Principal? principal, long itemId, string? fileName, byte[]? content, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ManageItemDrawings, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<ItemDrawingInfo>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        if (!StoredDocument.TryCreateItemDrawing(fileName, content, content is null ? new string('0', 64) : DocumentHash.Sha256Hex(content), policy.MaxRequestPdfBytes, user.UserId, now, out var document, out var error))
        {
            return Outcome<ItemDrawingInfo>.Invalid(error switch
            {
                PdfError.Empty => ValidationIssue.PdfEmpty,
                PdfError.TooLarge => ValidationIssue.PdfTooLarge,
                PdfError.NotPdf => ValidationIssue.PdfNotPdf,
                _ => ValidationIssue.PdfFileNameInvalid,
            });
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var info = await drawings.AddRevisionAsync(
                itemId, document!, user.UserId, now,
                (added, previousRevision) => new AuditLogEntry(
                    now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin,
                    previousRevision == 0 ? AuditActions.ItemDrawingUploaded : AuditActions.ItemDrawingReplaced, "RequestItem", itemId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"Item {itemId}: drawing '{added.FileName}' revision {added.Revision} ({added.ContentLength} bytes, SHA-256 {added.ContentHash})"
                    + (previousRevision == 0 ? string.Empty : $"; revision {previousRevision} kept as history")),
                cancellationToken);
            return info is null ? Outcome<ItemDrawingInfo>.Invalid(ValidationIssue.DrawingItemNotFound) : Outcome<ItemDrawingInfo>.Ok(info);
        });
    }

    /// <summary>The current drawing of each item that has one; an item without a drawing is simply absent from the answer.</summary>
    public async Task<Outcome<IReadOnlyList<ItemDrawingInfo>>> ListCurrentAsync(Principal? principal, IReadOnlyCollection<long> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        if (!(await authorization.CheckAsync(principal, Permission.ViewItemDrawings, cancellationToken)).IsAllowed)
        {
            return Outcome<IReadOnlyList<ItemDrawingInfo>>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<IReadOnlyList<ItemDrawingInfo>>.Ok(itemIds.Count == 0 ? [] : await drawings.ListCurrentAsync(itemIds, cancellationToken)));
    }

    public async Task<Outcome<IReadOnlyList<ItemDrawingInfo>>> ListRevisionsAsync(Principal? principal, long itemId, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewItemDrawings, cancellationToken)).IsAllowed)
        {
            return Outcome<IReadOnlyList<ItemDrawingInfo>>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<IReadOnlyList<ItemDrawingInfo>>.Ok(await drawings.ListRevisionsAsync(itemId, cancellationToken)));
    }

    /// <summary>The PDF of a revision (the current one when none is named). Loaded only here, when somebody explicitly opens it; NotFound when the item has no drawing.</summary>
    public async Task<Outcome<ItemDrawingFile>> OpenAsync(Principal? principal, long itemId, int? revision, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewItemDrawings, cancellationToken)).IsAllowed)
        {
            return Outcome<ItemDrawingFile>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var file = await drawings.FindFileAsync(itemId, revision, cancellationToken);
            return file is null ? Outcome<ItemDrawingFile>.Fail(OperationStatus.NotFound) : Outcome<ItemDrawingFile>.Ok(file);
        });
    }

    /// <summary>True when the item has a current drawing; used to warn, never to block.</summary>
    public async Task<bool> HasDrawingAsync(long itemId, CancellationToken cancellationToken = default) =>
        (await drawings.ListCurrentAsync([itemId], cancellationToken)).Count > 0;
}
