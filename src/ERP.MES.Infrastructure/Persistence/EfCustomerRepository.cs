using ERP.MES.Application.Persistence;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>
/// Customer persistence on <see cref="ErpMesDbContext"/> (provider-neutral). Each call uses its own short-lived context and one transaction.
/// Business rules stay in the Domain: values are stored and returned exactly as the Domain objects hold them.
/// </summary>
public sealed class EfCustomerRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : ICustomerRepository
{
    public async Task<Stored<Customer>> AddAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (customer.Id != 0)
        {
            throw new ArgumentException("The customer already has an id; use UpdateAsync to change a stored customer.", nameof(customer));
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Customers.Add(customer);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<Customer>(customer, context.GetRowVersion(customer));
    }

    public async Task<Stored<Customer>?> FindAsync(long customerId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var customer = await context.Customers.SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        return customer is null ? null : new Stored<Customer>(customer, context.GetRowVersion(customer));
    }

    public async Task<IReadOnlyList<Stored<Customer>>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        IQueryable<Customer> query = context.Customers;
        if (activeOnly)
        {
            query = query.Where(c => c.IsActive);
        }

        var customers = await query.OrderBy(c => c.Id).ToListAsync(cancellationToken);
        return customers.Select(c => new Stored<Customer>(c, context.GetRowVersion(c))).ToList();
    }

    public async Task<Stored<Customer>> UpdateAsync(long customerId, long? expectedVersion, Action<Customer> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var customer = await context.Customers.SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken)
            ?? throw new RecordNotFoundException("customer", customerId);

        PersistenceSave.CheckExpectedVersion(context, customer, expectedVersion);
        change(customer);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<Customer>(customer, context.GetRowVersion(customer));
    }
}
