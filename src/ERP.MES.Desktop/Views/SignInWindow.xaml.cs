using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>
/// The sign-in window: it shows the panel for the current <see cref="SignInMode"/> and hands what was typed to the view model. Password boxes are cleared after every
/// action, so a password does not stay in the window. All rules live in the Application use cases.
/// </summary>
public partial class SignInWindow : Window
{
    private readonly SignInViewModel _viewModel;

    public SignInWindow(SignInViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdatePanels();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnClosed(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SignInViewModel.Mode) or nameof(SignInViewModel.IsRecoveryAvailable))
        {
            UpdatePanels();
        }
    }

    private void UpdatePanels()
    {
        var mode = _viewModel.Mode;
        NotProvisionedPanel.Visibility = Show(mode == SignInMode.NotProvisioned);
        FirstRunPanel.Visibility = Show(mode == SignInMode.FirstRun);
        SignInPanel.Visibility = Show(mode == SignInMode.SignIn);
        RecoveryPanel.Visibility = Show(mode == SignInMode.Recovery);
        ChangePasswordPanel.Visibility = Show(mode == SignInMode.ChangePassword);
        UseRecoveryButton.Visibility = Show(_viewModel.IsRecoveryAvailable);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private async void OnSignInClick(object sender, RoutedEventArgs e)
    {
        var password = SignInPassword.Password;
        SignInPassword.Clear();
        await RunAsync(() => _viewModel.SignInAsync(SignInUsername.Text, password));
    }

    private async void OnFirstRunClick(object sender, RoutedEventArgs e)
    {
        var password = FirstRunPassword.Password;
        var repeat = FirstRunRepeat.Password;
        FirstRunPassword.Clear();
        FirstRunRepeat.Clear();
        await RunAsync(() => _viewModel.CompleteFirstRunAsync(FirstRunToken.Text, FirstRunUsername.Text, FirstRunDisplayName.Text, password, repeat));
        FirstRunToken.Clear();
    }

    private void OnUseRecoveryClick(object sender, RoutedEventArgs e) => _viewModel.ShowRecovery();

    private void OnBackClick(object sender, RoutedEventArgs e) => _viewModel.BackToSignIn();

    private async void OnRecoveryClick(object sender, RoutedEventArgs e)
    {
        var password = RecoveryPassword.Password;
        var repeat = RecoveryRepeat.Password;
        RecoveryPassword.Clear();
        RecoveryRepeat.Clear();
        await RunAsync(() => _viewModel.RedeemRecoveryAsync(RecoveryToken.Text, password, repeat));
        RecoveryToken.Clear();
    }

    private async void OnChangePasswordClick(object sender, RoutedEventArgs e)
    {
        var current = CurrentPassword.Password;
        var password = NewPassword.Password;
        var repeat = NewPasswordRepeat.Password;
        CurrentPassword.Clear();
        NewPassword.Clear();
        NewPasswordRepeat.Clear();
        await RunAsync(() => _viewModel.ChangePasswordAsync(current, password, repeat));
    }

    // Event handlers are async void, so nothing may escape them: an unexpected failure becomes a plain message.
    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
            _viewModel.ReportUnexpectedFailure();
        }
    }
}
