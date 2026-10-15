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

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ErpMesDbContext).Assembly);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
}
