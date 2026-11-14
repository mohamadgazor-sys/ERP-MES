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

    private void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        ReversalNatureBox.ItemsSource ??= CompletionViewModel.Natures.Select(n => new NatureChoice(n.Nature, n.Text)).ToList();
        ReversalTargetBox.ItemsSource = CompletionGrid.SelectedItem is CompletionRow row ? CompletionViewModel.TargetsOf(row.Item) : null;
        ReversalTargetBox.SelectedIndex = ReversalTargetBox.Items.Count > 0 ? 0 : -1;
        ReversalNatureBox.SelectedIndex = 0;
    }

    private async void OnProposeReversal(object sender, RoutedEventArgs e)
    {
        if (CompletionGrid.SelectedItem is CompletionRow row)
        {
            var nature = (ReversalNatureBox.SelectedItem as NatureChoice)?.Nature ?? ERP.MES.Domain.Warehouse.ReversalNature.RecordedInError;
            await _viewModel.ProposeReversalAsync(row, ReversalTargetBox.SelectedItem as ReversalTarget, nature, ReversalReasonBox.Text);
        }
    }

    private async void OnApproveReversal(object sender, RoutedEventArgs e)
    {
        if (ReversalGrid.SelectedItem is ReversalRow row)
        {
            await _viewModel.DecideReversalAsync(row, approve: true, DecisionReasonBox.Text);
        }
    }

    private async void OnRejectReversal(object sender, RoutedEventArgs e)
    {
        if (ReversalGrid.SelectedItem is ReversalRow row)
        {
            await _viewModel.DecideReversalAsync(row, approve: false, DecisionReasonBox.Text);
        }
    }

    private sealed record NatureChoice(ERP.MES.Domain.Warehouse.ReversalNature Nature, string Text);
}
