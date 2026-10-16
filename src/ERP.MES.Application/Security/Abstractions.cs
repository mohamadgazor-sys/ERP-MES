using ERP.MES.Domain.Identity;

namespace ERP.MES.Application.Security;

/// <summary>The source of the current time, so rules and tests do not read the system clock directly.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

/// <summary>Where an audited action came from (Milestone 1 plan section 5.6): the machine and the operating-system user, plus the database login where the database reports one.</summary>
public interface IAuditContext
{
    string MachineName { get; }

    string OsUserName { get; }

    string? DatabaseLogin { get; }
}

/// <summary>Everything stored with a password hash (Decision Log #41). Verification uses these stored values, never a global setting.</summary>
public sealed record PasswordHashRecord(byte[] Hash, byte[] Salt, string Algorithm, int Version, int WorkFactor);

/// <summary>Password hashing and verification (PBKDF2-HMAC-SHA256, Decision Log #27, #41). Implemented in Infrastructure with the built-in .NET implementation.</summary>
public interface IPasswordHasher
{
    /// <summary>Hashes with a fresh random salt and the given work factor.</summary>
    PasswordHashRecord Hash(string password, int workFactor);

    /// <summary>Verifies with the stored parameters, in constant time. An unknown algorithm or version never verifies.</summary>
    bool Verify(string password, PasswordHashRecord stored);

    /// <summary>True when the stored algorithm, version or work factor is below the current baseline / configured work factor.</summary>
    bool NeedsUpgrade(PasswordHashRecord stored, int configuredWorkFactor);
}

/// <summary>
/// One short database transaction over the security data (users with their credentials, setup state, audit log). Loaded objects are tracked;
/// <see cref="SaveAsync"/> writes every change together or nothing. A row changed by someone else after it was loaded raises
/// <see cref="Persistence.ConcurrencyConflictException"/> (the conditional update of Milestone 1 plan sections 5.2 and 5.7). No database type appears here.
/// </summary>
public interface ISecurityUnit
{
    Task<SystemSetup> GetSetupAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finds an account by login name, ignoring case (the canonical key of Decision Log #62). A text that is not a valid user name (empty, leading or trailing
    /// white space, control characters) cannot belong to any user and finds nothing. The user, its department and its role are loaded.
    /// </summary>
    Task<UserCredential?> FindAccountByUsernameAsync(string? username, CancellationToken cancellationToken);

    Task<UserCredential?> FindAccountByUserIdAsync(long userId, CancellationToken cancellationToken);

    Task<Department> GetDepartmentAsync(string code, CancellationToken cancellationToken);

    Task<Role> GetRoleAsync(string code, CancellationToken cancellationToken);

    Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken);

    /// <summary>Adds a user together with its credential.</summary>
    void AddAccount(UserCredential account);

    void AddAudit(AuditLogEntry entry);

    Task SaveAsync(CancellationToken cancellationToken);
}

/// <summary>Runs a piece of work in a new <see cref="ISecurityUnit"/> (one short-lived context per call).</summary>
public interface ISecurityUnitRunner
{
    Task<T> RunAsync<T>(Func<ISecurityUnit, Task<T>> work, CancellationToken cancellationToken);
}
