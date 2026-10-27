using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

/// <summary>The controlled pause-reason catalogue (Decision Log #112). The owner's seven reasons are seeded; Planning may add more.</summary>
public sealed class PauseReasonConfiguration : IEntityTypeConfiguration<PauseReason>
{
    private static readonly DateTime SeededAt = new(2026, 10, 27, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<PauseReason> builder)
    {
        builder.ToTable("PauseReasons");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();
        builder.Property(r => r.Code).IsRequired().HasMaxLength(40);
        builder.HasIndex(r => r.Code).IsUnique();
        builder.Property(r => r.Name).IsRequired().HasMaxLength(PauseReason.MaxNameLength);
        builder.Property(r => r.DefaultScope).HasConversion<int>().IsRequired();
        builder.Property(r => r.RequiresNote).IsRequired();
        builder.Property(r => r.IsSystem).IsRequired();
        builder.Property(r => r.IsActive).IsRequired();
        builder.Property(r => r.CreatedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.CreatedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
        builder.HasData(
            Seed(1, PauseReason.PowerOutage, "قطع برق", PauseScope.Unit, false),
            Seed(2, PauseReason.PlannedMaintenance, "تعمیر و نگهداری پیشگیرانهٔ برنامه‌ریزی‌شده", PauseScope.Machine, false),
            Seed(3, PauseReason.MachineBreakdown, "خرابی برنامه‌ریزی‌نشدهٔ ماشین", PauseScope.Machine, false),
            Seed(4, PauseReason.OperatorAbsenceOrHandover, "غیبت اپراتور یا تحویل شیفت", PauseScope.Operator, false),
            Seed(5, PauseReason.MaterialUnavailable, "نبود مواد", PauseScope.Session, false),
            Seed(6, PauseReason.WaitingAssistance, "انتظار برای سرپرست یا پشتیبانی فنی", PauseScope.Session, false),
            Seed(7, PauseReason.Other, "سایر (توضیح الزامی)", PauseScope.Session, true));
    }

    private static object Seed(long id, string code, string name, PauseScope scope, bool requiresNote) => new
    {
        Id = id,
        Code = code,
        Name = name,
        DefaultScope = scope,
        RequiresNote = requiresNote,
        IsSystem = true,
        IsActive = true,
        CreatedAtUtc = SeededAt,
        Version = 1L,
    };
}

public sealed class OutageEventConfiguration : IEntityTypeConfiguration<OutageEvent>
{
    public void Configure(EntityTypeBuilder<OutageEvent> builder)
    {
        builder.ToTable("OutageEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.ReasonId).IsRequired();
        builder.HasOne<PauseReason>().WithMany().HasForeignKey(e => e.ReasonId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.ProductionUnitId);
        builder.HasOne<ProductionUnit>().WithMany().HasForeignKey(e => e.ProductionUnitId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.Note).HasMaxLength(OutageEvent.MaxNoteLength);
        builder.Property(e => e.RecordedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.RecordedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.StartedAtUtc).IsRequired();
        builder.Property(e => e.EndedAtUtc);
        builder.Property(e => e.ClosedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ClosedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(e => e.IsOpen);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
    }
}

public sealed class WorkSessionConfiguration : IEntityTypeConfiguration<WorkSession>
{
    public void Configure(EntityTypeBuilder<WorkSession> builder)
    {
        builder.ToTable("WorkSessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();
        builder.Property(s => s.RequestItemId).IsRequired();
        builder.Property(s => s.StageNo).IsRequired();
        builder.HasOne<ItemStage>().WithMany().HasForeignKey(nameof(WorkSession.RequestItemId), nameof(WorkSession.StageNo))
            .HasPrincipalKey("RequestItemId", nameof(ItemStage.StageNo)).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.RequestItemId, s.StageNo });
        builder.Property(s => s.ProductionUnitId).IsRequired();
        builder.HasOne<ProductionUnit>().WithMany().HasForeignKey(s => s.ProductionUnitId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.ProductionUnitId, s.Status });
        builder.Property(s => s.MachineId).IsRequired();
        builder.HasOne<Machine>().WithMany().HasForeignKey(s => s.MachineId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.OperatorId).IsRequired();
        builder.HasOne<Operator>().WithMany().HasForeignKey(s => s.OperatorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.Status).HasConversion<int>().IsRequired();
        builder.Property(s => s.PredecessorSessionId);
        builder.HasOne<WorkSession>().WithMany().HasForeignKey(s => s.PredecessorSessionId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.QualificationWarning).IsRequired();
        builder.Property(s => s.OutsideShiftReason).HasMaxLength(WorkSession.MaxNoteLength);
        builder.Property(s => s.DispatchedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.DispatchedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.DispatchedAtUtc).IsRequired();
        builder.Property(s => s.StartedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.StartedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.StartedAtUtc);
        builder.Property(s => s.FinishedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.FinishedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.FinishedAtUtc);
        builder.Property(s => s.FinishKind).HasConversion<int?>();
        builder.Property(s => s.FinishNote).HasMaxLength(WorkSession.MaxNoteLength);
        builder.Property(s => s.TimeApprovedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.TimeApprovedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.TimeApprovedAtUtc);
        builder.Ignore(s => s.IsTimeApproved);
        builder.Ignore(s => s.IsUnfinished);
        builder.Ignore(s => s.OpenInterval);
        builder.Ignore(s => s.OpenPause);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();

        builder.HasMany(s => s.Intervals).WithOne().HasForeignKey("SessionId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(s => s.Intervals).HasField("_intervals").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(s => s.Pauses).WithOne().HasForeignKey("SessionId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(s => s.Pauses).HasField("_pauses").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(s => s.Events).WithOne().HasForeignKey("SessionId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(s => s.Events).HasField("_events").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(s => s.Corrections).WithOne().HasForeignKey("SessionId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(s => s.Corrections).HasField("_corrections").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

/// <summary>
/// Active-time intervals. The machine and the operator are repeated on the row so the database itself refuses a second OPEN interval for the same session, machine or operator: no double-booking
/// can survive a race between two supervisors (Decision Log #112). A handover closes the old interval before it opens the new one.
/// </summary>
public sealed class WorkSessionIntervalConfiguration : IEntityTypeConfiguration<WorkSessionInterval>
{
    public void Configure(EntityTypeBuilder<WorkSessionInterval> builder)
    {
        builder.ToTable("WorkSessionIntervals");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedOnAdd();
        builder.Property(i => i.MachineId).IsRequired();
        builder.HasOne<Machine>().WithMany().HasForeignKey(i => i.MachineId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.OperatorId).IsRequired();
        builder.HasOne<Operator>().WithMany().HasForeignKey(i => i.OperatorId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.StartedAtUtc).IsRequired();
        builder.Property(i => i.EndedAtUtc);
        builder.Property(i => i.StartedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.StartedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.EndedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.EndedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.EndedBy).HasConversion<int?>();
        builder.Ignore(i => i.IsOpen);
        builder.Property<long>("SessionId");
        builder.HasIndex("SessionId").IsUnique().HasFilter("[EndedAtUtc] IS NULL");
        builder.HasIndex(i => i.MachineId).IsUnique().HasFilter("[EndedAtUtc] IS NULL").HasDatabaseName("IX_WorkSessionIntervals_OpenMachine");
        builder.HasIndex(i => i.OperatorId).IsUnique().HasFilter("[EndedAtUtc] IS NULL").HasDatabaseName("IX_WorkSessionIntervals_OpenOperator");
        builder.HasIndex(i => new { i.MachineId, i.StartedAtUtc });
    }
}

public sealed class WorkSessionPauseConfiguration : IEntityTypeConfiguration<WorkSessionPause>
{
    public void Configure(EntityTypeBuilder<WorkSessionPause> builder)
    {
        builder.ToTable("WorkSessionPauses");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();
        builder.Property(p => p.ReasonId).IsRequired();
        builder.HasOne<PauseReason>().WithMany().HasForeignKey(p => p.ReasonId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(p => p.Note).HasMaxLength(WorkSession.MaxNoteLength);
        builder.Property(p => p.Scope).HasConversion<int>().IsRequired();
        builder.Property(p => p.OutageEventId);
        builder.HasOne<OutageEvent>().WithMany().HasForeignKey(p => p.OutageEventId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Property(p => p.PausedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.PausedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(p => p.PausedAtUtc).IsRequired();
        builder.Property(p => p.ResumedAtUtc);
        builder.Property(p => p.ResumedByUserId);
        builder.HasOne<User>().WithMany().HasForeignKey(p => p.ResumedByUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(p => p.IsOpen);
        builder.Property<long>("SessionId");
        builder.HasIndex("SessionId").IsUnique().HasFilter("[ResumedAtUtc] IS NULL").HasDatabaseName("IX_WorkSessionPauses_OpenPerSession");
    }
}

public sealed class WorkSessionEventConfiguration : IEntityTypeConfiguration<WorkSessionEvent>
{
    public void Configure(EntityTypeBuilder<WorkSessionEvent> builder)
    {
        builder.ToTable("WorkSessionEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.Type).HasConversion<int>().IsRequired();
        builder.Property(e => e.AtUtc).IsRequired();
        builder.Property(e => e.ByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(e => e.Detail).IsRequired();
        builder.Property<long>("SessionId");
    }
}

public sealed class WorkSessionCorrectionConfiguration : IEntityTypeConfiguration<WorkSessionCorrection>
{
    public void Configure(EntityTypeBuilder<WorkSessionCorrection> builder)
    {
        builder.ToTable("WorkSessionCorrections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();
        builder.Property(c => c.Boundary).HasConversion<int>().IsRequired();
        builder.Property(c => c.PauseIndex).IsRequired();
        builder.Property(c => c.OldValueUtc).IsRequired();
        builder.Property(c => c.NewValueUtc).IsRequired();
        builder.Property(c => c.Reason).IsRequired().HasMaxLength(WorkSession.MaxNoteLength);
        builder.Property(c => c.CorrectedByUserId).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.CorrectedByUserId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.Property(c => c.CorrectedAtUtc).IsRequired();
        builder.Property<long>("SessionId");
    }
}
