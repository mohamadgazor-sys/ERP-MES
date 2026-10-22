using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Planning;

/// <summary>
/// A production unit (Domain and Database Specification 14.1): the department-level group of machines that has its own work queue. Planning owns it (a labelled default of the
/// resource master data of Decision Log #89). The code is a stable business identifier; the Persian name and the active flag can change. Units are retired, never deleted.
/// </summary>
public sealed class ProductionUnit
{
    public ProductionUnit(long id, string code, string name, long createdByUserId, DateTime createdAtUtc)
    {
        Id = id;
        Code = UnitOfMeasure.RequireCode(code);
        Name = UnitOfMeasure.RequireName(name);
        IsActive = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Requirements.Utc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
    }

    public long Id { get; }

    public string Code { get; }

    public string Name { get; private set; }

    public bool IsActive { get; private set; }

    public long CreatedByUserId { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void Rename(string name, DateTime changedAtUtc)
    {
        Name = UnitOfMeasure.RequireName(name);
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    public void SetActive(bool isActive, DateTime changedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }
}
