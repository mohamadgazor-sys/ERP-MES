using ERP.MES.Domain.Identity;

namespace ERP.MES.Application.Security;

/// <summary>
/// How a user proves who they are (Milestone 1 plan 6.2): the password hash with its per-password salt, algorithm, version and work factor, the failed-attempt
/// counter and lockout, and whether the password is one-time. It belongs to Application and Infrastructure, not to the Domain (Decision Log #61): the Domain
/// <see cref="User"/> says who a person is in the organisation, this says how they sign in. Every user has exactly one credential. The password itself is never
/// stored; hashing is done through <see cref="IPasswordHasher"/>.
/// </summary>
public sealed class UserCredential
{
    private UserCredential()
    {
        User = null!;
        PasswordHash = [];
        PasswordSalt = [];
        PasswordAlgorithm = string.Empty;
    }

    public UserCredential(User user, PasswordHashRecord password, bool mustChangePassword, DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(password);

        User = user;
        UserId = user.Id;
        MustChangePassword = mustChangePassword;
        UpdatedAtUtc = createdAtUtc;
        PasswordHash = password.Hash;
        PasswordSalt = password.Salt;
        PasswordAlgorithm = password.Algorithm;
        PasswordHashVersion = password.Version;
        PasswordWorkFactor = password.WorkFactor;
    }

    /// <summary>The user this credential belongs to; also this row's key.</summary>
    public long UserId { get; private set; }

    public User User { get; private set; }

    /// <summary>True until the user has chosen their own password (initial or administrator-set passwords are one-time, Decision Log #64).</summary>
    public bool MustChangePassword { get; private set; }

    public int FailedLoginCount { get; private set; }

    public DateTime? LockoutEndUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public byte[] PasswordHash { get; private set; }

    public byte[] PasswordSalt { get; private set; }

    public string PasswordAlgorithm { get; private set; }

    public int PasswordHashVersion { get; private set; }

    public int PasswordWorkFactor { get; private set; }

    public PasswordHashRecord PasswordRecord => new(PasswordHash, PasswordSalt, PasswordAlgorithm, PasswordHashVersion, PasswordWorkFactor);

    public bool IsLockedOut(DateTime nowUtc) => LockoutEndUtc is { } end && end > nowUtc;

    /// <summary>Counts a failed attempt; returns true when this attempt locked the account (the counter then restarts). Never called for an account that is already locked, so a lock is not extended.</summary>
    public bool RegisterFailedLogin(DateTime nowUtc, AuthenticationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        FailedLoginCount++;
        UpdatedAtUtc = nowUtc;
        if (FailedLoginCount < policy.MaxFailedAttempts)
        {
            return false;
        }

        LockoutEndUtc = nowUtc.AddMinutes(policy.LockoutMinutes);
        FailedLoginCount = 0;
        return true;
    }

    public void RecordSuccessfulLogin(DateTime nowUtc)
    {
        FailedLoginCount = 0;
        LockoutEndUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    public void ClearLockout(DateTime nowUtc)
    {
        FailedLoginCount = 0;
        LockoutEndUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Stores a new hash; <paramref name="mustChangePassword"/> says whether the user has to choose their own password next.</summary>
    public void SetPassword(PasswordHashRecord password, bool mustChangePassword, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(password);
        ReplaceHash(password);
        MustChangePassword = mustChangePassword;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Re-hashes the same password with newer parameters; nothing else changes.</summary>
    public void UpgradeHash(PasswordHashRecord password, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(password);
        ReplaceHash(password);
        UpdatedAtUtc = nowUtc;
    }

    private void ReplaceHash(PasswordHashRecord password)
    {
        PasswordHash = password.Hash;
        PasswordSalt = password.Salt;
        PasswordAlgorithm = password.Algorithm;
        PasswordHashVersion = password.Version;
        PasswordWorkFactor = password.WorkFactor;
    }
}

/// <summary>
/// The single setup row (Milestone 1 plan 5.3): initialization state, the digests (never the plaintext) of the pending bootstrap and recovery tokens with
/// their expiry and attempt counters, and the stamp that serialises changes to the administrator set. Because it is one database row, the counters hold across
/// every application instance, and a conditional update of this row decides which of two concurrent claims wins.
/// </summary>
public sealed class SystemSetup
{
    public const long SingleRowId = 1;

    private SystemSetup()
    {
    }

    public long Id { get; private set; }

    public SetupState State { get; private set; }

    public byte[]? BootstrapTokenHash { get; private set; }

    public DateTime? BootstrapTokenExpiresAtUtc { get; private set; }

    public int BootstrapFailedAttempts { get; private set; }

    public DateTime? InitializedAtUtc { get; private set; }

    /// <summary>Advanced by every change that could remove an administrator, so two concurrent changes cannot both pass the last-administrator check.</summary>
    public long AdminChangeStamp { get; private set; }

    public byte[]? RecoveryTokenHash { get; private set; }

    public DateTime? RecoveryTokenExpiresAtUtc { get; private set; }

    public int RecoveryFailedAttempts { get; private set; }

    public long? RecoveryTargetUserId { get; private set; }

    /// <summary>Stores the digest of a new token, replacing (and so invalidating) any earlier one, and resets the attempt counter.</summary>
    public void IssueBootstrapToken(byte[] tokenDigest, DateTime expiresAtUtc)
    {
        if (State == SetupState.Initialized)
        {
            throw new InvalidOperationException("The system is already initialised; the bootstrap path is closed.");
        }

        State = SetupState.AwaitingBootstrap;
        BootstrapTokenHash = tokenDigest;
        BootstrapTokenExpiresAtUtc = expiresAtUtc;
        BootstrapFailedAttempts = 0;
    }

    /// <summary>Counts one bootstrap attempt (before the token is checked) and returns the number of attempts so far.</summary>
    public int CountBootstrapAttempt() => ++BootstrapFailedAttempts;

    /// <summary>Withdraws the pending token; IT has to issue a new one.</summary>
    public void LockBootstrap()
    {
        if (State == SetupState.AwaitingBootstrap)
        {
            State = SetupState.NotProvisioned;
        }

        BootstrapTokenHash = null;
        BootstrapTokenExpiresAtUtc = null;
    }

    public void CompleteBootstrap(DateTime nowUtc)
    {
        State = SetupState.Initialized;
        BootstrapTokenHash = null;
        BootstrapTokenExpiresAtUtc = null;
        InitializedAtUtc = nowUtc;
    }

    public void IssueRecoveryToken(byte[] tokenDigest, DateTime expiresAtUtc, long targetUserId)
    {
        RecoveryTokenHash = tokenDigest;
        RecoveryTokenExpiresAtUtc = expiresAtUtc;
        RecoveryFailedAttempts = 0;
        RecoveryTargetUserId = targetUserId;
    }

    public int CountRecoveryAttempt() => ++RecoveryFailedAttempts;

    public void ClearRecoveryToken()
    {
        RecoveryTokenHash = null;
        RecoveryTokenExpiresAtUtc = null;
        RecoveryTargetUserId = null;
    }

    public void AdvanceAdminChangeStamp() => AdminChangeStamp++;

    public bool IsBootstrapPending(DateTime nowUtc) =>
        State == SetupState.AwaitingBootstrap && BootstrapTokenHash is not null && BootstrapTokenExpiresAtUtc is { } end && end > nowUtc;

    public bool IsRecoveryPending(DateTime nowUtc, int maxAttempts) =>
        State == SetupState.Initialized && RecoveryTokenHash is not null && RecoveryTokenExpiresAtUtc is { } end && end > nowUtc && RecoveryFailedAttempts < maxAttempts;
}

/// <summary>One append-only audit entry (Decision Log #26). Entries are only ever added; the persistence layer rejects updates and deletes. They never contain a token or a password.</summary>
public sealed class AuditLogEntry
{
    private AuditLogEntry()
    {
        MachineName = string.Empty;
        OsUserName = string.Empty;
        ActionCode = string.Empty;
        TargetType = string.Empty;
        Summary = string.Empty;
    }

    public AuditLogEntry(
        DateTime occurredAtUtc,
        long? userId,
        string machineName,
        string osUserName,
        string? databaseLogin,
        string actionCode,
        string targetType,
        string? targetId,
        string summary)
    {
        OccurredAtUtc = occurredAtUtc;
        UserId = userId;
        MachineName = machineName;
        OsUserName = osUserName;
        DatabaseLogin = databaseLogin;
        ActionCode = actionCode;
        TargetType = targetType;
        TargetId = targetId;
        Summary = summary;
    }

    public long Id { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>The acting user, or null when no user is known (an unknown login name, an operator running Provisioning).</summary>
    public long? UserId { get; private set; }

    public string MachineName { get; private set; }

    public string OsUserName { get; private set; }

    public string? DatabaseLogin { get; private set; }

    public string ActionCode { get; private set; }

    public string TargetType { get; private set; }

    public string? TargetId { get; private set; }

    public string Summary { get; private set; }
}
