using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Planning;

/// <summary>
/// One entry of the stage-title catalog (Decision Log #124 B4): the titles Planning may give to production stages. Planning maintains it; a title is retired, never deleted, and a stage keeps
/// its own copy of the title text it was defined with, so retiring or reactivating an entry never changes existing stages. The title is compared exactly (case-sensitive).
/// </summary>
public sealed class StageTitle
{
    /// <summary>The longest title, the same limit a stage has.</summary>
    public const int MaxLength = 200;

    public StageTitle(long id, string title, long createdByUserId, DateTime createdAtUtc)
    {
        Id = id;
        Title = Require(title);
        IsActive = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Requirements.Utc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
    }

    public long Id { get; }

    public string Title { get; }

    public bool IsActive { get; private set; }

    public long CreatedByUserId { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>True when the text is a usable title: not empty, at most <see cref="MaxLength"/> characters, no leading or trailing white space.</summary>
    public static bool IsValid(string? title) => !string.IsNullOrWhiteSpace(title) && title.Length <= MaxLength && title.Trim() == title;

    public void SetActive(bool isActive, DateTime changedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    private static string Require(string? title) =>
        IsValid(title) ? title! : throw new ArgumentException("A stage title is 1 to " + MaxLength + " characters without leading or trailing white space.", nameof(title));
}
