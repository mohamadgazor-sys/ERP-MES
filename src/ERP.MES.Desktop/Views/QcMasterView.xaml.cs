using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Quality;

namespace ERP.MES.Desktop.Views;

/// <summary>The QC master-data screen (units of measure, conversions, quantity types). It hands what was typed to the view model; every rule and permission check is in the Application.</summary>
public partial class QcMasterView : UserControl
{
    private readonly QcMasterViewModel _viewModel;

    public QcMasterView(QcMasterViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnAddUnitClick(object sender, RoutedEventArgs e)
    {
        var kind = (UnitKindBox.SelectedItem as Choice<UnitKind>)?.Value ?? (UnitKind)0;
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.AddUnitAsync(UnitCodeBox.Text, UnitNameBox.Text, kind));
        if (saved)
        {
            UnitCodeBox.Clear();
            UnitNameBox.Clear();
        }
    }

    private async void OnRenameUnitClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.RenameSelectedUnitAsync(UnitNameBox.Text));
        if (saved)
        {
            UnitNameBox.Clear();
        }
    }

    private async void OnDeactivateUnitClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedUnitActiveAsync(false));

    private async void OnActivateUnitClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedUnitActiveAsync(true));

    private async void OnAddConversionClick(object sender, RoutedEventArgs e)
    {
        var from = (FromUnitBox.SelectedItem as Choice<long>)?.Value ?? 0;
        var to = (ToUnitBox.SelectedItem as Choice<long>)?.Value ?? 0;
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.AddConversionAsync(from, to, FactorBox.Text));
        if (saved)
        {
            FactorBox.Clear();
        }
    }

    private async void OnDeactivateConversionClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedConversionActiveAsync(false));

    private async void OnActivateConversionClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedConversionActiveAsync(true));

    private async void OnAddTypeClick(object sender, RoutedEventArgs e)
    {
        var baseUnit = (BaseUnitBox.SelectedItem as Choice<long>)?.Value ?? 0;
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.AddQuantityTypeAsync(TypeCodeBox.Text, TypeNameBox.Text, baseUnit, BaseUnitsBox.Text, MembersBox.Text));
        if (saved)
        {
            TypeCodeBox.Clear();
            TypeNameBox.Clear();
            BaseUnitsBox.Clear();
            MembersBox.Clear();
        }
    }

    private async void OnRenameTypeClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.RenameSelectedTypeAsync(TypeNameBox.Text));
        if (saved)
        {
            TypeNameBox.Clear();
        }
    }

    private async void OnDeactivateTypeClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedTypeActiveAsync(false));

    private async void OnActivateTypeClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedTypeActiveAsync(true));

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
