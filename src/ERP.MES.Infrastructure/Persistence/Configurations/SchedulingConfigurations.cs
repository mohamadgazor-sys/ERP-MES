using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

public sealed class ShiftTemplateConfiguration : IEntityTypeConfiguration<ShiftTemplate>
{
    public void Configure(EntityTypeBuilder<ShiftTemplate> builder)
    {
        builder.ToTable("ShiftTemplates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();
        builder.Property(t => t.Code).UseCollation("BINARY").IsRequired();
        builder.HasIndex(t => t.Code).IsUnique();
        builder.Property(t => t.Name).IsRequired();
        builder.Property(t => t.StartTime).IsRequired();
        builder.Property(t => t.EndTime).IsRequired();
        builder.Property(t => t.BreakStart);
        builder.Property(t => t.BreakEnd);
        builder.Property(t => t.IsActive).IsRequired();
        builder.Property(t => t.CreatedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.UpdatedAtUtc).IsRequired();
        builder.Ignore(t => t.CrossesMidnight);
        builder.Ignore(t => t.TotalMinutes);
        builder.Ignore(t => t.BreakMinutes);
        builder.Ignore(t => t.WorkingMinutes);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}

/// <summary>The regular weekly pattern: seven rows keyed by weekday, seeded with Saturday to Thursday working (Decision Log #13) and edited by Planning.</summary>
public sealed class WeeklyWorkdayConfiguration : IEntityTypeConfiguration<WeeklyWorkday>
{
    public void Configure(EntityTypeBuilder<WeeklyWorkday> builder)
    {
        builder.ToTable("WeeklyWorkdays");
        builder.HasKey(w => w.Day);
        builder.Property(w => w.Day).HasConversion<int>().ValueGeneratedNever();
        builder.Property(w => w.IsWorkday).IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
        var seededAt = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
        builder.HasData(Enum.GetValues<DayOfWeek>().Select(d => new { Day = d, IsWorkday = d != DayOfWeek.Friday, UpdatedAtUtc = seededAt, Version = 1L }));
    }
}

public sealed class CalendarExceptionConfiguration : IEntityTypeConfiguration<CalendarException>
{
    public void Configure(EntityTypeBuilder<CalendarException> builder)
    {
        builder.ToTable("CalendarExceptions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Date).IsRequired();
        builder.HasIndex(e => e.Date);
        builder.Property(e => e.Kind).HasConversion<int>().IsRequired();
        builder.Property(e => e.ProductionUnitId);
        builder.HasOne<ProductionUnit>().WithMany().HasForeignKey(e => e.ProductionUnitId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.ShiftTemplateId);
        builder.HasOne<ShiftTemplate>().WithMany().HasForeignKey(e => e.ShiftTemplateId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.Reason).IsRequired();
        builder.Property(e => e.RecordedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.RecordedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.RecordedAtUtc).IsRequired();
        builder.Property(e => e.IsActive).IsRequired();
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}

public sealed class UnitShiftConfiguration : IEntityTypeConfiguration<UnitShift>
{
    public void Configure(EntityTypeBuilder<UnitShift> builder)
    {
        builder.ToTable("UnitShifts");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedOnAdd();
        builder.Property(u => u.ProductionUnitId).IsRequired();
        builder.HasOne<ProductionUnit>().WithMany().HasForeignKey(u => u.ProductionUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(u => u.ShiftTemplateId).IsRequired();
        builder.HasOne<ShiftTemplate>().WithMany().HasForeignKey(u => u.ShiftTemplateId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(u => new { u.ProductionUnitId, u.ShiftTemplateId }).IsUnique();
        builder.Property(u => u.MaxOperators).IsRequired();
        builder.Property(u => u.MinOperators).IsRequired();
        builder.Property(u => u.OverlapAllowed).IsRequired();
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.CreatedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(u => u.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(u => u.CreatedAtUtc).IsRequired();
        builder.Property(u => u.UpdatedAtUtc).IsRequired();
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}

public sealed class OperatorOffDayConfiguration : IEntityTypeConfiguration<OperatorOffDay>
{
    public void Configure(EntityTypeBuilder<OperatorOffDay> builder)
    {
        builder.ToTable("OperatorOffDays");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedOnAdd();
        builder.Property(o => o.OperatorId).IsRequired();
        builder.HasOne<Operator>().WithMany().HasForeignKey(o => o.OperatorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => new { o.OperatorId, o.FromDate });
        builder.Property(o => o.FromDate).IsRequired();
        builder.Property(o => o.ToDate).IsRequired();
        builder.Property(o => o.Kind).HasConversion<int>().IsRequired();
        builder.Property(o => o.Note);
        builder.Property(o => o.RecordedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.RecordedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(o => o.RecordedAtUtc).IsRequired();
        builder.Property(o => o.IsActive).IsRequired();
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}

public sealed class WorkerScheduleConfiguration : IEntityTypeConfiguration<WorkerSchedule>
{
    public void Configure(EntityTypeBuilder<WorkerSchedule> builder)
    {
        builder.ToTable("WorkerSchedules");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedOnAdd();
        builder.Property(w => w.OperatorId).IsRequired();
        builder.HasOne<Operator>().WithMany().HasForeignKey(w => w.OperatorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(w => w.Date).IsRequired();
        builder.HasIndex(w => new { w.Date, w.ProductionUnitId });
        builder.HasIndex(w => new { w.OperatorId, w.Date });
        builder.Property(w => w.ProductionUnitId).IsRequired();
        builder.HasOne<ProductionUnit>().WithMany().HasForeignKey(w => w.ProductionUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(w => w.ShiftTemplateId).IsRequired();
        builder.HasOne<ShiftTemplate>().WithMany().HasForeignKey(w => w.ShiftTemplateId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(w => w.MachineId);
        builder.HasOne<Machine>().WithMany().HasForeignKey(w => w.MachineId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(w => w.HandoverMinutes);
        builder.Property(w => w.HandoverNote);
        builder.Property(w => w.Status).HasConversion<int>().IsRequired();
        builder.Property(w => w.CreatedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(w => w.CreatedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.CancelReason);
        builder.Property(w => w.CancelledByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(w => w.CancelledByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(w => w.CancelledAtUtc);
        builder.Ignore(w => w.IsPlanned);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}
