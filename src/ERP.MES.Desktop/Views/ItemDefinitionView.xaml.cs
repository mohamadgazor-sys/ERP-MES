using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Views;

/// <summary>The item-definition screen. It hands what was typed to the view model; the gates, the specification rules and the permission are checked in the Application.</summary>
public partial class ItemDefinitionView : UserControl
{
    private readonly ItemDefinitionViewModel _viewModel;

    public ItemDefinitionView(ItemDefinitionViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await SafeAsync(async () =>
        {
            await _viewModel.LoadAsync();
            PriorityBox.SelectedItem ??= _viewModel.Priorities.FirstOrDefault(p => p.Value == Priority.Normal);
        });
    }

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnRequestSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.SelectedRequest is { } row)
        {
            await SafeAsync(() => _viewModel.SelectRequestAsync(row.RequestId));
        }
    }

    private void OnUnitChanged(object sender, SelectionChangedEventArgs e) =>
        MachineBox.ItemsSource = UnitBox.SelectedItem is Choice<long> unit ? _viewModel.MachinesOf(unit.Value) : null;

    private async void OnDefineItemClick(object sender, RoutedEventArgs e)
    {
        var priority = (PriorityBox.SelectedItem as Choice<Priority>)?.Value ?? Priority.Normal;
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.DefineItemAsync(
            ItemNoBox.Text, PartNameBox.Text, QuantityBox.Text, priority, (TypeBox.SelectedItem as Choice<long>)?.Value, (MaterialBox.SelectedItem as Choice<long>)?.Value,
            WeightBox.Text, LengthBox.Text, DiameterBox.Text, default, (ParentBox.SelectedItem as Choice<long>)?.Value, AssemblyBox.IsChecked == true, DeliveryDateBox.Text, WarehouseCodeBox.Text));
        if (saved)
        {
            foreach (var box in new[] { ItemNoBox, PartNameBox, QuantityBox, WeightBox, LengthBox, DiameterBox, DeliveryDateBox, WarehouseCodeBox })
            {
                box.Clear();
            }

            AssemblyBox.IsChecked = false;
        }
    }

    private async void OnMakeAssemblyClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedAssemblyAsync(true));

    private async void OnMoveClick(object sender, RoutedEventArgs e) =>
        await SafeAsync(() => _viewModel.MoveSelectedAsync((ParentBox.SelectedItem as Choice<long>)?.Value));

    private async void OnDefineStageClick(object sender, RoutedEventArgs e)
    {
        var unit = (UnitBox.SelectedItem as Choice<long>)?.Value ?? 0;
        var machine = (MachineBox.SelectedItem as Choice<long>)?.Value;
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.DefineStageAsync(StageTitleBox.Text, MinutesBox.Text, unit, machine));
        if (saved)
        {
            StageTitleBox.Clear();
            MinutesBox.Clear();
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
