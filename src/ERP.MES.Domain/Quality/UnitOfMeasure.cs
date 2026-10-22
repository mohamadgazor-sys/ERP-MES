namespace ERP.MES.Domain.Quality;

/// <summary>What a unit of measure measures. Only a <see cref="Count"/> unit can be the base counting unit of a quantity type (Decision Log #90).</summary>
public enum UnitKind
{
    /// <summary>Countable things: piece, pair.</summary>
    Count = 1,
    Mass,
    Length,
    Other,
}

/// <summary>
/// A unit of measure (Decision Log #9, #90; Domain and Database Specification section 16): QC owns the definitions. It answers "how is an amount expressed" (kg, mm, PCS); it is
/// distinct from the quantity type (piece, pair, set), which says what countable thing is ordered, produced and inspected (<see cref="QuantityType"/>). The code is a stable
/// business identifier and never changes; the Persian name and the active flag can. Definitions are retired, never deleted.
/// </summary>
public sealed class UnitOfMeasure
{
    public const int MaxCodeLength = 20;

    public UnitOfMeasure(long id, string code, string name, UnitKind kind, long? createdByUserId, DateTime createdAtUtc)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown unit kind.");
        }

        Id = id;
        Code = RequireCode(code);
        Name = RequireName(name);
        Kind = kind;
        IsActive = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Requirements.Utc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
    }

    public long Id { get; }

    /// <summary>Upper-case ASCII letters, digits, '_' and '-', 1 to 20 characters, unique. Stored exactly as given.</summary>
    public string Code { get; }

    public string Name { get; private set; }

    public UnitKind Kind { get; }

    public bool IsActive { get; private set; }

    /// <summary>Null for a definition that ships with the system (the standard units); otherwise the QC user who created it.</summary>
    public long? CreatedByUserId { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void Rename(string name, DateTime changedAtUtc)
    {
        Name = RequireName(name);
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    public void SetActive(bool isActive, DateTime changedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    /// <summary>True when <paramref name="code"/> is a valid business code (see <see cref="Code"/>).</summary>
    public static bool IsValidCode(string? code) =>
        !string.IsNullOrEmpty(code) && code.Length <= MaxCodeLength && code.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-');

    internal static string RequireCode(string? code) =>
        IsValidCode(code)
            ? code!
            : throw new ArgumentException("A code is 1 to " + MaxCodeLength + " characters of A-Z, 0-9, '_' or '-'.", nameof(code));

    internal static string RequireName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A name is required.", nameof(name)) : name.Trim();
}

internal static class Requirements
{
    public static DateTime Utc(DateTime value, string parameterName) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new ArgumentException("Timestamps are stored as UTC (Decision Log #24).", parameterName);
}
