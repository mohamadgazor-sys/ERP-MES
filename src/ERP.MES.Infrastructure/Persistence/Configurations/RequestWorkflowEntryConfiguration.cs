using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>The request-level workflow history (Domain and Database Specification 4.4). Append-only: rows are added with the status change that caused them and never changed or deleted.</summary>
public sealed class RequestWorkflowEntryConfiguration : IEntityTypeConfiguration<RequestWorkflowEntry>
{
    public void Configure(EntityTypeBuilder<RequestWorkflowEntry> builder)
    {
        builder.ToTable("RequestWorkflowEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Action).HasConversion<int>().IsRequired();
        builder.Property(e => e.FromStatus).HasConversion<int>().IsRequired();
        builder.Property(e => e.ToStatus).HasConversion<int>().IsRequired();
        builder.Property(e => e.Outcome).HasConversion<int?>();
        builder.Property(e => e.Note);
        builder.Property(e => e.ActionByUserId).IsRequired();
        builder.Property(e => e.ActionByDepartmentCode).IsRequired();
        builder.Property(e => e.ActionAtUtc).IsRequired();

        // The acting user must exist (Decision Log #70).
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ActionByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);

        // The foreign key to the request is a shadow property (the domain entry does not carry its request id).
        builder.Property<long>("RequestId");
        builder.HasIndex("RequestId");
    }
}
