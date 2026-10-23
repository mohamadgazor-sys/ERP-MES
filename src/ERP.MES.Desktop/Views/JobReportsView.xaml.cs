using System.Windows;
using System.Windows.Controls;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Production;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Desktop.Views;

/// <summary>The daily job report screen. The buttons only hand the typed values to the view model; the Application decides who may do what and checks every rule.</summary>
public partial class JobReportsView : UserControl
{
    private readonly JobReportsViewModel _viewModel;

    public JobReportsView(JobReportsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private async void OnRange(object sender, RoutedEventArgs e) => await _viewModel.SetRangeAsync(RangeFrom.Text, RangeTo.Text);

    private async void OnUnitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UnitBox.SelectedItem is UnitInfo unit)
        {
            await _viewModel.SelectUnitAsync(unit.UnitId);
        }
    }

    private async void OnApprove(object sender, RoutedEventArgs e)
    {
        if (ReportGrid.SelectedItem is JobReportRow row)
        {
            await _viewModel.ApproveAsync(row);
        }
    }

    private async void OnReturn(object sender, RoutedEventArgs e)
    {
        if (ReportGrid.SelectedItem is JobReportRow row && PromptWindow.Ask(Window.GetWindow(this), "بازگرداندن گزارش", "دلیل بازگرداندن (الزامی):") is { } reason)
        {
            await _viewModel.ReturnAsync(row, reason);
        }
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        if (ReportGrid.SelectedItem is JobReportRow row)
        {
            _viewModel.Edit(row);
        }
    }

    private void OnAddLine(object sender, RoutedEventArgs e) =>
        _viewModel.AddLine(StageBox.SelectedItem as StageChoice, MachineBox.SelectedItem as Stored<Machine>, ReportDate.Text, StartBox.Text, EndBox.Text, DowntimeBox.Text, DowntimeReasonBox.Text, UnitsBox.Text, NoteBox.Text);

    private void OnRemoveLine(object sender, RoutedEventArgs e)
    {
        if (DraftGrid.SelectedItem is DraftLineRow row)
        {
            _viewModel.RemoveLine(row);
        }
    }

    private async void OnSaveDraft(object sender, RoutedEventArgs e) => await _viewModel.SaveDraftAsync(ReportDate.Text, ShiftBox.SelectedItem as Stored<ShiftTemplate>);

    private async void OnSubmit(object sender, RoutedEventArgs e) => await _viewModel.SubmitAsync();
}
