using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Reporting;
using ERP.MES.Domain.Requests;
using ERP.MES.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

public sealed class DailyJobReportConfiguration : IEntityTypeConfiguration<DailyJobReport>
{
    public void Configure(EntityTypeBuilder<DailyJobReport> builder)
    {
        builder.ToTable("DailyJobReports");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();
        builder.Property(r => r.OperatorId).IsRequired();
        builder.HasOne<Operator>().WithMany().HasForeignKey(r => r.OperatorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.WorkDate).IsRequired();
        builder.Property(r => r.ProductionUnitId).IsRequired();
        builder.HasOne<ProductionUnit>().WithMany().HasForeignKey(r => r.ProductionUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.ShiftTemplateId).IsRequired();
        builder.HasOne<ShiftTemplate>().WithMany().HasForeignKey(r => r.ShiftTemplateId).IsRequired().OnDelete(DeleteBehavior.Restrict);

        // One report per operator, work date and shift (docs/JOB_REPORTS_KPI_AND_OVERTIME_DESIGN.md section 5): a returned report is corrected, not duplicated.
        builder.HasIndex(r => new { r.OperatorId, r.WorkDate, r.ShiftTemplateId }).IsUnique();
        builder.HasIndex(r => new { r.WorkDate, r.ProductionUnitId });
        builder.Property(r => r.Status).HasConversion<int>().IsRequired();
        builder.Property(r => r.RevisionNo).IsRequired();
        builder.Property(r => r.CreatedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.SubmittedAtUtc);
        builder.Property(r => r.ReviewedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.ReviewedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.ReviewedAtUtc);
        builder.Property(r => r.ReturnReason);
        builder.Property(r => r.ReviewedByPlanningFallback).IsRequired();
        builder.Ignore(r => r.IsEditable);
        builder.Ignore(r => r.HasFlaggedLines);
        builder.HasMany(r => r.Lines).WithOne().HasForeignKey("DailyJobReportId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(r => r.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(r => r.Events).WithOne().HasForeignKey("DailyJobReportId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(r => r.Events).HasField("_events").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}

public sealed class JobReportLineConfiguration : IEntityTypeConfiguration<JobReportLine>
{
    public void Configure(EntityTypeBuilder<JobReportLine> builder)
    {
        builder.ToTable("DailyJobReportLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedOnAdd();
        builder.Property(l => l.LineNo).IsRequired();
        builder.Property(l => l.MachineId).IsRequired();
        builder.HasOne<Machine>().WithMany().HasForeignKey(l => l.MachineId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(l => l.RequestId).IsRequired();
        builder.HasOne<Request>().WithMany().HasForeignKey(l => l.RequestId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(l => l.RequestItemId).IsRequired();
        builder.Property(l => l.StageNo).IsRequired();
        builder.HasOne<ItemStage>().WithMany().HasForeignKey(nameof(JobReportLine.RequestItemId), nameof(JobReportLine.StageNo))
            .HasPrincipalKey("RequestItemId", nameof(ItemStage.StageNo)).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => new { l.MachineId, l.StartUtc });
        builder.Property(l => l.StartUtc).IsRequired();
        builder.Property(l => l.EndUtc).IsRequired();
        builder.Property(l => l.DowntimeMinutes).IsRequired();
        builder.Property(l => l.DowntimeReason);
        builder.Property(l => l.ProcessedBaseUnits).IsRequired();
        builder.Property(l => l.Note);
        builder.Property(l => l.Flags).HasConversion<int>().IsRequired();
        builder.Ignore(l => l.WorkedMinutes);
        builder.Ignore(l => l.ProductiveMinutes);
    }
}

public sealed class JobReportEventConfiguration : IEntityTypeConfiguration<JobReportEvent>
{
    public void Configure(EntityTypeBuilder<JobReportEvent> builder)
    {
        builder.ToTable("DailyJobReportEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Kind).HasConversion<int>().IsRequired();
        builder.Property(e => e.UserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.AtUtc).IsRequired();
        builder.Property(e => e.Reason);
        builder.Property(e => e.PlanningFallback).IsRequired();
    }
}
