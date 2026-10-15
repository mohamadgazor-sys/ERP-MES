namespace ERP.MES.Application.Security;

/// <summary>The signed-in user as the rest of the application sees them. Services never trust these values for an administrative action: they reload the user.</summary>
public sealed record Principal(long UserId, string Username, string DisplayName, string DepartmentCode, string RoleCode, bool MustChangePassword);

public enum SignInStatus
{
    Succeeded,

    /// <summary>Unknown user, wrong password or inactive account: deliberately not distinguished.</summary>
    InvalidCredentials,

    LockedOut,
}

public sealed record SignInResult(SignInStatus Status, Principal? Principal);
