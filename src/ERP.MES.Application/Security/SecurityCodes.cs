namespace ERP.MES.Application.Security;

/// <summary>Stable department codes (Decision Log #30, development seed). Final company names may replace the display names without a schema change; code uses these codes.</summary>
public static class DepartmentCodes
{
    public const string Central = "CENTRAL";
    public const string Management = "MANAGEMENT";
    public const string Planning = "PLANNING";
    public const string Drawing = "DRAWING";
    public const string Warehouse = "WAREHOUSE";
    public const string Production = "PRODUCTION";
    public const string Qc = "QC";
    public const string MaintenancePm = "MAINTENANCE_PM";
    public const string System = "SYSTEM";

    public static IReadOnlyList<string> All { get; } =
        [Central, Management, Planning, Drawing, Warehouse, Production, Qc, MaintenancePm, System];
}

/// <summary>Stable role codes (Decision Log #30).</summary>
public static class RoleCodes
{
    public const string Admin = "ADMIN";
    public const string Manager = "MANAGER";
    public const string Staff = "STAFF";

    public static IReadOnlyList<string> All { get; } = [Admin, Manager, Staff];
}

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
}
