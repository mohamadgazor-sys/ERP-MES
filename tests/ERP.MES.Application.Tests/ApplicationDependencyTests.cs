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

    [Fact]
    public void Application_source_does_not_mention_ef_core()
    {
        var files = Directory.EnumerateFiles(Path.Combine(SolutionLayout.Root, "src", "ERP.MES.Application"), "*.cs", SearchOption.AllDirectories);

        Assert.DoesNotContain(files, file => File.ReadAllText(file).Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void The_public_persistence_contracts_expose_only_domain_and_base_library_types()
    {
        var contracts = typeof(ERP.MES.Application.Persistence.ICustomerRepository).Assembly.GetExportedTypes();

        var used = contracts
            .SelectMany(type => type.GetMethods().Where(m => m.DeclaringType == type))
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .SelectMany(Flatten)
            .Select(t => t.Namespace ?? string.Empty)
            .Distinct()
            .ToArray();

        Assert.DoesNotContain(used, ns => ns.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) || ns.StartsWith("Microsoft.Data", StringComparison.Ordinal));
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return argument;
            }
        }
    }
}
