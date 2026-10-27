using System.Text.Json;
using System.Xml.Linq;
using ERP.MES.Tests.Shared;

namespace ERP.MES.Infrastructure.Tests;

// Guards the approved migration tooling (Decision Log #56): the design-time package is private to the projects that need
// it, the dotnet-ef tool is pinned in a committed manifest, and both use the same version as EF Core.
public class EfToolingTests
{
    private const string DesignPackage = "Microsoft.EntityFrameworkCore.Design";

    private static IEnumerable<string> SourceProjectFiles() =>
        Directory.EnumerateFiles(Path.Combine(SolutionLayout.Root, "src"), "*.csproj", SearchOption.AllDirectories);

    [Fact]
    public void The_design_package_is_always_private_to_the_project_that_references_it()
    {
        var references = SourceProjectFiles()
            .SelectMany(file => XDocument.Load(file).Descendants("PackageReference")
                .Where(e => string.Equals((string?)e.Attribute("Include"), DesignPackage, StringComparison.Ordinal))
                .Select(e => (File: Path.GetFileName(file), PrivateAssets: (string?)e.Attribute("PrivateAssets"))))
            .ToList();

        Assert.NotEmpty(references);
        Assert.All(references, r => Assert.Equal("all", r.PrivateAssets));
    }

    [Fact]
    public void The_design_package_is_only_in_the_migrations_projects_and_the_startup_projects()
    {
        var files = SourceProjectFiles()
            .Where(file => XDocument.Load(file).Descendants("PackageReference")
                .Any(e => string.Equals((string?)e.Attribute("Include"), DesignPackage, StringComparison.Ordinal)))
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "ERP.MES.Infrastructure.SqlServer", "ERP.MES.Infrastructure.SqlServer.Design", "ERP.MES.Infrastructure.Sqlite", "ERP.MES.Provisioning" }, files);
    }

    [Fact]
    public void Dotnet_ef_is_pinned_in_the_tool_manifest_to_the_ef_core_version()
    {
        var manifestPath = Path.Combine(SolutionLayout.Root, ".config", "dotnet-tools.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var tool = manifest.RootElement.GetProperty("tools").GetProperty("dotnet-ef");

        var props = XDocument.Load(Path.Combine(SolutionLayout.Root, "Directory.Packages.props"));
        var efVersions = props.Descendants("PackageVersion")
            .Where(e => ((string?)e.Attribute("Include") ?? string.Empty).StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .Select(e => (string?)e.Attribute("Version"))
            .Distinct()
            .ToArray();

        Assert.True(manifest.RootElement.GetProperty("isRoot").GetBoolean());
        Assert.Equal("10.0.13", tool.GetProperty("version").GetString());
        Assert.False(tool.GetProperty("rollForward").GetBoolean());
        Assert.Equal(new[] { "10.0.13" }, efVersions);
    }
}
