using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Planning;

/// <summary>
/// A production unit (Domain and Database Specification 14.1): the department-level group of machines that has its own work queue. Planning owns it (a labelled default of the
/// resource master data of Decision Log #89). The code is a stable business identifier; the Persian name and the active flag can change. Units are retired, never deleted.
/// </summary>
public sealed class ProductionUnit
{
    public ProductionUnit(long id, string code, string name, long createdByUserId, DateTime createdAtUtc)
        : this(id, code, name, createdByUserId, createdAtUtc, ProductionUnitCategory.InternalProduction)
    {
    }

    public ProductionUnit(long id, string code, string name, long createdByUserId, DateTime createdAtUtc, string category)
    {
        Category = RequireCategory(category);
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

    /// <summary>One of the nine codes of <see cref="ProductionUnitCategory"/> (Decision Log #129); existing units are <see cref="ProductionUnitCategory.InternalProduction"/>.</summary>
    public string Category { get; private set; } = ProductionUnitCategory.InternalProduction;

    /// <summary>An external route (contractor, plating, heat treatment...): its stages are sent and returned by the Warehouse, never queued or worked internally.</summary>
    public bool IsExternal => ProductionUnitCategory.IsExternal(Category);

    public long CreatedByUserId { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void Rename(string name, DateTime changedAtUtc)
    {
        Name = UnitOfMeasure.RequireName(name);
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    /// <summary>Program Admin sets the category; the Application refuses it while a stage that is not QC approved uses the unit, or while an external unit would keep machines.</summary>
    public void SetCategory(string category, DateTime changedAtUtc)
    {
        Category = RequireCategory(category);
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    public void SetActive(bool isActive, DateTime changedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    private static string RequireCategory(string category) =>
        ProductionUnitCategory.IsValid(category) ? category : throw new ArgumentException("The category must be one of the nine codes of Master Specification 16.2.", nameof(category));
}
