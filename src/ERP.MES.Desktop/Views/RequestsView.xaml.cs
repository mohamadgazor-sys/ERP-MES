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

    /// <summary>The largest file the window will even read into memory; the Application applies the real limit (Decision Log #54) and gives the message.</summary>
    private const long ReadCapBytes = 100L * 1024 * 1024;

    private async void OnAttachPdfClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "PDF (*.pdf)|*.pdf", CheckFileExists = true, Multiselect = false, Title = "انتخاب فایل PDF درخواست" };
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

    private async void OnApproveClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.ApproveAsync());

    private async void OnRejectClick(object sender, RoutedEventArgs e) => await ReasonActionAsync(() => _viewModel.RejectAsync(ReasonBox.Text));

    private async void OnReopenClick(object sender, RoutedEventArgs e) => await ReasonActionAsync(() => _viewModel.ReopenAsync(ReasonBox.Text));

    private async void OnForwardClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.ForwardToDrawingAsync());

    private async void OnSendBackClick(object sender, RoutedEventArgs e) => await ReasonActionAsync(() => _viewModel.SendBackToDrawingAsync(ReasonBox.Text));

    private async void OnDrawingResultClick(object sender, RoutedEventArgs e)
    {
        var outcome = (OutcomeBox.SelectedItem as Choice<DrawingOutcome>)?.Value ?? (DrawingOutcome)0;
        await ReasonActionAsync(() => _viewModel.RecordDrawingResultAsync(outcome, ReasonBox.Text));
    }

    // The reason box is cleared only when the action went through.
    private async Task ReasonActionAsync(Func<Task<bool>> action)
    {
        var saved = false;
        await SafeAsync(async () => saved = await action());
        if (saved)
        {
            ReasonBox.Clear();
        }
    }

    private async void OnSubmitClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SubmitAsync());

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
