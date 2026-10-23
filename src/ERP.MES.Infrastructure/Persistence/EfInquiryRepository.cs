using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Raw-material inquiry persistence on <see cref="ErpMesDbContext"/> (provider-neutral). Each call is one transaction that reads the request status, applies the Domain change and saves it with its audit entries.</summary>
public sealed class EfInquiryRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IInquiryRepository
{
    public async Task<IReadOnlyList<InquiryRequestItem>> ListRequestsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var requests = await (
                from r in context.Requests
                join c in context.Customers on r.CustomerId equals c.Id
                select new { r.Id, RequestNo = r.RequestNo.Value, c.Name, r.Status })
            .ToListAsync(cancellationToken);
        var inquiries = await context.RawMaterialInquiries.Select(i => new { i.RequestId, i.RoundNo, i.Status }).ToListAsync(cancellationToken);
        var byRequest = inquiries.GroupBy(i => i.RequestId).ToDictionary(g => g.Key, g => g.OrderBy(i => i.RoundNo).ToList());

        return requests
            .Where(r => byRequest.ContainsKey(r.Id) || r.Status is RequestStatus.UnderDrawingReview or RequestStatus.DrawingReturnedToPlanning)
            .Select(r => new InquiryRequestItem(
                r.Id, r.RequestNo, r.Name, r.Status, byRequest.TryGetValue(r.Id, out var list) ? list.Count : 0, byRequest.TryGetValue(r.Id, out var l2) ? l2[^1].Status : null))
            .OrderBy(i => i.RequestId)
            .ToList();
    }

    public async Task<IReadOnlyList<Stored<RawMaterialInquiry>>> ListForRequestAsync(long requestId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await Load(context).Where(i => i.RequestId == requestId).OrderBy(i => i.RoundNo).ToListAsync(cancellationToken);
        return list.Select(i => new Stored<RawMaterialInquiry>(i, context.GetRowVersion(i))).ToList();
    }

    public async Task<InquiryDetail?> FindAsync(long inquiryId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var inquiry = await Load(context).SingleOrDefaultAsync(i => i.Id == inquiryId, cancellationToken);
        if (inquiry is null)
        {
            return null;
        }

        var request = await context.Requests.SingleAsync(r => r.Id == inquiry.RequestId, cancellationToken);
        var customer = await context.Customers.SingleAsync(c => c.Id == request.CustomerId, cancellationToken);
        return new InquiryDetail(request.RequestNo.Value, customer.Name, request.Status, new Stored<RawMaterialInquiry>(inquiry, context.GetRowVersion(inquiry)));
    }

    public async Task<Stored<RawMaterialInquiry>> CreateAsync(
        long requestId, Func<InquiryContext, RawMaterialInquiry> create, Func<RawMaterialInquiry, string, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var request = await context.Requests.SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken) ?? throw new RecordNotFoundException("request", requestId);
        var rounds = await context.RawMaterialInquiries.Where(i => i.RequestId == requestId).Select(i => (int?)i.RoundNo).MaxAsync(cancellationToken);
        var inquiry = create(new InquiryContext(request.Status, (rounds ?? 0) + 1));
        context.RawMaterialInquiries.Add(inquiry);
        context.AuditLog.AddRange(audit(inquiry, request.RequestNo.Value));
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<RawMaterialInquiry>(inquiry, context.GetRowVersion(inquiry));
    }

    public async Task<Stored<RawMaterialInquiry>> UpdateAsync(
        long inquiryId, long expectedVersion, Action<RawMaterialInquiry, RequestStatus> change, Func<RawMaterialInquiry, string, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var inquiry = await Load(context).SingleOrDefaultAsync(i => i.Id == inquiryId, cancellationToken) ?? throw new RecordNotFoundException("raw-material inquiry", inquiryId);
        PersistenceSave.CheckExpectedVersion(context, inquiry, expectedVersion);
        var request = await context.Requests.SingleAsync(r => r.Id == inquiry.RequestId, cancellationToken);
        change(inquiry, request.Status);
        context.AuditLog.AddRange(audit(inquiry, request.RequestNo.Value));
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<RawMaterialInquiry>(inquiry, context.GetRowVersion(inquiry));
    }

    private static IQueryable<RawMaterialInquiry> Load(ErpMesDbContext context) =>
        context.RawMaterialInquiries
            .Include(i => i.Lines.OrderBy(l => l.LineNo)).ThenInclude(l => l.Responses.OrderBy(r => r.Id))
            .Include(i => i.Lines.OrderBy(l => l.LineNo)).ThenInclude(l => l.Dispositions.OrderBy(d => d.Id))
            .Include(i => i.Lines.OrderBy(l => l.LineNo)).ThenInclude(l => l.SupplyRoutes.OrderBy(r => r.Id))
            .Include(i => i.History.OrderBy(h => h.Id))
            .AsSplitQuery();
}
