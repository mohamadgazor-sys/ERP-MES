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
