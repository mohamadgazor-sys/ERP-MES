using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Views;

/// <summary>The QC inspection screen. It relays the typed counts and notes; the Application decides who may do what and enforces the quantity rules.</summary>
public partial class QcInspectionView : UserControl
{
    private readonly QcInspectionViewModel _viewModel;
    private bool _loading;

    public QcInspectionView(QcInspectionViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        _loading = true;
        try
        {
            await _viewModel.LoadAsync();
            Grid.SelectedItem = _viewModel.SelectedRow;
        }
        finally
        {
            _loading = false;
        }
    }

    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
        {
            await _viewModel.SelectAsync(Grid.SelectedItem as InspectionRow);
        }
    }

    private async void OnInspectClick(object sender, RoutedEventArgs e) => await _viewModel.InspectAsync(Accepted.Text, Rework.Text, Scrap.Text, Reason.Text);

    private async void OnAcceptShortfallClick(object sender, RoutedEventArgs e) => await _viewModel.DecideShortfallAsync(ShortfallKind.ManagementAccepted, ShortfallNote.Text);

    private async void OnReplacementClick(object sender, RoutedEventArgs e) => await _viewModel.DecideShortfallAsync(ShortfallKind.ReplacementOrdered, ShortfallNote.Text);
}
