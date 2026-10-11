using ERP.MES.Tests.Shared;

namespace ERP.MES.Infrastructure.Tests;

// Guards the approved provider separation and dependency direction (Decision Log #37, docs/MILESTONE_1_PLAN.md section 2).
public class InfrastructureDependencyTests
{
    private static ProjectFile Infrastructure => SolutionLayout.Load("Infrastructure");

    private static ProjectFile Sqlite => SolutionLayout.Load("Infrastructure.Sqlite");

    [Fact]
    public void Shared_infrastructure_references_only_application_and_domain()
    {
        Assert.Equal(new[] { "ERP.MES.Application", "ERP.MES.Domain" }, Infrastructure.ProjectReferences);
    }

    [Fact]
    public void Shared_infrastructure_references_no_database_provider_package()
    {
        var providers = Infrastructure.PackageReferences
            .Where(p => (p.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(p, "Microsoft.EntityFrameworkCore.Relational", StringComparison.OrdinalIgnoreCase))
                        || p.StartsWith("Microsoft.Data.", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(providers);
    }

    [Fact]
    public void Sqlite_project_references_only_the_shared_infrastructure_project()
    {
        Assert.Equal(new[] { "ERP.MES.Infrastructure" }, Sqlite.ProjectReferences);
    }

    [Fact]
    public void No_library_project_references_a_composition_root()
    {
        var libraries = new[] { "Domain", "Application", "Infrastructure", "Infrastructure.Sqlite" };
        var roots = new[] { "ERP.MES.Desktop", "ERP.MES.Provisioning" };

        foreach (var library in libraries)
        {
            var references = SolutionLayout.Load(library).ProjectReferences;
            Assert.DoesNotContain(references, r => roots.Contains(r));
        }
    }

    [Fact]
    public void Only_the_desktop_project_uses_wpf()
    {
        var others = new[] { "Domain", "Application", "Infrastructure", "Infrastructure.Sqlite", "Provisioning" };

        Assert.True(SolutionLayout.Load("Desktop").UsesWpf);
        foreach (var name in others)
        {
            Assert.False(SolutionLayout.Load(name).UsesWpf, name + " must not use WPF");
        }
    }
}
