using ERP.MES.Application.Requests;

namespace ERP.MES.Application.Security;

/// <summary>
/// One user as shown to people who need to know who someone is: stable identity (id and immutable user name), the display name, the department and role codes and whether the account is still active
/// (Decision Log #126 B3). It never contains a credential.
/// </summary>
public sealed record UserDirectoryEntry(long UserId, string Username, string DisplayName, string DepartmentCode, string RoleCode, bool IsActive, DateTime? LastLoginAtUtc);

/// <summary>Read-only access to the user list (provider-neutral).</summary>
public interface IUserDirectoryRepository
{
    Task<IReadOnlyList<UserDirectoryEntry>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The user directory (Decision Log #122, #126). Program Admin lists every account; every active user may resolve the identity of the people who acted on a record (who approved, forwarded, decided), so a
/// history screen can show a display name and user name instead of an id, with the department and an inactive marker. Nothing is written here.
/// </summary>
public sealed class UserDirectoryService(IUserDirectoryRepository directory, CurrentAuthorization authorization)
{
    /// <summary>All accounts, newest first by name. Program Admin only (account administration).</summary>
    public async Task<Outcome<IReadOnlyList<UserDirectoryEntry>>> ListAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.AdministerUsers, cancellationToken)).IsAllowed)
        {
            return Outcome<IReadOnlyList<UserDirectoryEntry>>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
            Outcome<IReadOnlyList<UserDirectoryEntry>>.Ok((await directory.ListAsync(cancellationToken)).OrderBy(u => u.Username, StringComparer.Ordinal).ToList()));
    }

    /// <summary>The identities of the given users, for history screens. Any active, signed-in user may ask; unknown ids are simply absent.</summary>
    public async Task<Outcome<IReadOnlyDictionary<long, UserDirectoryEntry>>> LookupAsync(Principal? principal, IEnumerable<long> userIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (!(await authorization.CheckAsync(principal, Permission.ViewCustomersAndRequests, cancellationToken)).IsAllowed)
        {
            return Outcome<IReadOnlyDictionary<long, UserDirectoryEntry>>.Fail(OperationStatus.NotAuthorized);
        }

        var wanted = userIds.ToHashSet();
        return await CustomerService.GuardAsync(async () =>
            Outcome<IReadOnlyDictionary<long, UserDirectoryEntry>>.Ok((await directory.ListAsync(cancellationToken)).Where(u => wanted.Contains(u.UserId)).ToDictionary(u => u.UserId)));
    }

    /// <summary>The text shown for an actor in a history: display name, user name and the inactive marker; the department is added by the caller when useful.</summary>
    public static string Describe(UserDirectoryEntry? entry) => entry is null
        ? string.Empty
        : entry.DisplayName + " (" + entry.Username + ")" + (entry.IsActive ? string.Empty : " — غیرفعال");
}
