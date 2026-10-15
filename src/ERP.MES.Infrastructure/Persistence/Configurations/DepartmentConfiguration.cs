using ERP.MES.Application.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Code).IsRequired().UseCollation("BINARY");
        builder.Property(d => d.Name).IsRequired();
        builder.HasIndex(d => d.Code).IsUnique();

        // Development seed (Decision Log #30), written by the migration.
        builder.HasData(ReferenceData.Departments);
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Code).IsRequired().UseCollation("BINARY");
        builder.Property(r => r.Name).IsRequired();
        builder.HasIndex(r => r.Code).IsUnique();
        builder.HasData(ReferenceData.Roles);
    }
}
