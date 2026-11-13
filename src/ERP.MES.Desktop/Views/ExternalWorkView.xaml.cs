using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The external-work screen. The buttons only hand the typed values to the view model; the Application decides who may do what and checks every rule.</summary>
public partial class ExternalWorkView : UserControl
{
    private readonly ExternalWorkViewModel _viewModel;

    public ExternalWorkView(ExternalWorkViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private async void OnReload(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private async void OnDispatch(object sender, RoutedEventArgs e) => await _viewModel.DispatchAsync(DispatchQuantityBox.Text, PieceBox.Text, DispatchNoteBox.Text);

    private async void OnReturn(object sender, RoutedEventArgs e) => await _viewModel.ReturnAsync(DispatchBox.SelectedItem as OpenDispatchChoice, ReturnQuantityBox.Text, ReturnNoteBox.Text);

    private async void OnMissing(object sender, RoutedEventArgs e) => await _viewModel.MarkMissingAsync(DispatchBox.SelectedItem as OpenDispatchChoice, ReturnQuantityBox.Text, ReturnNoteBox.Text);

    private async void OnCancel(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is ExternalShipmentRow row && PromptWindow.Ask(Window.GetWindow(this), "لغو ثبت ارسال یا برگشت", "دلیل لغو (الزامی):") is { } reason)
        {
            await _viewModel.CancelAsync(row, reason);
        }
    }
}
