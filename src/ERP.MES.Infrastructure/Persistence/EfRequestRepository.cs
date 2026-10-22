using ERP.MES.Domain.Documents;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>
/// Request persistence on <see cref="ErpMesDbContext"/> (provider-neutral): the request, its items, their stages and QC approvals are saved and
/// loaded as one aggregate. Each call uses its own short-lived context and one transaction. The Domain objects are stored exactly as they are:
/// no value is trimmed, re-cased or otherwise changed here, and the Domain's rules run when callers use its methods.
/// </summary>
public sealed class EfRequestRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IRequestRepository
{
    public async Task<Stored<Request>> AddAsync(Request request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Id != 0)
        {
            throw new ArgumentException("The request already has an id; use UpdateAsync to change a stored request.", nameof(request));
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await context.Customers.AnyAsync(c => c.Id == request.CustomerId, cancellationToken))
        {
            throw new RecordNotFoundException("customer", request.CustomerId);
        }

        if (await KeyExistsAsync(context, request.CustomerId, request.JalaliRequestYear, request.RequestNo, cancellationToken))
        {
            throw new DuplicateRequestException(request.Key);
        }

        context.Requests.Add(request);
        try
        {
            await PersistenceSave.SaveAsync(context, cancellationToken);
        }
        catch (PersistenceException exception) when (exception is not ConcurrencyConflictException)
        {
            // Another writer may have saved the same business key between the check above and the save; the database's unique index rejected it.
            if (await ExistsAsync(request.Key, cancellationToken))
            {
                throw new DuplicateRequestException(request.Key, exception);
            }

            throw;
        }

        return new Stored<Request>(request, context.GetRowVersion(request));
    }

    public async Task<Stored<Request>?> FindAsync(long requestId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var request = await LoadAggregate(context).SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        return request is null ? null : new Stored<Request>(request, context.GetRowVersion(request));
    }

    public async Task<bool> ExistsAsync(RequestKey key, CancellationToken cancellationToken = default)
    {
        // A number the Domain would reject (empty, or with leading/trailing white space) cannot be stored, so it does not exist.
        if (!RequestNo.TryCreate(key.RequestNo, out var requestNo, out _))
        {
            return false;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await KeyExistsAsync(context, key.CustomerId, key.JalaliRequestYear, requestNo!, cancellationToken);
    }

    public async Task<IReadOnlyList<RequestNo>> FindCaseInsensitiveNearDuplicatesAsync(
        long customerId, int jalaliRequestYear, RequestNo requestNo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestNo);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var sameCustomerAndYear = await context.Requests
            .Where(r => r.CustomerId == customerId && r.JalaliRequestYear == jalaliRequestYear)
            .Select(r => r.RequestNo)
            .ToListAsync(cancellationToken);

        // The comparison rule belongs to the Domain; the database only narrows the candidates.
        return sameCustomerAndYear.Where(existing => RequestNo.IsCaseInsensitiveNearDuplicate(existing, requestNo)).ToList();
    }

    public async Task<IReadOnlyList<RequestSummary>> ListAsync(long? customerId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<Request> requests = context.Requests;
        if (customerId is { } id)
        {
            requests = requests.Where(r => r.CustomerId == id);
        }

        var rows = await (
                from r in requests
                join c in context.Customers on r.CustomerId equals c.Id
                orderby r.RequestDate descending, r.Id descending
                select new
                {
                    RequestId = r.Id,
                    r.CustomerId,
                    CustomerName = c.Name,
                    r.RequestNo,
                    r.WorkType,
                    r.JalaliRequestYear,
                    r.RequestDate,
                    r.Title,
                    r.Status,
                    ItemCount = r.Items.Count(),
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new RequestSummary(r.RequestId, r.CustomerId, r.CustomerName, r.RequestNo, r.WorkType, r.JalaliRequestYear, r.RequestDate, r.Title, r.Status, r.ItemCount))
            .ToList();
    }

    public async Task<Stored<Request>> UpdateAsync(long requestId, long? expectedVersion, Action<Request> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var request = await LoadAggregate(context).SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new RecordNotFoundException("request", requestId);

        PersistenceSave.CheckExpectedVersion(context, request, expectedVersion);
        change(request);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<Request>(request, context.GetRowVersion(request));
    }

    public async Task<StoredDocument?> FindCurrentRequestPdfAsync(long requestId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await (
                from l in context.RequestDocumentLinks
                where EF.Property<long>(l, "RequestId") == requestId && l.Type == DocumentType.RequestPdf && l.SupersededAtUtc == null
                orderby l.Id descending
                select l.Document)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<VersionedRequest?> FindWithVersionsAsync(long requestId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var request = await LoadAggregate(context).SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        return request is null ? null : new VersionedRequest(request, CaptureVersions(context, request));
    }

    public Task<VersionedRequest> UpdateRowsAsync(long requestId, RowVersionExpectations expected, Action<Request> change, CancellationToken cancellationToken = default) =>
        UpdateRowsCoreAsync(requestId, expected, change, audit: null, cancellationToken);

    public Task<VersionedRequest> UpdateRowsAsync(
        long requestId, RowVersionExpectations expected, Action<Request> change, Func<Request, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audit);
        return UpdateRowsCoreAsync(requestId, expected, change, audit, cancellationToken);
    }

    private async Task<VersionedRequest> UpdateRowsCoreAsync(
        long requestId, RowVersionExpectations expected, Action<Request> change, Func<Request, IReadOnlyList<AuditLogEntry>>? audit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(change);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var request = await LoadAggregate(context).SingleOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new RecordNotFoundException("request", requestId);

        // Every named row is compared BEFORE the change runs, so a rejected operation has changed nothing, in memory or in the database.
        CheckExpected(context, request, expected);
        change(request);

        // The audit entries are added to the same context, so the change and its audit trail are one transaction.
        if (audit is not null)
        {
            context.AuditLog.AddRange(audit(request));
        }

        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new VersionedRequest(request, CaptureVersions(context, request));
    }

    private static RequestVersions CaptureVersions(ErpMesDbContext context, Request request)
    {
        var items = new Dictionary<long, long>();
        var stages = new Dictionary<StageRef, long>();
        foreach (var item in request.Items)
        {
            items[item.Id] = context.GetRowVersion(item);
            foreach (var stage in item.Stages)
            {
                stages[new StageRef(item.Id, stage.StageNo)] = context.GetRowVersion(stage);
            }
        }

        return new RequestVersions(context.GetRowVersion(request), items, stages);
    }

    private static void CheckExpected(ErpMesDbContext context, Request request, RowVersionExpectations expected)
    {
        PersistenceSave.CheckExpectedVersion(context, request, expected.Request);

        foreach (var (itemId, version) in expected.Items)
        {
            var item = request.Items.FirstOrDefault(i => i.Id == itemId) ?? throw new RecordNotFoundException("request item", itemId);
            PersistenceSave.CheckExpectedVersion(context, item, version);
        }

        foreach (var (stageRef, version) in expected.Stages)
        {
            var stage = request.Items.FirstOrDefault(i => i.Id == stageRef.ItemId)?.Stages.FirstOrDefault(s => s.StageNo == stageRef.StageNo)
                ?? throw new RecordNotFoundException("stage " + stageRef.StageNo + " of item", stageRef.ItemId);
            PersistenceSave.CheckExpectedVersion(context, stage, version);
        }
    }

    // Items, stages and approvals are loaded in a stable order: the Domain addresses stages by position, so they must come back in stage order.
    private static IQueryable<Request> LoadAggregate(ErpMesDbContext context) =>
        context.Requests
            .Include(r => r.Items.OrderBy(i => i.Id)).ThenInclude(i => i.Stages.OrderBy(s => s.StageNo))
            .Include(r => r.Items.OrderBy(i => i.Id)).ThenInclude(i => i.QcApprovals.OrderBy(a => EF.Property<long>(a, "Id")))
            .Include(r => r.DocumentLinks.OrderBy(l => l.Id))
            .AsSplitQuery();

    private static Task<bool> KeyExistsAsync(ErpMesDbContext context, long customerId, int jalaliRequestYear, RequestNo requestNo, CancellationToken cancellationToken) =>
        context.Requests.AnyAsync(
            r => r.CustomerId == customerId && r.JalaliRequestYear == jalaliRequestYear && r.RequestNo == requestNo,
            cancellationToken);
}
