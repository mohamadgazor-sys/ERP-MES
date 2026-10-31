using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The read-only production queue. It only asks the view model to load; the Application decides who may see what, and nothing on this page changes data.</summary>
public partial class ProductionQueueView : UserControl
{
    private readonly ProductionQueueViewModel _viewModel;

    public ProductionQueueView(ProductionQueueViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private async void OnOpenDrawingClick(object sender, RoutedEventArgs e) => await DrawingLauncher.OpenAsync(sender);
}
