using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The Warehouse completion screen. The buttons only hand the typed values to the view model; the Application decides who may do what and enforces the order and the duplicates.</summary>
public partial class CompletionView : UserControl
{
    private readonly CompletionViewModel _viewModel;

    public CompletionView(CompletionViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private async void OnReceive(object sender, RoutedEventArgs e)
    {
        if (CompletionGrid.SelectedItem is CompletionRow row)
        {
            await _viewModel.ReceiveAsync(row, ReceiptNoteBox.Text);
        }
    }

    private async void OnFinished(object sender, RoutedEventArgs e)
    {
        if (CompletionGrid.SelectedItem is CompletionRow row)
        {
            await _viewModel.MarkFinishedAsync(row);
        }
    }

    private async void OnDispatch(object sender, RoutedEventArgs e)
    {
        if (CompletionGrid.SelectedItem is CompletionRow row)
        {
            await _viewModel.DispatchAsync(row, DispatchDateBox.Text, DispatchReferenceBox.Text, DispatchNoteBox.Text);
        }
    }
}
