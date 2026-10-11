using ERP.MES.Tests.Shared;

namespace ERP.MES.Application.Tests;

// Guards the approved dependency direction (Decision Log #37): Application depends on Domain only.
public class ApplicationDependencyTests
{
    private static ProjectFile Application => SolutionLayout.Load("Application");

    [Fact]
    public void Application_references_only_the_domain_project()
    {
        Assert.Equal(new[] { "ERP.MES.Domain" }, Application.ProjectReferences);
    }

    [Fact]
    public void Application_references_no_persistence_package()
    {
        Assert.Empty(Application.PersistencePackages);
    }

    [Fact]
    public void Application_does_not_use_wpf()
    {
        Assert.False(Application.UsesWpf);
    }
}
