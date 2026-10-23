using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Views;

/// <summary>The raw-material inquiry screen. It hands what was typed to the view model; every rule and permission check is in the Application.</summary>
public partial class InquiriesView : UserControl
{
    private readonly InquiriesViewModel _viewModel;

    public InquiriesView(InquiriesViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.LoadAsync());

    private async void OnRequestSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.SelectedRequest is { } row)
        {
            await SafeAsync(() => _viewModel.SelectRequestAsync(row.RequestId));
        }
    }

    private async void OnInquirySelected(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.SelectedInquiry is { } row)
        {
            await SafeAsync(() => _viewModel.SelectInquiryAsync(row.InquiryId));
        }
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.CreateAsync(CreateNoteBox.Text));
        if (saved)
        {
            CreateNoteBox.Clear();
        }
    }

    private async void OnAddLineClick(object sender, RoutedEventArgs e)
    {
        var unit = (UnitBox.SelectedItem as Choice<long>)?.Value ?? 0;
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.AddLineAsync(GradeBox.Text, FormBox.Text, DiameterBox.Text, LengthBox.Text, OtherBox.Text, QuantityBox.Text, unit, StandardBox.Text, RemarksBox.Text));
        if (saved)
        {
            foreach (var box in new[] { GradeBox, FormBox, DiameterBox, LengthBox, OtherBox, QuantityBox, StandardBox, RemarksBox })
            {
                box.Clear();
            }
        }
    }

    private async void OnRemoveLineClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.RemoveSelectedLineAsync());

    private async void OnSendClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SendAsync());

    private async void OnYesClick(object sender, RoutedEventArgs e) => await NoteActionAsync(() => _viewModel.AnswerSelectedLineAsync(true, NoteBox.Text));

    private async void OnNoClick(object sender, RoutedEventArgs e) => await NoteActionAsync(() => _viewModel.AnswerSelectedLineAsync(false, NoteBox.Text));

    private async void OnCloseClick(object sender, RoutedEventArgs e) => await NoteActionAsync(() => _viewModel.CloseAsync(NoteBox.Text));

    private async void OnCancelClick(object sender, RoutedEventArgs e) => await NoteActionAsync(() => _viewModel.CancelAsync(NoteBox.Text));

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
