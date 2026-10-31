using ERP.MES.Application.Security;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>
/// Who acted, for the history screens (Decision Log #126 B3): the display name, the user name (the stable identity) and an inactive marker, resolved from the user id kept on the record, so a renamed
/// or deactivated person is still identified correctly. The department is shown next to it where the record keeps one. An unknown id (or a screen built without a directory) shows nothing.
/// </summary>
public static class ActorLookup
{
    public static async Task<IReadOnlyDictionary<long, string>> DescribeAsync(UserDirectoryService? users, Principal? principal, IEnumerable<long> userIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var wanted = userIds.Distinct().ToList();
        if (users is null || wanted.Count == 0)
        {
            return new Dictionary<long, string>();
        }

        var found = await users.LookupAsync(principal, wanted, cancellationToken);
        return found.Succeeded ? found.Value!.ToDictionary(p => p.Key, p => UserDirectoryService.Describe(p.Value)) : new Dictionary<long, string>();
    }

    /// <summary>The text for one actor; with a department the form is «name (user name) — department».</summary>
    public static string Text(IReadOnlyDictionary<long, string> names, long? userId, string? departmentText = null)
    {
        var name = userId is { } id && names.TryGetValue(id, out var text) ? text : string.Empty;
        return string.IsNullOrEmpty(departmentText) ? name : string.IsNullOrEmpty(name) ? departmentText : name + " — " + departmentText;
    }
}
