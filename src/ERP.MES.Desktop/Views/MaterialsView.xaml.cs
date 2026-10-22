using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The materials and price screen. It hands what was typed to the view model; every rule and permission check is in the Application.</summary>
public partial class MaterialsView : UserControl
{
    private readonly MaterialsViewModel _viewModel;

    public MaterialsView(MaterialsViewModel viewModel)
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
        await SafeAsync(async () => saved = await _viewModel.AddMaterialAsync(CodeBox.Text, NameBox.Text, GradeBox.Text, FormBox.Text));
        if (saved)
        {
            CodeBox.Clear();
            NameBox.Clear();
            GradeBox.Clear();
            FormBox.Clear();
        }
    }

    private async void OnUpdateClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.UpdateSelectedAsync(NameBox.Text, GradeBox.Text, FormBox.Text));

    private async void OnDeactivateClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedActiveAsync(false));

    private async void OnActivateClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedActiveAsync(true));

    private async void OnAddPriceClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.AddPriceForSelectedAsync(PriceBox.Text, PriceDateBox.Text, PriceReasonBox.Text));
        if (saved)
        {
            PriceBox.Clear();
            PriceDateBox.Clear();
            PriceReasonBox.Clear();
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
