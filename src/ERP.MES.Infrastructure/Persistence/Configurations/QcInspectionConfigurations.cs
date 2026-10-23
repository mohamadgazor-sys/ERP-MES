using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>Append-only QC decisions on base units of an item's stage (Decision Log #95 D2, #101).</summary>
public sealed class QcInspectionLineConfiguration : IEntityTypeConfiguration<QcInspectionLine>
{
    public void Configure(EntityTypeBuilder<QcInspectionLine> builder)
    {
        builder.ToTable("QcInspectionLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedOnAdd();
        builder.Property(l => l.RequestItemId).IsRequired();
        builder.Property(l => l.StageNo).IsRequired();
        builder.HasOne<ItemStage>().WithMany().HasForeignKey(nameof(QcInspectionLine.RequestItemId), nameof(QcInspectionLine.StageNo))
            .HasPrincipalKey("RequestItemId", nameof(ItemStage.StageNo)).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => new { l.RequestItemId, l.StageNo });
        builder.Property(l => l.InspectionId).IsRequired();
        builder.Property(l => l.Disposition).HasConversion<int>().IsRequired();
        builder.Property(l => l.BaseUnits).IsRequired();
        builder.Property(l => l.Reason);
        builder.Property(l => l.DecidedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.DecidedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(l => l.DecidedAtUtc).IsRequired();
    }
}

/// <summary>Append-only decisions that close a shortfall created by scrap (Decision Log #95 D3, #101).</summary>
public sealed class ShortfallDecisionConfiguration : IEntityTypeConfiguration<ShortfallDecision>
{
    public void Configure(EntityTypeBuilder<ShortfallDecision> builder)
    {
        builder.ToTable("ShortfallDecisions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();
        builder.Property(d => d.RequestItemId).IsRequired();
        builder.Property(d => d.StageNo).IsRequired();
        builder.HasOne<ItemStage>().WithMany().HasForeignKey(nameof(ShortfallDecision.RequestItemId), nameof(ShortfallDecision.StageNo))
            .HasPrincipalKey("RequestItemId", nameof(ItemStage.StageNo)).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => new { d.RequestItemId, d.StageNo });
        builder.Property(d => d.Kind).HasConversion<int>().IsRequired();
        builder.Property(d => d.ShortfallBaseUnits).IsRequired();
        builder.Property(d => d.Note).IsRequired();
        builder.Property(d => d.DecidedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.DecidedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.DecidedAtUtc).IsRequired();
    }
}
