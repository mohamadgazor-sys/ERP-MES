using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The clarification screen. It hands what was typed to the view model; every rule and permission check is in the Application.</summary>
public partial class ClarificationsView : UserControl
{
    private readonly ClarificationsViewModel _viewModel;

    public ClarificationsView(ClarificationsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnAskClick(object sender, RoutedEventArgs e)
    {
        var requestId = (RequestBox.SelectedItem as Choice<long>)?.Value ?? 0;
        await TextActionAsync(() => _viewModel.AskAsync(requestId, TextBox.Text));
    }

    private async void OnAnswerClick(object sender, RoutedEventArgs e) => await TextActionAsync(() => _viewModel.AnswerSelectedAsync(TextBox.Text));

    private async void OnCloseClick(object sender, RoutedEventArgs e) => await TextActionAsync(() => _viewModel.CloseSelectedAsync(TextBox.Text));

    // The text box is cleared only when the action went through.
    private async Task TextActionAsync(Func<Task<bool>> action)
    {
        var saved = false;
        await SafeAsync(async () => saved = await action());
        if (saved)
        {
            TextBox.Clear();
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
