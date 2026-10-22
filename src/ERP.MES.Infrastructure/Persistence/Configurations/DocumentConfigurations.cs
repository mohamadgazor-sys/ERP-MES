using ERP.MES.Domain.Documents;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>A stored binary (Domain and Database Specification section 20; Decision Log #35). Rows are never updated or deleted; the bytes are stored once.</summary>
public sealed class StoredDocumentConfiguration : IEntityTypeConfiguration<StoredDocument>
{
    public void Configure(EntityTypeBuilder<StoredDocument> builder)
    {
        builder.ToTable("Documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();
        builder.Property(d => d.Type).HasConversion<int>().IsRequired();
        builder.Property(d => d.FileName).IsRequired();
        builder.Property(d => d.ContentLength).IsRequired();
        builder.Property(d => d.ContentHash).IsRequired();
        builder.Property(d => d.Content).IsRequired();
        builder.Property(d => d.UploadedByUserId).IsRequired();

        // The acting user must exist (Decision Log #70).
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.UploadedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.UploadedAtUtc).IsRequired();
    }
}

/// <summary>The association of a document with a request (a link table, Decision Log #35). A link is never deleted; a replaced PDF is marked superseded.</summary>
public sealed class RequestDocumentLinkConfiguration : IEntityTypeConfiguration<RequestDocumentLink>
{
    public void Configure(EntityTypeBuilder<RequestDocumentLink> builder)
    {
        builder.ToTable("RequestDocumentLinks");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedOnAdd();
        builder.Property(l => l.Type).HasConversion<int>().IsRequired();
        builder.Property(l => l.FileName).IsRequired();
        builder.Property(l => l.ContentLength).IsRequired();
        builder.Property(l => l.ContentHash).IsRequired();
        builder.Property(l => l.LinkedByUserId).IsRequired();
        builder.Property(l => l.LinkedAtUtc).IsRequired();
        builder.Property(l => l.SupersededAtUtc);
        builder.Ignore(l => l.IsCurrent);

        // The binary is referenced, never loaded with the request: the navigation is only used to insert a new document and its link together.
        builder.HasOne(l => l.Document).WithMany().HasForeignKey(l => l.DocumentId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.LinkedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);

        // The foreign key to the request is a shadow property (the domain link does not carry its request id).
        builder.Property<long>("RequestId");
        builder.HasIndex("RequestId", nameof(RequestDocumentLink.Type));
    }
}
