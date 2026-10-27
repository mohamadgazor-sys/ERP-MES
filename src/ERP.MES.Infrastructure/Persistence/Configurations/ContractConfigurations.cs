using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>The request-level contract gate (Domain and Database Specification 11): at most one per request.</summary>
public sealed class RequestContractConfiguration : IEntityTypeConfiguration<RequestContract>
{
    public void Configure(EntityTypeBuilder<RequestContract> builder)
    {
        builder.ToTable("Contracts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();
        builder.Property(c => c.RequestId).IsRequired();
        builder.HasOne<Request>().WithMany().HasForeignKey(c => c.RequestId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.RequestId).IsUnique();
        builder.Property(c => c.Requirement).HasConversion<int>().IsRequired();
        builder.Property(c => c.Status).HasConversion<int>().IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).IsRequired();
        builder.Ignore(c => c.CurrentVersion);
        builder.Ignore(c => c.IsSatisfied);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();

        builder.HasMany(c => c.Versions).WithOne().HasForeignKey("ContractId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(c => c.Versions).HasField("_versions").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(c => c.History).WithOne().HasForeignKey("ContractId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(c => c.History).HasField("_history").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ContractVersionConfiguration : IEntityTypeConfiguration<ContractVersion>
{
    public void Configure(EntityTypeBuilder<ContractVersion> builder)
    {
        builder.ToTable("ContractVersions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedOnAdd();
        builder.Property(v => v.VersionNo).IsRequired();
        builder.Property(v => v.ContractNo);
        builder.Property(v => v.ContractDate);
        builder.Property(v => v.Title);
        builder.Property(v => v.Note);
        builder.Property(v => v.State).HasConversion<int>().IsRequired();
        builder.Property(v => v.CreatedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(v => v.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(v => v.CreatedAtUtc).IsRequired();
        builder.Property(v => v.SubmittedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(v => v.SubmittedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(v => v.SubmittedAtUtc);
        builder.Property(v => v.Decision).HasConversion<int?>();
        builder.Property(v => v.DecisionNote);
        builder.Property(v => v.DecidedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(v => v.DecidedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(v => v.DecidedAtUtc);
        builder.Ignore(v => v.CurrentPdf);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();

        builder.HasMany(v => v.DocumentLinks).WithOne().HasForeignKey("ContractVersionId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(v => v.DocumentLinks).HasField("_documentLinks").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property<long>("ContractId");
        builder.HasIndex("ContractId", nameof(ContractVersion.VersionNo)).IsUnique();
    }
}

/// <summary>The association of a stored PDF with a contract version (Decision Log #35): a link row, never deleted; a replaced PDF is marked superseded.</summary>
public sealed class ContractDocumentLinkConfiguration : IEntityTypeConfiguration<ContractDocumentLink>
{
    public void Configure(EntityTypeBuilder<ContractDocumentLink> builder)
    {
        builder.ToTable("ContractDocumentLinks");
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
        builder.HasOne(l => l.Document).WithMany().HasForeignKey(l => l.DocumentId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.LinkedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>The contract history. Append-only: rows are added with the change they record and never changed or deleted.</summary>
public sealed class ContractHistoryEntryConfiguration : IEntityTypeConfiguration<ContractHistoryEntry>
{
    public void Configure(EntityTypeBuilder<ContractHistoryEntry> builder)
    {
        builder.ToTable("ContractHistory");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Action).HasConversion<int>().IsRequired();
        builder.Property(e => e.FromStatus).HasConversion<int?>();
        builder.Property(e => e.ToStatus).HasConversion<int>().IsRequired();
        builder.Property(e => e.VersionNo);
        builder.Property(e => e.Note);
        builder.Property(e => e.ActionByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ActionByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.ActionByDepartmentCode).IsRequired();
        builder.Property(e => e.ActionAtUtc).IsRequired();
    }
}

/// <summary>Item-level contract decisions (Decision Log #111). Append-only: the latest row of an item is its current decision.</summary>
public sealed class ItemContractDecisionConfiguration : IEntityTypeConfiguration<ItemContractDecision>
{
    public void Configure(EntityTypeBuilder<ItemContractDecision> builder)
    {
        builder.ToTable("ItemContractDecisions");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();
        builder.Property(d => d.RequestId).IsRequired();
        builder.HasOne<Request>().WithMany().HasForeignKey(d => d.RequestId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.RequestItemId).IsRequired();
        builder.HasOne<RequestItem>().WithMany().HasForeignKey(d => d.RequestItemId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.Requirement).HasConversion<int>().IsRequired();
        builder.Property(d => d.Reason).IsRequired().HasMaxLength(ItemContractDecision.MaxReasonLength);
        builder.Property(d => d.DecidedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.DecidedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.DecidedAtUtc).IsRequired();
        builder.HasIndex(d => d.RequestItemId);
    }
}
