using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

/// <summary>
/// Customer list and maintenance (Decision Log #32, #75). Customers are never deleted (requests refer to them with restricting foreign keys): they are deactivated
/// and reactivated. Every method reloads the acting user from the database and checks the permission NOW (<see cref="CurrentAuthorization"/>); the principal the caller
/// passes is only a way to name the user. The creator recorded is the reloaded user. Rename and activate/deactivate pass the version the record was loaded at, so a
/// change made by someone else in the meantime is reported as <see cref="OperationStatus.ConcurrencyConflict"/> instead of being overwritten.
/// </summary>
public sealed class CustomerService(ICustomerRepository customers, CurrentAuthorization authorization, IClock clock)
{
    public async Task<Outcome<IReadOnlyList<Stored<Customer>>>> ListAsync(Principal? principal, bool activeOnly, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ViewCustomersAndRequests, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<IReadOnlyList<Stored<Customer>>>.Fail(OperationStatus.NotAuthorized);
        }

        return await GuardAsync(async () => Outcome<IReadOnlyList<Stored<Customer>>>.Ok(await customers.ListAsync(activeOnly, cancellationToken)));
    }

    public async Task<Outcome<Stored<Customer>>> CreateAsync(Principal? principal, string? name, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainCustomers, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<Customer>>.Fail(OperationStatus.NotAuthorized);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Outcome<Stored<Customer>>.Invalid(ValidationIssue.CustomerNameRequired);
        }

        // The name is stored exactly as typed (Decision Log #58 G2): no trimming, no case change, no uniqueness rule.
        return await GuardAsync(async () => Outcome<Stored<Customer>>.Ok(
            await customers.AddAsync(new Customer(0, name, decision.Current!.UserId, clock.UtcNow), cancellationToken)));
    }

    public async Task<Outcome<Stored<Customer>>> RenameAsync(Principal? principal, long customerId, long expectedVersion, string? name, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainCustomers, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<Customer>>.Fail(OperationStatus.NotAuthorized);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Outcome<Stored<Customer>>.Invalid(ValidationIssue.CustomerNameRequired);
        }

        var now = clock.UtcNow;
        return await GuardAsync(async () => Outcome<Stored<Customer>>.Ok(
            await customers.UpdateAsync(customerId, expectedVersion, c => c.Rename(name, now), cancellationToken)));
    }

    public async Task<Outcome<Stored<Customer>>> SetActiveAsync(Principal? principal, long customerId, long expectedVersion, bool isActive, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainCustomers, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<Customer>>.Fail(OperationStatus.NotAuthorized);
        }

        var now = clock.UtcNow;
        return await GuardAsync(async () => Outcome<Stored<Customer>>.Ok(
            await customers.UpdateAsync(customerId, expectedVersion, c => c.SetActive(isActive, now), cancellationToken)));
    }

    // Persistence failures become a status; anything else (a bug) is not swallowed.
    internal static async Task<Outcome<T>> GuardAsync<T>(Func<Task<Outcome<T>>> work)
    {
        try
        {
            return await work();
        }
        catch (ConcurrencyConflictException)
        {
            return Outcome<T>.Fail(OperationStatus.ConcurrencyConflict);
        }
        catch (RecordNotFoundException)
        {
            return Outcome<T>.Fail(OperationStatus.NotFound);
        }
        catch (DuplicateRequestException)
        {
            return Outcome<T>.Fail(OperationStatus.DuplicateRequest);
        }
        catch (PersistenceException)
        {
            return Outcome<T>.Fail(OperationStatus.Failed);
        }
    }
}
