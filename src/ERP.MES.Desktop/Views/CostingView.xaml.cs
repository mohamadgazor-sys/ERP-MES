using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The cost and price screen. The buttons only hand the typed values to the view model; the Application decides who may do what and computes every figure.</summary>
public partial class CostingView : UserControl
{
    private readonly CostingViewModel _viewModel;

    public CostingView(CostingViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private async void OnSelected(object sender, SelectionChangedEventArgs e)
    {
        if (CostGrid.SelectedItem is CostingRow row)
        {
            await _viewModel.ShowDetailAsync(row);
        }
    }

    private async void OnCalculate(object sender, RoutedEventArgs e)
    {
        if (CostGrid.SelectedItem is CostingRow row)
        {
            await _viewModel.CalculateAsync(row);
        }
    }

    private async void OnSubmit(object sender, RoutedEventArgs e)
    {
        if (CostGrid.SelectedItem is CostingRow row)
        {
            await _viewModel.SubmitAsync(row);
        }
    }

    private async void OnApprove(object sender, RoutedEventArgs e)
    {
        if (CostGrid.SelectedItem is CostingRow row)
        {
            await _viewModel.ApproveAsync(row, PriceBox.Text, ReasonBox.Text, ReferenceBox.Text);
        }
    }
}
