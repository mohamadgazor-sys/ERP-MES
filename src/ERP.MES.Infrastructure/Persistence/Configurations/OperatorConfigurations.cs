using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

public sealed class OperatorConfiguration : IEntityTypeConfiguration<Operator>
{
    public void Configure(EntityTypeBuilder<Operator> builder)
    {
        builder.ToTable("Operators");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedOnAdd();
        builder.Property(o => o.Code).UseCollation("BINARY").IsRequired();
        builder.HasIndex(o => o.Code).IsUnique();
        builder.Property(o => o.FullName).IsRequired();

        // At most one operator per user account is checked by the Application; a unique index over a nullable column behaves differently on SQLite and SQL Server (one NULL only), so the
        // database keeps a plain index and a filtered unique index is a SQL Server provider task.
        builder.Property(o => o.UserId);
        builder.HasIndex(o => o.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.UserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(o => o.IsActive).IsRequired();
        builder.Property(o => o.CreatedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(o => o.CreatedAtUtc).IsRequired();
        builder.Property(o => o.UpdatedAtUtc).IsRequired();
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}

public sealed class OperatorMachineAssignmentConfiguration : IEntityTypeConfiguration<OperatorMachineAssignment>
{
    public void Configure(EntityTypeBuilder<OperatorMachineAssignment> builder)
    {
        builder.ToTable("OperatorMachineAssignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();
        builder.Property(a => a.OperatorId).IsRequired();
        builder.Property(a => a.MachineId).IsRequired();
        builder.HasOne<Operator>().WithMany().HasForeignKey(a => a.OperatorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Machine>().WithMany().HasForeignKey(a => a.MachineId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(a => a.EffectiveFrom).IsRequired();
        builder.Property(a => a.EffectiveTo);
        builder.Property(a => a.IsPrimary).IsRequired();
        builder.Property(a => a.Reason);
        builder.Property(a => a.AssignedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.AssignedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(a => a.AssignedAtUtc).IsRequired();
        builder.Property(a => a.ClosedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.ClosedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(a => a.ClosedAtUtc);
        builder.Ignore(a => a.IsOpen);
        builder.HasIndex(a => new { a.OperatorId, a.MachineId });
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}

public sealed class OperatorMachineQualificationConfiguration : IEntityTypeConfiguration<OperatorMachineQualification>
{
    public void Configure(EntityTypeBuilder<OperatorMachineQualification> builder)
    {
        builder.ToTable("OperatorMachineQualifications");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).ValueGeneratedOnAdd();
        builder.Property(q => q.OperatorId).IsRequired();
        builder.Property(q => q.MachineId).IsRequired();
        builder.HasOne<Operator>().WithMany().HasForeignKey(q => q.OperatorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Machine>().WithMany().HasForeignKey(q => q.MachineId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(q => q.QualifiedFrom).IsRequired();
        builder.Property(q => q.QualifiedTo);
        builder.Property(q => q.Note);
        builder.Property(q => q.RecordedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(q => q.RecordedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(q => q.RecordedAtUtc).IsRequired();
        builder.Ignore(q => q.IsOpen);
        builder.HasIndex(q => new { q.OperatorId, q.MachineId });
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}
