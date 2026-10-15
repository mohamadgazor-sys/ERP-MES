namespace ERP.MES.Domain.Requests;

/// <summary>
/// Customer master (Domain and Database Specification section 4.5, Decision Log #32): name, active flag and audit
/// fields only. No customer code and no contact fields. Persistence assigns the key later; new objects use id 0.
/// </summary>
public sealed class Customer
{
    public Customer(long id, string name, long createdByUserId, DateTime createdAtUtc)
    {
        Id = id;
        Name = RequireName(name);
        IsActive = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = RequireUtc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
    }

    public long Id { get; }

    public string Name { get; private set; }

    public bool IsActive { get; private set; }

    public long CreatedByUserId { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void Rename(string name, DateTime changedAtUtc)
    {
        Name = RequireName(name);
        UpdatedAtUtc = RequireUtc(changedAtUtc, nameof(changedAtUtc));
    }

    public void SetActive(bool isActive, DateTime changedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = RequireUtc(changedAtUtc, nameof(changedAtUtc));
    }

    private static string RequireName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A customer name is required.", nameof(name)) : name;

    internal static DateTime RequireUtc(DateTime value, string parameterName) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new ArgumentException("Timestamps are stored as UTC (Decision Log #24).", parameterName);
}
