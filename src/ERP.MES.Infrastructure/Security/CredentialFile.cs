using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace ERP.MES.Infrastructure.Security;

/// <summary>The credential file could not be written safely.</summary>
public sealed class CredentialFileException(string message) : InvalidOperationException(message);

/// <summary>
/// Writes the one-time passwords of the fictional demonstration accounts (Four-Day Demonstration Plan T6). The file is created OUTSIDE any Git working tree
/// (a path inside one is refused), never overwrites an existing file, and is readable only by the current user: on Windows the file is created with a protected
/// access list that grants only the current user; elsewhere with mode 0600 at creation. The passwords are shown nowhere else: not on the console, not in a log.
/// </summary>
public static class CredentialFile
{
    public static void WriteNew(string path, string databasePath, IReadOnlyList<(string Username, string Password)> credentials)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(credentials);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new CredentialFileException("The credential file needs a folder.");
        if (IsInsideGitWorkingTree(directory))
        {
            throw new CredentialFileException("The credential file must be outside the repository: '" + fullPath + "' is inside a Git working tree.");
        }

        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            throw new CredentialFileException("The credential file already exists and is never overwritten: '" + fullPath + "'.");
        }

        Directory.CreateDirectory(directory);

        var text = new StringBuilder()
            .AppendLine("ERP/MES DEMONSTRATION CREDENTIALS - fictional accounts, one-time passwords.")
            .AppendLine("Each password must be replaced by its user at the first sign-in. Keep this file private; do not copy it into the repository or share it.")
            .AppendLine("Database: " + databasePath)
            .AppendLine();
        foreach (var (username, password) in credentials)
        {
            text.Append(username).Append('\t').AppendLine(password);
        }

        using var stream = OpenNewProtected(fullPath);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text.ToString());
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>True when the folder or any parent holds a Git repository marker or the solution file.</summary>
    public static bool IsInsideGitWorkingTree(string directory)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(directory)); current is not null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git"))
                || File.Exists(Path.Combine(current.FullName, "ERP-MES.sln")))
            {
                return true;
            }
        }

        return false;
    }

    private static FileStream OpenNewProtected(string fullPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateWindowsOwnerOnly(fullPath);
        }

        return new FileStream(fullPath, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });
    }

    [SupportedOSPlatform("windows")]
    private static FileStream CreateWindowsOwnerOnly(string fullPath)
    {
        var owner = WindowsIdentity.GetCurrent().User ?? throw new CredentialFileException("The current Windows user could not be determined.");
        var security = new FileSecurity();

        // Inheritance is cut and only the current user is granted access, set atomically at creation.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
        return new FileInfo(fullPath).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, security);
    }
}
