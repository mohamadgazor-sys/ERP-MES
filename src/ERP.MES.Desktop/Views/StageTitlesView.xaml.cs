using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The stage-title catalog screen. It hands what was typed to the view model; the Application checks the Planning permission and every rule (Decision Log #124 B4).</summary>
public partial class StageTitlesView : UserControl
{
    private readonly StageTitlesViewModel _viewModel;

    public StageTitlesView(StageTitlesViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.AddAsync(NewTitleBox.Text));
        if (saved)
        {
            NewTitleBox.Clear();
        }
    }

    private async void OnRetireClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedActiveAsync(false));

    private async void OnReactivateClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedActiveAsync(true));

    private static async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception)
        {
            // The view model reports its own failures; a failure while the window closes is ignored.
        }
    }
}
