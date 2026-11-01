using ERP.MES.Application.Requests;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Desktop.Tests;

// The Planning calendar screen against the real Application services (no window is opened).
public class CalendarViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CalendarViewModel For(WorkflowHarness h, ERP.MES.Application.Security.Principal user) =>
        new(h.Workflow.Calendar, h.Workflow.WorkerSchedule, h.Workflow.PlanningMaster, h.Workflow.Operators, h.Dates, DigitStyle.Persian, () => new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc), () => user);

    [Fact]
    public async Task Planning_builds_the_calendar_in_jalali_text_and_the_screen_shows_persian_digits_rules_and_flags()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var unit = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Admin, "MILLING", "فرزکاری", Token)).Value!;
        var person = (await h.Workflow.Operators.RegisterAsync(h.Admin, "OP-1", "علی رضایی", null, Token)).Value!;
        var vm = For(h, h.Planning);
        await vm.LoadAsync(Token);

        Assert.True(vm.CanMaintain);
        Assert.Equal(7, vm.Weekdays.Count);
        Assert.Equal("شنبه", vm.Weekdays[0].DayText);
        Assert.Equal("تعطیل", vm.Weekdays.Single(w => w.DayText == "جمعه").StateText);

        await vm.AddTemplateAsync("D", "روزکار", "۰۸:۰۰", "۱۷:۰۰", "۱۲:۰۰", "۱۳:۰۰");
        Assert.False(vm.IsErrorMessage, vm.Message);
        var template = Assert.Single(vm.Templates);
        Assert.Equal("۰۸:۰۰ تا ۱۷:۰۰؛ استراحت ۱۲:۰۰ تا ۱۳:۰۰", template.TimesText);
        Assert.Equal("۴۸۰ دقیقه", template.WorkingText);
        await vm.AddTemplateAsync("X", "بد", "08:00", "08:00", null, null);
        Assert.True(vm.IsErrorMessage);
        Assert.Equal(Messages.ForIssue(ValidationIssue.ShiftTimesInvalid, JalaliDateError.None), vm.Message);

        await vm.AddUnitShiftAsync(unit.Value.Id, template.Stored.Value.Id, "۲", "۱", false);
        Assert.False(vm.IsErrorMessage, vm.Message);
        Assert.Equal("۱ تا ۲", vm.UnitShifts.Single().CapacityText);

        // 1405/08/02 is a Saturday (2026-10-24). Plan the operator, then make the day a holiday: the row stays, flagged.
        await vm.ScheduleAsync(person.Value.Id, "۱۴۰۵/۰۸/۰۲", unit.Value.Id, template.Stored.Value.Id, null, null, null);
        Assert.False(vm.IsErrorMessage, vm.Message);
        await vm.SetRangeAsync("1405/08/01", "1405/08/10");
        Assert.Equal("علی رضایی", Assert.Single(vm.Schedule).OperatorText);
        await vm.AddExceptionAsync("1405/08/02", CalendarExceptionKind.Off, null, null, "تعطیل رسمی");
        Assert.False(vm.IsErrorMessage);
        Assert.Contains("علامت خورده است", vm.Message, StringComparison.Ordinal);
        Assert.Equal("روز اکنون تعطیل است", vm.Schedule.Single().FlagText);
        Assert.Equal("برنامه‌ریزی‌شده", vm.Schedule.Single().StatusText);

        // A second operator on the holiday is refused with the rule text.
        await vm.ScheduleAsync(person.Value.Id, "1405/08/02", unit.Value.Id, template.Stored.Value.Id, null, null, null);
        Assert.True(vm.IsErrorMessage);
        Assert.Equal(Messages.ForIssue(ValidationIssue.ScheduleDayOff, JalaliDateError.None), vm.Message);
        await vm.ScheduleAsync(person.Value.Id, "not a date", unit.Value.Id, template.Stored.Value.Id, null, null, null);
        Assert.Equal(Messages.ForIssue(ValidationIssue.DateInvalid, JalaliDateError.None), vm.Message);
    }

    [Fact]
    public async Task Others_may_read_the_calendar_in_their_roles_but_not_change_it_and_unrelated_departments_are_denied()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        foreach (var user in new[] { h.Manager, h.Production })
        {
            var vm = For(h, user);
            await vm.LoadAsync(Token);
            Assert.False(vm.IsDenied);
            Assert.Equal(7, vm.Weekdays.Count);
            Assert.False(vm.CanMaintain || vm.CanScheduleWorkers);
            await vm.AddTemplateAsync("D", "روزکار", "08:00", "17:00", null, null);
            Assert.True(vm.IsErrorMessage);
            Assert.Equal(Messages.NotAuthorized, vm.Message);
        }

        foreach (var user in new[] { h.Drawing, h.Central, h.Qc, h.Warehouse })
        {
            var vm = For(h, user);
            await vm.LoadAsync(Token);
            Assert.True(vm.IsDenied);
            Assert.Empty(vm.Weekdays);
        }
    }

    [Fact]
    public async Task A_standing_friday_schedule_and_a_dated_own_hours_exception_are_set_through_the_screen()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var unit = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Admin, "MILLING", "فرزکاری", Token)).Value!;
        var vm = For(h, h.Planning);
        await vm.LoadAsync(Token);
        await vm.AddTemplateAsync("D", "روزکار", "08:00", "17:00", "12:00", "13:00");
        await vm.AddTemplateAsync("F", "جمعه", "06:00", "12:00", null, null);
        var day = vm.Templates.Single(t => t.Stored.Value.Code == "D").Stored.Value.Id;
        var friday = vm.Templates.Single(t => t.Stored.Value.Code == "F").Stored.Value.Id;
        await vm.AddUnitShiftAsync(unit.Value.Id, day, "2", "1", false);
        Assert.False(vm.IsErrorMessage, vm.Message);
        Assert.Equal("الگوی شرکت", vm.UnitShifts.Single().DaysText);

        await vm.AddUnitShiftAsync(unit.Value.Id, friday, "2", "1", false, UnitShift.MaskOf([DayOfWeek.Friday]));
        Assert.False(vm.IsErrorMessage, vm.Message);
        Assert.Equal("جمعه", vm.UnitShifts.Single(u => u.ShiftText == "F").DaysText);

        // A dated exception: on Saturday 1405/08/02 the unit works only 08:00-12:00; wrong input is reported in Persian.
        await vm.AddUnitDateHoursAsync(unit.Value.Id, "1405/08/02", "08:00", "12:00", "1", "نیم‌روز");
        Assert.False(vm.IsErrorMessage, vm.Message);
        await vm.AddUnitDateHoursAsync(unit.Value.Id, "not a date", "08:00", "12:00", "1", "x");
        Assert.Equal(Messages.ForIssue(ValidationIssue.DateInvalid, JalaliDateError.None), vm.Message);
        await vm.AddUnitDateHoursAsync(unit.Value.Id, "1405/08/03", "08:00", "08:00", "1", "x");
        Assert.Equal(Messages.ForIssue(ValidationIssue.ShiftTimesInvalid, JalaliDateError.None), vm.Message);
        await vm.AddUnitDateHoursAsync(unit.Value.Id, "1405/08/03", "08:00", "12:00", "1", " ");
        Assert.Equal(Messages.ForIssue(ValidationIssue.CalendarReasonRequired, JalaliDateError.None), vm.Message);

        // Others only see the calendar.
        var qc = For(h, h.Qc);
        await qc.LoadAsync(Token);
        Assert.False(qc.CanMaintain);
    }
}
