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

        // Derived from minutes per piece x quantity; not stored.
        builder.Ignore(s => s.PlannedTotalMinutes);

        builder.Property<long>("RequestItemId");
        builder.HasIndex("RequestItemId", nameof(ItemStage.StageNo)).IsUnique();
    }
}
