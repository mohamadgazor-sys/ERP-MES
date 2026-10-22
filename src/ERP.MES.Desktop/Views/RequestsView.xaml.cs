using System.Windows;
using System.Windows.Controls;
using ERP.MES.Application.Requests;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Views;

/// <summary>The request screen. It hands what was typed to the view model; every rule and every permission check is in the Application use cases.</summary>
public partial class RequestsView : UserControl
{
    private readonly RequestsViewModel _viewModel;

    public RequestsView(RequestsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await SafeAsync(async () =>
        {
            await _viewModel.LoadAsync();
            if (string.IsNullOrEmpty(DateBox.Text))
            {
                DateBox.Text = _viewModel.TodayText;
            }

            PriorityBox.SelectedItem ??= _viewModel.Priorities.FirstOrDefault(p => p.Value == Priority.Normal);
        });
    }

    private async void OnRequestSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var row = RequestGrid.SelectedItem as RequestRow;

        // Reloading the list empties the grid for a moment; that is not the user choosing "nothing".
        if (row is null && _viewModel.IsBusy)
        {
            return;
        }

        await SafeAsync(() => _viewModel.SelectAsync(row));
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        var customerId = (CustomerBox.SelectedItem as CustomerChoice)?.Id ?? 0;
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.CreateAsync(customerId, RequestNoBox.Text, (CreateWorkTypeBox.SelectedItem as Choice<WorkType>)?.Value ?? (WorkType)0, DateBox.Text, TitleBox.Text, DescriptionBox.Text));
        if (saved)
        {
            RequestNoBox.Clear();
            TitleBox.Clear();
            DescriptionBox.Clear();
        }
    }

    private async void OnSubmitClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SubmitAsync());

    private async void OnAddItemClick(object sender, RoutedEventArgs e)
    {
        var priority = (PriorityBox.SelectedItem as Choice<Priority>)?.Value ?? Priority.Normal;
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.AddItemAsync(ItemNoBox.Text, PartNameBox.Text, QuantityBox.Text, priority));
        if (saved)
        {
            ItemNoBox.Clear();
            PartNameBox.Clear();
            QuantityBox.Clear();
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
