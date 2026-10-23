using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The item-import screen. It picks the file and relays it to the view model; reading, validation, conflict rules and the commit are in the Application.</summary>
public partial class ItemImportView : UserControl
{
    private const long ReadCapBytes = 100L * 1024 * 1024;

    private readonly ItemImportViewModel _viewModel;

    public ItemImportView(ItemImportViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnSaveTemplateClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", FileName = "item-import-template.xlsx", Title = "ذخیرهٔ قالب نمونه" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await SafeAsync(async () => await System.IO.File.WriteAllBytesAsync(dialog.FileName, _viewModel.BuildTemplate()));
    }

    private async void OnChooseFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Excel (*.xlsx)|*.xlsx", CheckFileExists = true, Multiselect = false, Title = "انتخاب فایل اکسل آیتم‌ها" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await SafeAsync(async () =>
        {
            var info = new System.IO.FileInfo(dialog.FileName);
            if (info.Length > ReadCapBytes)
            {
                await _viewModel.PreviewAsync(info.Name, new byte[1]);
                return;
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(dialog.FileName);
            await _viewModel.PreviewAsync(info.Name, bytes);
        });
    }

    private async void OnCommitClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.CommitAsync());

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
