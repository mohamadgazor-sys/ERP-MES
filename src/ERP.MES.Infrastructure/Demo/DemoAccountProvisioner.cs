using System.Security.Cryptography;
using ERP.MES.Application.Security;
using ERP.MES.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Demo;

public sealed record DemoAccount(string Username, string DisplayName, string DepartmentCode, string RoleCode);

/// <summary>
/// Creates the FICTIONAL demonstration accounts (Four-Day Demonstration Plan T6). It uses only the production use cases: the first administrator comes from the real
/// bootstrap path (a token that is issued, redeemed in the same process and never leaves it), and the other accounts are created by that administrator through
/// <see cref="AccountService"/>, so the password policy, hashing, authorization and audit are exactly the production ones. Every password is random
/// (operating-system generator), different per account and per provisioning, and one-time: each account must choose its own password at the first sign-in. No
/// password is hard-coded, logged or returned anywhere except to the caller, which writes them to the protected credential file.
/// </summary>
public sealed class DemoAccountProvisioner(
    BootstrapService bootstrap, AccountService accounts, IDbContextFactory<ErpMesDbContext> contexts)
{
    public const string AdminUsername = "demo.admin";
    public const string CentralUsername = "demo.central";
    public const string QcUsername = "demo.qc";

    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
    private const int PasswordLength = 20;

    private const string Fictional = " (نمونه آزمایشی)";

    /// <summary>The fictional accounts: one administrator and one user for each department the demonstration walkthrough needs.</summary>
    public static IReadOnlyList<DemoAccount> Accounts { get; } =
    [
        new(AdminUsername, "مدیر سیستم" + Fictional, DepartmentCodes.System, RoleCodes.Admin),
        new(CentralUsername, "کاربر مرکزی" + Fictional, DepartmentCodes.Central, RoleCodes.Staff),
        new("demo.management", "مدیر مدیریت" + Fictional, DepartmentCodes.Management, RoleCodes.Manager),
        new("demo.planning", "کاربر برنامه‌ریزی" + Fictional, DepartmentCodes.Planning, RoleCodes.Staff),
        new("demo.drawing", "کاربر نقشه‌کشی" + Fictional, DepartmentCodes.Drawing, RoleCodes.Staff),
        new(QcUsername, "کاربر کنترل کیفیت" + Fictional, DepartmentCodes.Qc, RoleCodes.Staff),
        new("demo.warehouse", "کاربر انبار" + Fictional, DepartmentCodes.Warehouse, RoleCodes.Staff),
    ];

    /// <summary>A random password of <see cref="PasswordLength"/> characters from an alphabet without look-alike characters.</summary>
    public static string GeneratePassword()
    {
        var characters = new char[PasswordLength];
        for (var i = 0; i < characters.Length; i++)
        {
            characters[i] = PasswordAlphabet[RandomNumberGenerator.GetInt32(PasswordAlphabet.Length)];
        }

        return new string(characters);
    }

    /// <summary>Creates all demonstration accounts in a database that has not been initialised, and returns their one-time credentials.</summary>
    public async Task<IReadOnlyList<(string Username, string Password)>> ProvisionAsync(CancellationToken cancellationToken = default)
    {
        if (await bootstrap.GetStateAsync(cancellationToken) == SetupState.Initialized)
        {
            throw new InvalidOperationException("The system is already initialised; demonstration accounts are only created in a new database.");
        }

        var credentials = new List<(string, string)>();
        var admin = Accounts[0];

        // The real bootstrap path, with a token that exists only in this process; the password chosen here is replaced below by the one-time credential.
        var issued = await bootstrap.IssueTokenAsync(cancellationToken: cancellationToken);
        var bootstrapResult = await bootstrap.CompleteAsync(issued.Token, admin.Username, admin.DisplayName, GeneratePassword(), cancellationToken);
        if (bootstrapResult.Status != BootstrapStatus.Succeeded)
        {
            throw new InvalidOperationException("The demonstration administrator could not be created: " + bootstrapResult.Status + ".");
        }

        var adminId = await FindUserIdAsync(admin.Username, cancellationToken);
        var actor = new Principal(adminId, admin.Username, admin.DisplayName, admin.DepartmentCode, admin.RoleCode, MustChangePassword: false);

        var adminPassword = GeneratePassword();
        Require(await accounts.ResetPasswordAsync(actor, adminId, adminPassword, cancellationToken), "set the one-time password of " + admin.Username);
        credentials.Add((admin.Username, adminPassword));

        foreach (var account in Accounts.Skip(1))
        {
            var password = GeneratePassword();
            Require(
                await accounts.CreateUserAsync(actor, account.Username, account.DisplayName, account.DepartmentCode, account.RoleCode, password, cancellationToken),
                "create " + account.Username);
            credentials.Add((account.Username, password));
        }

        return credentials;
    }

    /// <summary>The user rows the sample records are attributed to. Throws when the demonstration accounts do not exist.</summary>
    public async Task<DemoSeedActors> ResolveActorsAsync(CancellationToken cancellationToken = default) =>
        new(await FindUserIdAsync(CentralUsername, cancellationToken), await FindUserIdAsync(QcUsername, cancellationToken));

    private async Task<long> FindUserIdAsync(string username, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var id = await context.Users.Where(u => u.Username == username).Select(u => (long?)u.Id).FirstOrDefaultAsync(cancellationToken);
        return id ?? throw new InvalidOperationException("The demonstration account '" + username + "' does not exist.");
    }

    private static void Require(AccountResult result, string what)
    {
        if (result.Status != AccountStatus.Succeeded)
        {
            throw new InvalidOperationException("Could not " + what + ": " + result.Status + ".");
        }
    }
}
