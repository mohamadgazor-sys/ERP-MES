using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Quality;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>A raw-material inquiry (docs/RAW_MATERIAL_INQUIRY_DESIGN.md): belongs to a request; one row per round.</summary>
public sealed class RawMaterialInquiryConfiguration : IEntityTypeConfiguration<RawMaterialInquiry>
{
    public void Configure(EntityTypeBuilder<RawMaterialInquiry> builder)
    {
        builder.ToTable("RawMaterialInquiries");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedOnAdd();
        builder.Property(i => i.RequestId).IsRequired();
        builder.HasOne<Request>().WithMany().HasForeignKey(i => i.RequestId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.RoundNo).IsRequired();
        builder.HasIndex(i => new { i.RequestId, i.RoundNo }).IsUnique();
        builder.Property(i => i.Status).HasConversion<int>().IsRequired();
        builder.Property(i => i.Note);
        builder.Property(i => i.CreatedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.CreatedAtUtc).IsRequired();
        builder.Property(i => i.UpdatedAtUtc).IsRequired();
        builder.Ignore(i => i.IsOpen);
        builder.Ignore(i => i.AnsweredLineCount);
        builder.Ignore(i => i.UnansweredLineCount);
        builder.Ignore(i => i.PendingDispositionCount);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();

        // A draft line can be removed before the inquiry is sent (the Domain allows it only then), so removal is a real delete of a line that was never sent.
        builder.HasMany(i => i.Lines).WithOne().HasForeignKey("InquiryId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(i => i.History).WithOne().HasForeignKey("InquiryId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(i => i.History).HasField("_history").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class RawMaterialInquiryLineConfiguration : IEntityTypeConfiguration<RawMaterialInquiryLine>
{
    public void Configure(EntityTypeBuilder<RawMaterialInquiryLine> builder)
    {
        builder.ToTable("RawMaterialInquiryLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedOnAdd();
        builder.Property(l => l.LineNo).IsRequired();
        builder.Property(l => l.MaterialGrade).IsRequired();
        builder.Property(l => l.MaterialForm).IsRequired();
        builder.Property(l => l.DiameterMm);
        builder.Property(l => l.LengthMm);
        builder.Property(l => l.OtherDimension);
        builder.Property(l => l.RequiredQuantity).IsRequired();
        builder.Property(l => l.QuantityUnitId).IsRequired();
        builder.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(l => l.QuantityUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(l => l.StandardOrSpec);
        builder.Property(l => l.Remarks);
        builder.Ignore(l => l.CurrentResponse);
        builder.Property<long>("InquiryId");
        builder.HasIndex("InquiryId", nameof(RawMaterialInquiryLine.LineNo)).IsUnique();

        builder.HasMany(l => l.Responses).WithOne().HasForeignKey("LineId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(l => l.Responses).HasField("_responses").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(l => l.Dispositions).WithOne().HasForeignKey("LineId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(l => l.Dispositions).HasField("_dispositions").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(l => l.SupplyRoutes).WithOne().HasForeignKey("LineId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(l => l.SupplyRoutes).HasField("_supplyRoutes").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(l => l.CurrentDisposition);
        builder.Ignore(l => l.CurrentSupplyRoute);
        builder.Ignore(l => l.NeedsDisposition);
    }
}

/// <summary>Warehouse's answers. Append-only.</summary>
public sealed class RawMaterialInquiryResponseConfiguration : IEntityTypeConfiguration<RawMaterialInquiryResponse>
{
    public void Configure(EntityTypeBuilder<RawMaterialInquiryResponse> builder)
    {
        builder.ToTable("RawMaterialInquiryResponses");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();
        builder.Property(r => r.Answer).HasConversion<int>().IsRequired();
        builder.Property(r => r.Note);
        builder.Property(r => r.RespondedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.RespondedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.RespondedByDepartmentCode).IsRequired();
        builder.Property(r => r.RespondedAtUtc).IsRequired();
    }
}

/// <summary>The inquiry history. Append-only.</summary>
public sealed class InquiryHistoryEntryConfiguration : IEntityTypeConfiguration<InquiryHistoryEntry>
{
    public void Configure(EntityTypeBuilder<InquiryHistoryEntry> builder)
    {
        builder.ToTable("RawMaterialInquiryHistory");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Action).HasConversion<int>().IsRequired();
        builder.Property(e => e.FromStatus).HasConversion<int?>();
        builder.Property(e => e.ToStatus).HasConversion<int>().IsRequired();
        builder.Property(e => e.LineNo);
        builder.Property(e => e.Answer).HasConversion<int?>();
        builder.Property(e => e.Note);
        builder.Property(e => e.ActionByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ActionByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.ActionByDepartmentCode).IsRequired();
        builder.Property(e => e.ActionAtUtc).IsRequired();
    }
}

/// <summary>Drawing's dispositions of No answers. Append-only; each belongs to one Warehouse response.</summary>
public sealed class InquiryLineDispositionConfiguration : IEntityTypeConfiguration<InquiryLineDisposition>
{
    public void Configure(EntityTypeBuilder<InquiryLineDisposition> builder)
    {
        builder.ToTable("RawMaterialInquiryDispositions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();
        builder.Property(d => d.ResponseId).IsRequired();
        builder.HasOne<RawMaterialInquiryResponse>().WithMany().HasForeignKey(d => d.ResponseId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.Kind).HasConversion<int>().IsRequired();
        builder.Property(d => d.Note);
        builder.Property(d => d.NewRoundInquiryId);
        builder.HasOne<RawMaterialInquiry>().WithMany().HasForeignKey(d => d.NewRoundInquiryId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.DecidedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.DecidedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.DecidedByDepartmentCode).IsRequired();
        builder.Property(d => d.DecidedAtUtc).IsRequired();
    }
}

/// <summary>Planning's supply-route notes for No answers. Append-only.</summary>
public sealed class InquirySupplyRouteConfiguration : IEntityTypeConfiguration<InquirySupplyRoute>
{
    public void Configure(EntityTypeBuilder<InquirySupplyRoute> builder)
    {
        builder.ToTable("RawMaterialInquirySupplyRoutes");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();
        builder.Property(r => r.ResponseId).IsRequired();
        builder.HasOne<RawMaterialInquiryResponse>().WithMany().HasForeignKey(r => r.ResponseId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.Note).IsRequired();
        builder.Property(r => r.ExternalPurchase).IsRequired();
        builder.Property(r => r.DecidedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.DecidedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.DecidedByDepartmentCode).IsRequired();
        builder.Property(r => r.DecidedAtUtc).IsRequired();
    }
}

/// <summary>Drawing's clarification questions to Planning. Each fact is written once; the row is the history.</summary>
public sealed class DrawingClarificationConfiguration : IEntityTypeConfiguration<DrawingClarification>
{
    public void Configure(EntityTypeBuilder<DrawingClarification> builder)
    {
        builder.ToTable("DrawingClarifications");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();
        builder.Property(c => c.RequestId).IsRequired();
        builder.HasOne<Request>().WithMany().HasForeignKey(c => c.RequestId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.RequestId);
        builder.Property(c => c.Question).IsRequired();
        builder.Property(c => c.Status).HasConversion<int>().IsRequired();
        builder.Property(c => c.AskedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.AskedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(c => c.AskedByDepartmentCode).IsRequired();
        builder.Property(c => c.AskedAtUtc).IsRequired();
        builder.Property(c => c.Answer);
        builder.Property(c => c.AnsweredByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.AnsweredByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(c => c.AnsweredByDepartmentCode);
        builder.Property(c => c.AnsweredAtUtc);
        builder.Property(c => c.CloseNote);
        builder.Property(c => c.ClosedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.ClosedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(c => c.ClosedAtUtc);
        builder.Property(c => c.UpdatedAtUtc).IsRequired();
        builder.Ignore(c => c.IsResolved);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}
