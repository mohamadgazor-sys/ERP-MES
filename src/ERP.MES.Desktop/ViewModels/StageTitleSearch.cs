namespace ERP.MES.Desktop.ViewModels;

/// <summary>
/// The matching rules of the stage-title box on the item-definition screen. Only a title that exists in the active catalog can be committed (Decision Log #124 B4);
/// typing narrows the list and never creates a title. Pure and UI-free so the rules are tested without a window.
/// </summary>
public static class StageTitleSearch
{
    /// <summary>Titles containing the typed text (culture-insensitive, ordinal comparison, Persian text unchanged); an empty query returns everything.</summary>
    public static IReadOnlyList<string> Filter(IEnumerable<string> catalog, string? query)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var text = (query ?? string.Empty).Trim();
        return text.Length == 0
            ? catalog.ToList()
            : catalog.Where(t => t.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>
    /// The catalog title that Enter or Tab commits for the typed text: an exact match, else the only match, else null (nothing is committed; an arbitrary title is never accepted).
    /// A highlighted entry, when the caller has one that is part of the filtered list, wins.
    /// </summary>
    public static string? Resolve(IEnumerable<string> catalog, string? typed, string? highlighted = null)
    {
        var all = catalog.ToList();
        if (highlighted is not null && all.Contains(highlighted, StringComparer.Ordinal))
        {
            return highlighted;
        }

        var text = (typed ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var exact = all.FirstOrDefault(t => string.Equals(t, text, StringComparison.Ordinal));
        if (exact is not null)
        {
            return exact;
        }

        var matches = Filter(all, text);
        return matches.Count == 1 ? matches[0] : null;
    }
}
