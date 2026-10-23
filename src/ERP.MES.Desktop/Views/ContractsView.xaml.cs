using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The contract screen. It hands what was typed to the view model; every rule and permission check is in the Application.</summary>
public partial class ContractsView : UserControl
{
    private const long ReadCapBytes = 100L * 1024 * 1024;

    private readonly ContractsViewModel _viewModel;

    public ContractsView(ContractsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnRowSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.SelectedRow is { } row)
        {
            await SafeAsync(() => _viewModel.SelectAsync(row.RequestId));
        }
    }

    private async void OnDecideRequiredClick(object sender, RoutedEventArgs e) => await NoteActionAsync(() => _viewModel.DecideAsync(true, NoteBox.Text));

    private async void OnDecideNotRequiredClick(object sender, RoutedEventArgs e) => await NoteActionAsync(() => _viewModel.DecideAsync(false, NoteBox.Text));

    private async void OnChangeToRequiredClick(object sender, RoutedEventArgs e) => await NoteActionAsync(() => _viewModel.ChangeRequirementAsync(true, NoteBox.Text));

    private async void OnChangeToNotRequiredClick(object sender, RoutedEventArgs e) => await NoteActionAsync(() => _viewModel.ChangeRequirementAsync(false, NoteBox.Text));

    private async void OnSaveDraftClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SaveDraftAsync(ContractNoBox.Text, ContractDateBox.Text, TitleBox.Text, NoteBox.Text));

    private async void OnSubmitClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SubmitAsync());

    private async void OnStartRevisionClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.StartRevisionAsync());

    private async void OnApproveClick(object sender, RoutedEventArgs e) => await NoteActionAsync(() => _viewModel.ApproveAsync(NoteBox.Text));

    private async void OnRejectClick(object sender, RoutedEventArgs e) => await NoteActionAsync(() => _viewModel.RejectAsync(NoteBox.Text));

    // The note box is cleared only when the action went through.
    private async Task NoteActionAsync(Func<Task<bool>> action)
    {
        var saved = false;
        await SafeAsync(async () => saved = await action());
        if (saved)
        {
            NoteBox.Clear();
        }
    }

    private async void OnAttachPdfClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PDF (*.pdf)|*.pdf", CheckFileExists = true, Multiselect = false, Title = "انتخاب فایل PDF قرارداد" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await SafeAsync(async () =>
        {
            var info = new System.IO.FileInfo(dialog.FileName);
            if (info.Length > ReadCapBytes)
            {
                _viewModel.ReportPdfTooLarge();
                return;
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(dialog.FileName);
            await _viewModel.AttachPdfAsync(info.Name, bytes);
        });
    }

    private async void OnViewPdfClick(object sender, RoutedEventArgs e)
    {
        await SafeAsync(async () =>
        {
            var file = await _viewModel.LoadPdfAsync();
            if (file is null)
            {
                return;
            }

            // A copy in the current user's temp folder, opened with the PDF viewer the user has installed.
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ERP-MES-" + Guid.NewGuid().ToString("N") + "-" + System.IO.Path.GetFileName(file.FileName));
            await System.IO.File.WriteAllBytesAsync(path, file.Content);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
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
            MessageBox.Show(Messages.Unexpected, "سامانه ERP/MES", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
