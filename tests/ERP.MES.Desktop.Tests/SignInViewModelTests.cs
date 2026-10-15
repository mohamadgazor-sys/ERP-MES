using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Identity;
using ERP.MES.Infrastructure.Security;
using ERP.MES.Infrastructure.Sqlite;

namespace ERP.MES.Desktop.Tests;

// The sign-in window's state machine against the real services on a temporary SQLite database (no window is opened).
public sealed class SignInViewModelTests : IDisposable
{
    private const string AdminName = "admin.one";
    private const string AdminPassword = "Admin-Pass-Phrase-1";
    private const string StaffName = "staff.one";
    private const string StaffPassword = "Staff-Pass-Phrase-2";
    private const string OwnPassword = "My-Own-Pass-Phrase-3";

    private static readonly AuthenticationPolicy Policy = new() { Pbkdf2Iterations = AuthenticationPolicy.Pbkdf2IterationsFloor };

    private readonly string _path = Path.Combine(Path.GetTempPath(), "erpmes-desktop-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly List<Principal> _signedIn = [];
    private SecurityServices? _services;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        foreach (var suffix in new[] { string.Empty, "-journal", "-wal", "-shm" })
        {
            try
            {
                File.Delete(_path + suffix);
            }
            catch (IOException)
            {
                // A leftover temp file does not affect the result.
            }
        }
    }

    private async Task<SignInViewModel> CreateAsync()
    {
        await SqliteDatabase.MigrateAsync(_path, Token);
        _services = SecurityServices.Create(SqliteDatabase.CreateContextFactory(_path), Policy);
        var viewModel = new SignInViewModel(_services.Authentication, _services.Bootstrap, _services.Recovery, _services.Accounts, Policy.MinimumPasswordLength);
        viewModel.SignedIn += (_, user) => _signedIn.Add(user);
        return viewModel;
    }

    private async Task<Principal> InitialiseWithAdminAsync()
    {
        var issued = await _services!.Bootstrap.IssueTokenAsync(cancellationToken: Token);
        var result = await _services.Bootstrap.CompleteAsync(issued.Token, AdminName, "Administrator", AdminPassword, Token);
        Assert.Equal(BootstrapStatus.Succeeded, result.Status);
        return (await _services.Authentication.SignInAsync(AdminName, AdminPassword, Token)).Principal!;
    }

    [Fact]
    public async Task Before_initialisation_nothing_can_be_entered()
    {
        var viewModel = await CreateAsync();

        await viewModel.LoadAsync(Token);

        Assert.Equal(SignInMode.NotProvisioned, viewModel.Mode);
        Assert.False(string.IsNullOrWhiteSpace(viewModel.Message));
        Assert.False(viewModel.IsRecoveryAvailable);
        Assert.Empty(_signedIn);
    }

    [Fact]
    public async Task With_a_pending_token_the_first_run_screen_creates_the_administrator_and_then_offers_sign_in()
    {
        var viewModel = await CreateAsync();
        var issued = await _services!.Bootstrap.IssueTokenAsync(cancellationToken: Token);
        await viewModel.LoadAsync(Token);
        Assert.Equal(SignInMode.FirstRun, viewModel.Mode);

        await viewModel.CompleteFirstRunAsync(issued.Token, AdminName, "Administrator", AdminPassword, "a different repeat", Token);
        Assert.Equal(SignInMode.FirstRun, viewModel.Mode);
        Assert.Contains("تکرار", viewModel.Message, StringComparison.Ordinal);

        await viewModel.CompleteFirstRunAsync("a wrong token", AdminName, "Administrator", AdminPassword, AdminPassword, Token);
        Assert.Equal(SignInMode.FirstRun, viewModel.Mode);

        await viewModel.CompleteFirstRunAsync(issued.Token, AdminName, "Administrator", AdminPassword, AdminPassword, Token);
        Assert.Equal(SignInMode.SignIn, viewModel.Mode);
        Assert.Empty(_signedIn);

        await viewModel.SignInAsync(AdminName, AdminPassword, Token);
        Assert.Equal(SignInMode.Done, viewModel.Mode);
        var user = Assert.Single(_signedIn);
        Assert.Equal(RoleCodes.Admin, user.RoleCode);
    }

    [Fact]
    public async Task Once_initialised_the_first_run_screen_never_appears_again()
    {
        var viewModel = await CreateAsync();
        await InitialiseWithAdminAsync();

        await viewModel.LoadAsync(Token);

        Assert.Equal(SignInMode.SignIn, viewModel.Mode);
        Assert.NotEqual(SignInMode.FirstRun, viewModel.Mode);
    }

    [Fact]
    public async Task A_stale_first_run_screen_learns_that_the_system_was_initialised_meanwhile()
    {
        var viewModel = await CreateAsync();
        var issued = await _services!.Bootstrap.IssueTokenAsync(cancellationToken: Token);
        await viewModel.LoadAsync(Token);
        Assert.Equal(SignInMode.FirstRun, viewModel.Mode);

        // Another PC finishes first.
        await _services.Bootstrap.CompleteAsync(issued.Token, "admin.other", "Other", "Other-Pass-Phrase-9", Token);
        await viewModel.CompleteFirstRunAsync(issued.Token, AdminName, "Administrator", AdminPassword, AdminPassword, Token);

        Assert.Equal(SignInMode.SignIn, viewModel.Mode);
        Assert.Empty(_signedIn);
    }

    [Fact]
    public async Task A_failed_sign_in_gives_the_same_message_for_an_unknown_user_and_a_wrong_password()
    {
        var viewModel = await CreateAsync();
        await InitialiseWithAdminAsync();
        await viewModel.LoadAsync(Token);

        await viewModel.SignInAsync("nobody", AdminPassword, Token);
        var unknown = viewModel.Message;
        await viewModel.SignInAsync(AdminName, "wrong password here", Token);

        Assert.Equal(unknown, viewModel.Message);
        Assert.False(string.IsNullOrWhiteSpace(unknown));
        Assert.Equal(SignInMode.SignIn, viewModel.Mode);
        Assert.Empty(_signedIn);
    }

    [Fact]
    public async Task A_locked_account_shows_the_same_message_as_any_other_failure()
    {
        var viewModel = await CreateAsync();
        await InitialiseWithAdminAsync();
        await viewModel.LoadAsync(Token);

        await viewModel.SignInAsync(AdminName, "wrong 0", Token);
        var wrong = viewModel.Message;
        for (var attempt = 1; attempt < 5; attempt++)
        {
            await viewModel.SignInAsync(AdminName, "wrong " + attempt, Token);
        }

        // The fifth failure locked the account; neither that attempt nor a later one with the right password says so (Decision Log #65).
        Assert.Equal(wrong, viewModel.Message);
        await viewModel.SignInAsync(AdminName, AdminPassword, Token);
        Assert.Equal(wrong, viewModel.Message);
        Assert.Equal(SignInMode.SignIn, viewModel.Mode);
        Assert.Empty(_signedIn);
    }

    [Fact]
    public async Task A_one_time_password_leads_to_the_change_password_step_before_the_user_is_let_in()
    {
        var viewModel = await CreateAsync();
        var admin = await InitialiseWithAdminAsync();
        await _services!.Accounts.CreateUserAsync(admin, StaffName, "Staff", DepartmentCodes.Planning, RoleCodes.Staff, StaffPassword, Token);
        await viewModel.LoadAsync(Token);

        await viewModel.SignInAsync(StaffName, StaffPassword, Token);
        Assert.Equal(SignInMode.ChangePassword, viewModel.Mode);
        Assert.Empty(_signedIn);

        await viewModel.ChangePasswordAsync("not my password", OwnPassword, OwnPassword, Token);
        Assert.Equal(SignInMode.ChangePassword, viewModel.Mode);
        await viewModel.ChangePasswordAsync(StaffPassword, OwnPassword, "different repeat", Token);
        Assert.Equal(SignInMode.ChangePassword, viewModel.Mode);
        await viewModel.ChangePasswordAsync(StaffPassword, StaffPassword, StaffPassword, Token);
        Assert.Equal(SignInMode.ChangePassword, viewModel.Mode);
        await viewModel.ChangePasswordAsync(StaffPassword, "short", "short", Token);
        Assert.Equal(SignInMode.ChangePassword, viewModel.Mode);
        Assert.Empty(_signedIn);

        await viewModel.ChangePasswordAsync(StaffPassword, OwnPassword, OwnPassword, Token);

        Assert.Equal(SignInMode.Done, viewModel.Mode);
        var user = Assert.Single(_signedIn);
        Assert.Equal(StaffName, user.Username);
        Assert.False(user.MustChangePassword);
        Assert.Equal(SignInStatus.Succeeded, (await _services.Authentication.SignInAsync(StaffName, OwnPassword, Token)).Status);
    }

    [Fact]
    public async Task The_recovery_option_is_offered_only_while_a_recovery_token_is_pending()
    {
        var viewModel = await CreateAsync();
        var admin = await InitialiseWithAdminAsync();
        await viewModel.LoadAsync(Token);
        Assert.False(viewModel.IsRecoveryAvailable);

        viewModel.ShowRecovery();
        Assert.Equal(SignInMode.SignIn, viewModel.Mode);

        var issued = await _services!.Recovery.IssueTokenAsync(admin.Username, "lost access", Token);
        await viewModel.LoadAsync(Token);
        Assert.True(viewModel.IsRecoveryAvailable);

        viewModel.ShowRecovery();
        Assert.Equal(SignInMode.Recovery, viewModel.Mode);
        viewModel.BackToSignIn();
        Assert.Equal(SignInMode.SignIn, viewModel.Mode);

        viewModel.ShowRecovery();
        await viewModel.RedeemRecoveryAsync("a wrong token", OwnPassword, OwnPassword, Token);
        Assert.Equal(SignInMode.Recovery, viewModel.Mode);
        await viewModel.RedeemRecoveryAsync(issued.Token, AdminPassword, AdminPassword, Token);
        Assert.Equal(SignInMode.Recovery, viewModel.Mode);

        await viewModel.RedeemRecoveryAsync(issued.Token, OwnPassword, OwnPassword, Token);

        Assert.Equal(SignInMode.SignIn, viewModel.Mode);
        Assert.False(viewModel.IsRecoveryAvailable);
        await viewModel.SignInAsync(AdminName, OwnPassword, Token);
        Assert.Single(_signedIn);
    }

    [Fact]
    public async Task The_view_model_exposes_and_stores_no_password_or_token()
    {
        var viewModel = await CreateAsync();

        var members = typeof(SignInViewModel).GetProperties().Select(p => p.Name)
            .Concat(typeof(SignInViewModel).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public).Select(f => f.Name))
            .ToArray();

        Assert.DoesNotContain(members, name => name.Contains("password", StringComparison.OrdinalIgnoreCase) && !name.Contains("minimum", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(members, name => name.Contains("token", StringComparison.OrdinalIgnoreCase) && !name.Contains("cancellation", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(viewModel);
    }
}
