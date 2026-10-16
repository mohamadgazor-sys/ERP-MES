namespace ERP.MES.Application.Security;

/// <summary>The signed-in user as the rest of the application sees them. Services never trust these values for an administrative action: they reload the user.</summary>
public sealed record Principal(long UserId, string Username, string DisplayName, string DepartmentCode, string RoleCode, bool MustChangePassword);

public enum SignInStatus
{
    Succeeded,

    /// <summary>
    /// Unknown user, wrong password, inactive account and locked account are deliberately not distinguished (Decision Log #65). The audit log keeps the
    /// distinctions; the caller never sees them.
    /// </summary>
    Failed,
}

public sealed record SignInResult(SignInStatus Status, Principal? Principal);
