using ERP.MES.Application.Security;
using ERP.MES.Domain.Documents;

namespace ERP.MES.Application.Persistence;

/// <summary>One drawing revision as listed (no binary).</summary>
public sealed record ItemDrawingInfo(long ItemId, int Revision, string FileName, long ContentLength, string ContentHash, long UploadedByUserId, DateTime UploadedAtUtc, bool IsCurrent);

/// <summary>A drawing with its binary, loaded only when somebody opens it.</summary>
public sealed record ItemDrawingFile(ItemDrawingInfo Info, byte[] Content);

/// <summary>
/// Saves and loads the item drawings (Decision Log #125 Q14). The binary is stored once in the documents table and linked by a row per revision; adding a revision supersedes the current one in
/// the same transaction as its audit entry. Listings never load the binary.
/// </summary>
public interface IItemDrawingRepository
{
    /// <summary>True when an item with this id exists.</summary>
    Task<bool> ItemExistsAsync(long itemId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the document as the next revision of the item and supersedes the current revision, in one transaction with the audit entry. Returns the new revision's info, or null when the item does not
    /// exist. A concurrent upload for the same item raises <see cref="ConcurrencyConflictException"/> or fails on the unique (item, revision) key; nothing is saved then.
    /// </summary>
    Task<ItemDrawingInfo?> AddRevisionAsync(long itemId, StoredDocument document, long userId, DateTime atUtc, Func<ItemDrawingInfo, int, AuditLogEntry> audit, CancellationToken cancellationToken = default);

    /// <summary>The current drawing of each of the given items that has one (missing items have none).</summary>
    Task<IReadOnlyList<ItemDrawingInfo>> ListCurrentAsync(IReadOnlyCollection<long> itemIds, CancellationToken cancellationToken = default);

    /// <summary>All revisions of one item, newest first.</summary>
    Task<IReadOnlyList<ItemDrawingInfo>> ListRevisionsAsync(long itemId, CancellationToken cancellationToken = default);

    /// <summary>The binary of a revision (the current one when <paramref name="revision"/> is null), or null when there is none.</summary>
    Task<ItemDrawingFile?> FindFileAsync(long itemId, int? revision, CancellationToken cancellationToken = default);
}
