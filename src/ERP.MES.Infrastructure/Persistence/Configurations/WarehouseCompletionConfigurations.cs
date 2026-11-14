using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using ERP.MES.Domain.Warehouse;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>The final warehouse receipt of an item (Decision Log #95 D7, #105): at most one per item, append-only.</summary>
public sealed class WarehouseReceiptConfiguration : IEntityTypeConfiguration<WarehouseReceipt>
{
    public void Configure(EntityTypeBuilder<WarehouseReceipt> builder)
    {
        builder.ToTable("WarehouseReceipts");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();
        builder.Property(r => r.RequestItemId).IsRequired();
        builder.HasOne<RequestItem>().WithMany().HasForeignKey(r => r.RequestItemId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.Sequence).IsRequired().HasDefaultValue(1);
        builder.HasIndex(r => new { r.RequestItemId, r.Sequence }).IsUnique();
        builder.Property(r => r.ReceivedBaseUnits).IsRequired();
        builder.Property(r => r.Note);
        builder.Property(r => r.ReceivedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.ReceivedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.ReceivedAtUtc).IsRequired();
    }
}

public sealed class ItemFinishedSentConfiguration : IEntityTypeConfiguration<ItemFinishedSent>
{
    public void Configure(EntityTypeBuilder<ItemFinishedSent> builder)
    {
        builder.ToTable("ItemFinishedSent");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedOnAdd();
        builder.Property(f => f.RequestItemId).IsRequired();
        builder.HasOne<RequestItem>().WithMany().HasForeignKey(f => f.RequestItemId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(f => f.Sequence).IsRequired().HasDefaultValue(1);
        builder.HasIndex(f => new { f.RequestItemId, f.Sequence }).IsUnique();
        builder.Property(f => f.ReceiptId).IsRequired();
        builder.HasOne<WarehouseReceipt>().WithMany().HasForeignKey(f => f.ReceiptId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(f => f.PriceDecisionId).IsRequired();
        builder.HasOne<ItemPriceDecision>().WithMany().HasForeignKey(f => f.PriceDecisionId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(f => f.ApprovedPriceRial).IsRequired();
        builder.Property(f => f.MarkedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(f => f.MarkedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(f => f.MarkedAtUtc).IsRequired();
    }
}

public sealed class CustomerDispatchConfiguration : IEntityTypeConfiguration<CustomerDispatch>
{
    public void Configure(EntityTypeBuilder<CustomerDispatch> builder)
    {
        builder.ToTable("CustomerDispatches");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();
        builder.Property(d => d.RequestItemId).IsRequired();
        builder.HasOne<RequestItem>().WithMany().HasForeignKey(d => d.RequestItemId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.RequestItemId);
        builder.Property(d => d.DispatchDate).IsRequired();
        builder.Property(d => d.DocumentReference).UseCollation("BINARY").IsRequired();
        builder.Property(d => d.Note);
        builder.Property(d => d.RecordedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.RecordedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.RecordedAtUtc).IsRequired();
    }
}

/// <summary>A proposed, approved or rejected reversal of a Warehouse record (owner decision B9, Decision Log #142): never deleted; the status is the concurrency token of the single decision.</summary>
public sealed class WarehouseReversalConfiguration : IEntityTypeConfiguration<WarehouseReversal>
{
    public void Configure(EntityTypeBuilder<WarehouseReversal> builder)
    {
        builder.ToTable("WarehouseReversals");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();
        builder.Property(r => r.RequestItemId).IsRequired();
        builder.HasOne<RequestItem>().WithMany().HasForeignKey(r => r.RequestItemId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.TargetKind).HasConversion<int>().IsRequired();
        builder.Property(r => r.TargetId).IsRequired();
        builder.HasIndex(r => new { r.TargetKind, r.TargetId });
        builder.HasIndex(r => r.RequestItemId);
        builder.Property(r => r.Nature).HasConversion<int>().IsRequired();
        builder.Property(r => r.Status).HasConversion<int>().IsRequired().IsConcurrencyToken();
        builder.Property(r => r.ProposalReason).IsRequired();
        builder.Property(r => r.ProposedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.ProposedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.ProposedAtUtc).IsRequired();
        builder.Property(r => r.DecidedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.DecidedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.DecidedAtUtc);
        builder.Property(r => r.DecisionReason);
    }
}
