using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Imports;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>The log of a committed item import: append-only, one row per import with one row per worksheet row.</summary>
public sealed class ItemImportConfiguration : IEntityTypeConfiguration<ItemImport>
{
    public void Configure(EntityTypeBuilder<ItemImport> builder)
    {
        builder.ToTable("ItemImports");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedOnAdd();
        builder.Property(i => i.RequestId).IsRequired();
        builder.HasOne<Request>().WithMany().HasForeignKey(i => i.RequestId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.RequestId);
        builder.Property(i => i.FileName).IsRequired();
        builder.Property(i => i.FileHash).IsRequired();
        builder.Property(i => i.ImportedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.ImportedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.ImportedAtUtc).IsRequired();
        builder.HasMany(i => i.Rows).WithOne().HasForeignKey("ItemImportId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(i => i.Rows).HasField("_rows").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ItemImportRowConfiguration : IEntityTypeConfiguration<ItemImportRow>
{
    public void Configure(EntityTypeBuilder<ItemImportRow> builder)
    {
        builder.ToTable("ItemImportRows");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();
        builder.Property(r => r.SheetRowNo).IsRequired();
        builder.Property(r => r.ItemNo).UseCollation("BINARY").IsRequired();
        builder.Property(r => r.Outcome).HasConversion<int>().IsRequired();
    }
}
