using ERP.MES.Tests.Shared;

namespace ERP.MES.Infrastructure.Tests;

// Source guards for the demonstration data (docs/DEMO_SEED_AND_RESET.md): the safe-reset entry point is never reachable from ordinary application
// start-up, the demonstration-only domain method is used only by the seed, and no credential is created or stored by the seed.
public class DemoGuardTests
{
    private static IEnumerable<string> SourceFiles(string project) =>
        Directory.EnumerateFiles(Path.Combine(SolutionLayout.Root, "src", project), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    private static string Relative(string file) => Path.GetRelativePath(SolutionLayout.Root, file).Replace('\\', '/');

    [Fact]
    public void Only_the_provisioning_commands_and_the_demo_class_itself_use_the_demo_database_entry_points()
    {
        var offenders = Directory.EnumerateDirectories(Path.Combine(SolutionLayout.Root, "src"))
            .SelectMany(project => SourceFiles(Path.GetFileName(project)))
            .Where(file => File.ReadAllText(file).Contains("DemoDatabase", StringComparison.Ordinal))
            .Select(Relative)
            .Where(file => file != "src/ERP.MES.Infrastructure.Sqlite/Demo/DemoDatabase.cs" && file != "src/ERP.MES.Provisioning/Program.cs")
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_desktop_application_does_not_seed_or_reset_anything()
    {
        var desktop = SolutionLayout.Load("Desktop");

        Assert.DoesNotContain("ERP.MES.Provisioning", desktop.ProjectReferences);
        Assert.DoesNotContain(
            SourceFiles("ERP.MES.Desktop"),
            file => File.ReadAllText(file).Contains("DemoDataSeeder", StringComparison.Ordinal));
    }

    [Fact]
    public void The_demonstration_only_status_placement_is_used_only_by_the_seed()
    {
        var users = Directory.EnumerateDirectories(Path.Combine(SolutionLayout.Root, "src"))
            .SelectMany(project => SourceFiles(Path.GetFileName(project)))
            .Where(file => File.ReadAllText(file).Contains("PlaceInStatusForDemonstration", StringComparison.Ordinal))
            .Select(Relative)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "src/ERP.MES.Domain/Requests/Request.cs", "src/ERP.MES.Infrastructure/Demo/DemoDataSeeder.cs" },
            users);
    }

    [Fact]
    public void The_seed_and_reset_code_creates_no_credentials()
    {
        var files = SourceFiles("ERP.MES.Infrastructure").Where(f => Relative(f).Contains("/Demo/", StringComparison.Ordinal))
            .Concat(SourceFiles("ERP.MES.Infrastructure.Sqlite").Where(f => Relative(f).Contains("/Demo/", StringComparison.Ordinal)))
            .Append(Path.Combine(SolutionLayout.Root, "src", "ERP.MES.Provisioning", "Program.cs"))
            .ToArray();

        Assert.Equal(3, files.Length);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PBKDF2", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
