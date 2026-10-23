using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Clarification-question persistence on <see cref="ErpMesDbContext"/> (provider-neutral). Each call is one transaction.</summary>
public sealed class EfClarificationRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IClarificationRepository
{
    public async Task<IReadOnlyList<ClarificationItem>> ListUnresolvedAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var open = await context.DrawingClarifications.Where(c => c.Status != ClarificationStatus.Closed).OrderBy(c => c.Id).ToListAsync(cancellationToken);
        var requestIds = open.Select(c => c.RequestId).Distinct().ToList();
        var requests = await (
                from r in context.Requests
                join c in context.Customers on r.CustomerId equals c.Id
                where requestIds.Contains(r.Id)
                select new { r.Id, RequestNo = r.RequestNo.Value, c.Name, r.Status })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        return open.Select(c => new ClarificationItem(requests[c.RequestId].RequestNo, requests[c.RequestId].Name, requests[c.RequestId].Status, new Stored<DrawingClarification>(c, context.GetRowVersion(c)))).ToList();
    }

    public async Task<IReadOnlyList<ReviewRequestItem>> ListUnderReviewAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await (
                from r in context.Requests
                join c in context.Customers on r.CustomerId equals c.Id
                where r.Status == RequestStatus.UnderDrawingReview
                orderby r.Id
                select new { r.Id, RequestNo = r.RequestNo.Value, c.Name })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new ReviewRequestItem(r.Id, r.RequestNo, r.Name)).ToList();
    }

    public async Task<IReadOnlyList<Stored<DrawingClarification>>> ListForRequestAsync(long requestId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await context.DrawingClarifications.Where(c => c.RequestId == requestId).OrderBy(c => c.Id).ToListAsync(cancellationToken);
        return list.Select(c => new Stored<DrawingClarification>(c, context.GetRowVersion(c))).ToList();
    }

    public async Task<Stored<DrawingClarification>> AskAsync(
        long requestId, Func<RequestStatus, DrawingClarification> create, Func<DrawingClarification, string, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var request = await context.Requests.SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken) ?? throw new RecordNotFoundException("request", requestId);
        var clarification = create(request.Status);
        context.DrawingClarifications.Add(clarification);
        context.AuditLog.AddRange(audit(clarification, request.RequestNo.Value));
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<DrawingClarification>(clarification, context.GetRowVersion(clarification));
    }

    public async Task<Stored<DrawingClarification>> UpdateAsync(
        long clarificationId, long expectedVersion, Action<DrawingClarification> change, Func<DrawingClarification, string, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var clarification = await context.DrawingClarifications.SingleOrDefaultAsync(c => c.Id == clarificationId, cancellationToken) ?? throw new RecordNotFoundException("clarification", clarificationId);
        PersistenceSave.CheckExpectedVersion(context, clarification, expectedVersion);
        var request = await context.Requests.SingleAsync(r => r.Id == clarification.RequestId, cancellationToken);
        change(clarification);
        context.AuditLog.AddRange(audit(clarification, request.RequestNo.Value));
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<DrawingClarification>(clarification, context.GetRowVersion(clarification));
    }
}
