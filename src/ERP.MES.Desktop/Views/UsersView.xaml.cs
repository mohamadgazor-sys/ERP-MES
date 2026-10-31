using System.Windows;
using System.Windows.Controls;
using ERP.MES.Application.Production;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The user and account administration screen. It hands what was typed to the view model; the Application checks the Admin role and every rule (Decision Log #122).</summary>
public partial class UsersView : UserControl
{
    private readonly UsersViewModel _viewModel;

    public UsersView(UsersViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.CreateAsync(
            NewUsernameBox.Text, NewDisplayNameBox.Text, (NewDepartmentBox.SelectedItem as Choice<string>)?.Value, (NewRoleBox.SelectedItem as Choice<string>)?.Value, NewPasswordBox.Password));
        if (saved)
        {
            NewUsernameBox.Clear();
            NewDisplayNameBox.Clear();
            NewPasswordBox.Clear();
        }
    }

    private async void OnChangeDepartmentClick(object sender, RoutedEventArgs e) =>
        await SafeAsync(() => _viewModel.ChangeDepartmentAsync((ChangeDepartmentBox.SelectedItem as Choice<string>)?.Value));

    private async void OnChangeRoleClick(object sender, RoutedEventArgs e) =>
        await SafeAsync(() => _viewModel.ChangeRoleAsync((ChangeRoleBox.SelectedItem as Choice<string>)?.Value));

    private async void OnResetPasswordClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.ResetPasswordAsync(ResetPasswordBox.Password));
        if (saved)
        {
            ResetPasswordBox.Clear();
        }
    }

    private async void OnUnlockClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.UnlockAsync());

    private async void OnDeactivateClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetActiveAsync(false));

    private async void OnActivateClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetActiveAsync(true));

    private async void OnAssignUnitClick(object sender, RoutedEventArgs e) =>
        await SafeAsync(() => _viewModel.AssignWorkingUnitAsync((WorkingUnitBox.SelectedItem as UnitInfo)?.UnitId ?? 0));

    private async void OnRevokeUnitClick(object sender, RoutedEventArgs e) =>
        await SafeAsync(() => _viewModel.RevokeWorkingUnitAsync((WorkingUnitBox.SelectedItem as UnitInfo)?.UnitId ?? 0));

    private static async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
            // The view model reports its own failures; a failure while the window closes is ignored.
        }
    }
}
