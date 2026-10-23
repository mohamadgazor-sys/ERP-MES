using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Imports;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Item-import commit on <see cref="ErpMesDbContext"/> (provider-neutral): the Domain changes, the import log and the audit entries are one SaveChanges, hence one transaction.</summary>
public sealed class EfItemImportRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IItemImportRepository
{
    public async Task<Stored<Request>> CommitAsync(
        long requestId,
        long expectedRequestVersion,
        Action<Request> apply,
        Func<Request, ItemImport> buildLog,
        Func<Request, IReadOnlyList<AuditLogEntry>> audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(buildLog);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var request = await context.Requests
            .Include(r => r.Items.OrderBy(i => i.Id)).ThenInclude(i => i.Stages.OrderBy(s => s.StageNo))
            .Include(r => r.Items.OrderBy(i => i.Id)).ThenInclude(i => i.QcApprovals)
            .AsSplitQuery()
            .SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new RecordNotFoundException("request", requestId);
        PersistenceSave.CheckExpectedVersion(context, request, expectedRequestVersion);

        apply(request);
        context.ItemImports.Add(buildLog(request));
        context.AuditLog.AddRange(audit(request));
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<Request>(request, context.GetRowVersion(request));
    }
}
