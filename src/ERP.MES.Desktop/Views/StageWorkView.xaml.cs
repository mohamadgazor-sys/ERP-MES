using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The demonstration stage-work screen. The buttons only hand the action to the view model; the Application decides who may do what, checks the row versions and enforces the QC gate.</summary>
public partial class StageWorkView : UserControl
{
    private readonly StageWorkViewModel _viewModel;

    public StageWorkView(StageWorkViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private async void OnReleaseClick(object sender, RoutedEventArgs e) => await _viewModel.ReleaseAsync();

    private async void OnStartClick(object sender, RoutedEventArgs e) => await _viewModel.StartAsync();

    private async void OnSubmitClick(object sender, RoutedEventArgs e) => await _viewModel.SubmitForQcAsync();

    private async void OnApproveClick(object sender, RoutedEventArgs e) => await _viewModel.ApproveAsync();
}
