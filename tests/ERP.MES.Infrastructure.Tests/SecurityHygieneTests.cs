using System.Text.RegularExpressions;
using ERP.MES.Domain.Requests;
using ERP.MES.Tests.Shared;

namespace ERP.MES.Infrastructure.Tests;

// Source-level guards for the approved authentication design (Decision Log #39, #47; Milestone 1 plan 5.8): no credential in the repository, no backdoor or
// universal recovery construct, and no security behaviour in the Domain.
public class SecurityHygieneTests
{
    private static readonly string[] IgnoredFolders = [".git", "bin", "obj", "TestResults", "publish", ".vs"];

    private static IEnumerable<string> RepositoryFiles(params string[] extensions)
    {
        var pending = new Stack<string>();
        pending.Push(SolutionLayout.Root);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            foreach (var sub in Directory.EnumerateDirectories(folder).Where(d => !IgnoredFolders.Contains(Path.GetFileName(d), StringComparer.Ordinal)))
            {
                pending.Push(sub);
            }

            foreach (var file in Directory.EnumerateFiles(folder).Where(f => extensions.Length == 0 || extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
            {
                yield return file;
            }
        }
    }

    private static string Relative(string file) => Path.GetRelativePath(SolutionLayout.Root, file).Replace('\\', '/');

    private static IEnumerable<string> SourceFolderFiles(string project, params string[] extensions) =>
        RepositoryFiles(extensions).Where(f => Relative(f).StartsWith("src/" + project + "/", StringComparison.Ordinal));

    private static IEnumerable<string> CodeLines(string file) =>
        File.ReadAllLines(file).Where(line =>
        {
            var trimmed = line.TrimStart();
            return !trimmed.StartsWith("//", StringComparison.Ordinal) && !trimmed.StartsWith('#') && !trimmed.StartsWith("<!--", StringComparison.Ordinal);
        });

    [Fact]
    public void No_credential_file_is_in_the_repository_and_the_patterns_are_ignored_by_git()
    {
        Assert.DoesNotContain(RepositoryFiles(), f => f.EndsWith(".credentials.txt", StringComparison.OrdinalIgnoreCase));

        var ignore = File.ReadAllText(Path.Combine(SolutionLayout.Root, ".gitignore"));
        Assert.Contains("*.credentials.txt", ignore, StringComparison.Ordinal);
        Assert.Contains("appsettings.Local.json", ignore, StringComparison.Ordinal);
        Assert.Contains("*.db", ignore, StringComparison.Ordinal);
    }

    // The patterns describe constructs, not words in documentation: comment lines are skipped.
    [Theory]
    [InlineData(@"(?i)\bbackdoor\b")]
    [InlineData(@"(?i)master[ _-]?key")]
    [InlineData(@"(?i)skeleton[ _-]?key")]
    [InlineData(@"(?i)universal[ _-]?(recovery[ _-]?)?password")]
    [InlineData(@"(?i)default[ _-]?(admin[ _-]?)?password")]
    [InlineData(@"(?i)recovery[ _-]?(password|code)[ _-]?=")]
    [InlineData(@"(?i:""(password|passw0rd|letmein|changeme|qwerty|123456|12345678|admin123|password123|p@ssw0rd)"")|""(admin|administrator|root)""")]
    public void The_source_has_no_weak_literal_no_default_credential_and_no_backdoor_construct(string pattern)
    {
        var regex = new Regex(pattern);
        var offenders = RepositoryFiles(".cs", ".xaml", ".json", ".yml", ".csproj", ".props")
            .Where(f => Relative(f).StartsWith("src/", StringComparison.Ordinal) || Relative(f).StartsWith(".github/", StringComparison.Ordinal))
            .Where(f => CodeLines(f).Any(regex.IsMatch))
            .Select(Relative)
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_settings_files_hold_only_the_documented_non_secret_authentication_values()
    {
        foreach (var file in RepositoryFiles(".json").Where(f => Path.GetFileName(f).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("password", text.Replace("MinimumPasswordLength", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("connectionstring", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // The Domain DOES hold the approved identity and organisation concepts (Architecture Specification 3.3; Decision Log #61): user, role, department and the
    // login name. What it must stay free of is everything about proving identity and every technology.
    [Fact]
    public void The_domain_holds_the_identity_concepts_but_no_credential_cryptography_persistence_or_ui()
    {
        var names = typeof(Customer).Assembly.GetTypes().Select(t => t.Name).ToArray();
        Assert.Contains("User", names);
        Assert.Contains("Role", names);
        Assert.Contains("Department", names);
        Assert.Contains("Username", names);

        // Credentials, tokens, lockout and the other security workflow are not Domain types; they live in Application.
        Assert.DoesNotContain(names, name => name.Contains("Credential", StringComparison.Ordinal) || name.Contains("Password", StringComparison.Ordinal)
            || name.Contains("Token", StringComparison.Ordinal) || name is "SystemSetup" or "AuditLogEntry");
        Assert.NotEqual(typeof(Customer).Assembly, typeof(ERP.MES.Application.Security.UserCredential).Assembly);

        var domain = SolutionLayout.Load("Domain");
        Assert.Empty(domain.ProjectReferences);
        Assert.Empty(domain.PackageReferences);
        Assert.False(domain.UsesWpf);

        foreach (var file in SourceFolderFiles("ERP.MES.Domain", ".cs"))
        {
            var code = string.Join("\n", CodeLines(file));
            foreach (var forbidden in new[] { "System.Security.Cryptography", "Microsoft.EntityFrameworkCore", "Microsoft.Data", "System.Windows", "System.Data", "password", "pbkdf", "credential", "lockout", "bootstrap" })
            {
                Assert.DoesNotContain(forbidden, code, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void The_domain_identity_types_expose_no_password_hash_salt_or_token_member()
    {
        var members = typeof(Customer).Assembly.GetTypes()
            .Where(t => t.Namespace == "ERP.MES.Domain.Identity")
            .SelectMany(t => t.GetMembers(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
            .Select(m => m.Name)
            .Where(name => name != nameof(object.GetHashCode))
            .ToArray();

        foreach (var forbidden in new[] { "Password", "Hash", "Salt", "Token", "Credential", "Lockout" })
        {
            Assert.DoesNotContain(members, name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_desktop_application_never_calls_the_creating_migration_entry_point()
    {
        // Decision Log #69: start-up applies migrations to an existing database only and never creates, seeds or resets one.
        foreach (var file in SourceFolderFiles("ERP.MES.Desktop", ".cs"))
        {
            var code = string.Join("\n", CodeLines(file));
            Assert.DoesNotContain("SqliteDatabase.MigrateAsync(", code, StringComparison.Ordinal);
            Assert.DoesNotContain("EnsureCreated", code, StringComparison.Ordinal);
            Assert.DoesNotContain("DemoDatabase", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_security_use_cases_read_time_only_through_the_clock_abstraction()
    {
        foreach (var file in SourceFolderFiles("ERP.MES.Application", ".cs").Where(f => Relative(f).Contains("/Security/", StringComparison.Ordinal)))
        {
            var text = string.Join("\n", CodeLines(file));
            Assert.DoesNotContain("DateTime.UtcNow", text, StringComparison.Ordinal);
            Assert.DoesNotContain("DateTime.Now", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Tokens_and_passwords_are_not_written_to_the_console_or_to_a_logger_by_the_application_code()
    {
        foreach (var project in new[] { "ERP.MES.Application", "ERP.MES.Infrastructure", "ERP.MES.Infrastructure.Sqlite", "ERP.MES.Desktop" })
        {
            foreach (var file in SourceFolderFiles(project, ".cs"))
            {
                var text = string.Join("\n", CodeLines(file));
                Assert.DoesNotContain("Console.Write", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Debug.Write", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Trace.Write", text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_only_console_output_of_a_token_is_the_issue_command_of_the_restricted_provisioning_tool()
    {
        var program = File.ReadAllText(Path.Combine(SolutionLayout.Root, "src", "ERP.MES.Provisioning", "Program.cs"));

        // Exactly the two issue commands print a token (once); the demonstration commands print no password.
        Assert.Equal(2, Regex.Matches(program, @"Console\.WriteLine\(issued\.Token\)").Count);
        Assert.DoesNotContain("Console.WriteLine(credentials", program, StringComparison.Ordinal);
    }
}
