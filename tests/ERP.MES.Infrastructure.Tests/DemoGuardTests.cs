using ERP.MES.Domain.Requests;
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

    private const string StatusPlacement = "PlaceInStatusForDemonstration";

    private static string[] InfrastructureSources() =>
        SourceFiles("ERP.MES.Infrastructure").Select(file => File.ReadAllText(file)).ToArray();

    [Fact]
    public void The_demonstration_only_status_placement_has_exactly_one_production_call_site_the_seed()
    {
        var mentions = Directory.EnumerateDirectories(Path.Combine(SolutionLayout.Root, "src"))
            .SelectMany(project => SourceFiles(Path.GetFileName(project)))
            .Where(file => File.ReadAllText(file).Contains(StatusPlacement, StringComparison.Ordinal))
            .Select(Relative)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "src/ERP.MES.Domain/Requests/Request.cs", "src/ERP.MES.Infrastructure/Demo/DemoDataSeeder.cs" },
            mentions);

        // Exactly one call in the seed, and the Domain only declares it (internal, never public).
        var seeder = File.ReadAllText(Path.Combine(SolutionLayout.Root, "src", "ERP.MES.Infrastructure", "Demo", "DemoDataSeeder.cs"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(seeder, StatusPlacement + @"\("));

        var request = File.ReadAllText(Path.Combine(SolutionLayout.Root, "src", "ERP.MES.Domain", "Requests", "Request.cs"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(request, @"\binternal void " + StatusPlacement + @"\("));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(request, StatusPlacement + @"\("));
    }

    [Fact]
    public void The_domain_shares_its_internals_with_exactly_the_domain_tests_and_the_infrastructure_assembly()
    {
        var friends = typeof(Request).Assembly
            .GetCustomAttributes(typeof(System.Runtime.CompilerServices.InternalsVisibleToAttribute), inherit: false)
            .Cast<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "ERP.MES.Domain.Tests", "ERP.MES.Infrastructure" }, friends);
    }

    // The Infrastructure assembly can see the Domain's internals (needed for the status placement). This keeps it from using any other internal
    // method, constructor or property setter of the Domain.
    [Fact]
    public void Infrastructure_uses_no_internal_domain_member_other_than_the_status_placement()
    {
        const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
        var types = typeof(Request).Assembly.GetTypes()
            .Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false) && !t.Name.StartsWith('<'))
            .ToArray();

        var internalMethods = types.SelectMany(t => t.GetMethods(all))
            .Where(m => (m.IsAssembly || m.IsFamilyOrAssembly) && !m.Name.StartsWith('<') && !m.Name.Contains('$', StringComparison.Ordinal))
            .ToArray();
        var internalConstructors = types.SelectMany(t => t.GetConstructors(all)).Where(c => c.IsAssembly || c.IsFamilyOrAssembly).ToArray();

        // Guard the guard: the members that exist today are found.
        Assert.Contains(internalMethods, m => m.Name == StatusPlacement);
        Assert.Contains(internalMethods, m => m.Name == "SetStatus");
        Assert.NotEmpty(internalConstructors);

        var sources = InfrastructureSources();
        var usedMethods = new List<string>();
        foreach (var method in internalMethods.Where(m => m.Name != StatusPlacement))
        {
            var name = method.IsSpecialName && method.Name.StartsWith("set_", StringComparison.Ordinal) ? method.Name[4..] : method.Name;
            var pattern = method.IsSpecialName
                ? @"\." + System.Text.RegularExpressions.Regex.Escape(name) + @"\s*=(?!=)"
                : @"\b" + System.Text.RegularExpressions.Regex.Escape(name) + @"\s*\(";
            if (sources.Any(text => System.Text.RegularExpressions.Regex.IsMatch(text, pattern)))
            {
                usedMethods.Add(method.DeclaringType!.Name + "." + method.Name);
            }
        }

        var usedConstructors = internalConstructors
            .Select(c => c.DeclaringType!.Name)
            .Distinct()
            .Where(type => sources.Any(text => System.Text.RegularExpressions.Regex.IsMatch(text, @"\bnew " + type + @"\s*\(")))
            .ToList();

        Assert.Empty(usedMethods);
        Assert.Empty(usedConstructors);
    }

    private static IEnumerable<string> CodeLines(string file) =>
        File.ReadAllLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));

    [Fact]
    public void The_seed_and_reset_entry_points_hard_code_no_password()
    {
        var files = new[]
        {
            Path.Combine(SolutionLayout.Root, "src", "ERP.MES.Infrastructure", "Demo", "DemoDataSeeder.cs"),
            Path.Combine(SolutionLayout.Root, "src", "ERP.MES.Infrastructure.Sqlite", "Demo", "DemoDatabase.cs"),
        };

        foreach (var file in files)
        {
            Assert.DoesNotContain(CodeLines(file), line => line.Contains("password", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(CodeLines(file), line => line.Contains("PBKDF2", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Only_the_demonstration_database_class_and_the_demo_folder_use_the_demo_account_provisioner()
    {
        var users = Directory.EnumerateDirectories(Path.Combine(SolutionLayout.Root, "src"))
            .SelectMany(project => SourceFiles(Path.GetFileName(project)))
            .Where(file => File.ReadAllText(file).Contains("DemoAccountProvisioner", StringComparison.Ordinal))
            .Select(Relative)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "src/ERP.MES.Infrastructure.Sqlite/Demo/DemoDatabase.cs", "src/ERP.MES.Infrastructure/Demo/DemoAccountProvisioner.cs" },
            users);
    }
}
