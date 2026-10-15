using System.Windows;
using ERP.MES.Application.Security;
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

            // Normal start-up path for an existing database: apply pending migrations, nothing else.
            await SqliteDatabase.MigrateAsync(databasePath);

            var services = SecurityServices.Create(SqliteDatabase.CreateContextFactory(databasePath), policy);
            var viewModel = new SignInViewModel(services.Authentication, services.Bootstrap, services.Recovery, services.Accounts, policy.MinimumPasswordLength);
            viewModel.SignedIn += OnSignedIn;

            _signInWindow = new Views.SignInWindow(viewModel);
            _signInWindow.Show();
            await viewModel.LoadAsync();
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

    private void OnSignedIn(object? sender, Principal user)
    {
        var siteZone = TimeZoneInfo.FindSystemTimeZoneById(SiteTimeZoneId);
        var dates = new JalaliDateService(DigitStyle.Persian, siteZone);
        var shell = new ShellViewModel(dates, () => DateTime.UtcNow) { UserText = "کاربر: " + user.DisplayName };

        var window = new MainWindow(shell);
        window.Show();
        _signInWindow?.Close();
        _signInWindow = null;
    }

    private void Fail(string message)
    {
        MessageBox.Show(message, "سامانه ERP/MES", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        Shutdown(1);
    }
}
