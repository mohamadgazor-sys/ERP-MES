using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>
/// Provider-neutral EF Core model of the approved initial subset: customer, request, request item, item stage and QC
/// approval (docs/DOMAIN_MODEL_DEMO_SCOPE.md). It maps the Domain types as they are; business rules stay in the Domain.
/// The database provider and the migrations live in a provider project (ERP.MES.Infrastructure.Sqlite).
/// </summary>
public sealed class ErpMesDbContext(DbContextOptions<ErpMesDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Request> Requests => Set<Request>();

    public DbSet<RequestItem> RequestItems => Set<RequestItem>();

    public DbSet<ItemStage> ItemStages => Set<ItemStage>();

    public DbSet<QcApproval> QcApprovals => Set<QcApproval>();

    /// <summary>Name of the shadow property that holds the application-managed row version (Decision Log #23).</summary>
    public const string RowVersionPropertyName = "Version";

    private static readonly HashSet<Type> VersionedTypes = [typeof(Customer), typeof(Request), typeof(RequestItem), typeof(ItemStage)];

    /// <summary>The row version of a tracked entity of a versioned type (customer, request, request item, item stage).</summary>
    public long GetRowVersion(object entity) => (long)Entry(entity).Property(RowVersionPropertyName).CurrentValue!;

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyRowVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyRowVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Application-managed optimistic concurrency, portable across providers: a new row starts at version 1 and every update raises the version by one.
    /// The version is also the concurrency token, so an update that was based on an older version affects no row and EF Core throws
    /// <see cref="DbUpdateConcurrencyException"/> instead of silently overwriting the newer data. The version is a shadow property:
    /// the Domain types do not carry it and do not know about EF Core. QC approvals are append-only and have no version.
    /// </summary>
    private void ApplyRowVersions()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries().Where(e => VersionedTypes.Contains(e.Metadata.ClrType)).ToList())
        {
            var version = entry.Property(RowVersionPropertyName);
            if (entry.State == EntityState.Added)
            {
                version.CurrentValue = 1L;
            }
            else if (entry.State == EntityState.Modified)
            {
                version.CurrentValue = (long)version.OriginalValue! + 1;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErpMesDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
}
