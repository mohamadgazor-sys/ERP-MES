using ERP.MES.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Shared by the repositories: one save is one transaction, and EF Core failures are translated into the Application's persistence exceptions.</summary>
internal static class PersistenceSave
{
    public static async Task SaveAsync(ErpMesDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyConflictException(
                "The record was changed by someone else after it was loaded. Nothing was saved; reload it and try again.", exception);
        }
        catch (DbUpdateException exception)
        {
            throw new PersistenceException("The data could not be saved.", exception);
        }
    }

    public static void CheckExpectedVersion(ErpMesDbContext context, object entity, long? expectedVersion)
    {
        if (expectedVersion is { } expected && context.GetRowVersion(entity) != expected)
        {
            throw new ConcurrencyConflictException(
                $"The record is at version {context.GetRowVersion(entity)}, but version {expected} was expected. Nothing was saved; reload it and try again.");
        }
    }
}
