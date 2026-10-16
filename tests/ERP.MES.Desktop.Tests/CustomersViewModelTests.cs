using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Desktop.Tests;

// The customer screen's state machine against the real Application services on a temporary SQLite database (no window is opened).
public class CustomersViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_empty_list_shows_the_empty_state_and_central_may_maintain()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = h.CustomersFor(h.Central);
        Assert.False(vm.IsEmpty);

        await vm.LoadAsync(Token);

        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Customers);
        Assert.True(vm.CanMaintain);
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsLoading);
        Assert.Equal(string.Empty, vm.Message);
    }

    [Fact]
    public async Task A_user_without_the_right_sees_the_list_but_the_buttons_stay_disabled_and_a_change_is_refused()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await h.CustomersFor(h.Central).CreateAsync("مشتری نمونه", Token);
        var vm = h.CustomersFor(h.Qc);

        await vm.LoadAsync(Token);
        var saved = await vm.CreateAsync("Not allowed", Token);

        Assert.Single(vm.Customers);
        Assert.False(vm.CanMaintain);
        Assert.False(saved);
        Assert.Equal(Messages.NotAuthorized, vm.Message);
        Assert.Single((await h.Workflow.Customers.ListAsync(h.Admin, false, Token)).Value!);
    }

    [Fact]
    public async Task Creating_a_customer_persists_it_and_it_is_still_listed_after_a_restart()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = h.CustomersFor(h.Central);
        await vm.LoadAsync(Token);

        var saved = await vm.CreateAsync("مشتری نمونه (فرضی)", Token);

        Assert.True(saved);
        Assert.Equal("مشتری ثبت شد.", vm.Message);
        var row = Assert.Single(vm.Customers);
        Assert.Equal("مشتری نمونه (فرضی)", row.Name);
        Assert.Equal("فعال", row.StatusText);
        Assert.False(vm.IsEmpty);
        Assert.Equal(row, vm.Selected);

        var afterRestart = h.CustomersFor(h.Planning, h.Restarted());
        await afterRestart.LoadAsync(Token);
        Assert.Equal("مشتری نمونه (فرضی)", Assert.Single(afterRestart.Customers).Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_missing_name_shows_the_persian_validation_message_and_saves_nothing(string? name)
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = h.CustomersFor(h.Central);
        await vm.LoadAsync(Token);

        var saved = await vm.CreateAsync(name, Token);

        Assert.False(saved);
        Assert.Equal(Messages.ForIssue(ValidationIssue.CustomerNameRequired, JalaliDateError.None), vm.Message);
        Assert.Empty(vm.Customers);
        Assert.Empty((await h.Workflow.Customers.ListAsync(h.Central, false, Token)).Value!);
    }

    [Fact]
    public async Task Renaming_the_selected_customer_changes_it_and_with_nothing_selected_says_so()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = h.CustomersFor(h.Central);
        await vm.CreateAsync("Old name", Token);

        vm.Selected = null;
        Assert.False(await vm.RenameSelectedAsync("New name", Token));
        Assert.Equal(Messages.NothingSelected, vm.Message);

        vm.Selected = vm.Customers.Single();
        Assert.True(await vm.RenameSelectedAsync("New name", Token));
        Assert.Equal("نام مشتری تغییر کرد.", vm.Message);
        Assert.Equal("New name", vm.Customers.Single().Name);
        Assert.Equal("New name", (await h.Restarted().Customers.ListAsync(h.Central, false, Token)).Value!.Single().Value.Name);
    }

    [Fact]
    public async Task A_change_made_by_someone_else_is_reported_and_the_list_is_reloaded_not_overwritten()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var central = h.CustomersFor(h.Central);
        var planning = h.CustomersFor(h.Planning);
        await central.CreateAsync("Original", Token);
        await planning.LoadAsync(Token);
        planning.Selected = planning.Customers.Single();
        central.Selected = central.Customers.Single();

        Assert.True(await central.RenameSelectedAsync("Renamed by Central", Token));
        var saved = await planning.RenameSelectedAsync("Renamed by Planning", Token);

        Assert.False(saved);
        Assert.Equal(Messages.Conflict, planning.Message);
        Assert.Equal("Renamed by Central", planning.Customers.Single().Name);
        Assert.Equal("Renamed by Central", (await h.Workflow.Customers.ListAsync(h.Central, false, Token)).Value!.Single().Value.Name);

        // The screen now holds the current version, so the second attempt goes through.
        planning.Selected = planning.Customers.Single();
        Assert.True(await planning.RenameSelectedAsync("Renamed by Planning", Token));
    }

    [Fact]
    public async Task Deactivating_hides_the_customer_unless_inactive_ones_are_shown_and_it_can_be_activated_again()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = h.CustomersFor(h.Central);
        await vm.CreateAsync("Customer", Token);
        vm.Selected = vm.Customers.Single();

        Assert.True(await vm.SetSelectedActiveAsync(false, Token));
        Assert.Equal("مشتری غیرفعال شد.", vm.Message);
        Assert.Empty(vm.Customers);
        Assert.True(vm.IsEmpty);

        vm.ShowInactive = true;
        await vm.LoadAsync(Token);
        var inactive = Assert.Single(vm.Customers);
        Assert.Equal("غیرفعال", inactive.StatusText);

        vm.Selected = inactive;
        Assert.True(await vm.SetSelectedActiveAsync(true, Token));
        Assert.Equal("فعال", vm.Customers.Single().StatusText);
    }

    private sealed class ThrowingCustomers(Exception failure) : ICustomerRepository
    {
        public Task<Stored<Customer>> AddAsync(Customer customer, CancellationToken cancellationToken = default) => throw failure;

        public Task<Stored<Customer>?> FindAsync(long customerId, CancellationToken cancellationToken = default) => throw failure;

        public Task<IReadOnlyList<Stored<Customer>>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default) => throw failure;

        public Task<Stored<Customer>> UpdateAsync(long customerId, long? expectedVersion, Action<Customer> change, CancellationToken cancellationToken = default) => throw failure;
    }

    [Fact]
    public async Task A_database_failure_becomes_a_persian_message_and_never_crashes_the_screen()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var failing = new CustomerService(new ThrowingCustomers(new PersistenceException("The data could not be read.")), h.Security.Authorization, h.Clock);
        var vm = new CustomersViewModel(failing, h.Workflow.Requests, h.Dates, () => h.Central);

        await vm.LoadAsync(Token);
        Assert.Equal(Messages.Failed, vm.Message);
        Assert.False(vm.IsEmpty);

        Assert.False(await vm.CreateAsync("Name", Token));
        Assert.Equal(Messages.Failed, vm.Message);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task An_unexpected_exception_is_caught_and_shown_as_a_generic_message()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var throwing = new CustomerService(new ThrowingCustomers(new InvalidOperationException("boom")), h.Security.Authorization, h.Clock);
        var vm = new CustomersViewModel(throwing, h.Workflow.Requests, h.Dates, () => h.Central);

        await vm.LoadAsync(Token);

        Assert.Equal(Messages.Unexpected, vm.Message);
        Assert.False(vm.IsBusy);
        Assert.False(vm.IsLoading);
        Assert.DoesNotContain("boom", vm.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deactivated_user_loses_access_at_the_next_action_even_with_an_old_principal()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = h.CustomersFor(h.Central);
        await vm.LoadAsync(Token);
        Assert.Equal(AccountStatus.Succeeded, (await h.Security.Accounts.SetActiveAsync(h.Admin, h.Central.UserId, false, Token)).Status);

        var saved = await vm.CreateAsync("Late", Token);
        await vm.LoadAsync(Token);

        Assert.False(saved);
        Assert.Equal(Messages.NotAuthorized, vm.Message);
        Assert.False(vm.CanMaintain);
        Assert.Empty((await h.Workflow.Customers.ListAsync(h.Admin, false, Token)).Value!);
    }
}
