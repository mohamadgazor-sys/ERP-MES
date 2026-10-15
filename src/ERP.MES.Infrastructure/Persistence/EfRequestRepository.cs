using ERP.MES.Application.Persistence;
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
                    r.JalaliRequestYear,
                    r.RequestDate,
                    r.Title,
                    r.Status,
                    ItemCount = r.Items.Count(),
                })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new RequestSummary(r.RequestId, r.CustomerId, r.CustomerName, r.RequestNo, r.JalaliRequestYear, r.RequestDate, r.Title, r.Status, r.ItemCount))
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

    // Items, stages and approvals are loaded in a stable order: the Domain addresses stages by position, so they must come back in stage order.
    private static IQueryable<Request> LoadAggregate(ErpMesDbContext context) =>
        context.Requests
            .Include(r => r.Items.OrderBy(i => i.Id)).ThenInclude(i => i.Stages.OrderBy(s => s.StageNo))
            .Include(r => r.Items.OrderBy(i => i.Id)).ThenInclude(i => i.QcApprovals.OrderBy(a => EF.Property<long>(a, "Id")))
            .AsSplitQuery();

    private static Task<bool> KeyExistsAsync(ErpMesDbContext context, long customerId, int jalaliRequestYear, RequestNo requestNo, CancellationToken cancellationToken) =>
        context.Requests.AnyAsync(
            r => r.CustomerId == customerId && r.JalaliRequestYear == jalaliRequestYear && r.RequestNo == requestNo,
            cancellationToken);
}
