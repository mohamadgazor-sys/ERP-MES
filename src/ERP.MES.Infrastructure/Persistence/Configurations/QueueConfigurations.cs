using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>The current Management override state of a stage; one row per stage, never deleted (Decision Log #78, #99).</summary>
public sealed class QueueOverrideConfiguration : IEntityTypeConfiguration<QueueOverride>
{
    public void Configure(EntityTypeBuilder<QueueOverride> builder)
    {
        builder.ToTable("QueueOverrides");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedOnAdd();
        builder.Property(o => o.RequestItemId).IsRequired();
        builder.Property(o => o.StageNo).IsRequired();
        builder.HasOne<ItemStage>().WithMany().HasForeignKey(nameof(QueueOverride.RequestItemId), nameof(QueueOverride.StageNo))
            .HasPrincipalKey("RequestItemId", nameof(ItemStage.StageNo)).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => new { o.RequestItemId, o.StageNo }).IsUnique();
        builder.Property(o => o.ProductionUnitId).IsRequired();
        builder.HasOne<ProductionUnit>().WithMany().HasForeignKey(o => o.ProductionUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => new { o.ProductionUnitId, o.IsActive });
        builder.Property(o => o.Rank).IsRequired();
        builder.Property(o => o.IsActive).IsRequired();
        builder.Property(o => o.Reason).IsRequired();
        builder.Property(o => o.SetByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.SetByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(o => o.SetAtUtc).IsRequired();
        builder.Property(o => o.NeedsReview).IsRequired();
        builder.Property(o => o.ReviewNote);
        builder.Property(o => o.ReviewFlaggedAtUtc);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}

/// <summary>Append-only record of every override operation (who, when, reason, old and new rank and queue position).</summary>
public sealed class QueueOverrideHistoryEntryConfiguration : IEntityTypeConfiguration<QueueOverrideHistoryEntry>
{
    public void Configure(EntityTypeBuilder<QueueOverrideHistoryEntry> builder)
    {
        builder.ToTable("QueueOverrideHistory");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.OperationId).IsRequired();
        builder.HasIndex(e => e.OperationId);
        builder.Property(e => e.RequestItemId).IsRequired();
        builder.Property(e => e.StageNo).IsRequired();
        builder.HasOne<ItemStage>().WithMany().HasForeignKey(nameof(QueueOverrideHistoryEntry.RequestItemId), nameof(QueueOverrideHistoryEntry.StageNo))
            .HasPrincipalKey("RequestItemId", nameof(ItemStage.StageNo)).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.ProductionUnitId).IsRequired();
        builder.HasOne<ProductionUnit>().WithMany().HasForeignKey(e => e.ProductionUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.Action).HasConversion<int>().IsRequired();
        builder.Property(e => e.UserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.AtUtc).IsRequired();
        builder.Property(e => e.Reason).IsRequired();
        builder.Property(e => e.OldRank);
        builder.Property(e => e.NewRank);
        builder.Property(e => e.OldOrder);
        builder.Property(e => e.NewOrder);
    }
}

/// <summary>User-to-unit designations; withdrawn rows are kept (Decision Log #99).</summary>
public sealed class UserUnitDesignationConfiguration : IEntityTypeConfiguration<UserUnitDesignation>
{
    public void Configure(EntityTypeBuilder<UserUnitDesignation> builder)
    {
        builder.ToTable("UserUnitDesignations");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();
        builder.Property(d => d.UserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.UserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.ProductionUnitId).IsRequired();
        builder.HasOne<ProductionUnit>().WithMany().HasForeignKey(d => d.ProductionUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => new { d.UserId, d.ProductionUnitId }).IsUnique();
        builder.Property(d => d.AssignedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.AssignedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.AssignedAtUtc).IsRequired();
        builder.Property(d => d.RevokedAtUtc);
        builder.Property(d => d.RevokedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.RevokedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(d => d.IsActive);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}
