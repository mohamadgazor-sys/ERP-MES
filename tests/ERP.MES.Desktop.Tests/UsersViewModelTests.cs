using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Identity;

namespace ERP.MES.Desktop.Tests;

// The account administration screen against the real Application services (no window is opened): Program Admin administers, nobody else.
public class UsersViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static UsersViewModel For(WorkflowHarness h, Principal user) =>
        new(h.Security.Accounts, h.Workflow.Users, h.Workflow.UnitDesignations, h.Workflow.PlanningMaster, h.Dates, () => user);

    [Fact]
    public async Task Program_admin_creates_an_account_assigns_its_department_and_a_working_unit_and_everyone_else_is_denied()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var unit = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Admin, "MILLING", "فرزکاری", Token)).Value!.Value.Id;
        var admin = For(h, h.Admin);
        await admin.LoadAsync(Token);

        Assert.False(admin.IsDenied);
        Assert.Contains(admin.Users, u => u.Username == "admin.one");

        Assert.True(await admin.CreateAsync("worker.one", "کارگر یک", DepartmentCodes.Production, RoleCodes.Staff, "Secret-1"), admin.Message);
        Assert.False(admin.IsErrorMessage);
        var created = admin.Users.Single(u => u.Username == "worker.one");
        Assert.Equal("تولید", created.DepartmentText);
        Assert.Equal("کارمند", created.RoleText);
        Assert.Equal("فعال", created.StatusText);
        Assert.Equal("—", created.WorkingUnitsText);

        admin.Selected = created;
        Assert.True(await admin.AssignWorkingUnitAsync(unit), admin.Message);
        Assert.Equal("MILLING", admin.Users.Single(u => u.Username == "worker.one").WorkingUnitsText);

        admin.Selected = admin.Users.Single(u => u.Username == "worker.one");
        Assert.True(await admin.ChangeDepartmentAsync(DepartmentCodes.Qc), admin.Message);
        Assert.Equal("کنترل کیفیت", admin.Users.Single(u => u.Username == "worker.one").DepartmentText);

        admin.Selected = admin.Users.Single(u => u.Username == "worker.one");
        Assert.True(await admin.SetActiveAsync(false), admin.Message);
        Assert.Equal("غیرفعال", admin.Users.Single(u => u.Username == "worker.one").StatusText);

        foreach (var other in new[] { h.Planning, h.Manager, h.ProductionManager, h.Production })
        {
            var denied = For(h, other);
            await denied.LoadAsync(Token);
            Assert.True(denied.IsDenied);
            Assert.Empty(denied.Users);
            Assert.False(await denied.CreateAsync("x.user", "x", DepartmentCodes.Qc, RoleCodes.Staff, "Secret-1"));
            Assert.Equal(Messages.NotAuthorized, denied.Message);
        }
    }

    [Fact]
    public async Task Bad_input_and_the_last_administrator_get_specific_persian_messages()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var admin = For(h, h.Admin);
        await admin.LoadAsync(Token);

        Assert.False(await admin.CreateAsync("", "x", DepartmentCodes.Qc, RoleCodes.Staff, "Secret-1"));
        Assert.Equal("نام کاربری را وارد کنید.", admin.Message);
        Assert.False(await admin.CreateAsync("ok.name", "x", DepartmentCodes.Qc, RoleCodes.Staff, "12 345"));
        Assert.Contains("فاصله", admin.Message, StringComparison.Ordinal);
        Assert.False(await admin.CreateAsync("ADMIN.ONE", "x", DepartmentCodes.Qc, RoleCodes.Staff, "Secret-1"));
        Assert.Contains("قبلاً", admin.Message, StringComparison.Ordinal);
        Assert.False(await admin.CreateAsync("ok.name", "x", null, RoleCodes.Staff, "Secret-1"));
        Assert.Equal(Messages.NothingSelected, admin.Message);

        admin.Selected = admin.Users.Single(u => u.Username == "admin.one");
        Assert.False(await admin.SetActiveAsync(false));
        Assert.Contains("آخرین مدیر سیستم", admin.Message, StringComparison.Ordinal);
        Assert.False(await admin.ChangeRoleAsync(RoleCodes.Staff));
        Assert.Contains("آخرین مدیر سیستم", admin.Message, StringComparison.Ordinal);
    }
}
