using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;

namespace ERP.MES.Desktop.Tests;

// The materials screen against the real Application services (no window is opened).
public class MaterialsViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static MaterialsViewModel For(WorkflowHarness h, Principal user) => new(h.Workflow.Materials, h.Dates, DigitStyle.Persian, () => user);

    private static async Task<MaterialsViewModel> WithMaterialAsync(WorkflowHarness h)
    {
        var vm = For(h, h.Warehouse);
        await vm.LoadAsync(Token);
        Assert.True(await vm.AddMaterialAsync("ST37-R30", "میلگرد", "ST37", "میلگرد", Token));
        vm.SelectedMaterial = vm.Materials.Single();
        return vm;
    }

    [Fact]
    public async Task Warehouse_registers_a_material_and_others_only_read_the_list()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var warehouse = await WithMaterialAsync(h);
        var production = For(h, h.Production);

        await production.LoadAsync(Token);

        Assert.True(warehouse.CanMaintain);
        Assert.Equal("ماده ثبت شد.", warehouse.Message);
        Assert.Equal("ثبت نشده", warehouse.Materials.Single().LatestPriceText);
        Assert.False(production.CanMaintain);
        Assert.False(production.CanViewPrices);
        Assert.Equal("ST37-R30", production.Materials.Single().Code);
        Assert.Equal("—", production.Materials.Single().LatestPriceText);
        Assert.False(await production.AddMaterialAsync("AL-01", "x", null, null, Token));
        Assert.Equal(Messages.NotAuthorized, production.Message);
    }

    [Fact]
    public async Task A_price_is_shown_in_persian_digits_in_rial_with_history_newest_first_and_planning_cannot_change_it()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = await WithMaterialAsync(h);

        Assert.True(await vm.AddPriceForSelectedAsync("۹۰۰۰۰", "۱۴۰۵/۰۱/۰۱", "اولیه", Token));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await vm.AddPriceForSelectedAsync("105500", "1405/07/01", null, Token));

        Assert.Equal("قیمت ثبت شد.", vm.Message);
        Assert.Equal(2, vm.Prices.Count);
        Assert.Equal("۱۰۵,۵۰۰ ریال بر کیلوگرم", vm.Prices[0].PriceText);
        Assert.Equal("۱۴۰۵/۰۷/۰۱", vm.Prices[0].EffectiveFromText);
        Assert.Equal("۹۰,۰۰۰ ریال بر کیلوگرم", vm.Prices[1].PriceText);
        Assert.Equal("اولیه", vm.Prices[1].ReasonText);
        Assert.Contains("۱۰۵,۵۰۰", vm.Materials.Single().LatestPriceText, StringComparison.Ordinal);

        var planning = For(h, h.Planning);
        await planning.LoadAsync(Token);
        planning.SelectedMaterial = planning.Materials.Single();
        Assert.True(planning.CanViewPrices);
        Assert.False(planning.CanChangePrices);
        Assert.Equal(2, planning.Prices.Count);
        Assert.False(await planning.AddPriceForSelectedAsync("1", "1406/01/01", null, Token));
        Assert.Equal(Messages.NotAuthorized, planning.Message);
    }

    [Fact]
    public async Task Bad_price_input_and_nothing_selected_show_persian_messages()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = await WithMaterialAsync(h);

        Assert.False(await vm.AddPriceForSelectedAsync("abc", "1405/01/01", null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.PriceInvalid, JalaliDateError.None), vm.Message);
        Assert.False(await vm.AddPriceForSelectedAsync("100", "not a date", null, Token));
        Assert.Empty(vm.Prices);

        vm.SelectedMaterial = null;
        Assert.False(await vm.AddPriceForSelectedAsync("100", "1405/01/01", null, Token));
        Assert.Equal(Messages.NothingSelected, vm.Message);
    }

    [Fact]
    public async Task A_stale_row_reloads_after_a_conflict()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = await WithMaterialAsync(h);
        var other = For(h, h.Warehouse);
        await other.LoadAsync(Token);
        other.SelectedMaterial = other.Materials.Single();
        Assert.True(await other.UpdateSelectedAsync("نام جدید", "ST52", null, Token));

        Assert.False(await vm.SetSelectedActiveAsync(false, Token));

        Assert.Equal("نام جدید", vm.Materials.Single().Name);
        Assert.Equal("فعال", vm.Materials.Single().StatusText);
    }
}
