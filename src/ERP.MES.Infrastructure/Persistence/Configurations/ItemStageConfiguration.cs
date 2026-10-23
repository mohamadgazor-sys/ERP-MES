using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

public sealed class ItemStageConfiguration : IEntityTypeConfiguration<ItemStage>
{
    public void Configure(EntityTypeBuilder<ItemStage> builder)
    {
        builder.ToTable("ItemStages");

        // The domain type has no identity of its own; the surrogate key is a shadow property (Domain and Database Specification 25.1).
        builder.Property<long>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");

        builder.Property(s => s.StageNo).IsRequired();
        builder.Property(s => s.StageTitle).IsRequired();
        builder.Property(s => s.PlannedMinutesPerPiece).IsRequired();
        builder.Property(s => s.PlannedQuantity).IsRequired();
        builder.Property(s => s.Status).HasConversion<int>().IsRequired();

        // Planning's planned unit and machine (Decision Log #89); null for stages of the simplified demonstration.
        builder.Property(s => s.ProductionUnitId);
        builder.HasOne<ERP.MES.Domain.Planning.ProductionUnit>().WithMany().HasForeignKey(s => s.ProductionUnitId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.MachineId);
        builder.HasOne<ERP.MES.Domain.Planning.Machine>().WithMany().HasForeignKey(s => s.MachineId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        // Operational queue state (Decision Log #78, #99): Planning position, when the stage became available (first come, first served), and a stop with its reason.
        builder.Property(s => s.PlanningPosition);
        builder.Property(s => s.AvailableSinceUtc);
        builder.Property(s => s.StoppedAtUtc);
        builder.Property(s => s.StopReason);
        builder.Property(s => s.StoppedByUserId);
        builder.HasOne<ERP.MES.Domain.Identity.User>().WithMany().HasForeignKey(s => s.StoppedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(s => s.IsStopped);

        // Quantity-level QC (Decision Log #92, #101): base units, never per-piece identities.
        builder.Property(s => s.InputBaseUnits);
        builder.Property(s => s.AwaitingQcBaseUnits).IsRequired().HasDefaultValue(0L);
        builder.Property(s => s.AcceptedBaseUnits).IsRequired().HasDefaultValue(0L);
        builder.Property(s => s.ReworkBaseUnits).IsRequired().HasDefaultValue(0L);
        builder.Property(s => s.ScrapBaseUnits).IsRequired().HasDefaultValue(0L);
        builder.Ignore(s => s.RemainingBaseUnits);

        // Derived from minutes per piece x quantity; not stored.
        builder.Ignore(s => s.PlannedTotalMinutes);

        builder.Property<long>("RequestItemId");

        // Application-managed row version and concurrency token (Decision Log #23); see ErpMesDbContext.ApplyRowVersions.
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();

        // (RequestItemId, StageNo) is unique (Domain and Database Specification 26) and is the target of the QC approval's composite
        // foreign key, so it is declared as an alternate key (a UNIQUE constraint); the surrogate primary key is unchanged.
        builder.HasAlternateKey("RequestItemId", nameof(ItemStage.StageNo));
    }
}
