using ERP.MES.Tests.Shared;

namespace ERP.MES.Domain.Tests;

// Guards the approved rule (Decision Log #37): Domain stays independent of infrastructure and UI.
public class DomainDependencyTests
{
    private static ProjectFile Domain => SolutionLayout.Load("Domain");

    [Fact]
    public void Domain_references_no_other_project()
    {
        Assert.Empty(Domain.ProjectReferences);
    }

    [Fact]
    public void Domain_references_no_persistence_package()
    {
        Assert.Empty(Domain.PersistencePackages);
    }

    [Fact]
    public void Domain_does_not_use_wpf()
    {
        Assert.False(Domain.UsesWpf);
    }
}
