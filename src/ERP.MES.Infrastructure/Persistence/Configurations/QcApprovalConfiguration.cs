using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>
/// The demonstration-only QC approval record (one approved quantity). It is append-only history; it does not represent
/// the accepted/rework/scrap dispositions of Domain and Database Specification 9.3, which remain future production work.
/// </summary>
public sealed class QcApprovalConfiguration : IEntityTypeConfiguration<QcApproval>
{
    public void Configure(EntityTypeBuilder<QcApproval> builder)
    {
        builder.ToTable("QcApprovals");
        builder.Property<long>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");

        builder.Property<long>("RequestItemId");
        builder.Property(a => a.StageNo).IsRequired();
        builder.Property(a => a.ApprovedQuantity).IsRequired();
        builder.Property(a => a.ApprovedByUserId).IsRequired();
        builder.Property(a => a.ApprovedAtUtc).IsRequired();
    }
}
