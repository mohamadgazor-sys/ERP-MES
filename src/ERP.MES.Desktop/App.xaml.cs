using System.IO;
using System.Windows;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.Session;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Desktop.Views;
using ERP.MES.Domain.Calendar;
using ERP.MES.Infrastructure.Security;
using ERP.MES.Infrastructure.Sqlite;

namespace ERP.MES.Desktop;

// "Application" would resolve to the namespace ERP.MES.Application here, so the WPF base class is fully qualified.
public partial class App : System.Windows.Application
{
    // Demonstration build: the site time zone is fixed to Iran Standard Time. Making it a configured site setting
    // (owner decision Q2) is part of the JalaliDateService follow-up, not of this change.
    private const string SiteTimeZoneId = "Iran Standard Time";

    /// <summary>Environment variable that names the database file. Without it the demonstration database in the user's local application data is used.</summary>
    public const string DatabaseVariable = "ERPMES_DATABASE";

    private Views.SignInWindow? _signInWindow;
    private SecurityServices? _services;
    private Microsoft.EntityFrameworkCore.IDbContextFactory<ERP.MES.Infrastructure.Persistence.ErpMesDbContext>? _contexts;
    private AuthenticationPolicy? _policy;
    private UserSession? _session;
    private MainWindow? _mainWindow;
    private WindowsSessionMonitor? _windowsMonitor;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // Settings first: an authentication setting below its safety floor stops the application (Milestone 1 plan 6.1).
            var policy = AuthenticationPolicyFile.Load(
                Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json"));

            var databasePath = Environment.GetEnvironmentVariable(DatabaseVariable) is { Length: > 0 } configured
                ? configured
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ERP-MES", "erpmes.demo.db");

            // The application never creates or resets a database. IT creates it (Provisioning: migrate, or demo-init for the fictional demonstration database).
            if (!File.Exists(databasePath))
            {
                Fail("پایگاه داده پیدا نشد:\n" + databasePath + "\n\nابتدا ابزار راه‌اندازی (Provisioning) را اجرا کنید.");
                return;
            }

            // Normal start-up path for an existing database (Decision Log #69): apply pending migrations, nothing else. Never creates, seeds or resets a database.
            await SqliteDatabase.MigrateExistingAsync(databasePath);

            _policy = policy;
            _contexts = SqliteDatabase.CreateContextFactory(databasePath);
            _services = SecurityServices.Create(_contexts, policy);

            // Windows logoff or shutdown: the session ends without questions and the clean shutdown is audited best-effort (Decision Log #74).
            SessionEnding += (_, _) => _session?.EndForShutdown();

            await ShowSignInAsync(notice: null);
        }
        catch (ActorReferencesInvalidException exception)
        {
            // A database from before sign-in existed: the message says what is wrong and how to replace a demonstration database.
            Fail("پایگاه داده قدیمی است و ارتقا نیافت؛ هیچ تغییری داده نشد:\n" + exception.Message);
        }
        catch (RequestWorkTypeInvalidException exception)
        {
            // A database whose requests mix MAKE and REPAIR items (or have no items): one type per request cannot be derived, so nothing was upgraded.
            Fail("پایگاه داده قدیمی است و ارتقا نیافت؛ نوع کار (ساخت یا تعمیر) برخی درخواست‌ها مشخص نیست و هیچ تغییری داده نشد:\n" + exception.Message);
        }
        catch (InvalidAuthenticationPolicyException exception)
        {
            Fail("تنظیمات احراز هویت معتبر نیست؛ برنامه اجرا نمی‌شود:\n" + string.Join("\n", exception.Problems));
        }
        catch (Exception)
        {
            Fail("اجرای برنامه ممکن نبود. پایگاه داده یا تنظیمات را بررسی کنید.");
        }
    }

    /// <summary>Shows a new sign-in window with a new view model, so nothing of a previous session is carried over.</summary>
    private async Task ShowSignInAsync(string? notice)
    {
        var viewModel = new SignInViewModel(
            _services!.Authentication, _services.Bootstrap, _services.Recovery, _services.Accounts, _policy!.MinimumPasswordLength);
        viewModel.SignedIn += OnSignedIn;

        _signInWindow = new Views.SignInWindow(viewModel);
        _signInWindow.Show();
        await viewModel.LoadAsync();
        if (notice is not null)
        {
            viewModel.ShowNotice(notice);
        }
    }

    private async void ShowSignIn(string? notice)
    {
        try
        {
            await ShowSignInAsync(notice);
        }
        catch (Exception)
        {
            Fail("اجرای برنامه ممکن نبود. پایگاه داده یا تنظیمات را بررسی کنید.");
        }
    }

    private void OnSignedIn(object? sender, Principal user)
    {
        var siteZone = TimeZoneInfo.FindSystemTimeZoneById(SiteTimeZoneId);
        var dates = new JalaliDateService(DigitStyle.Persian, siteZone);
        var shell = new ShellViewModel(dates, () => DateTime.UtcNow) { UserText = "کاربر: " + user.DisplayName };

        // The session lives in memory only: no token, nothing stored, nothing that survives closing the application (Decision Log #74).
        var prompts = new MessageBoxPrompts();
        var session = new UserSession(user, _services!.Sessions, _services.Accounts, new StopwatchClock(), _policy!, new UnsavedWorkRegistry(), prompts, prompts);
        session.Ended += OnSessionEnded;
        _session = session;
        _windowsMonitor = new WindowsSessionMonitor(Dispatcher, reason => OnWindowsSessionEvent(session, reason));

        // The functional pages of this session. The principal is only a UI snapshot (refreshed at unlock); every Application command checks the user again.
        var workflow = WorkflowServices.Create(_contexts!, _services.Authorization, dates);
        Principal CurrentUser() => session.Principal;
        var pages = new WorkflowPages(
            new CustomersViewModel(workflow.Customers, workflow.Requests, dates, CurrentUser),
            new RequestsViewModel(workflow.Requests, workflow.Documents, workflow.Lifecycle, workflow.Customers, dates, DigitStyle.Persian, () => DateTime.UtcNow, CurrentUser),
            new ProductionQueueViewModel(workflow.Queue, dates, DigitStyle.Persian, () => DateTime.UtcNow, CurrentUser),
            new StageWorkViewModel(workflow.StageWork, DigitStyle.Persian, CurrentUser),
            new QcMasterViewModel(workflow.QcMaster, CurrentUser),
            new PlanningMasterViewModel(workflow.PlanningMaster, dates, DigitStyle.Persian, CurrentUser),
            new OperatorsViewModel(workflow.Operators, workflow.PlanningMaster, dates, CurrentUser),
            new MaterialsViewModel(workflow.Materials, dates, DigitStyle.Persian, CurrentUser),
            new ContractsViewModel(workflow.Contracts, dates, CurrentUser));

        _mainWindow = new MainWindow(shell, session, pages);
        _mainWindow.Show();
        _signInWindow?.Close();
        _signInWindow = null;
    }

    private static async void OnWindowsSessionEvent(UserSession session, Microsoft.Win32.SessionSwitchReason reason)
    {
        try
        {
            await session.OnWindowsSessionEventAsync(reason);
        }
        catch (Exception)
        {
            // Locking has already taken effect; only its audit entry can fail, and the session counts that.
        }
    }

    private void OnSessionEnded(object? sender, SessionEndReason reason)
    {
        _windowsMonitor?.Dispose();
        _windowsMonitor = null;
        var window = _mainWindow;
        _mainWindow = null;
        _session = null;

        if (reason == SessionEndReason.ApplicationClosed)
        {
            window?.CloseWithoutPrompt();
            Shutdown();
            return;
        }

        // The new sign-in window is shown before the old main window closes, so the application never has no window.
        ShowSignIn(reason == SessionEndReason.AccountUnavailable ? "نشست پایان یافت. دوباره وارد شوید." : null);
        window?.CloseWithoutPrompt();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Safety net for an exit that did not go through a normal close: end the session and audit it best-effort.
        _session?.EndForShutdown();
        _windowsMonitor?.Dispose();
        base.OnExit(e);
    }

    private void Fail(string message)
    {
        MessageBox.Show(message, "سامانه ERP/MES", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        Shutdown(1);
    }
}
