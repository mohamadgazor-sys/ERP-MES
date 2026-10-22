using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Planning;

/// <summary>
/// A production operator (Decision Log #82 R2, #89): Planning registers operators. The operator code is a stable business identifier (unique, never changed, never reused). The link
/// to a user account is OPTIONAL and, once an operator has to submit daily job reports in the application (R5), required in practice; a user belongs to at most one operator. Operators are
/// retired, never deleted, so reports and costs that name them stay explainable.
/// </summary>
public sealed class Operator
{
    public const int MaxNameLength = 200;

    public Operator(long id, string code, string fullName, long? userId, long createdByUserId, DateTime createdAtUtc)
    {
        Id = id;
        Code = UnitOfMeasure.RequireCode(code);
        FullName = RequireFullName(fullName);
        UserId = userId;
        IsActive = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Requirements.Utc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
    }

    public long Id { get; }

    public string Code { get; }

    public string FullName { get; private set; }

    /// <summary>The user account the operator signs in with, or null while the operator has none.</summary>
    public long? UserId { get; private set; }

    public bool IsActive { get; private set; }

    public long CreatedByUserId { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void Rename(string fullName, DateTime changedAtUtc)
    {
        FullName = RequireFullName(fullName);
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    /// <summary>Links or unlinks the account (null unlinks). The caller has checked that the user exists and is not linked to another operator.</summary>
    public void LinkUser(long? userId, DateTime changedAtUtc)
    {
        UserId = userId;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    public void SetActive(bool isActive, DateTime changedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    private static string RequireFullName(string? name)
    {
        var text = UnitOfMeasure.RequireName(name);
        return text.Length <= MaxNameLength ? text : throw new ArgumentException("A name is limited to " + MaxNameLength + " characters.", nameof(name));
    }
}
