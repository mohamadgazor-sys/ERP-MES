using System.Text.RegularExpressions;
using ERP.MES.Tests.Shared;

namespace ERP.MES.Infrastructure.Tests;

// Guards the approved persistence rules: the schema comes from migrations only, and the context stays provider-neutral.
public partial class PersistenceGuardTests
{
    [GeneratedRegex(@"\.EnsureCreated(Async)?\s*\(")]
    private static partial Regex EnsureCreatedCall();

    [Fact]
    public void No_source_file_creates_the_schema_with_ensure_created()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(SolutionLayout.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => EnsureCreatedCall().IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(SolutionLayout.Root, file))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_migrations_and_the_snapshot_are_committed_in_the_sqlite_project()
    {
        var folder = Path.Combine(SolutionLayout.Root, "src", "ERP.MES.Infrastructure.Sqlite", "Migrations");
        var files = Directory.EnumerateFiles(folder, "*.cs").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Contains("ErpMesDbContextModelSnapshot.cs", files);
        Assert.Single(files, f => f!.EndsWith("_InitialCreate.cs", StringComparison.Ordinal));
        Assert.Single(files, f => f!.EndsWith("_InitialCreate.Designer.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void The_domain_project_does_not_know_about_persistence()
    {
        var domainFiles = Directory.EnumerateFiles(Path.Combine(SolutionLayout.Root, "src", "ERP.MES.Domain"), "*.cs", SearchOption.AllDirectories);

        Assert.DoesNotContain(domainFiles, file => File.ReadAllText(file).Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }
}
