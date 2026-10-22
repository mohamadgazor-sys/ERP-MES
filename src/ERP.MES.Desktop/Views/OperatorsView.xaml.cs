using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The operators screen (operators, machine assignments, qualifications). It hands what was typed to the view model; every rule and permission check is in the Application.</summary>
public partial class OperatorsView : UserControl
{
    private readonly OperatorsViewModel _viewModel;

    public OperatorsView(OperatorsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnRegisterClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.RegisterAsync(OperatorCodeBox.Text, OperatorNameBox.Text));
        if (saved)
        {
            OperatorCodeBox.Clear();
            OperatorNameBox.Clear();
        }
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.RenameSelectedAsync(OperatorNameBox.Text));
        if (saved)
        {
            OperatorNameBox.Clear();
        }
    }

    private async void OnDeactivateClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedActiveAsync(false));

    private async void OnActivateClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedActiveAsync(true));

    private async void OnAssignClick(object sender, RoutedEventArgs e)
    {
        var worker = (AssignOperatorBox.SelectedItem as Choice<long>)?.Value ?? 0;
        var machine = (AssignMachineBox.SelectedItem as Choice<long>)?.Value ?? 0;
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.AssignAsync(worker, machine, AssignDateBox.Text, AssignPrimaryBox.IsChecked == true, AssignReasonBox.Text));
        if (saved)
        {
            AssignDateBox.Clear();
            AssignReasonBox.Clear();
        }
    }

    private async void OnCloseAssignmentClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.CloseSelectedAssignmentAsync(CloseDateBox.Text));
        if (saved)
        {
            CloseDateBox.Clear();
        }
    }

    private async void OnRecordQualificationClick(object sender, RoutedEventArgs e)
    {
        var worker = (QualOperatorBox.SelectedItem as Choice<long>)?.Value ?? 0;
        var machine = (QualMachineBox.SelectedItem as Choice<long>)?.Value ?? 0;
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.RecordQualificationAsync(worker, machine, QualDateBox.Text, QualNoteBox.Text));
        if (saved)
        {
            QualDateBox.Clear();
            QualNoteBox.Clear();
        }
    }

    private async void OnEndQualificationClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.EndSelectedQualificationAsync(EndQualDateBox.Text));
        if (saved)
        {
            EndQualDateBox.Clear();
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
