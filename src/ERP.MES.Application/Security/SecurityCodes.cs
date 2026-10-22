namespace ERP.MES.Application.Security;

/// <summary>Initialization state of the system (Milestone 1 plan section 5.3).</summary>
public enum SetupState
{
    /// <summary>No administrator exists and no bootstrap token is pending.</summary>
    NotProvisioned = 1,

    /// <summary>A bootstrap token has been issued and can be redeemed.</summary>
    AwaitingBootstrap = 2,

    /// <summary>The first administrator exists; the bootstrap path is permanently closed.</summary>
    Initialized = 3,
}

/// <summary>Action codes written to the append-only audit log (Milestone 1 plan sections 5.6 and 6.1). Tokens and passwords never appear in an audit entry.</summary>
public static class AuditActions
{
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string AccountLocked = "AccountLocked";
    public const string AccountUnlocked = "AccountUnlocked";
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordReset = "PasswordReset";
    public const string PasswordHashUpgraded = "PasswordHashUpgraded";
    public const string UserCreated = "UserCreated";
    public const string UserActivated = "UserActivated";
    public const string UserDeactivated = "UserDeactivated";
    public const string UserRoleChanged = "UserRoleChanged";
    public const string LastAdminProtectionRefused = "LastAdminProtectionRefused";
    public const string AdminTokenIssued = "AdminTokenIssued";
    public const string BootstrapFailed = "BootstrapFailed";
    public const string BootstrapLocked = "BootstrapLocked";
    public const string BootstrapCompleted = "BootstrapCompleted";
    public const string RecoveryTokenIssued = "RecoveryTokenIssued";
    public const string RecoveryFailed = "RecoveryFailed";
    public const string RecoveryLocked = "RecoveryLocked";
    public const string RecoveryCompleted = "RecoveryCompleted";

    // Desktop session lifecycle (Decision Log #74). Nothing here ever carries a password, hash or token.
    public const string SessionSignedOut = "SessionSignedOut";
    public const string SessionLocked = "SessionLocked";
    public const string SessionUnlocked = "SessionUnlocked";
    public const string SessionUserSwitched = "SessionUserSwitched";

    // Demonstration stage flow (Decision Log #79). Written in the same transaction as the stage change. Nothing here ever carries a password or token.
    public const string StageReleased = "StageReleased";
    public const string StageStarted = "StageStarted";
    public const string StageSubmittedForQc = "StageSubmittedForQc";
    public const string StageQcApproved = "StageQcApproved";

    /// <summary>Written on a clean shutdown, on a best-effort basis. A crash, a forced termination or a power loss writes nothing, and nothing is written later to pretend otherwise.</summary>
    public const string ApplicationSessionEnded = "ApplicationSessionEnded";
}
