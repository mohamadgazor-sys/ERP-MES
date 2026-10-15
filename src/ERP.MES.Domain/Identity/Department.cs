namespace ERP.MES.Domain.Identity;

/// <summary>
/// A department (Domain and Database Specification 15.1 and 15.3). It is identified by its stable <see cref="Code"/>; the <see cref="Name"/> is a replaceable
/// display label (Decision Log #30 allows final company names without a schema change) and is never used to decide anything.
/// </summary>
public sealed class Department
{
    private Department()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Department(long id, string code, string name)
    {
        Id = id;
        Code = RequireCode(code);
        Name = RequireName(name);
    }

    public long Id { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public void Rename(string name) => Name = RequireName(name);

    internal static string RequireCode(string code) =>
        string.IsNullOrWhiteSpace(code) || code.Any(c => char.IsWhiteSpace(c) || char.IsLower(c))
            ? throw new ArgumentException("A code is required: non-empty, upper case, without white space.", nameof(code))
            : code;

    internal static string RequireName(string name) =>
        string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A display name is required.", nameof(name)) : name;
}

/// <summary>A role (Domain and Database Specification 15.1 and 15.3), identified by its stable <see cref="Code"/>.</summary>
public sealed class Role
{
    private Role()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Role(long id, string code, string name)
    {
        Id = id;
        Code = Department.RequireCode(code);
        Name = Department.RequireName(name);
    }

    public long Id { get; private set; }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public void Rename(string name) => Name = Department.RequireName(name);
}
