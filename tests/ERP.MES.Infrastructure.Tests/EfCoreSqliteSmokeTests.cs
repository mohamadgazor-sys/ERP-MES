using System.Xml.Linq;
using ERP.MES.Tests.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Tests;

// Proves the approved EF Core / SQLite packages (Decision Log #53, batch 2) restore, load their native SQLite
// library and work on the CI runner. These tests use a throwaway test-only model and an in-memory database;
// they say nothing about the application's schema, which is designed in its own change.
public class EfCoreSqliteSmokeTests
{
    private const string ApprovedVersion = "10.0.13";

    private sealed class SmokeEntity
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class SmokeContext(SqliteConnection connection) : DbContext
    {
        public DbSet<SmokeEntity> Items => Set<SmokeEntity>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseSqlite(connection);
    }

    [Fact]
    public void Sqlite_provider_round_trips_a_row_in_memory()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var context = new SmokeContext(connection))
        {
            Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", context.Database.ProviderName);
            context.Database.EnsureCreated();
            context.Items.Add(new SmokeEntity { Name = "sample" });
            context.SaveChanges();
        }

        using (var context = new SmokeContext(connection))
        {
            var stored = Assert.Single(context.Items.ToList());
            Assert.Equal("sample", stored.Name);
        }
    }

    [Fact]
    public void Ef_core_packages_are_pinned_to_the_same_approved_version()
    {
        var props = XDocument.Load(Path.Combine(SolutionLayout.Root, "Directory.Packages.props"));
        var versions = props.Descendants("PackageVersion")
            .Where(e => ((string?)e.Attribute("Include") ?? string.Empty).StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .ToDictionary(e => (string)e.Attribute("Include")!, e => (string?)e.Attribute("Version"));

        Assert.NotEmpty(versions);
        Assert.All(versions, pair => Assert.Equal(ApprovedVersion, pair.Value));
    }

    [Fact]
    public void Only_the_sqlite_project_references_the_sqlite_provider_package()
    {
        Assert.Contains("Microsoft.EntityFrameworkCore.Sqlite", SolutionLayout.Load("Infrastructure.Sqlite").PackageReferences);

        foreach (var name in new[] { "Domain", "Application", "Infrastructure", "Desktop", "Provisioning" })
        {
            Assert.DoesNotContain("Microsoft.EntityFrameworkCore.Sqlite", SolutionLayout.Load(name).PackageReferences);
        }
    }

    [Fact]
    public void No_sql_server_provider_package_is_referenced_anywhere_in_the_solution()
    {
        var all = new[] { "Domain", "Application", "Infrastructure", "Infrastructure.Sqlite", "Desktop", "Provisioning" }
            .SelectMany(n => SolutionLayout.Load(n).PackageReferences);

        Assert.DoesNotContain(all, p => p.Contains("SqlServer", StringComparison.OrdinalIgnoreCase));
    }
}
