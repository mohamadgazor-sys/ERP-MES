using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Documents;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="IItemDrawingRepository"/> (Decision Log #125 Q14). Listings project to metadata only; the binary is read by <see cref="FindFileAsync"/> alone.</summary>
public sealed class EfItemDrawingRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IItemDrawingRepository
{
    public async Task<bool> ItemExistsAsync(long itemId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Set<RequestItem>().AnyAsync(i => i.Id == itemId, cancellationToken);
    }

    public async Task<ItemDrawingInfo?> AddRevisionAsync(
        long itemId, StoredDocument document, long userId, DateTime atUtc, Func<ItemDrawingInfo, int, AuditLogEntry> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Set<RequestItem>().AnyAsync(i => i.Id == itemId, cancellationToken))
        {
            return null;
        }

        var current = await context.ItemDrawings.Where(d => d.RequestItemId == itemId && d.SupersededAtUtc == null).ToListAsync(cancellationToken);
        var latest = await context.ItemDrawings.Where(d => d.RequestItemId == itemId).MaxAsync(d => (int?)d.Revision, cancellationToken) ?? 0;
        foreach (var previous in current)
        {
            previous.MarkSuperseded(userId, atUtc);
        }

        var added = new ItemDrawing(itemId, document, latest + 1, userId, atUtc);
        context.ItemDrawings.Add(added);
        var info = new ItemDrawingInfo(itemId, added.Revision, added.FileName, added.ContentLength, added.ContentHash, userId, atUtc, true);
        context.AuditLog.Add(audit(info, latest));
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return info;
    }

    public async Task<IReadOnlyList<ItemDrawingInfo>> ListCurrentAsync(IReadOnlyCollection<long> itemIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(itemIds);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var ids = itemIds.ToList();
        return await context.ItemDrawings.AsNoTracking().Where(d => ids.Contains(d.RequestItemId) && d.SupersededAtUtc == null)
            .Select(d => new ItemDrawingInfo(d.RequestItemId, d.Revision, d.FileName, d.ContentLength, d.ContentHash, d.UploadedByUserId, d.UploadedAtUtc, true))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ItemDrawingInfo>> ListRevisionsAsync(long itemId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.ItemDrawings.AsNoTracking().Where(d => d.RequestItemId == itemId).OrderByDescending(d => d.Revision)
            .Select(d => new ItemDrawingInfo(d.RequestItemId, d.Revision, d.FileName, d.ContentLength, d.ContentHash, d.UploadedByUserId, d.UploadedAtUtc, d.SupersededAtUtc == null))
            .ToListAsync(cancellationToken);
    }

    public async Task<ItemDrawingFile?> FindFileAsync(long itemId, int? revision, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = context.ItemDrawings.AsNoTracking().Where(d => d.RequestItemId == itemId);
        query = revision is { } r ? query.Where(d => d.Revision == r) : query.Where(d => d.SupersededAtUtc == null);
        var row = await query.Select(d => new { d.RequestItemId, d.Revision, d.FileName, d.ContentLength, d.ContentHash, d.UploadedByUserId, d.UploadedAtUtc, Current = d.SupersededAtUtc == null, d.Document!.Content })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new ItemDrawingFile(new ItemDrawingInfo(row.RequestItemId, row.Revision, row.FileName, row.ContentLength, row.ContentHash, row.UploadedByUserId, row.UploadedAtUtc, row.Current), row.Content);
    }
}
