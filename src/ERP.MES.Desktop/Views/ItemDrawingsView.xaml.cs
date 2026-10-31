using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;
using Microsoft.Win32;

namespace ERP.MES.Desktop.Views;

/// <summary>The item drawings screen. It reads the chosen file and hands it to the view model; the Application checks the Drawing permission, the PDF rules and the size (Decision Log #125 Q14).</summary>
public partial class ItemDrawingsView : UserControl
{
    private const long MaxBytes = 20L * 1024 * 1024;

    private readonly ItemDrawingsViewModel _viewModel;

    public ItemDrawingsView(ItemDrawingsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnRequestChanged(object sender, SelectionChangedEventArgs e) =>
        await SafeAsync(() => _viewModel.SelectRequestAsync(RequestBox.SelectedItem as DrawingRequestRow));

    private async void OnItemChanged(object sender, SelectionChangedEventArgs e) =>
        await SafeAsync(() => _viewModel.SelectItemAsync(_viewModel.SelectedItem));

    private async void OnOpenCurrentClick(object sender, RoutedEventArgs e) => await OpenAsync(null);

    private async void OnOpenRevisionClick(object sender, RoutedEventArgs e) => await OpenAsync((RevisionsGrid.SelectedItem as DrawingRevisionRow)?.Revision);

    private async Task OpenAsync(int? revision)
    {
        string? path = null;
        await SafeAsync(async () => path = await _viewModel.PrepareSelectedAsync(revision, DrawingLauncher.TempFolder));
        if (path is not null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception)
            {
                MessageBox.Show("باز کردن نقشه ممکن نشد؛ برنامه‌ای برای نمایش فایل PDF روی این رایانه نصب نیست.", "نقشه آیتم", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private async void OnUploadClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "PDF (*.pdf)|*.pdf", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await SafeAsync(async () =>
        {
            var info = new System.IO.FileInfo(dialog.FileName);
            if (info.Length > MaxBytes)
            {
                _viewModel.ReportTooLarge();
                return;
            }

            await _viewModel.UploadAsync(info.Name, await System.IO.File.ReadAllBytesAsync(dialog.FileName));
        });
    }

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
