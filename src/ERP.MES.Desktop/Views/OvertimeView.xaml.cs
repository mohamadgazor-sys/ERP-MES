using System.Windows;
using System.Windows.Controls;
using ERP.MES.Application.Persistence;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Planning;

namespace ERP.MES.Desktop.Views;

/// <summary>The overtime screen. The buttons only hand the typed values to the view model; the Application decides who may do what and checks every rule.</summary>
public partial class OvertimeView : UserControl
{
    private readonly OvertimeViewModel _viewModel;

    public OvertimeView(OvertimeViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private async void OnRange(object sender, RoutedEventArgs e) => await _viewModel.SetRangeAsync(RangeFrom.Text, RangeTo.Text);

    private async void OnApprove(object sender, RoutedEventArgs e)
    {
        if (OvertimeGrid.SelectedItem is OvertimeRow row)
        {
            await _viewModel.ApproveAsync(row, null);
        }
    }

    private async void OnReject(object sender, RoutedEventArgs e)
    {
        if (OvertimeGrid.SelectedItem is OvertimeRow row && PromptWindow.Ask(Window.GetWindow(this), "رد اضافه‌کاری", "دلیل رد (الزامی):") is { } reason)
        {
            await _viewModel.RejectAsync(row, reason);
        }
    }

    private async void OnManagementApprove(object sender, RoutedEventArgs e)
    {
        if (OvertimeGrid.SelectedItem is OvertimeRow row)
        {
            await _viewModel.ManagementApproveAsync(row, null);
        }
    }

    private async void OnManagementReject(object sender, RoutedEventArgs e)
    {
        if (OvertimeGrid.SelectedItem is OvertimeRow row && PromptWindow.Ask(Window.GetWindow(this), "رد اضافه‌کاری", "دلیل رد (الزامی):") is { } reason)
        {
            await _viewModel.ManagementRejectAsync(row, reason);
        }
    }

    private async void OnCancel(object sender, RoutedEventArgs e)
    {
        if (OvertimeGrid.SelectedItem is OvertimeRow row && PromptWindow.Ask(Window.GetWindow(this), "لغو اضافه‌کاری", "دلیل لغو (الزامی):") is { } reason)
        {
            await _viewModel.CancelAsync(row, reason);
        }
    }

    private async void OnSaveLimits(object sender, RoutedEventArgs e) => await _viewModel.SaveLimitsAsync(DayLimitBox.Text, WeekLimitBox.Text);

    private async void OnPropose(object sender, RoutedEventArgs e) =>
        await _viewModel.ProposeAsync(PersonBox.SelectedItem as Stored<Operator>, UnitBox.SelectedItem as Stored<ProductionUnit>, MachineBox.SelectedItem as Stored<Machine>, DateBox.Text, StartBox.Text, EndBox.Text, ReasonBox.Text);
}
