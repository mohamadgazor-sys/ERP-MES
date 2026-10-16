using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The customer screen. It hands what was typed to the view model; every rule and every permission check is in the Application use cases.</summary>
public partial class CustomersView : UserControl
{
    private readonly CustomersViewModel _viewModel;

    public CustomersView(CustomersViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    // Event handlers are async void, so nothing may escape them; the view model already turns failures into messages.
    private async void OnLoaded(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnShowInactiveClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.CreateAsync(NameBox.Text));
        if (saved)
        {
            NameBox.Clear();
        }
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.RenameSelectedAsync(NameBox.Text));
        if (saved)
        {
            NameBox.Clear();
        }
    }

    private async void OnDeactivateClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedActiveAsync(false));

    private async void OnActivateClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedActiveAsync(true));

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
