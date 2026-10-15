using ERP.MES.Application.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.MES.Infrastructure.Persistence.Configurations;

public sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedOnAdd();

        // The login name is stored exactly as given and compared as binary text (case-sensitive); it is never normalised.
        builder.Property(u => u.Username).IsRequired().UseCollation("BINARY");
        builder.HasIndex(u => u.Username).IsUnique();
        builder.Property(u => u.DisplayName).IsRequired();

        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.MustChangePassword).IsRequired();
        builder.Property(u => u.FailedLoginCount).IsRequired();
        builder.Property(u => u.LockoutEndUtc);
        builder.Property(u => u.LastLoginAtUtc);
        builder.Property(u => u.CreatedAtUtc).IsRequired();
        builder.Property(u => u.UpdatedAtUtc).IsRequired();

        // Password fields (Decision Log #41): hash, per-password salt, algorithm, version and work factor; the plaintext is never stored.
        builder.Property(u => u.PasswordHash).IsRequired();
        builder.Property(u => u.PasswordSalt).IsRequired();
        builder.Property(u => u.PasswordAlgorithm).IsRequired();
        builder.Property(u => u.PasswordHashVersion).IsRequired();
        builder.Property(u => u.PasswordWorkFactor).IsRequired();
        builder.Ignore(u => u.PasswordRecord);
        builder.Ignore(u => u.IsAdministrator);

        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();

        // Users are never physically deleted: both references restrict deletion.
        builder.HasOne(u => u.Department).WithMany().HasForeignKey(u => u.DepartmentId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(u => u.Role).WithMany().HasForeignKey(u => u.RoleId).IsRequired().OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLog");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();
        builder.Property(a => a.OccurredAtUtc).IsRequired();
        builder.Property(a => a.UserId);
        builder.Property(a => a.MachineName).IsRequired();
        builder.Property(a => a.OsUserName).IsRequired();
        builder.Property(a => a.DatabaseLogin);
        builder.Property(a => a.ActionCode).IsRequired();
        builder.Property(a => a.TargetType).IsRequired();
        builder.Property(a => a.TargetId);
        builder.Property(a => a.Summary).IsRequired();

        // Append-only: the context rejects updates and deletes (see ErpMesDbContext); there is no version column.
        builder.HasOne<UserAccount>().WithMany().HasForeignKey(a => a.UserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SystemSetupConfiguration : IEntityTypeConfiguration<SystemSetup>
{
    public void Configure(EntityTypeBuilder<SystemSetup> builder)
    {
        builder.ToTable("SystemSetup", table => table.HasCheckConstraint("CK_SystemSetup_SingleRow", "Id = 1"));
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.State).HasConversion<string>().IsRequired();
        builder.Property(s => s.BootstrapTokenHash);
        builder.Property(s => s.BootstrapTokenExpiresAtUtc);
        builder.Property(s => s.BootstrapFailedAttempts).IsRequired();
        builder.Property(s => s.InitializedAtUtc);
        builder.Property(s => s.AdminChangeStamp).IsRequired();
        builder.Property(s => s.RecoveryTokenHash);
        builder.Property(s => s.RecoveryTokenExpiresAtUtc);
        builder.Property(s => s.RecoveryFailedAttempts).IsRequired();
        builder.Property(s => s.RecoveryTargetUserId);
        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();

        builder.HasOne<UserAccount>().WithMany().HasForeignKey(s => s.RecoveryTargetUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

        // A freshly migrated database is NotProvisioned and has no users (Milestone 1 plan 5.2). The single row is created by the migration.
        builder.HasData(new
        {
            Id = SystemSetup.SingleRowId,
            State = SetupState.NotProvisioned,
            BootstrapFailedAttempts = 0,
            AdminChangeStamp = 0L,
            RecoveryFailedAttempts = 0,
            Version = 1L,
        });
    }
}
