using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Materials;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

public sealed class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("Materials");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedOnAdd();
        builder.Property(m => m.Code).UseCollation("BINARY").IsRequired();
        builder.HasIndex(m => m.Code).IsUnique();
        builder.Property(m => m.Name).IsRequired();
        builder.Property(m => m.Grade);
        builder.Property(m => m.Form);
        builder.Property(m => m.IsActive).IsRequired();
        builder.Property(m => m.CreatedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(m => m.CreatedAtUtc).IsRequired();
        builder.Property(m => m.UpdatedAtUtc).IsRequired();
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}

/// <summary>The material price history: append-only, one entry per material and effective date.</summary>
public sealed class MaterialPriceConfiguration : IEntityTypeConfiguration<MaterialPrice>
{
    public void Configure(EntityTypeBuilder<MaterialPrice> builder)
    {
        builder.ToTable("MaterialPrices");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();
        builder.Property(p => p.MaterialId).IsRequired();
        builder.HasOne<Material>().WithMany().HasForeignKey(p => p.MaterialId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(p => p.PricePerKgRial).IsRequired();
        builder.Property(p => p.EffectiveFrom).IsRequired();
        builder.Property(p => p.Reason);
        builder.Property(p => p.ChangedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.ChangedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(p => p.ChangedAtUtc).IsRequired();
        builder.HasIndex(p => new { p.MaterialId, p.EffectiveFrom }).IsUnique();
    }
}
