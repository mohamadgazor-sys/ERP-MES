using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using ERP.MES.Application.Shell;

namespace ERP.MES.Desktop;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly DispatcherTimer _clock;

    public MainWindow(ShellViewModel shell)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        InitializeComponent();
        DataContext = _shell;

        _shell.PropertyChanged += OnShellPropertyChanged;
        ShowCurrentPage();

        // The status bar shows the current Jalali date and time; refresh it every 30 seconds.
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clock.Tick += (_, _) => _shell.RefreshClock();
        _clock.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _clock.Stop();
        _shell.PropertyChanged -= OnShellPropertyChanged;
        base.OnClosed(e);
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.SelectedItem))
        {
            ShowCurrentPage();
        }
    }

    private void ShowCurrentPage()
    {
        PageHost.Content = PageFactory.Create(_shell);
    }
}
