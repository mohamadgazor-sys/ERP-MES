using ERP.MES.Application.Requests;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;

namespace ERP.MES.Desktop.Tests;

// The Planning resource screen against the real Application services (no window is opened).
public class PlanningMasterViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static PlanningMasterViewModel For(WorkflowHarness h, ERP.MES.Application.Security.Principal user) =>
        new(h.Workflow.PlanningMaster, h.Dates, DigitStyle.Persian, () => user);

    private static async Task<PlanningMasterViewModel> WithMachineAsync(WorkflowHarness h)
    {
        var vm = For(h, h.Planning);
        await vm.LoadAsync(Token);
        Assert.True(await vm.AddUnitAsync("MILLING", "فرزکاری", Token));
        Assert.True(await vm.AddMachineAsync("FCC-06", "فرز CNC", vm.Units.Single().Id, Token));
        vm.SelectedMachine = vm.Machines.Single();
        return vm;
    }

    [Fact]
    public async Task Planning_registers_a_unit_and_a_machine_and_other_departments_only_read()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var planning = await WithMachineAsync(h);
        var production = For(h, h.Production);

        await production.LoadAsync(Token);

        Assert.True(planning.CanMaintainResources);
        Assert.True(planning.CanChangeRates);
        Assert.Equal("FCC-06", planning.Machines.Single().Code);
        Assert.Equal("MILLING", planning.Machines.Single().UnitText);
        Assert.Equal("ثبت نشده", planning.Machines.Single().LatestRateText);
        Assert.False(production.CanMaintainResources);
        Assert.False(production.CanChangeRates);
        Assert.False(production.CanViewRates);
        Assert.Equal("FCC-06", production.Machines.Single().Code);
        Assert.Equal("—", production.Machines.Single().LatestRateText);
        Assert.False(await production.AddUnitAsync("X", "x", Token));
        Assert.Equal(Messages.NotAuthorized, production.Message);
    }

    [Fact]
    public async Task A_rate_is_added_shown_in_persian_digits_in_rial_and_history_is_kept_newest_first()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = await WithMachineAsync(h);

        Assert.True(await vm.AddRateForSelectedMachineAsync("۱۲۰۰۰۰۰", "۱۴۰۵/۰۱/۰۱", "قرارداد", Token));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await vm.AddRateForSelectedMachineAsync("1500000", "1405/07/01", null, Token));

        Assert.Equal("نرخ ثبت شد.", vm.Message);
        Assert.Equal(2, vm.Rates.Count);
        Assert.Equal("۱,۵۰۰,۰۰۰ ریال در ساعت", vm.Rates[0].RateText);
        Assert.Equal("۱۴۰۵/۰۷/۰۱", vm.Rates[0].EffectiveFromText);
        Assert.Equal("۱,۲۰۰,۰۰۰ ریال در ساعت", vm.Rates[1].RateText);
        Assert.Equal("قرارداد", vm.Rates[1].ReasonText);
        Assert.Contains("۱,۵۰۰,۰۰۰", vm.Machines.Single().LatestRateText, StringComparison.Ordinal);

        var warehouse = For(h, h.Warehouse);
        await warehouse.LoadAsync(Token);
        warehouse.SelectedMachine = warehouse.Machines.Single();
        Assert.True(warehouse.CanViewRates);
        Assert.False(warehouse.CanChangeRates);
        Assert.Equal(2, warehouse.Rates.Count);
        Assert.False(await warehouse.AddRateForSelectedMachineAsync("1", "1405/09/01", null, Token));
        Assert.Equal(Messages.NotAuthorized, warehouse.Message);
    }

    [Fact]
    public async Task Bad_rate_input_gives_a_specific_persian_message_and_saves_nothing()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = await WithMachineAsync(h);
        Assert.True(await vm.AddRateForSelectedMachineAsync("100", "1405/05/01", null, Token));

        Assert.False(await vm.AddRateForSelectedMachineAsync("1,200,000", "1405/07/01", null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.RateInvalid, JalaliDateError.None), vm.Message);
        Assert.False(await vm.AddRateForSelectedMachineAsync("0", "1405/07/01", null, Token));
        Assert.False(await vm.AddRateForSelectedMachineAsync("100", "", null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.EffectiveDateRequired, JalaliDateError.None), vm.Message);
        Assert.False(await vm.AddRateForSelectedMachineAsync("100", "۱۴۰۵/۱۳/۰۱", null, Token));
        Assert.False(await vm.AddRateForSelectedMachineAsync("100", "1405/04/01", null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.RateNotLater, JalaliDateError.None), vm.Message);
        Assert.Single(vm.Rates);

        vm.SelectedMachine = null;
        Assert.False(await vm.AddRateForSelectedMachineAsync("100", "1405/09/01", null, Token));
        Assert.Equal(Messages.NothingSelected, vm.Message);
        Assert.Empty(vm.Rates);
    }

    [Fact]
    public async Task A_duplicate_code_and_an_outdated_screen_are_reported_and_the_current_state_is_shown()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var first = await WithMachineAsync(h);
        var second = For(h, h.Planning);
        await second.LoadAsync(Token);
        second.SelectedMachine = second.Machines.Single();
        h.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.False(await first.AddMachineAsync("FCC-06", "دیگر", first.Units.Single().Id, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.CodeDuplicate, JalaliDateError.None), first.Message);
        Assert.True(await first.RenameSelectedMachineAsync("فرز عمودی", Token));
        Assert.False(await second.SetSelectedMachineActiveAsync(false, Token));
        Assert.Equal(Messages.Conflict, second.Message);
        Assert.Equal("فرز عمودی", second.Machines.Single().Name);
    }
}
