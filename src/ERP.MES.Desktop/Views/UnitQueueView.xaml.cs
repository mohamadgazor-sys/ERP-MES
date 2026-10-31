using System.Windows;
using System.Windows.Controls;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Views;

/// <summary>
/// The operational production-unit queue screen. The buttons only hand the action to the view model; the Application decides who may do what, checks the unit designation, the gates
/// and the row versions, and writes the history and the audit.
/// </summary>
public partial class UnitQueueView : UserControl
{
    private readonly UnitQueueViewModel _viewModel;
    private bool _loading;

    public UnitQueueView(UnitQueueViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        _loading = true;
        try
        {
            await _viewModel.LoadAsync();
            UnitBox.SelectedItem = _viewModel.SelectedUnit;
        }
        finally
        {
            _loading = false;
        }
    }

    private async void OnUnitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && UnitBox.SelectedItem is ERP.MES.Application.Production.UnitInfo unit)
        {
            await _viewModel.SelectUnitAsync(unit.UnitId);
        }
    }

    private UnitQueueRow? One() => QueueGrid.SelectedItems.OfType<UnitQueueRow>().FirstOrDefault();

    private List<UnitQueueRow> Many() => QueueGrid.SelectedItems.OfType<UnitQueueRow>().OrderBy(r => r.Entry.Order).ToList();

    private async void OnUpClick(object sender, RoutedEventArgs e)
    {
        if (One() is { } row)
        {
            await MoveAsync(row, -1);
        }
    }

    private async void OnDownClick(object sender, RoutedEventArgs e)
    {
        if (One() is { } row)
        {
            await MoveAsync(row, +1);
        }
    }

    /// <summary>The Production Manager reorders only with a reason (Decision Log #122); Planning does not need one.</summary>
    private async Task MoveAsync(UnitQueueRow row, int delta)
    {
        string? reason = null;
        if (_viewModel.OrderNeedsReason)
        {
            reason = PromptWindow.Ask(Window.GetWindow(this), "تغییر ترتیب صف", "دلیل تغییر ترتیب (الزامی):");
            if (reason is null)
            {
                return;
            }
        }

        await _viewModel.MoveAsync(row, delta, reason);
    }

    private async void OnPriorityClick(object sender, RoutedEventArgs e)
    {
        if (One() is { } row && PriorityBox.SelectedItem is Choice<Priority> choice)
        {
            await _viewModel.ChangePriorityAsync(row, choice.Value);
        }
    }

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (One() is { } row)
        {
            await _viewModel.StartAsync(row);
        }
    }

    private async void OnSubmitClick(object sender, RoutedEventArgs e)
    {
        if (One() is { } row)
        {
            await _viewModel.SubmitForQcAsync(row);
        }
    }

    private async void OnStopClick(object sender, RoutedEventArgs e)
    {
        if (One() is { } row && PromptWindow.Ask(Window.GetWindow(this), "توقف مرحله", "دلیل توقف (الزامی):") is { } reason)
        {
            await _viewModel.StopAsync(row, reason);
        }
    }

    private async void OnResumeClick(object sender, RoutedEventArgs e)
    {
        if (One() is { } row)
        {
            await _viewModel.ResumeAsync(row);
        }
    }

    private async void OnOverrideClick(object sender, RoutedEventArgs e)
    {
        var rows = Many();
        if (rows.Count > 0 && PromptWindow.Ask(Window.GetWindow(this), "جایگزینی مدیریت", "دلیل جایگزینی (الزامی). ردیف‌های انتخاب‌شده به ترتیب جدول در ابتدای صف قرار می‌گیرند:") is { } reason)
        {
            await _viewModel.OverrideAsync(rows, reason);
        }
    }

    private async void OnClearOverrideClick(object sender, RoutedEventArgs e)
    {
        var rows = Many();
        if (rows.Count > 0 && PromptWindow.Ask(Window.GetWindow(this), "پاک‌کردن جایگزینی", "دلیل پاک‌کردن (الزامی):") is { } reason)
        {
            await _viewModel.ClearOverrideAsync(rows, reason);
        }
    }

    private async void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        var rows = Many();
        if (rows.Count > 0)
        {
            await _viewModel.ConfirmReviewAsync(rows);
        }
    }

    private async void OnReleaseClick(object sender, RoutedEventArgs e)
    {
        if (PendingGrid.SelectedItem is UnitQueueRow row)
        {
            await _viewModel.ReleaseAsync(row);
        }
    }

    private async void OnMakeSupervisorClick(object sender, RoutedEventArgs e)
    {
        if (DesignationGrid.SelectedItem is DesignationDisplayRow row)
        {
            await _viewModel.SetSupervisorAsync(row, true);
        }
    }

    private async void OnRemoveSupervisorClick(object sender, RoutedEventArgs e)
    {
        if (DesignationGrid.SelectedItem is DesignationDisplayRow row)
        {
            await _viewModel.SetSupervisorAsync(row, false);
        }
    }
}

/// <summary>A small modal prompt for a required reason. Returns null when cancelled.</summary>
internal static class PromptWindow
{
    public static string? Ask(Window? owner, string title, string label)
    {
        var box = new TextBox { MinWidth = 360, Margin = new Thickness(0, 6, 0, 10), AcceptsReturn = false };
        var ok = new Button { Content = "تأیید", IsDefault = true, Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "انصراف", IsCancel = true, Padding = new Thickness(14, 4, 14, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var window = new Window
        {
            Title = title,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            FlowDirection = FlowDirection.RightToLeft,
            ResizeMode = ResizeMode.NoResize,
        };
        ok.Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true ? box.Text : null;
    }

    private async void OnOpenDrawingClick(object sender, RoutedEventArgs e) => await DrawingLauncher.OpenAsync(sender);
}
