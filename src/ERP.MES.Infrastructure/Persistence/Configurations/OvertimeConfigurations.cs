using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>The single row of overtime limits; it does not exist until Planning configures it (Decision Log #95 D5, #103).</summary>
public sealed class OvertimeLimitsConfiguration : IEntityTypeConfiguration<OvertimeLimits>
{
    public void Configure(EntityTypeBuilder<OvertimeLimits> builder)
    {
        builder.ToTable("OvertimeLimits", t => t.HasCheckConstraint("CK_OvertimeLimits_Single", "\"Id\" = 1"));
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.MaxMinutesPerDay).IsRequired();
        builder.Property(l => l.MaxMinutesPerWeek).IsRequired();
        builder.Property(l => l.ChangedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.ChangedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(l => l.ChangedAtUtc).IsRequired();
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}

public sealed class OvertimeAssignmentConfiguration : IEntityTypeConfiguration<OvertimeAssignment>
{
    public void Configure(EntityTypeBuilder<OvertimeAssignment> builder)
    {
        builder.ToTable("OvertimeAssignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();
        builder.Property(a => a.OperatorId).IsRequired();
        builder.HasOne<Operator>().WithMany().HasForeignKey(a => a.OperatorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(a => a.ProductionUnitId).IsRequired();
        builder.HasOne<ProductionUnit>().WithMany().HasForeignKey(a => a.ProductionUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(a => a.MachineId).IsRequired();
        builder.HasOne<Machine>().WithMany().HasForeignKey(a => a.MachineId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(a => a.WorkDate).IsRequired();
        builder.HasIndex(a => new { a.OperatorId, a.WorkDate });
        builder.HasIndex(a => new { a.MachineId, a.WorkDate });
        builder.Property(a => a.StartLocal).IsRequired();
        builder.Property(a => a.EndLocal).IsRequired();
        builder.Property(a => a.Reason).IsRequired();
        builder.Property(a => a.Status).HasConversion<int>().IsRequired();
        builder.Property(a => a.ProposedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.ProposedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(a => a.ProposedAtUtc).IsRequired();
        builder.Property(a => a.ExceedsLimits).IsRequired();
        builder.Property(a => a.PlanningUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.PlanningUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(a => a.PlanningAtUtc);
        builder.Property(a => a.ManagementUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.ManagementUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(a => a.ManagementAtUtc);
        builder.Property(a => a.DecisionNote);
        builder.Ignore(a => a.Minutes);
        builder.Ignore(a => a.IsLive);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}
