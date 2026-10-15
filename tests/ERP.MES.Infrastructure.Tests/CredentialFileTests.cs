using System.Security.AccessControl;
using System.Security.Principal;
using ERP.MES.Infrastructure.Security;
using ERP.MES.Tests.Shared;

namespace ERP.MES.Infrastructure.Tests;

// The credential file of the fictional demonstration accounts (Four-Day Demonstration Plan T6): outside the repository, never overwritten, owner-only.
public class CredentialFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "erpmes-credentials-" + Guid.NewGuid().ToString("N"));

    private static readonly (string, string)[] Sample = [("demo.one", "Sample-One-Pass-1"), ("demo.two", "Sample-Two-Pass-2")];

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void The_file_is_written_with_one_line_per_account_and_the_database_path()
    {
        var path = Path.Combine(_folder, "sub", "demo.credentials.txt");

        CredentialFile.WriteNew(path, "C:/data/erpmes.demo.db", Sample);

        var text = File.ReadAllText(path);
        Assert.Contains("demo.one\tSample-One-Pass-1", text, StringComparison.Ordinal);
        Assert.Contains("demo.two\tSample-Two-Pass-2", text, StringComparison.Ordinal);
        Assert.Contains("C:/data/erpmes.demo.db", text, StringComparison.Ordinal);
        Assert.False(text.StartsWith('\uFEFF'));
    }

    [Fact]
    public void An_existing_file_is_never_overwritten()
    {
        var path = Path.Combine(_folder, "demo.credentials.txt");
        CredentialFile.WriteNew(path, "db", Sample);
        var before = File.ReadAllText(path);

        var exception = Assert.Throws<CredentialFileException>(() => CredentialFile.WriteNew(path, "db", [("other", "Other-Pass-Phrase-3")]));

        Assert.Contains("never overwritten", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void A_path_inside_the_repository_is_refused_and_nothing_is_created()
    {
        var inside = Path.Combine(SolutionLayout.Root, "tests", "never-written.credentials.txt");

        var exception = Assert.Throws<CredentialFileException>(() => CredentialFile.WriteNew(inside, "db", Sample));

        Assert.Contains("outside the repository", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(inside));
        Assert.True(CredentialFile.IsInsideGitWorkingTree(SolutionLayout.Root));
        Assert.False(CredentialFile.IsInsideGitWorkingTree(Path.GetTempPath()));
    }

    [Fact]
    public void The_file_can_be_read_only_by_the_current_user()
    {
        var path = Path.Combine(_folder, "private.credentials.txt");

        CredentialFile.WriteNew(path, "db", Sample);

        if (OperatingSystem.IsWindows())
        {
            AssertOwnerOnlyAccessList(path);
        }
        else
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void AssertOwnerOnlyAccessList(string path)
    {
        var security = new FileInfo(path).GetAccessControl();
        var me = WindowsIdentity.GetCurrent().User!;

        // Inheritance is cut, so the folder's permissions (for example other users or groups) do not apply.
        Assert.True(security.AreAccessRulesProtected);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        Assert.NotEmpty(rules);
        Assert.All(rules, rule => Assert.Equal(me, rule.IdentityReference));
        Assert.All(rules, rule => Assert.Equal(AccessControlType.Allow, rule.AccessControlType));
    }
}
