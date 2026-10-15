using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
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
        builder.HasData(ReferenceDataSeed.Departments);
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
        builder.HasData(ReferenceDataSeed.Roles);
    }
}

/// <summary>The Domain user: who a person is in the organisation. No credential column lives here (see <see cref="UserCredentialConfiguration"/>).</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedOnAdd();

        // The login name is stored as given; uniqueness is decided by the canonical key computed by the Domain (Decision Log #62), compared as binary text,
        // so the application and the database apply exactly one rule (no database case-folding is involved).
        builder.Property(u => u.Username).HasConversion(name => name.Value, text => Username.Create(text)).IsRequired();
        builder.Property(u => u.UsernameKey).IsRequired().UseCollation("BINARY");
        builder.HasIndex(u => u.UsernameKey).IsUnique();
        builder.Property(u => u.DisplayName).IsRequired();
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.CreatedAtUtc).IsRequired();
        builder.Property(u => u.UpdatedAtUtc).IsRequired();
        builder.Property(u => u.LastLoginAtUtc);
        builder.Ignore(u => u.IsActiveAdministrator);

        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();

        // Users are never physically deleted: both references restrict deletion.
        builder.HasOne(u => u.Department).WithMany().HasForeignKey(u => u.DepartmentId).IsRequired().OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(u => u.Role).WithMany().HasForeignKey(u => u.RoleId).IsRequired().OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>How a user signs in: the password hash with its parameters, the lockout state and the one-time flag. One row per user, keyed by the user.</summary>
public sealed class UserCredentialConfiguration : IEntityTypeConfiguration<UserCredential>
{
    public void Configure(EntityTypeBuilder<UserCredential> builder)
    {
        builder.ToTable("UserCredentials");
        builder.HasKey(c => c.UserId);
        builder.Property(c => c.UserId).ValueGeneratedNever();
        builder.HasOne(c => c.User).WithOne().HasForeignKey<UserCredential>(c => c.UserId).IsRequired().OnDelete(DeleteBehavior.Restrict);

        builder.Property(c => c.MustChangePassword).IsRequired();
        builder.Property(c => c.FailedLoginCount).IsRequired();
        builder.Property(c => c.LockoutEndUtc);
        builder.Property(c => c.UpdatedAtUtc).IsRequired();

        // Password fields (Decision Log #41): hash, per-password salt, algorithm, version and work factor; the plaintext is never stored.
        builder.Property(c => c.PasswordHash).IsRequired();
        builder.Property(c => c.PasswordSalt).IsRequired();
        builder.Property(c => c.PasswordAlgorithm).IsRequired();
        builder.Property(c => c.PasswordHashVersion).IsRequired();
        builder.Property(c => c.PasswordWorkFactor).IsRequired();
        builder.Ignore(c => c.PasswordRecord);

        builder.Property<long>(ErpMesDbContext.RowVersionPropertyName).IsConcurrencyToken().IsRequired();
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

        // Append-only: the context rejects updates and deletes (see ErpMesDbContext); there is no version column. The acting user is optional only because an
        // entry can describe an event with no known user (an unknown login name, an operator running Provisioning); when there is one it must exist.
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);
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

        // Optional only because no recovery is pending in the normal state; when one is, the target must exist.
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.RecoveryTargetUserId).IsRequired(false).OnDelete(DeleteBehavior.Restrict);

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
