using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Documents;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Contract gate persistence on <see cref="ErpMesDbContext"/> (provider-neutral). Each call is one transaction that reads the request facts, applies the Domain change, fences the request row and saves the contract with its audit entries.</summary>
public sealed class EfContractRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IContractRepository
{
    public async Task<IReadOnlyList<ContractListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var requests = await (
                from r in context.Requests
                join c in context.Customers on r.CustomerId equals c.Id
                where r.WorkType == WorkType.Make
                select new { r.Id, RequestNo = r.RequestNo.Value, c.Name, r.Status })
            .ToListAsync(cancellationToken);
        var contracts = await LoadContracts(context).ToListAsync(cancellationToken);
        var byRequest = contracts.ToDictionary(c => c.RequestId);

        return requests
            .Where(r => byRequest.ContainsKey(r.Id) || r.Status == RequestStatus.DrawingReturnedToPlanning)
            .Select(r =>
            {
                byRequest.TryGetValue(r.Id, out var contract);
                var version = contract?.CurrentVersion;
                return new ContractListItem(r.Id, r.RequestNo, r.Name, r.Status, contract?.Status, version?.ContractNo, version?.ContractDate, version?.VersionNo, contract?.UpdatedAtUtc);
            })
            .OrderBy(i => i.RequestId)
            .ToList();
    }

    public async Task<ContractDetail?> FindAsync(long requestId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var request = await context.Requests.Include(r => r.WorkflowHistory).SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null)
        {
            return null;
        }

        var customer = await context.Customers.SingleAsync(c => c.Id == request.CustomerId, cancellationToken);
        var contract = await LoadContracts(context).SingleOrDefaultAsync(c => c.RequestId == requestId, cancellationToken);
        return new ContractDetail(
            requestId, request.RequestNo.Value, customer.Name, request.WorkType, Facts(request), contract is null ? null : new Stored<RequestContract>(contract, context.GetRowVersion(contract)));
    }

    public async Task<Stored<RequestContract>> ApplyAsync(
        long requestId,
        long? expectedVersion,
        Func<RequestContract?, ContractEligibility, RequestContract> change,
        Func<RequestContract, string, IReadOnlyList<AuditLogEntry>> audit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var request = await context.Requests.Include(r => r.WorkflowHistory).SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new RecordNotFoundException("request", requestId);
        var contract = await LoadContracts(context).SingleOrDefaultAsync(c => c.RequestId == requestId, cancellationToken);
        if (expectedVersion is not null)
        {
            if (contract is null)
            {
                throw new RecordNotFoundException("contract of request", requestId);
            }

            PersistenceSave.CheckExpectedVersion(context, contract, expectedVersion);
        }

        var result = change(contract, Facts(request));
        if (contract is null)
        {
            context.RequestContracts.Add(result);
        }

        // The request row is fenced: its version is raised with the contract change, so a request that changed (for example sent back to Drawing) after its facts were read cannot be given a stale contract decision.
        context.Entry(request).Property(ErpMesDbContext.RowVersionPropertyName).IsModified = true;
        context.AuditLog.AddRange(audit(result, request.RequestNo.Value));
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<RequestContract>(result, context.GetRowVersion(result));
    }

    public async Task<StoredDocument?> FindPdfAsync(long requestId, int versionNo, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await (
                from l in context.ContractDocumentLinks
                join v in context.ContractVersions on EF.Property<long>(l, "ContractVersionId") equals v.Id
                join c in context.RequestContracts on EF.Property<long>(v, "ContractId") equals c.Id
                where c.RequestId == requestId && v.VersionNo == versionNo && l.SupersededAtUtc == null
                orderby l.Id descending
                select l.Document)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static ContractEligibility Facts(Request request) =>
        new(request.WorkType, request.Status, request.WorkflowHistory.LastOrDefault(e => e.Action == RequestAction.RecordDrawingResult)?.Outcome);

    private static IQueryable<RequestContract> LoadContracts(ErpMesDbContext context) =>
        context.RequestContracts
            .Include(c => c.Versions.OrderBy(v => v.VersionNo)).ThenInclude(v => v.DocumentLinks.OrderBy(l => l.Id))
            .Include(c => c.History.OrderBy(h => h.Id))
            .AsSplitQuery();
}
