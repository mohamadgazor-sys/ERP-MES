using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The Planning resource screen (units, machines, hourly rates). It hands what was typed to the view model; every rule and permission check is in the Application.</summary>
public partial class PlanningMasterView : UserControl
{
    private readonly PlanningMasterViewModel _viewModel;

    public PlanningMasterView(PlanningMasterViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnAddUnitClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.AddUnitAsync(UnitCodeBox.Text, UnitNameBox.Text));
        if (saved)
        {
            UnitCodeBox.Clear();
            UnitNameBox.Clear();
        }
    }

    private async void OnRenameUnitClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.RenameSelectedUnitAsync(UnitNameBox.Text));
        if (saved)
        {
            UnitNameBox.Clear();
        }
    }

    private async void OnDeactivateUnitClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedUnitActiveAsync(false));

    private async void OnActivateUnitClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedUnitActiveAsync(true));

    private async void OnAddMachineClick(object sender, RoutedEventArgs e)
    {
        var unit = (MachineUnitBox.SelectedItem as Choice<long>)?.Value ?? 0;
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.AddMachineAsync(MachineCodeBox.Text, MachineNameBox.Text, unit));
        if (saved)
        {
            MachineCodeBox.Clear();
            MachineNameBox.Clear();
        }
    }

    private async void OnRenameMachineClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.RenameSelectedMachineAsync(MachineNameBox.Text));
        if (saved)
        {
            MachineNameBox.Clear();
        }
    }

    private async void OnDeactivateMachineClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedMachineActiveAsync(false));

    private async void OnActivateMachineClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedMachineActiveAsync(true));

    private async void OnAddRateClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.AddRateForSelectedMachineAsync(RateBox.Text, RateDateBox.Text, RateReasonBox.Text));
        if (saved)
        {
            RateBox.Clear();
            RateDateBox.Clear();
            RateReasonBox.Clear();
        }
    }

    private static async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
            MessageBox.Show(Messages.Unexpected, "سامانه ERP/MES", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
