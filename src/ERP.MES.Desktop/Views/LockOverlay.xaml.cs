using System.Windows;
using System.Windows.Controls;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.Session;

namespace ERP.MES.Desktop.Views;

/// <summary>
/// The lock screen. It hands what was typed to the <see cref="UserSession"/> and clears its password boxes after every action; all rules live in the
/// Application use cases and the session. It shows the signed-in user's name and a generic message only.
/// </summary>
public partial class LockOverlay : UserControl
{
    private UserSession? _session;

    public LockOverlay()
    {
        InitializeComponent();
    }

    public void Attach(UserSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        session.Changed += (_, _) => Refresh();
        Refresh();
    }

    private void Refresh()
    {
        if (_session is null)
        {
            return;
        }

        UserText.Text = _session.Principal.DisplayName;
        ReasonText.Text = _session.LockReason switch
        {
            SessionLockReason.Inactivity => "قفل شد: مدتی از برنامه استفاده نشد.",
            SessionLockReason.Manual => "قفل شد: درخواست کاربر.",
            SessionLockReason.WindowsLock => "قفل شد: ویندوز قفل شد.",
            SessionLockReason.UserSessionSwitch => "قفل شد: نشست کاربری ویندوز عوض شد.",
            _ => string.Empty,
        };
        MessageText.Text = _session.Message;
        UnlockPanel.Visibility = _session.PasswordChangeRequired ? Visibility.Collapsed : Visibility.Visible;
        ChangePanel.Visibility = _session.PasswordChangeRequired ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Puts the cursor in the password box when the lock screen appears.</summary>
    public void FocusInput()
    {
        if (_session is { PasswordChangeRequired: true })
        {
            CurrentPassword.Focus();
        }
        else
        {
            UnlockPassword.Focus();
        }
    }

    private async void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        var password = UnlockPassword.Password;
        UnlockPassword.Clear();
        await RunAsync(() => _session!.UnlockAsync(password));
        FocusInput();
    }

    private async void OnChangePasswordClick(object sender, RoutedEventArgs e)
    {
        var current = CurrentPassword.Password;
        var password = NewPassword.Password;
        var repeat = NewPasswordRepeat.Password;
        CurrentPassword.Clear();
        NewPassword.Clear();
        NewPasswordRepeat.Clear();
        await RunAsync(() => _session!.ChangePasswordAsync(current, password, repeat));
        FocusInput();
    }

    private async void OnSwitchUserClick(object sender, RoutedEventArgs e) => await RunAsync(() => _session!.SwitchUserAsync());

    // Event handlers are async void, so nothing may escape them: an unexpected failure becomes a plain message.
    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
            MessageText.Text = "خطای غیرمنتظره رخ داد. دوباره تلاش کنید.";
        }
    }
}
