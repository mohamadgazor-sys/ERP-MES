using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>The standard QC definitions that ship with the system (Decision Log #9, #90). They are ordinary rows: QC may rename or retire them and add its own. No creator is recorded for them.</summary>
public static class QcMasterSeed
{
    public static readonly DateTime SeededAtUtc = new(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);

    public const long PieceUnitId = 1;
    public const long PairUnitId = 2;
    public const long KilogramUnitId = 3;
    public const long MillimetreUnitId = 4;

    public const string PieceCode = "PCS";
    public const string PairCode = "PAIR";
    public const string PieceTypeCode = "PIECE";
    public const string PairTypeCode = "PAIR";
}

public sealed class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> builder)
    {
        builder.ToTable("UnitsOfMeasure");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedOnAdd();

        // Stable business code: exact, case-sensitive, unique (binary collation like the other business keys).
        builder.Property(u => u.Code).UseCollation("BINARY").IsRequired();
        builder.HasIndex(u => u.Code).IsUnique();
        builder.Property(u => u.Name).IsRequired();
        builder.Property(u => u.Kind).HasConversion<int>().IsRequired();
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.CreatedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(u => u.CreatedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(u => u.CreatedAtUtc).IsRequired();
        builder.Property(u => u.UpdatedAtUtc).IsRequired();
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();

        var at = QcMasterSeed.SeededAtUtc;
        builder.HasData(
            Seed(QcMasterSeed.PieceUnitId, QcMasterSeed.PieceCode, "عدد (قطعه)", UnitKind.Count, at),
            Seed(QcMasterSeed.PairUnitId, QcMasterSeed.PairCode, "جفت", UnitKind.Count, at),
            Seed(QcMasterSeed.KilogramUnitId, "KG", "کیلوگرم", UnitKind.Mass, at),
            Seed(QcMasterSeed.MillimetreUnitId, "MM", "میلی‌متر", UnitKind.Length, at));
    }

    private static object Seed(long id, string code, string name, UnitKind kind, DateTime at) => new
    {
        Id = id,
        Code = code,
        Name = name,
        Kind = kind,
        IsActive = true,
        CreatedByUserId = (long?)null,
        CreatedAtUtc = at,
        UpdatedAtUtc = at,
        Version = 1L,
    };
}

public sealed class UnitConversionConfiguration : IEntityTypeConfiguration<UnitConversion>
{
    public void Configure(EntityTypeBuilder<UnitConversion> builder)
    {
        builder.ToTable("UnitConversions");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();
        builder.Property(c => c.FromUnitId).IsRequired();
        builder.Property(c => c.ToUnitId).IsRequired();

        // The exact decimal is kept as text by the provider; nothing is rounded.
        builder.Property(c => c.Factor).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.CreatedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.CreatedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(c => c.FromUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(c => c.ToUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);

        // One definition per ordered pair; the application also refuses the opposite pair, because the reciprocal is derived.
        builder.HasIndex(c => new { c.FromUnitId, c.ToUnitId }).IsUnique();
        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.UpdatedAtUtc).IsRequired();
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();

        builder.HasData(new
        {
            Id = 1L,
            FromUnitId = QcMasterSeed.PairUnitId,
            ToUnitId = QcMasterSeed.PieceUnitId,
            Factor = 2m,
            IsActive = true,
            CreatedByUserId = (long?)null,
            CreatedAtUtc = QcMasterSeed.SeededAtUtc,
            UpdatedAtUtc = QcMasterSeed.SeededAtUtc,
            Version = 1L,
        });
    }
}

public sealed class QuantityTypeConfiguration : IEntityTypeConfiguration<QuantityType>
{
    public void Configure(EntityTypeBuilder<QuantityType> builder)
    {
        builder.ToTable("QuantityTypes");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedOnAdd();
        builder.Property(q => q.Code).UseCollation("BINARY").IsRequired();
        builder.HasIndex(q => q.Code).IsUnique();
        builder.Property(q => q.Name).IsRequired();
        builder.Property(q => q.BaseUnitId).IsRequired();
        builder.Property(q => q.BaseUnitsPerType).IsRequired();
        builder.Property(q => q.IsActive).IsRequired();
        builder.Property(q => q.CreatedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(q => q.CreatedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(q => q.BaseUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(q => q.CreatedAtUtc).IsRequired();
        builder.Property(q => q.UpdatedAtUtc).IsRequired();
        builder.Ignore(q => q.IsSet);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();

        builder.HasMany(q => q.Members).WithOne().HasForeignKey("QuantityTypeId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(q => q.Members).HasField("_members").UsePropertyAccessMode(PropertyAccessMode.Field);

        var at = QcMasterSeed.SeededAtUtc;
        builder.HasData(
            Seed(1, QcMasterSeed.PieceTypeCode, "قطعه", QcMasterSeed.PieceUnitId, 1, at),
            Seed(2, QcMasterSeed.PairTypeCode, "جفت", QcMasterSeed.PieceUnitId, 2, at));
    }

    private static object Seed(long id, string code, string name, long baseUnitId, int baseUnitsPerType, DateTime at) => new
    {
        Id = id,
        Code = code,
        Name = name,
        BaseUnitId = baseUnitId,
        BaseUnitsPerType = baseUnitsPerType,
        IsActive = true,
        CreatedByUserId = (long?)null,
        CreatedAtUtc = at,
        UpdatedAtUtc = at,
        Version = 1L,
    };
}

public sealed class QuantityTypeMemberConfiguration : IEntityTypeConfiguration<QuantityTypeMember>
{
    public void Configure(EntityTypeBuilder<QuantityTypeMember> builder)
    {
        builder.ToTable("QuantityTypeMembers");
        builder.Property<long>("QuantityTypeId");
        builder.HasKey("QuantityTypeId", nameof(QuantityTypeMember.MemberNo));
        builder.Property(m => m.MemberNo).ValueGeneratedNever();
        builder.Property(m => m.Description).IsRequired();
        builder.Property(m => m.BaseUnits).IsRequired();
    }
}
