using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

public sealed class ProductionUnitConfiguration : IEntityTypeConfiguration<ProductionUnit>
{
    public void Configure(EntityTypeBuilder<ProductionUnit> builder)
    {
        builder.ToTable("ProductionUnits");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedOnAdd();
        builder.Property(u => u.Code).UseCollation("BINARY").IsRequired();
        builder.HasIndex(u => u.Code).IsUnique();
        builder.Property(u => u.Name).IsRequired();
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.CreatedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(u => u.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(u => u.CreatedAtUtc).IsRequired();
        builder.Property(u => u.UpdatedAtUtc).IsRequired();
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}

public sealed class StageTitleConfiguration : IEntityTypeConfiguration<StageTitle>
{
    public void Configure(EntityTypeBuilder<StageTitle> builder)
    {
        builder.ToTable("StageTitles");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        // Exact, case-sensitive and unique, like the other business codes; a stage keeps its own copy of the text.
        builder.Property(t => t.Title).UseCollation("BINARY").HasMaxLength(StageTitle.MaxLength).IsRequired();
        builder.HasIndex(t => t.Title).IsUnique();
        builder.Property(t => t.IsActive).IsRequired();
        builder.Property(t => t.CreatedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.UpdatedAtUtc).IsRequired();
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}

public sealed class MachineConfiguration : IEntityTypeConfiguration<Machine>
{
    public void Configure(EntityTypeBuilder<Machine> builder)
    {
        builder.ToTable("Machines");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedOnAdd();

        // The stable machine code (FCC-06, TCC-12, HUB-01): exact, case-sensitive, unique, never changed.
        builder.Property(m => m.Code).UseCollation("BINARY").IsRequired();
        builder.HasIndex(m => m.Code).IsUnique();
        builder.Property(m => m.Name).IsRequired();
        builder.Property(m => m.ProductionUnitId).IsRequired();
        builder.HasOne<ProductionUnit>().WithMany().HasForeignKey(m => m.ProductionUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(m => m.IsActive).IsRequired();
        builder.Property(m => m.CreatedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(m => m.CreatedAtUtc).IsRequired();
        builder.Property(m => m.UpdatedAtUtc).IsRequired();
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}

/// <summary>The machine rate history (Domain and Database Specification 12.2): append-only, one entry per machine and effective date.</summary>
public sealed class MachineHourlyRateConfiguration : IEntityTypeConfiguration<MachineHourlyRate>
{
    public void Configure(EntityTypeBuilder<MachineHourlyRate> builder)
    {
        builder.ToTable("MachineHourlyRates");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();
        builder.Property(r => r.MachineId).IsRequired();
        builder.HasOne<Machine>().WithMany().HasForeignKey(r => r.MachineId).IsRequired().OnDelete(DeleteBehavior.Restrict);

        // Exact decimal Rial per hour; nothing is rounded (the Rial rounding rules are an open decision).
        builder.Property(r => r.RatePerHourRial).IsRequired();
        builder.Property(r => r.EffectiveFrom).IsRequired();
        builder.Property(r => r.Reason);
        builder.Property(r => r.ChangedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.ChangedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.ChangedAtUtc).IsRequired();
        builder.HasIndex(r => new { r.MachineId, r.EffectiveFrom }).IsUnique();
    }
}
