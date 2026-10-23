using ERP.MES.Domain.Costing;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Documents;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Imports;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Materials;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Quality;
using ERP.MES.Domain.Reporting;
using ERP.MES.Domain.Scheduling;
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

    public DbSet<QcInspectionLine> QcInspectionLines => Set<QcInspectionLine>();

    public DbSet<ShortfallDecision> ShortfallDecisions => Set<ShortfallDecision>();

    public DbSet<QcApproval> QcApprovals => Set<QcApproval>();

    public DbSet<StoredDocument> Documents => Set<StoredDocument>();

    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    public DbSet<ProductionUnit> ProductionUnits => Set<ProductionUnit>();

    public DbSet<Machine> Machines => Set<Machine>();

    public DbSet<RequestContract> RequestContracts => Set<RequestContract>();

    public DbSet<ContractVersion> ContractVersions => Set<ContractVersion>();

    public DbSet<ContractDocumentLink> ContractDocumentLinks => Set<ContractDocumentLink>();

    public DbSet<ContractHistoryEntry> ContractHistory => Set<ContractHistoryEntry>();

    public DbSet<DailyJobReport> DailyJobReports => Set<DailyJobReport>();

    public DbSet<JobReportLine> JobReportLines => Set<JobReportLine>();

    public DbSet<JobReportEvent> JobReportEvents => Set<JobReportEvent>();

    public DbSet<ShiftTemplate> ShiftTemplates => Set<ShiftTemplate>();

    public DbSet<WeeklyWorkday> WeeklyWorkdays => Set<WeeklyWorkday>();

    public DbSet<CalendarException> CalendarExceptions => Set<CalendarException>();

    public DbSet<UnitShift> UnitShifts => Set<UnitShift>();

    public DbSet<OperatorOffDay> OperatorOffDays => Set<OperatorOffDay>();

    public DbSet<WorkerSchedule> WorkerSchedules => Set<WorkerSchedule>();

    public DbSet<OvertimeLimits> OvertimeLimits => Set<OvertimeLimits>();

    public DbSet<OvertimeAssignment> OvertimeAssignments => Set<OvertimeAssignment>();

    public DbSet<ItemCostCalculation> ItemCostCalculations => Set<ItemCostCalculation>();

    public DbSet<ItemCostLine> ItemCostLines => Set<ItemCostLine>();

    public DbSet<ItemPriceDecision> ItemPriceDecisions => Set<ItemPriceDecision>();

    public DbSet<QueueOverride> QueueOverrides => Set<QueueOverride>();

    public DbSet<QueueOverrideHistoryEntry> QueueOverrideHistory => Set<QueueOverrideHistoryEntry>();

    public DbSet<UserUnitDesignation> UserUnitDesignations => Set<UserUnitDesignation>();

    public DbSet<ItemImport> ItemImports => Set<ItemImport>();

    public DbSet<ItemImportRow> ItemImportRows => Set<ItemImportRow>();

    public DbSet<RawMaterialInquiry> RawMaterialInquiries => Set<RawMaterialInquiry>();

    public DbSet<RawMaterialInquiryLine> RawMaterialInquiryLines => Set<RawMaterialInquiryLine>();

    public DbSet<InquiryLineDisposition> RawMaterialInquiryDispositions => Set<InquiryLineDisposition>();

    public DbSet<InquirySupplyRoute> RawMaterialInquirySupplyRoutes => Set<InquirySupplyRoute>();

    public DbSet<DrawingClarification> DrawingClarifications => Set<DrawingClarification>();

    public DbSet<RawMaterialInquiryResponse> RawMaterialInquiryResponses => Set<RawMaterialInquiryResponse>();

    public DbSet<InquiryHistoryEntry> RawMaterialInquiryHistory => Set<InquiryHistoryEntry>();

    public DbSet<Material> Materials => Set<Material>();

    public DbSet<MaterialPrice> MaterialPrices => Set<MaterialPrice>();

    public DbSet<Operator> Operators => Set<Operator>();

    public DbSet<OperatorMachineAssignment> OperatorMachineAssignments => Set<OperatorMachineAssignment>();

    public DbSet<OperatorMachineQualification> OperatorMachineQualifications => Set<OperatorMachineQualification>();

    public DbSet<MachineHourlyRate> MachineHourlyRates => Set<MachineHourlyRate>();

    public DbSet<UnitConversion> UnitConversions => Set<UnitConversion>();

    public DbSet<QuantityType> QuantityTypes => Set<QuantityType>();

    public DbSet<RequestDocumentLink> RequestDocumentLinks => Set<RequestDocumentLink>();

    public DbSet<RequestWorkflowEntry> RequestWorkflowEntries => Set<RequestWorkflowEntry>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<User> Users => Set<User>();

    public DbSet<UserCredential> UserCredentials => Set<UserCredential>();

    public DbSet<SystemSetup> SystemSetups => Set<SystemSetup>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    /// <summary>Name of the shadow property that holds the application-managed row version (Decision Log #23).</summary>
    public const string RowVersionPropertyName = "Version";

    private static readonly HashSet<Type> VersionedTypes = [typeof(Customer), typeof(Request), typeof(RequestItem), typeof(ItemStage), typeof(User), typeof(UserCredential), typeof(SystemSetup), typeof(UnitOfMeasure), typeof(UnitConversion), typeof(QuantityType), typeof(ProductionUnit), typeof(Machine), typeof(RawMaterialInquiry), typeof(DrawingClarification), typeof(RequestContract), typeof(ContractVersion), typeof(Material), typeof(Operator), typeof(OperatorMachineAssignment), typeof(OperatorMachineQualification), typeof(QueueOverride), typeof(UserUnitDesignation), typeof(ShiftTemplate), typeof(WeeklyWorkday), typeof(CalendarException), typeof(UnitShift), typeof(OperatorOffDay), typeof(WorkerSchedule), typeof(DailyJobReport), typeof(OvertimeLimits), typeof(OvertimeAssignment), typeof(ItemPriceDecision)];

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

        // The audit log is append-only (Decision Log #26): an entry can be added, never changed or removed.
        if (ChangeTracker.Entries<AuditLogEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("The audit log is append-only: entries cannot be changed or deleted.");
        }

        // The request workflow history is append-only too: an entry is added with the status change that caused it, never changed or removed.
        if (ChangeTracker.Entries<RequestWorkflowEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("The request workflow history is append-only: entries cannot be changed or deleted.");
        }

        // Inquiry answers and history are append-only as well.
        if (ChangeTracker.Entries<InquiryHistoryEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<RawMaterialInquiryResponse>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<InquiryLineDisposition>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<InquirySupplyRoute>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("The inquiry answers and history are append-only: entries cannot be changed or deleted.");
        }

        // The item-import log is append-only as well.
        if (ChangeTracker.Entries<ItemImport>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<ItemImportRow>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("The item-import log is append-only: entries cannot be changed or deleted.");
        }

        // QC inspection lines and shortfall decisions are append-only as well.
        if (ChangeTracker.Entries<QcInspectionLine>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<ShortfallDecision>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("The QC inspection lines and shortfall decisions are append-only: entries cannot be changed or deleted.");
        }

        // The job report events are append-only as well (lines of a draft are replaced as a whole, but events are never edited or removed).
        if (ChangeTracker.Entries<JobReportEvent>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("The job report events are append-only: entries cannot be changed or deleted.");
        }

        // Cost snapshots and their lines are immutable (Decision Log #104): a recalculation adds a new snapshot.
        if (ChangeTracker.Entries().Any(e => e.Entity is ItemCostCalculation or ItemCostLine && e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Cost snapshots are append-only: entries cannot be changed or deleted.");
        }

        // The queue override history is append-only as well.
        if (ChangeTracker.Entries<QueueOverrideHistoryEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("The queue override history is append-only: entries cannot be changed or deleted.");
        }

        // The contract history is append-only as well.
        if (ChangeTracker.Entries<ContractHistoryEntry>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("The contract history is append-only: entries cannot be changed or deleted.");
        }

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

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }
}
