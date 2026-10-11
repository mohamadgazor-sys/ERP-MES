using System.Xml.Linq;

namespace ERP.MES.Tests.Shared;

/// <summary>
/// Reads the solution's project files so tests can guard the approved dependency direction
/// (docs/MILESTONE_1_PLAN.md section 2). Linked into every test project; not a project of its own.
/// </summary>
internal static class SolutionLayout
{
    private static readonly Lazy<string> RootDirectory = new(FindRoot);

    public static string Root => RootDirectory.Value;

    /// <summary>Loads src/ERP.MES.{shortName}/ERP.MES.{shortName}.csproj, e.g. "Domain".</summary>
    public static ProjectFile Load(string shortName)
    {
        var name = "ERP.MES." + shortName;
        var path = Path.Combine(Root, "src", name, name + ".csproj");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Project file not found.", path);
        }

        var project = XDocument.Load(path).Root
            ?? throw new InvalidDataException("Project file has no root element: " + path);

        var projectReferences = project.Descendants("ProjectReference")
            // Project files use backslashes; normalise so this also works on non-Windows hosts.
            .Select(e => Path.GetFileNameWithoutExtension(((string?)e.Attribute("Include") ?? string.Empty).Replace('\\', '/')))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var packageReferences = project.Descendants("PackageReference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var usesWpf = string.Equals(project.Descendants("UseWPF").FirstOrDefault()?.Value, "true", StringComparison.OrdinalIgnoreCase);

        return new ProjectFile(name, projectReferences, packageReferences, usesWpf);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ERP-MES.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("ERP-MES.sln was not found above " + AppContext.BaseDirectory);
    }
}

internal sealed record ProjectFile(
    string Name,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences,
    bool UsesWpf)
{
    /// <summary>Package ids that would tie a layer to a database provider or a data-access library.</summary>
    public static bool IsPersistencePackage(string packageId) =>
        packageId.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.OrdinalIgnoreCase)
        || packageId.StartsWith("Microsoft.Data.", StringComparison.OrdinalIgnoreCase)
        || packageId.StartsWith("System.Data.", StringComparison.OrdinalIgnoreCase)
        || packageId.StartsWith("Dapper", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> PersistencePackages => PackageReferences.Where(IsPersistencePackage).ToList();
}
