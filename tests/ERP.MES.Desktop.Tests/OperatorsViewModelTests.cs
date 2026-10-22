using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Tests;

// The operators screen against the real Application services (no window is opened).
public class OperatorsViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static OperatorsViewModel For(WorkflowHarness h, Principal user) =>
        new(h.Workflow.Operators, h.Workflow.PlanningMaster, h.Dates, () => user);

    private static async Task<(OperatorsViewModel Vm, long MachineId)> WithMachineAndOperatorAsync(WorkflowHarness h)
    {
        var unit = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Planning, "MILLING", "فرزکاری", Token)).Value!;
        var machine = (await h.Workflow.PlanningMaster.AddMachineAsync(h.Planning, "FCC-06", "فرز CNC", unit.Value.Id, Token)).Value!;
        var vm = For(h, h.Planning);
        await vm.LoadAsync(Token);
        Assert.True(await vm.RegisterAsync("OP-01", "علی رضایی", Token));
        return (vm, machine.Value.Id);
    }

    [Fact]
    public async Task Planning_registers_an_operator_and_production_only_reads()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var (planning, _) = await WithMachineAndOperatorAsync(h);
        var production = For(h, h.Production);

        await production.LoadAsync(Token);

        Assert.True(planning.CanMaintain);
        Assert.Equal("اپراتور ثبت شد.", planning.Message);
        Assert.Equal("OP-01", planning.Operators.Single().Code);
        Assert.Equal("فعال", planning.Operators.Single().StatusText);
        Assert.False(production.CanMaintain);
        Assert.Equal("OP-01", production.Operators.Single().Code);
        Assert.False(await production.RegisterAsync("OP-02", "x", Token));
        Assert.Equal(Messages.NotAuthorized, production.Message);
    }

    [Fact]
    public async Task Qc_and_warehouse_see_nothing_and_get_the_not_authorized_message()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await WithMachineAndOperatorAsync(h);

        var qc = For(h, h.Qc);
        await qc.LoadAsync(Token);

        Assert.Empty(qc.Operators);
        Assert.Equal(Messages.NotAuthorized, qc.Message);
    }

    [Fact]
    public async Task An_assignment_without_qualification_warns_and_then_shows_the_qualification_state_in_persian_dates()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var (vm, machineId) = await WithMachineAndOperatorAsync(h);
        var operatorId = vm.Operators.Single().Id;

        Assert.True(await vm.AssignAsync(operatorId, machineId, "۱۴۰۵/۰۷/۰۱", true, null, Token));
        Assert.Contains("هشدار", vm.Message, StringComparison.Ordinal);
        var row = vm.Assignments.Single();
        Assert.Equal("ندارد", row.QualifiedText);
        Assert.Equal("۱۴۰۵/۰۷/۰۱", row.FromText);
        Assert.Equal("باز", row.ToText);
        Assert.Equal("اصلی", row.PrimaryText);

        Assert.True(await vm.RecordQualificationAsync(operatorId, machineId, "1405/06/01", "گواهی", Token));
        Assert.Equal("گواهی", vm.Qualifications.Single().NoteText);
        Assert.Equal("دارد", vm.Assignments.Single().QualifiedText);
    }

    [Fact]
    public async Task Closing_the_selected_assignment_and_ending_the_qualification_keep_the_rows()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var (vm, machineId) = await WithMachineAndOperatorAsync(h);
        var operatorId = vm.Operators.Single().Id;
        Assert.True(await vm.AssignAsync(operatorId, machineId, "1405/07/01", false, null, Token));
        Assert.True(await vm.RecordQualificationAsync(operatorId, machineId, "1405/07/01", null, Token));

        Assert.False(await vm.CloseSelectedAssignmentAsync("1405/07/10", Token));
        Assert.Equal(Messages.NothingSelected, vm.Message);

        vm.SelectedAssignment = vm.Assignments.Single();
        Assert.True(await vm.CloseSelectedAssignmentAsync("1405/07/10", Token));
        Assert.Equal("۱۴۰۵/۰۷/۱۰", vm.Assignments.Single().ToText);

        vm.SelectedQualification = vm.Qualifications.Single();
        Assert.True(await vm.EndSelectedQualificationAsync("1405/07/10", Token));
        Assert.Single(vm.Qualifications);
        Assert.False(vm.Qualifications.Single().IsOpen);
    }

    [Fact]
    public async Task Bad_input_shows_a_persian_message_and_a_stale_row_reloads()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var (vm, machineId) = await WithMachineAndOperatorAsync(h);
        var operatorId = vm.Operators.Single().Id;

        Assert.False(await vm.AssignAsync(operatorId, machineId, "نادرست", true, null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.DateInvalid, ERP.MES.Domain.Calendar.JalaliDateError.None), vm.Message);
        Assert.False(await vm.RegisterAsync("op-bad", "x", Token));

        // Another session renames the operator; this screen still holds the old version.
        var other = For(h, h.Planning);
        await other.LoadAsync(Token);
        other.SelectedOperator = other.Operators.Single();
        Assert.True(await other.RenameSelectedAsync("نام جدید", Token));

        vm.SelectedOperator = vm.Operators.Single();
        Assert.False(await vm.SetSelectedActiveAsync(false, Token));
        Assert.Equal("نام جدید", vm.Operators.Single().FullName);
        Assert.Equal("فعال", vm.Operators.Single().StatusText);
    }
}
