using System.Windows;
using System.Windows.Controls;
using ERP.MES.Application.Persistence;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Desktop.Views;

/// <summary>The Planning calendar screen. The buttons only hand the typed values to the view model; the Application decides who may do what and enforces every scheduling rule.</summary>
public partial class CalendarView : UserControl
{
    private readonly CalendarViewModel _viewModel;

    public CalendarView(CalendarViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.LoadAsync();

    private long? UnitOf(ComboBox box) => (box.SelectedItem as Stored<ProductionUnit>)?.Value.Id;

    private long? ShiftOf(ComboBox box) => (box.SelectedItem as TemplateRow)?.Stored.Value.Id;

    private async void OnAddTemplate(object sender, RoutedEventArgs e) =>
        await _viewModel.AddTemplateAsync(TplCode.Text, TplName.Text, TplStart.Text, TplEnd.Text, TplBreakStart.Text, TplBreakEnd.Text);

    private async void OnToggleTemplate(object sender, RoutedEventArgs e)
    {
        if (TemplateGrid.SelectedItem is TemplateRow row)
        {
            await _viewModel.SetTemplateActiveAsync(row, !row.Stored.Value.IsActive);
        }
    }

    private async void OnToggleWeekday(object sender, RoutedEventArgs e)
    {
        if (WeekGrid.SelectedItem is WeekdayRow row)
        {
            await _viewModel.SetWeekdayAsync(row, !row.Stored.Value.IsWorkday);
        }
    }

    private async void OnAddException(object sender, RoutedEventArgs e)
    {
        if (ExKind.SelectedItem is Choice<CalendarExceptionKind> kind)
        {
            await _viewModel.AddExceptionAsync(ExDate.Text, kind.Value, UnitOf(ExUnit), ShiftOf(ExShift), ExReason.Text);
        }
    }

    private void OnClearScope(object sender, RoutedEventArgs e)
    {
        ExUnit.SelectedItem = null;
        ExShift.SelectedItem = null;
    }

    private async void OnCancelException(object sender, RoutedEventArgs e)
    {
        if (ExceptionGrid.SelectedItem is ExceptionRow row)
        {
            await _viewModel.CancelExceptionAsync(row);
        }
    }

    private async void OnAddUnitShift(object sender, RoutedEventArgs e)
    {
        if (UnitOf(UsUnit) is { } unit && ShiftOf(UsShift) is { } shift)
        {
            await _viewModel.AddUnitShiftAsync(unit, shift, UsMax.Text, UsMin.Text, UsOverlap.IsChecked == true, CheckedDaysMask());
        }
    }

    // The weekday boxes (Saturday first, like the week of the factory) as the bit mask of the Domain (bit n = DayOfWeek n); nothing checked means "follow the company pattern".
    private int? CheckedDaysMask()
    {
        var boxes = new (CheckBox Box, DayOfWeek Day)[]
        {
            (DaySat, DayOfWeek.Saturday), (DaySun, DayOfWeek.Sunday), (DayMon, DayOfWeek.Monday), (DayTue, DayOfWeek.Tuesday), (DayWed, DayOfWeek.Wednesday), (DayThu, DayOfWeek.Thursday), (DayFri, DayOfWeek.Friday),
        };
        var days = boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Day).ToList();
        return days.Count == 0 ? null : ERP.MES.Domain.Scheduling.UnitShift.MaskOf(days);
    }

    private async void OnSetUnitShiftDays(object sender, RoutedEventArgs e)
    {
        if (UnitShiftGrid.SelectedItem is UnitShiftRow row)
        {
            await _viewModel.SetUnitShiftDaysAsync(row, CheckedDaysMask());
        }
    }

    private async void OnAddDateHours(object sender, RoutedEventArgs e)
    {
        if (UnitOf(UsUnit) is { } unit)
        {
            await _viewModel.AddUnitDateHoursAsync(unit, DhDate.Text, DhStart.Text, DhEnd.Text, DhMax.Text, DhReason.Text);
        }
    }

    private async void OnToggleUnitShift(object sender, RoutedEventArgs e)
    {
        if (UnitShiftGrid.SelectedItem is UnitShiftRow row)
        {
            await _viewModel.SetUnitShiftActiveAsync(row, !row.Stored.Value.IsActive);
        }
    }

    private async void OnRange(object sender, RoutedEventArgs e) => await _viewModel.SetRangeAsync(RangeFrom.Text, RangeTo.Text);

    private async void OnSchedule(object sender, RoutedEventArgs e)
    {
        if (SchOperator.SelectedItem is Stored<Operator> person && UnitOf(SchUnit) is { } unit && ShiftOf(SchShift) is { } shift)
        {
            await _viewModel.ScheduleAsync(person.Value.Id, SchDate.Text, unit, shift, (SchMachine.SelectedItem as Stored<Machine>)?.Value.Id, SchHandover.Text, SchNote.Text);
        }
    }

    private async void OnCancelSchedule(object sender, RoutedEventArgs e)
    {
        if (ScheduleGrid.SelectedItem is ScheduleDisplayRow row && PromptWindow.Ask(Window.GetWindow(this), "لغو برنامه", "دلیل لغو (الزامی):") is { } reason)
        {
            await _viewModel.CancelScheduleAsync(row, reason);
        }
    }

    private async void OnAddOffDay(object sender, RoutedEventArgs e)
    {
        if (OffOperator.SelectedItem is Stored<Operator> person && OffKind.SelectedItem is Choice<OffDayKind> kind)
        {
            await _viewModel.AddOffDayAsync(person.Value.Id, OffFrom.Text, OffTo.Text, kind.Value, OffNote.Text);
        }
    }

    private async void OnCancelOffDay(object sender, RoutedEventArgs e)
    {
        if (OffDayGrid.SelectedItem is OffDayRow row)
        {
            await _viewModel.CancelOffDayAsync(row);
        }
    }
}
