using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>Dispatches to and returns from an external route, recorded by the Warehouse (Decision Log #129). Rows are cancelled with a reason, never deleted.</summary>
public sealed class ExternalShipmentConfiguration : IEntityTypeConfiguration<ExternalShipment>
{
    public void Configure(EntityTypeBuilder<ExternalShipment> builder)
    {
        builder.ToTable("ExternalShipments", t => t.HasCheckConstraint("CK_ExternalShipments_BaseUnits", "\"BaseUnits\" > 0"));
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();
        builder.Property(s => s.RequestItemId).IsRequired();
        builder.Property(s => s.StageNo).IsRequired();
        // The foreign key to the stage (RequestItemId, StageNo) is declared in ItemStageConfiguration, where the stage's shadow key is defined, so the order the configurations run in does not matter.
        builder.HasIndex(s => new { s.RequestItemId, s.StageNo });
        builder.Property(s => s.Kind).HasConversion<int>().IsRequired();
        builder.Property(s => s.DispatchId);
        builder.HasOne<ExternalShipment>().WithMany().HasForeignKey(s => s.DispatchId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.BaseUnits).IsRequired();
        builder.Property(s => s.PieceDescription).HasMaxLength(ExternalShipment.MaxTextLength);
        builder.Property(s => s.Note).HasMaxLength(ExternalShipment.MaxTextLength);
        builder.Property(s => s.RecordedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.RecordedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.RecordedAtUtc).IsRequired();
        builder.Property(s => s.CancelledByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.CancelledByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.CancelledAtUtc);
        builder.Property(s => s.CancelReason).HasMaxLength(ExternalShipment.MaxTextLength);
        builder.Ignore(s => s.IsActive);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}
