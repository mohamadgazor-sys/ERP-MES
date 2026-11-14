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
        builder.Property(d => d.LostAtContractorBaseUnits).IsRequired().HasDefaultValue(0L);
        builder.Property(d => d.Note).IsRequired();
        builder.Property(d => d.DecidedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.DecidedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.DecidedAtUtc).IsRequired();
    }
}

/// <summary>Append-only QC corrections of a final stage decision (Master Specification 1.10, 15; Decision Log #142).</summary>
public sealed class QcCorrectionConfiguration : IEntityTypeConfiguration<QcCorrection>
{
    public void Configure(EntityTypeBuilder<QcCorrection> builder)
    {
        builder.ToTable("QcCorrections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();
        builder.Property(c => c.RequestItemId).IsRequired();
        builder.Property(c => c.StageNo).IsRequired();
        builder.HasOne<RequestItem>().WithMany().HasForeignKey(c => c.RequestItemId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.RequestItemId, c.StageNo });
        builder.Property(c => c.ReworkBaseUnits).IsRequired();
        builder.Property(c => c.ScrapBaseUnits).IsRequired();
        builder.Ignore(c => c.CorrectedBaseUnits);
        builder.Property(c => c.Reason).IsRequired();
        builder.Property(c => c.AcceptedBeforeBaseUnits).IsRequired();
        builder.Ignore(c => c.AcceptedAfterBaseUnits);
        builder.Property(c => c.ResultingStatus).HasConversion<int>().IsRequired();
        builder.Property(c => c.AfterCustomerDispatch).IsRequired();
        builder.Property(c => c.CorrectedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.CorrectedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(c => c.CorrectedAtUtc).IsRequired();
        builder.HasMany(c => c.Sources).WithOne().HasForeignKey("CorrectionId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(c => c.Sources).HasField("_sources").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class QcCorrectionSourceConfiguration : IEntityTypeConfiguration<QcCorrectionSource>
{
    public void Configure(EntityTypeBuilder<QcCorrectionSource> builder)
    {
        builder.ToTable("QcCorrectionSources");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();
        builder.Property(s => s.InspectionLineId).IsRequired();
        builder.HasOne<QcInspectionLine>().WithMany().HasForeignKey(s => s.InspectionLineId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.SupersededBaseUnits).IsRequired();
    }
}

/// <summary>The shared "no longer current" marker of cost and Warehouse records (owner decisions Q5, B2, Decision Log #142). Append-only.</summary>
public sealed class RecordInvalidationConfiguration : IEntityTypeConfiguration<ERP.MES.Domain.Costing.RecordInvalidation>
{
    public void Configure(EntityTypeBuilder<ERP.MES.Domain.Costing.RecordInvalidation> builder)
    {
        builder.ToTable("RecordInvalidations");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedOnAdd();
        builder.Property(i => i.RequestItemId).IsRequired();
        builder.HasOne<RequestItem>().WithMany().HasForeignKey(i => i.RequestItemId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.Kind).HasConversion<int>().IsRequired();
        builder.Property(i => i.TargetId).IsRequired();
        builder.HasIndex(i => new { i.Kind, i.TargetId });
        builder.HasIndex(i => i.RequestItemId);
        builder.Property(i => i.Effect).HasConversion<int>().IsRequired();
        builder.Property(i => i.Reason).IsRequired();
        builder.Property(i => i.ByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.ByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.AtUtc).IsRequired();
        builder.Property(i => i.QcCorrectionId);
        builder.HasOne(i => i.QcCorrection).WithMany().HasForeignKey(i => i.QcCorrectionId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.JobReportRevisionId);
    }
}
