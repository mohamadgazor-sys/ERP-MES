using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The unit metrics screen. It only shows what the Application computed for the chosen Jalali range.</summary>
public partial class KpiView : UserControl
{
    private readonly KpiViewModel _viewModel;

    public KpiView(KpiViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private async void OnRange(object sender, RoutedEventArgs e) => await _viewModel.SetRangeAsync(RangeFrom.Text, RangeTo.Text);
}
