using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

public sealed class RequestItemConfiguration : IEntityTypeConfiguration<RequestItem>
{
    public void Configure(EntityTypeBuilder<RequestItem> builder)
    {
        builder.ToTable("RequestItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedOnAdd();

        // Exact, case-sensitive text (binary collation), never parsed or normalised (Domain and Database Specification 5.2; Decision Log #58, G5).
        builder.Property(i => i.ItemNo).UseCollation("BINARY").IsRequired();
        builder.Property(i => i.PartName).IsRequired();

        // Exact decimal values; the scale is still open for Milestone 2, so no precision is set and nothing is rounded.
        builder.Property(i => i.RequestedQuantity).IsRequired();

        // Planning's specification (Decision Log #86, #95): nullable because REPAIR items may omit it and items of the simplified demonstration have none. Exact decimals, never rounded.
        builder.Property(i => i.QuantityTypeId);
        builder.HasOne<ERP.MES.Domain.Quality.QuantityType>().WithMany().HasForeignKey(i => i.QuantityTypeId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.BaseUnitsPerUnit).IsRequired().HasDefaultValue(1);
        builder.Property(i => i.MaterialId);
        builder.HasOne<ERP.MES.Domain.Materials.Material>().WithMany().HasForeignKey(i => i.MaterialId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.WeightKgPerBaseUnit);
        builder.Property(i => i.LengthMm);
        builder.Property(i => i.DiameterMm);

        // The Planning priority lives on the item; stages inherit it (Decision Log #58, G6).
        builder.Property(i => i.Priority).HasConversion<int>().IsRequired();
        builder.Property(i => i.CreatedByUserId).IsRequired();

        // The acting user must exist (Decision Log #70): a real foreign key to Users, deletion restricted.
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.CreatedAtUtc).IsRequired();
        builder.Property(i => i.UpdatedAtUtc).IsRequired();
        builder.Ignore(i => i.IsRouteCompleted);

        // Application-managed row version and concurrency token (Decision Log #23); see ErpMesDbContext.ApplyRowVersions.
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();

        builder.HasMany(i => i.Stages).WithOne().HasForeignKey("RequestItemId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(i => i.Stages).HasField("_stages").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(i => i.QcApprovals).WithOne().HasForeignKey("RequestItemId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(i => i.QcApprovals).HasField("_qcApprovals").UsePropertyAccessMode(PropertyAccessMode.Field);

        // The foreign key to the request is a shadow property (the domain item does not carry its request id).
        builder.Property<long>("RequestId");
        builder.HasIndex("RequestId", nameof(RequestItem.ItemNo)).IsUnique();
    }
}
