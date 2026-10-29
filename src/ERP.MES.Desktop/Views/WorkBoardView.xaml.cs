using System.Windows;
using System.Windows.Controls;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Production;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Production;

namespace ERP.MES.Desktop.Views;

/// <summary>
/// The supervisor and operator work board. The buttons only hand the action to the view model; the Application decides who may do what, checks the unit designation, the gates, the resources
/// and the row versions, and writes the history and the audit.
/// </summary>
public partial class WorkBoardView : UserControl
{
    private readonly WorkBoardViewModel _viewModel;
    private bool _loading;

    public WorkBoardView(WorkBoardViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        Resources.Add("BoolToVisibility", new BooleanToVisibilityConverter());
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
        if (!_loading && UnitBox.SelectedItem is UnitInfo unit)
        {
            await _viewModel.SelectUnitAsync(unit.UnitId);
        }
    }

    private void OnStageChanged(object sender, SelectionChangedEventArgs e) => _viewModel.SelectStage(StageGrid.SelectedItem as WorkStageRow);

    private async void OnMetricsClick(object sender, RoutedEventArgs e) => await _viewModel.ShowMetricsAsync();

    private async void OnSubmitStageClick(object sender, RoutedEventArgs e) => await _viewModel.SubmitStageForQcAsync();

    private async void OnDispatchStartClick(object sender, RoutedEventArgs e) => await _viewModel.DispatchAsync(MachineBox.SelectedItem as MachineChoice, OperatorBox.SelectedItem as OperatorChoice, true, OutsideBox.Text);

    private async void OnDispatchOnlyClick(object sender, RoutedEventArgs e) => await _viewModel.DispatchAsync(MachineBox.SelectedItem as MachineChoice, OperatorBox.SelectedItem as OperatorChoice, false, OutsideBox.Text);

    // The session the buttons act on: the one selected among the stage's sessions, otherwise the one selected among "my sessions".
    private SessionDisplayRow? Selected() => SessionGrid.SelectedItem as SessionDisplayRow ?? MineGrid.SelectedItem as SessionDisplayRow;

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        if (Selected() is { } row)
        {
            await _viewModel.StartAsync(row, OutsideBox.Text);
            await AskOutsideShiftReasonAsync(row, (r, reason) => _viewModel.StartAsync(r, reason));
        }
    }

    private async void OnPauseClick(object sender, RoutedEventArgs e)
    {
        if (Selected() is { } row && ReasonBox.SelectedItem is Stored<PauseReason> reason)
        {
            var note = PromptWindow.Ask(Window.GetWindow(this), "توقف نوبت", reason.Value.RequiresNote ? "توضیح (برای این دلیل الزامی است):" : "توضیح (اختیاری):");
            if (note is not null || !reason.Value.RequiresNote)
            {
                await _viewModel.PauseAsync(row, reason, note);
            }
        }
        else
        {
            MessageBox.Show(Window.GetWindow(this), "ابتدا یک نوبت و یک دلیل توقف را انتخاب کنید.", "توقف نوبت");
        }
    }

    private async void OnResumeClick(object sender, RoutedEventArgs e)
    {
        if (Selected() is { } row)
        {
            await _viewModel.ResumeAsync(row, OutsideBox.Text);
            await AskOutsideShiftReasonAsync(row, (r, reason) => _viewModel.ResumeAsync(r, reason));
        }
    }

    // Work outside the operator's scheduled shift needs a recorded reason. The supervisor's dispatch panel has a box for it, but an operator resuming their own session has none, so the refusal is answered with a prompt
    // (found by the UI interaction test: the operator could not resume a paused session outside shift hours).
    private async Task AskOutsideShiftReasonAsync(SessionDisplayRow row, Func<SessionDisplayRow, string, Task> retry)
    {
        if (_viewModel.LastIssue == ERP.MES.Application.Requests.ValidationIssue.SessionOutsideShiftReasonRequired
            && PromptWindow.Ask(Window.GetWindow(this), "کار خارج از شیفت", "دلیل کار خارج از شیفت (الزامی):") is { Length: > 0 } reason)
        {
            await retry(row, reason);
        }
    }

    private async void OnFinishClick(object sender, RoutedEventArgs e)
    {
        if (Selected() is { } row)
        {
            await _viewModel.FinishAsync(row, PromptWindow.Ask(Window.GetWindow(this), "پایان نوبت", "یادداشت (اختیاری):"));
        }
    }

    private async void OnHandOverClick(object sender, RoutedEventArgs e)
    {
        if (Selected() is { } row)
        {
            await _viewModel.HandOverAsync(row, OperatorBox.SelectedItem as OperatorChoice, OutsideBox.Text);
        }
    }

    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (Selected() is { } row && PromptWindow.Ask(Window.GetWindow(this), "لغو نوبت", "دلیل لغو (الزامی):") is { } reason)
        {
            await _viewModel.CancelAsync(row, reason);
        }
    }

    private async void OnOutageClick(object sender, RoutedEventArgs e)
    {
        if (ReasonBox.SelectedItem is Stored<PauseReason> reason)
        {
            var note = PromptWindow.Ask(Window.GetWindow(this), "ثبت قطعی", "توضیح قطعی (برای دلیل «سایر» الزامی است):");
            await _viewModel.RecordOutageAsync(reason, note, []);
        }
        else
        {
            MessageBox.Show(Window.GetWindow(this), "ابتدا دلیل قطعی را از فهرست «دلیل توقف» انتخاب کنید.", "ثبت قطعی");
        }
    }

    private async void OnEndOutageClick(object sender, RoutedEventArgs e)
    {
        if (OutageGrid.SelectedItem is OutageEvent outage)
        {
            await _viewModel.EndOutageAsync(outage);
        }
    }

    private async void OnCorrectClick(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } row || BoundaryBox.SelectedItem is not ComboBoxItem { Tag: string tag } || !Enum.TryParse<SessionBoundary>(tag, out var boundary))
        {
            return;
        }

        var reason = PromptWindow.Ask(Window.GetWindow(this), "اصلاح زمان", "دلیل اصلاح (الزامی):");
        if (reason is not null)
        {
            _ = int.TryParse(PauseIndexBox.Text, out var index);
            await _viewModel.CorrectAsync(row, boundary, index, InstantBox.Text, reason);
        }
    }

    private async void OnApproveClick(object sender, RoutedEventArgs e)
    {
        if (Selected() is { } row)
        {
            await _viewModel.ApproveTimeAsync(row);
        }
    }
}
