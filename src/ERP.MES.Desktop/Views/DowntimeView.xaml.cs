using System.Windows;
using System.Windows.Controls;
using ERP.MES.Application.Scheduling;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The machine-downtime screen. The buttons only hand the typed values to the view model; the Application decides who may do what and checks every rule.</summary>
public partial class DowntimeView : UserControl
{
    private readonly DowntimeViewModel _viewModel;

    public DowntimeView(DowntimeViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private async void OnRange(object sender, RoutedEventArgs e) => await _viewModel.SetRangeAsync(RangeFrom.Text, RangeTo.Text);

    private async void OnReport(object sender, RoutedEventArgs e) =>
        await _viewModel.ReportAsync(MachineBox.SelectedItem as DowntimeMachine, ReasonBox.SelectedItem as DowntimeReasonOption, StartDateBox.Text, StartTimeBox.Text, EndDateBox.Text, EndTimeBox.Text, NoteBox.Text);

    private async void OnAccept(object sender, RoutedEventArgs e)
    {
        if (DowntimeGrid.SelectedItem is DowntimeRow row)
        {
            await _viewModel.AcceptAsync(row, null);
        }
    }

    private async void OnReject(object sender, RoutedEventArgs e)
    {
        if (DowntimeGrid.SelectedItem is DowntimeRow row && PromptWindow.Ask(Window.GetWindow(this), "رد توقف ماشین", "دلیل رد (الزامی):") is { } reason)
        {
            await _viewModel.RejectAsync(row, reason);
        }
    }

    private async void OnChangeEnd(object sender, RoutedEventArgs e)
    {
        if (DowntimeGrid.SelectedItem is DowntimeRow row && PromptWindow.Ask(Window.GetWindow(this), "اصلاح پایان توقف", "دلیل اصلاح (الزامی):") is { } reason)
        {
            await _viewModel.ChangeEndAsync(row, EndDateBox.Text, EndTimeBox.Text, reason);
        }
    }

    private async void OnCancel(object sender, RoutedEventArgs e)
    {
        if (DowntimeGrid.SelectedItem is DowntimeRow row && PromptWindow.Ask(Window.GetWindow(this), "لغو توقف ماشین", "دلیل لغو (الزامی):") is { } reason)
        {
            await _viewModel.CancelAsync(row, reason);
        }
    }
}
