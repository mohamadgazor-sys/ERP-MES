using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>
/// Saves and loads customers. Every method is one transaction. Loaded objects are detached: change them by calling
/// <see cref="UpdateAsync"/>, which applies the change to a freshly loaded customer through the Domain methods (so the Domain's
/// rules run) and saves it. No database type appears in this contract.
/// </summary>
public interface ICustomerRepository
{
    /// <summary>Saves a new customer (id 0) and returns it with its generated id at version 1.</summary>
    Task<Stored<Customer>> AddAsync(Customer customer, CancellationToken cancellationToken = default);

    Task<Stored<Customer>?> FindAsync(long customerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Stored<Customer>>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the customer, runs <paramref name="change"/> (which should call the Domain methods), and saves. When
    /// <paramref name="expectedVersion"/> is given and the stored version differs, nothing is saved and
    /// <see cref="ConcurrencyConflictException"/> is thrown; a change saved by someone else during the operation is reported the same way.
    /// An exception thrown by <paramref name="change"/> (a Domain rule) propagates and nothing is saved.
    /// </summary>
    Task<Stored<Customer>> UpdateAsync(long customerId, long? expectedVersion, Action<Customer> change, CancellationToken cancellationToken = default);
}
