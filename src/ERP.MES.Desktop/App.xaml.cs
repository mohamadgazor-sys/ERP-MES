using System.Windows;
using ERP.MES.Application.Shell;
using ERP.MES.Domain.Calendar;

namespace ERP.MES.Desktop;

// "Application" would resolve to the namespace ERP.MES.Application here, so the WPF base class is fully qualified.
public partial class App : System.Windows.Application
{
    // Demonstration build: the site time zone is fixed to Iran Standard Time. Making it a configured site setting
    // (owner decision Q2) is part of the JalaliDateService follow-up, not of this change.
    private const string SiteTimeZoneId = "Iran Standard Time";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var siteZone = TimeZoneInfo.FindSystemTimeZoneById(SiteTimeZoneId);
        var dates = new JalaliDateService(DigitStyle.Persian, siteZone);
        var shell = new ShellViewModel(dates, () => DateTime.UtcNow);

        var window = new MainWindow(shell);
        window.Show();
    }
}
