using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Views;

/// <summary>The item-definition screen. It hands what was typed to the view model; the gates, the specification rules and the permission are checked in the Application.</summary>
public partial class ItemDefinitionView : UserControl
{
    private readonly ItemDefinitionViewModel _viewModel;

    public ItemDefinitionView(ItemDefinitionViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
        StageTitleBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnStageTitleTextChanged));

        // The combo box's own class handler marks Enter as handled before an ordinary handler would see it, so this one also receives handled events.
        StageTitleBox.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnStageTitleKeyDown), handledEventsToo: true);
    }

    private bool _titleSuppress;

    private string TitleText => StageTitleBox.Text ?? string.Empty;

    private System.ComponentModel.ICollectionView? _titleView;
    private string _titleFilter = string.Empty;

    /// <summary>The drop-down shows one persistent list of the active catalog titles; typing only changes the view's filter, so the combo box's own selection and text handling are never fought.</summary>
    private void EnsureTitleItems()
    {
        if (ReferenceEquals(StageTitleBox.ItemsSource, _viewModel.StageTitleChoices))
        {
            return;
        }

        StageTitleBox.ItemsSource = _viewModel.StageTitleChoices;
        _titleView = CollectionViewSource.GetDefaultView(StageTitleBox.ItemsSource);
        _titleView.Filter = item => item is string title && (_titleFilter.Length == 0 || title.Contains(_titleFilter, StringComparison.OrdinalIgnoreCase));
    }

    private void OnStageTitleFocused(object sender, KeyboardFocusChangedEventArgs e) => EnsureTitleItems();

    /// <summary>Typing narrows the list to the catalog titles that contain the text; the text itself is never turned into a title.</summary>
    private void OnStageTitleTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_titleSuppress)
        {
            return;
        }

        // A selection made from the drop-down changes the text natively; that is a commit, not typing.
        if (StageTitleBox.SelectedItem is string selected && string.Equals(selected, TitleText, StringComparison.Ordinal))
        {
            return;
        }

        // A complete catalog title is a commit (the combo box changes the text before it updates its selection), never a reason to narrow the list.
        if (_viewModel.StageTitleChoices.Contains(TitleText, StringComparer.Ordinal))
        {
            _titleFilter = string.Empty;
            return;
        }

        EnsureTitleItems();
        var typed = TitleText;
        var inner = StageTitleBox.Template.FindName("PART_EditableTextBox", StageTitleBox) as TextBox;
        var caret = inner?.CaretIndex ?? typed.Length;
        _titleFilter = typed.Trim();
        _titleSuppress = true;
        try
        {
            _titleView?.Refresh();
            if (!string.Equals(StageTitleBox.Text, typed, StringComparison.Ordinal))
            {
                StageTitleBox.Text = typed;
            }

            if (inner is not null)
            {
                inner.CaretIndex = Math.Min(caret, typed.Length);
            }
        }
        finally
        {
            _titleSuppress = false;
        }

        StageTitleBox.IsDropDownOpen = StageTitleBox.IsKeyboardFocusWithin && typed.Length > 0;
    }

    /// <summary>Commits a catalog title: the filter is cleared, the entry becomes the real selection and the text is set to it (re-applied once the combo box has finished closing its drop-down).</summary>
    private void CommitTitle(string title)
    {
        EnsureTitleItems();
        _titleSuppress = true;
        try
        {
            _titleFilter = string.Empty;
            _titleView?.Refresh();
            StageTitleBox.SelectedItem = title;
            StageTitleBox.Text = title;
            StageTitleBox.IsDropDownOpen = false;
        }
        finally
        {
            _titleSuppress = false;
        }

        ReassertTitleLater(title);
    }

    private void ReassertTitleLater(string title) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _titleSuppress = true;
            try
            {
                if (!string.Equals(StageTitleBox.Text, title, StringComparison.Ordinal))
                {
                    StageTitleBox.SelectedItem = title;
                    StageTitleBox.Text = title;
                }
            }
            finally
            {
                _titleSuppress = false;
            }
        }), System.Windows.Threading.DispatcherPriority.Background);

    private void ClearTitle()
    {
        _titleSuppress = true;
        try
        {
            _titleFilter = string.Empty;
            _titleView?.Refresh();
            StageTitleBox.SelectedItem = null;
            StageTitleBox.Text = string.Empty;
        }
        finally
        {
            _titleSuppress = false;
        }
    }

    /// <summary>Choosing an entry with the mouse (or any selection) is a commit; the combo box sets the text itself, which is re-applied afterwards in case closing the drop-down disturbed it.</summary>
    private void OnStageTitleSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_titleSuppress && StageTitleBox.SelectedItem is string chosen)
        {
            _titleFilter = string.Empty;
            ReassertTitleLater(chosen);
        }
    }

    /// <summary>Enter commits the highlighted or the single matching title; Tab commits a valid title and moves on, and stays put (with a message) when the text is not a catalog title.</summary>
    private void OnStageTitleKeyDown(object sender, KeyEventArgs e)
    {
        // With an input method active WPF reports Key.ImeProcessed and the real key in ImeProcessedKey.
        var pressed = e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (pressed is not (Key.Enter or Key.Tab))
        {
            return;
        }

        var inner = StageTitleBox.Template.FindName("PART_EditableTextBox", StageTitleBox) as TextBox;
        var typed = inner?.Text ?? TitleText;
        var resolved = StageTitleSearch.Resolve(_viewModel.StageTitleChoices, typed, StageTitleBox.SelectedItem as string);
        if (resolved is not null)
        {
            CommitTitle(resolved);
            if (pressed == Key.Enter)
            {
                e.Handled = true;
            }

            return;
        }

        if (TitleText.Trim().Length > 0)
        {
            _viewModel.ReportStageTitleNotInCatalog();
            e.Handled = true;
        }
        else if (pressed == Key.Enter)
        {
            StageTitleBox.IsDropDownOpen = true;
            e.Handled = true;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await SafeAsync(async () =>
        {
            await _viewModel.LoadAsync();
            EnsureTitleItems();
            PriorityBox.SelectedItem ??= _viewModel.Priorities.FirstOrDefault(p => p.Value == Priority.Normal);
        });
    }

    private async void OnReloadClick(object sender, RoutedEventArgs e) => await SafeAsync(async () =>
    {
        await _viewModel.LoadAsync();
        EnsureTitleItems();
    });

    private async void OnRequestSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.SelectedRequest is { } row)
        {
            await SafeAsync(() => _viewModel.SelectRequestAsync(row.RequestId));
        }
    }

    private void OnUnitChanged(object sender, SelectionChangedEventArgs e) =>
        MachineBox.ItemsSource = UnitBox.SelectedItem is Choice<long> unit ? _viewModel.MachinesOf(unit.Value) : null;

    private async void OnDefineItemClick(object sender, RoutedEventArgs e)
    {
        var priority = (PriorityBox.SelectedItem as Choice<Priority>)?.Value ?? Priority.Normal;
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.DefineItemAsync(
            ItemNoBox.Text, PartNameBox.Text, QuantityBox.Text, priority, (TypeBox.SelectedItem as Choice<long>)?.Value, (MaterialBox.SelectedItem as Choice<long>)?.Value,
            WeightBox.Text, LengthBox.Text, DiameterBox.Text, default, (ParentBox.SelectedItem as Choice<long>)?.Value, AssemblyBox.IsChecked == true, DeliveryDateBox.Text, WarehouseCodeBox.Text));
        if (saved)
        {
            foreach (var box in new[] { ItemNoBox, PartNameBox, QuantityBox, WeightBox, LengthBox, DiameterBox, DeliveryDateBox, WarehouseCodeBox })
            {
                box.Clear();
            }

            AssemblyBox.IsChecked = false;
        }
    }

    private async void OnReleaseAllClick(object sender, RoutedEventArgs e)
    {
        // «خیر» (or closing the dialog) changes nothing; only «بله» calls the Application.
        if (!ConfirmDialog.Ask(Window.GetWindow(this), "ارسال به صف کار", ItemDefinitionViewModel.ReleaseConfirmationText))
        {
            return;
        }

        await SafeAsync(() => _viewModel.ReleaseAllAsync());
    }

    private async void OnMakeAssemblyClick(object sender, RoutedEventArgs e) => await SafeAsync(() => _viewModel.SetSelectedAssemblyAsync(true));

    private async void OnMoveClick(object sender, RoutedEventArgs e) =>
        await SafeAsync(() => _viewModel.MoveSelectedAsync((ParentBox.SelectedItem as Choice<long>)?.Value));

    private async void OnDefineStageClick(object sender, RoutedEventArgs e)
    {
        var unit = (UnitBox.SelectedItem as Choice<long>)?.Value ?? 0;
        var machine = (MachineBox.SelectedItem as Choice<long>)?.Value;
        var saved = false;
        await SafeAsync(async () => saved = await _viewModel.DefineStageAsync(StageTitleSearch.Resolve(_viewModel.StageTitleChoices, TitleText, null) is { } chosenTitle && string.Equals(chosenTitle, TitleText.Trim(), StringComparison.Ordinal) ? chosenTitle : TitleText, MinutesBox.Text, unit, machine, leadTimeDaysText: LeadTimeBox.Text));
        if (saved)
        {
            ClearTitle();
            MinutesBox.Clear();
            LeadTimeBox.Clear();
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
            MessageBox.Show(Messages.Unexpected, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnOpenDrawingClick(object sender, RoutedEventArgs e) => await DrawingLauncher.OpenAsync(sender);
}
