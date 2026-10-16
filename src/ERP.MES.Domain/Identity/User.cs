using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Identity;

/// <summary>
/// A user of the system (Domain and Database Specification 15.1): a login name, a display name, exactly one department and exactly one role, an active
/// flag and audit timestamps. Users are never physically deleted: they are deactivated.
///
/// What is NOT here: the password and everything about proving identity (hashes, salts, lockout counters, tokens). Credentials are an Application and
/// Infrastructure concern (Decision Log #61); the Domain describes who a person is in the organisation, not how they sign in. Rules that need several
/// users at once (for example that the last active administrator cannot be removed) belong to the account-administration use case.
/// </summary>
public sealed class User
{
    private User()
    {
        Username = null!;
        UsernameKey = string.Empty;
        DisplayName = string.Empty;
        Department = null!;
        Role = null!;
    }

    public User(long id, Username username, string displayName, Department department, Role role, DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(department);
        ArgumentNullException.ThrowIfNull(role);

        Id = id;
        Username = username;
        UsernameKey = username.Key;
        DisplayName = RequireDisplayName(displayName);
        Department = department;
        DepartmentId = department.Id;
        Role = role;
        RoleId = role.Id;
        IsActive = true;
        CreatedAtUtc = Customer.RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
    }

    public long Id { get; private set; }

    /// <summary>The login name as given. It cannot be changed after creation.</summary>
    public Username Username { get; private set; }

    /// <summary>The canonical form of the login name (<see cref="Identity.Username.Key"/>), unique among users.</summary>
    public string UsernameKey { get; private set; }

    public string DisplayName { get; private set; }

    public long DepartmentId { get; private set; }

    public Department Department { get; private set; }

    public long RoleId { get; private set; }

    public Role Role { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public DateTime? LastLoginAtUtc { get; private set; }

    /// <summary>True for an active user whose role is Admin.</summary>
    public bool IsActiveAdministrator => IsActive && Role.Code == RoleCodes.Admin;

    public void Rename(string displayName, DateTime changedAtUtc)
    {
        DisplayName = RequireDisplayName(displayName);
        UpdatedAtUtc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
    }

    public void SetActive(bool isActive, DateTime changedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
    }

    /// <summary>Gives the user another role. A user has exactly one.</summary>
    public void AssignRole(Role role, DateTime changedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(role);
        var utc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
        Role = role;
        RoleId = role.Id;
        UpdatedAtUtc = utc;
    }

    /// <summary>Moves the user to another department. A user has exactly one.</summary>
    public void AssignDepartment(Department department, DateTime changedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(department);
        var utc = Customer.RequireUtc(changedAtUtc, nameof(changedAtUtc));
        Department = department;
        DepartmentId = department.Id;
        UpdatedAtUtc = utc;
    }

    public void RecordLogin(DateTime loggedInAtUtc)
    {
        LastLoginAtUtc = Customer.RequireUtc(loggedInAtUtc, nameof(loggedInAtUtc));
        UpdatedAtUtc = loggedInAtUtc;
    }

    private static string RequireDisplayName(string displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("A display name is required.", nameof(displayName)) : displayName.Trim();
}
