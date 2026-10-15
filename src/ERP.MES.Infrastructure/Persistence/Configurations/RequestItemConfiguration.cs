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
        builder.Property(i => i.WorkType).HasConversion<int>().IsRequired();

        // Exact decimal values; the scale is still open for Milestone 2, so no precision is set and nothing is rounded.
        builder.Property(i => i.RequestedQuantity).IsRequired();

        // The Planning priority lives on the item; stages inherit it (Decision Log #58, G6).
        builder.Property(i => i.Priority).HasConversion<int>().IsRequired();
        builder.Property(i => i.CreatedByUserId).IsRequired();
        builder.Property(i => i.CreatedAtUtc).IsRequired();
        builder.Property(i => i.UpdatedAtUtc).IsRequired();
        builder.Ignore(i => i.IsRouteCompleted);

        builder.HasMany(i => i.Stages).WithOne().HasForeignKey("RequestItemId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(i => i.Stages).HasField("_stages").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(i => i.QcApprovals).WithOne().HasForeignKey("RequestItemId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(i => i.QcApprovals).HasField("_qcApprovals").UsePropertyAccessMode(PropertyAccessMode.Field);

        // The foreign key to the request is a shadow property (the domain item does not carry its request id).
        builder.Property<long>("RequestId");
        builder.HasIndex("RequestId", nameof(RequestItem.ItemNo)).IsUnique();
    }
}
