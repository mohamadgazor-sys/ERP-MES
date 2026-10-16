using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.Session;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop;

public partial class MainWindow : Window
{
    /// <summary>"Lock now" (button and Ctrl+L). It uses the same mechanism as the inactivity lock.</summary>
    public static readonly RoutedUICommand LockNowCommand = new("Lock now", nameof(LockNowCommand), typeof(MainWindow));

    private readonly ShellViewModel _shell;
    private readonly UserSession _session;
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _idleCheck;
    private bool _closeApproved;
    private bool _closePending;

    public MainWindow(ShellViewModel shell, UserSession session)
    {
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        InitializeComponent();
        DataContext = _shell;

        _shell.PropertyChanged += OnShellPropertyChanged;
        ShowCurrentPage();

        // The status bar shows the current Jalali date and time; refresh it every 30 seconds.
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clock.Tick += (_, _) => _shell.RefreshClock();
        _clock.Start();

        // Inactivity: any keyboard, mouse, touch or stylus input anywhere in the application counts as activity (even in a dialog); once a second the
        // session compares the time since the last input, measured on a monotonic clock, with the configured limit.
        LockScreen.Attach(_session);
        _session.Changed += OnSessionChanged;
        InputManager.Current.PreProcessInput += OnPreProcessInput;
        _idleCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _idleCheck.Tick += OnIdleCheck;
        _idleCheck.Start();
    }

    /// <summary>Closes the window without asking again: the session has already ended (sign-out, forced end or a clean shutdown that was confirmed).</summary>
    public void CloseWithoutPrompt()
    {
        _closeApproved = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeApproved)
        {
            return;
        }

        // Closing the window closes the application: unsaved changes are settled first, and the user may still cancel.
        e.Cancel = true;
        if (!_closePending)
        {
            _closePending = true;
            RequestCloseAsync();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        InputManager.Current.PreProcessInput -= OnPreProcessInput;
        _session.Changed -= OnSessionChanged;
        _idleCheck.Stop();
        _clock.Stop();
        _shell.PropertyChanged -= OnShellPropertyChanged;
        base.OnClosed(e);
    }

    private async void RequestCloseAsync()
    {
        try
        {
            // When this returns true the session has ended and the application handles the rest (it closes this window).
            await _session.CloseApplicationAsync();
        }
        catch (Exception)
        {
            // A failure while closing must not trap the user in the window; the session is ended without further questions.
            _session.EndForShutdown();
        }
        finally
        {
            _closePending = false;
        }
    }

    private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is MouseEventArgs or KeyboardEventArgs or TouchEventArgs or StylusEventArgs)
        {
            _session.RecordActivity();
        }
    }

    private async void OnIdleCheck(object? sender, EventArgs e)
    {
        try
        {
            await _session.TickAsync();
        }
        catch (Exception)
        {
            // The lock itself never throws (its audit entry is best-effort); nothing else can fail here.
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        var locked = _session.State == SessionState.Locked;
        ContentRoot.Visibility = locked ? Visibility.Hidden : Visibility.Visible;
        ContentRoot.IsEnabled = !locked;
        LockScreen.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        if (locked)
        {
            Dispatcher.BeginInvoke(new Action(LockScreen.FocusInput), DispatcherPriority.Input);
        }
    }

    private async void OnLockNowExecuted(object sender, ExecutedRoutedEventArgs e)
    {
        try
        {
            await _session.LockAsync(SessionLockReason.Manual);
        }
        catch (Exception)
        {
            // Locking has already taken effect; only its audit entry can fail, and that is counted by the session.
        }
    }

    private async void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _session.SignOutAsync();
        }
        catch (Exception)
        {
            MessageBox.Show("خروج از حساب ممکن نبود. دوباره تلاش کنید.", "سامانه ERP/MES", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
