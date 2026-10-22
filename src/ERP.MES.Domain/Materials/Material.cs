using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Materials;

/// <summary>
/// A raw material in the Warehouse-owned material list (Decision Log #10; docs/RAW_MATERIAL_INQUIRY_DESIGN.md U-08 recommendation, a labelled default). The code is a STABLE business
/// identifier (unique, never reused or changed) because price history, inquiries and cost snapshots refer to it. Grade (for example ST37) and form (bar, tube, plate) are optional
/// descriptive text. Materials are retired, never deleted.
/// </summary>
public sealed class Material
{
    public const int MaxDescriptorLength = 100;

    public Material(long id, string code, string name, string? grade, string? form, long createdByUserId, DateTime createdAtUtc)
    {
        Id = id;
        Code = UnitOfMeasure.RequireCode(code);
        Name = UnitOfMeasure.RequireName(name);
        Grade = Descriptor(grade, nameof(grade));
        Form = Descriptor(form, nameof(form));
        IsActive = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Requirements.Utc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
    }

    public long Id { get; }

    public string Code { get; }

    public string Name { get; private set; }

    public string? Grade { get; private set; }

    public string? Form { get; private set; }

    public bool IsActive { get; private set; }

    public long CreatedByUserId { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void Update(string name, string? grade, string? form, DateTime changedAtUtc)
    {
        Name = UnitOfMeasure.RequireName(name);
        Grade = Descriptor(grade, nameof(grade));
        Form = Descriptor(form, nameof(form));
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    public void SetActive(bool isActive, DateTime changedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    private static string? Descriptor(string? value, string parameter)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return text is { Length: > MaxDescriptorLength }
            ? throw new ArgumentException("The value is limited to " + MaxDescriptorLength + " characters.", parameter)
            : text;
    }
}
