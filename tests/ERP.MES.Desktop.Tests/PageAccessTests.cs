using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;

namespace ERP.MES.Desktop.Tests;

// Which pages a user sees (Decision Log #122 B2). The menu is a convenience; the Application checks every command (see the authorization tests).
public class PageAccessTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly TimeZoneInfo SiteZone = TimeZoneInfo.CreateCustomTimeZone("test-site", TimeSpan.FromMinutes(210), "test-site", "test-site");

    private static async Task<IReadOnlySet<string>> VisibleAsync(WorkflowHarness h, Principal user) =>
        await PageAccess.VisibleKeysAsync(h.Security.Authorization, user, Token);

    [Fact]
    public async Task Everyone_sees_the_dashboard_and_the_limitations_page_and_nobody_sees_pages_without_a_permission()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);

        foreach (var user in new[] { h.Admin, h.Central, h.Planning, h.Drawing, h.Qc, h.Management, h.Production, h.Warehouse, h.ProductionManager })
        {
            var visible = await VisibleAsync(h, user);
            Assert.Contains(ShellViewModel.DashboardKey, visible);
            Assert.Contains(ShellViewModel.LimitationsKey, visible);
        }

        var production = await VisibleAsync(h, h.Production);
        Assert.DoesNotContain(ShellViewModel.UsersKey, production);
        Assert.DoesNotContain(ShellViewModel.CostingKey, production);
        Assert.DoesNotContain(ShellViewModel.ContractsKey, production);
        Assert.DoesNotContain(ShellViewModel.ItemImportKey, production);
        Assert.Contains(ShellViewModel.WorkBoardKey, production);
        Assert.Contains(ShellViewModel.UnitQueueKey, production);

        Assert.DoesNotContain(ShellViewModel.StageTitlesKey, production);
        Assert.Contains(ShellViewModel.StageTitlesKey, await VisibleAsync(h, h.Planning));

        var qc = await VisibleAsync(h, h.Qc);
        Assert.Contains(ShellViewModel.QcInspectionKey, qc);
        Assert.DoesNotContain(ShellViewModel.UnitQueueKey, qc);
        Assert.DoesNotContain(ShellViewModel.OvertimeKey, qc);
    }

    [Fact]
    public async Task The_users_page_is_for_program_admin_alone_and_master_data_pages_follow_the_new_ownership()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);

        foreach (var user in new[] { h.Central, h.Planning, h.Drawing, h.Qc, h.Management, h.Manager, h.Production, h.Warehouse, h.ProductionManager })
        {
            Assert.DoesNotContain(ShellViewModel.UsersKey, await VisibleAsync(h, user));
        }

        var admin = await VisibleAsync(h, h.Admin);
        Assert.Contains(ShellViewModel.UsersKey, admin);
        Assert.Contains(ShellViewModel.MachinesKey, admin);
        Assert.Contains(ShellViewModel.OperatorsKey, admin);
        Assert.DoesNotContain(ShellViewModel.ItemImportKey, admin);
        Assert.DoesNotContain(ShellViewModel.UnitQueueKey, admin);

        var productionManager = await VisibleAsync(h, h.ProductionManager);
        Assert.Contains(ShellViewModel.UnitQueueKey, productionManager);
        Assert.Contains(ShellViewModel.OvertimeKey, productionManager);
        Assert.Contains(ShellViewModel.KpiKey, productionManager);
        Assert.Contains(ShellViewModel.CalendarKey, productionManager);
        Assert.DoesNotContain(ShellViewModel.CostingKey, productionManager);
        Assert.DoesNotContain(ShellViewModel.ItemImportKey, productionManager);
    }

    [Fact]
    public async Task A_user_who_must_still_choose_a_password_or_is_unknown_sees_only_the_always_visible_pages()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var ghost = new Principal(424242, "ghost", "ghost", "PLANNING", "STAFF", false);

        Assert.Equal(
            new[] { ShellViewModel.DashboardKey, ShellViewModel.LimitationsKey }.OrderBy(k => k),
            (await VisibleAsync(h, ghost)).OrderBy(k => k));
    }

    [Fact]
    public async Task Applying_access_narrows_the_navigation_keeps_the_order_and_leaves_a_hidden_page_for_the_dashboard()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var shell = new ShellViewModel(new JalaliDateService(DigitStyle.Persian, SiteZone), () => DateTime.UtcNow);
        var allKeys = shell.Items.Select(i => i.Key).ToArray();
        shell.Navigate(ShellViewModel.CostingKey);

        shell.ApplyAccess(await VisibleAsync(h, h.Production));

        var keys = shell.Items.Select(i => i.Key).ToArray();
        Assert.Equal(allKeys.Where(k => keys.Contains(k)).ToArray(), keys);
        Assert.DoesNotContain(ShellViewModel.CostingKey, keys);
        Assert.Equal(ShellViewModel.DashboardKey, shell.SelectedItem.Key);
        Assert.Throws<ArgumentException>(() => shell.Navigate(ShellViewModel.CostingKey));

        // A later refresh for a user with more pages brings them back in their original positions.
        shell.ApplyAccess(await VisibleAsync(h, h.Admin));
        Assert.Contains(ShellViewModel.UsersKey, shell.Items.Select(i => i.Key));
        var restored = shell.Items.Select(i => i.Key).ToArray();
        Assert.Equal(allKeys.Where(k => restored.Contains(k)).ToArray(), restored);
    }

    [Fact]
    public void Every_guarded_page_has_a_navigation_entry_and_every_navigation_entry_is_guarded_or_always_visible()
    {
        var shell = new ShellViewModel(new JalaliDateService(DigitStyle.Persian, SiteZone), () => DateTime.UtcNow);
        var keys = shell.Items.Select(i => i.Key).ToHashSet();

        Assert.All(PageAccess.GuardedKeys, key => Assert.Contains(key, keys));
        Assert.All(keys.Where(k => k is not (ShellViewModel.DashboardKey or ShellViewModel.LimitationsKey)), key => Assert.Contains(key, PageAccess.GuardedKeys));
    }
}
