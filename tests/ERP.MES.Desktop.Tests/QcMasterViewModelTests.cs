using ERP.MES.Application.Requests;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Quality;

namespace ERP.MES.Desktop.Tests;

// The QC master-data screen against the real Application services (no window is opened): QC maintains, everyone reads, the screen only mirrors the Application's answers.
public class QcMasterViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static QcMasterViewModel For(WorkflowHarness h, ERP.MES.Application.Security.Principal user) => new(h.Workflow.QcMaster, () => user);

    [Fact]
    public async Task Qc_sees_the_standard_definitions_and_may_maintain_other_departments_only_read()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var qc = For(h, h.Qc);
        var planning = For(h, h.Planning);

        await qc.LoadAsync(Token);
        await planning.LoadAsync(Token);

        Assert.True(qc.CanMaintain);
        Assert.False(planning.CanMaintain);
        Assert.Equal(new[] { "PCS", "PAIR", "KG", "MM" }, planning.Units.Select(u => u.Code).ToArray());
        Assert.Equal("شمارشی", planning.Units[0].KindText);
        Assert.Equal("فعال", planning.Units[0].StatusText);
        var conversion = Assert.Single(planning.Conversions);
        Assert.Equal("۱ PAIR", conversion.FromText);
        Assert.Equal("2 PCS", conversion.ToText);
        Assert.Equal("۱ PCS = 0.5 PAIR", conversion.ReciprocalText);
        Assert.Equal(new[] { "PIECE", "PAIR" }, planning.QuantityTypes.Select(q => q.Code).ToArray());
        Assert.Equal("2 PCS", planning.QuantityTypes[1].MappingText);
        Assert.Equal(new[] { "PCS — عدد (قطعه)", "PAIR — جفت" }, planning.BaseUnitChoices.Select(c => c.Text).ToArray());
        Assert.False(await planning.AddUnitAsync("BOX", "جعبه", UnitKind.Count, Token));
        Assert.Equal(Messages.NotAuthorized, planning.Message);
    }

    [Fact]
    public async Task Qc_adds_a_unit_a_conversion_and_quantity_types_and_the_lists_show_them()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = For(h, h.Qc);
        await vm.LoadAsync(Token);

        Assert.True(await vm.AddUnitAsync("BOX", "جعبه", UnitKind.Count, Token));
        Assert.Equal("واحد ثبت شد.", vm.Message);
        var box = vm.Units.Single(u => u.Code == "BOX");
        Assert.True(await vm.AddConversionAsync(box.Id, 1, "۱۲", Token));
        Assert.Equal("۱ BOX", vm.Conversions.Last().FromText);
        Assert.Equal("12 PCS", vm.Conversions.Last().ToText);

        Assert.True(await vm.AddQuantityTypeAsync("TRIPLE", "سه‌تایی", 1, "۳", null, Token));
        Assert.Equal(3, vm.QuantityTypes.Single(q => q.Code == "TRIPLE").BaseUnitsPerType);
        Assert.True(await vm.AddQuantityTypeAsync("SET-A", "ست الف", 1, null, "شفت: ۱\nبوش: 2\nواشر = 4", Token));
        var set = vm.QuantityTypes.Single(q => q.Code == "SET-A");
        Assert.Equal(7, set.BaseUnitsPerType);
        Assert.Equal("شفت (1) + بوش (2) + واشر (4)", set.MappingText);
        Assert.Contains(vm.UnitChoices, c => c.Text.StartsWith("BOX", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Bad_input_gives_persian_messages_and_saves_nothing()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = For(h, h.Qc);
        await vm.LoadAsync(Token);

        Assert.False(await vm.AddUnitAsync("pcs", "x", UnitKind.Count, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.CodeInvalid, JalaliDateError.None), vm.Message);
        Assert.False(await vm.AddUnitAsync("PCS", "x", UnitKind.Count, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.CodeDuplicate, JalaliDateError.None), vm.Message);
        Assert.False(await vm.AddConversionAsync(1, 2, "abc", Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.FactorInvalid, JalaliDateError.None), vm.Message);
        Assert.False(await vm.AddConversionAsync(4242, 1, "2", Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.UnitNotFound, JalaliDateError.None), vm.Message);
        Assert.False(await vm.AddConversionAsync(1, 2, "2", Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.ConversionDuplicate, JalaliDateError.None), vm.Message);
        Assert.False(await vm.AddQuantityTypeAsync("X", "x", 1, "0", null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.BaseUnitsInvalid, JalaliDateError.None), vm.Message);
        Assert.False(await vm.AddQuantityTypeAsync("X", "x", 1, "1.5", null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.BaseUnitsInvalid, JalaliDateError.None), vm.Message);
        Assert.False(await vm.AddQuantityTypeAsync("X", "x", 1, null, "فقط یک عضو: 2", Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.SetMembersInvalid, JalaliDateError.None), vm.Message);
        Assert.False(await vm.AddQuantityTypeAsync("X", "x", 1, null, "الف: 1\nب بدون تعداد", Token));
        Assert.False(await vm.AddQuantityTypeAsync("X", "x", 3, "2", null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.BaseUnitInvalid, JalaliDateError.None), vm.Message);
        Assert.Equal(4, vm.Units.Count);
        Assert.Single(vm.Conversions);
        Assert.Equal(2, vm.QuantityTypes.Count);
    }

    [Fact]
    public async Task Rename_and_retire_work_on_the_selected_row_and_an_outdated_screen_gets_a_conflict()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var first = For(h, h.Qc);
        var second = For(h, h.Qc);
        await first.LoadAsync(Token);
        await second.LoadAsync(Token);
        first.SelectedUnit = first.Units.Single(u => u.Code == "KG");
        second.SelectedUnit = second.Units.Single(u => u.Code == "KG");
        h.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(await first.RenameSelectedUnitAsync("کیلوگرم (وزن)", Token));
        Assert.Equal("کیلوگرم (وزن)", first.Units.Single(u => u.Code == "KG").Name);
        Assert.False(await second.SetSelectedUnitActiveAsync(false, Token));
        Assert.Equal(Messages.Conflict, second.Message);
        Assert.Equal("کیلوگرم (وزن)", second.Units.Single(u => u.Code == "KG").Name);

        second.SelectedUnit = second.Units.Single(u => u.Code == "KG");
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await second.SetSelectedUnitActiveAsync(false, Token));
        Assert.Equal("غیرفعال", second.Units.Single(u => u.Code == "KG").StatusText);

        first.SelectedType = null;
        Assert.False(await first.RenameSelectedTypeAsync("x", Token));
        Assert.Equal(Messages.NothingSelected, first.Message);
    }
}
