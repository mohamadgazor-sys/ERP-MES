using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>Machine downtime reported by Maintenance/PM and reviewed by Planning (Master Specification section 21, Decision Log #128). Rows are never deleted.</summary>
public sealed class MachineDowntimeConfiguration : IEntityTypeConfiguration<MachineDowntime>
{
    public void Configure(EntityTypeBuilder<MachineDowntime> builder)
    {
        builder.ToTable("MachineDowntimes", t => t.HasCheckConstraint("CK_MachineDowntimes_EndAfterStart", "\"EndLocal\" > \"StartLocal\""));
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();
        builder.Property(d => d.MachineId).IsRequired();
        builder.HasOne<Machine>().WithMany().HasForeignKey(d => d.MachineId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.ProductionUnitId).IsRequired();
        builder.HasOne<ProductionUnit>().WithMany().HasForeignKey(d => d.ProductionUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => new { d.MachineId, d.StartLocal });
        builder.HasIndex(d => d.StartLocal);
        builder.Property(d => d.Reason).HasConversion<int>().IsRequired();
        builder.Property(d => d.StartLocal).IsRequired();
        builder.Property(d => d.EndLocal).IsRequired();
        builder.Property(d => d.Note).HasMaxLength(MachineDowntime.MaxNoteLength);
        builder.Property(d => d.Status).HasConversion<int>().IsRequired();
        builder.Property(d => d.ReportedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.ReportedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.ReportedAtUtc).IsRequired();
        builder.Property(d => d.DecidedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.DecidedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.DecidedAtUtc);
        builder.Property(d => d.DecisionNote).HasMaxLength(MachineDowntime.MaxNoteLength);
        builder.Property(d => d.CancelledByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(d => d.CancelledByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(d => d.CancelledAtUtc);
        builder.Ignore(d => d.DurationMinutes);
        builder.Ignore(d => d.Counts);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}
