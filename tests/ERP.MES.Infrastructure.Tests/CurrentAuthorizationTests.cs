using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;

namespace ERP.MES.Infrastructure.Tests;

// Decision Log #74: the desktop Principal is a UI snapshot and never authorizes a business operation by itself. Every role-sensitive command checks the user's
// CURRENT status, department and role in the database. Real SQLite database, real services.
public class CurrentAuthorizationTests
{
    private const string UserName = "manager.one";
    private const string InitialPassword = "Staff-Pass-Phrase-2";
    private const string OwnPassword = "My-Own-Pass-Phrase-3";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed record Scene(SecurityFixture Fixture, Principal Admin, Principal User, long UserId);

    private static async Task<Scene> StartAsync(string department = DepartmentCodes.Management, string role = RoleCodes.Manager)
    {
        var fixture = await SecurityFixture.CreateAsync();
        var admin = await fixture.BootstrapAdminAsync(cancellationToken: Token);
        var id = await fixture.CreateUserAsync(admin, UserName, InitialPassword, department, role, Token);
        var first = await fixture.SignInAsync(UserName, InitialPassword, Token);
        await fixture.Services.Accounts.ChangeOwnPasswordAsync(first, InitialPassword, OwnPassword, Token);
        return new Scene(fixture, admin, await fixture.SignInAsync(UserName, OwnPassword, Token), id);
    }

    [Fact]
    public async Task An_active_user_with_the_right_department_and_role_is_authorized_now()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;

        var decision = await scene.Fixture.Services.Authorization.CheckAsync(scene.User, Permission.ApproveOrRejectInitialRequest, Token);

        Assert.True(decision.IsAllowed);
        Assert.Equal(scene.UserId, decision.Current!.UserId);
        Assert.Equal(scene.User, await scene.Fixture.Services.Authorization.RequireAsync(scene.User, Permission.ApproveOrRejectInitialRequest, Token));
    }

    [Fact]
    public async Task A_changed_role_cannot_keep_authorizing_through_a_stale_principal()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        var stale = scene.User;
        Assert.Equal(RoleCodes.Manager, stale.RoleCode);

        Assert.Equal(AccountStatus.Succeeded, (await scene.Fixture.Services.Accounts.ChangeRoleAsync(scene.Admin, scene.UserId, RoleCodes.Staff, Token)).Status);

        // The snapshot still says MANAGER - that is exactly why it must not decide anything.
        Assert.Equal(RoleCodes.Manager, stale.RoleCode);
        Assert.True(AuthorizationRules.IsAllowed(stale, Permission.ApproveOrRejectInitialRequest), "the snapshot alone would still allow it");
        var decision = await scene.Fixture.Services.Authorization.CheckAsync(stale, Permission.ApproveOrRejectInitialRequest, Token);
        Assert.False(decision.IsAllowed);
        Assert.Equal(RoleCodes.Staff, decision.Current!.RoleCode);
        var refusal = await Assert.ThrowsAsync<NotAuthorizedException>(() => scene.Fixture.Services.Authorization.RequireAsync(stale, Permission.ApproveOrRejectInitialRequest, Token));
        Assert.Equal(Permission.ApproveOrRejectInitialRequest, refusal.Permission);
    }

    [Fact]
    public async Task A_changed_department_cannot_keep_authorizing_through_a_stale_principal()
    {
        var scene = await StartAsync(DepartmentCodes.Planning, RoleCodes.Staff);
        using var _ = scene.Fixture;
        Assert.True((await scene.Fixture.Services.Authorization.CheckAsync(scene.User, Permission.ChangePlanningPriority, Token)).IsAllowed);

        scene.Fixture.Db.Execute("UPDATE Users SET DepartmentId = (SELECT Id FROM Departments WHERE Code = '" + DepartmentCodes.Qc + "') WHERE Username = '" + UserName + "'");

        Assert.True(AuthorizationRules.IsAllowed(scene.User, Permission.ChangePlanningPriority), "the snapshot alone would still allow it");
        Assert.False((await scene.Fixture.Services.Authorization.CheckAsync(scene.User, Permission.ChangePlanningPriority, Token)).IsAllowed);
        Assert.True((await scene.Fixture.Services.Authorization.CheckAsync(scene.User, Permission.InspectOrCorrectQc, Token)).IsAllowed);
    }

    [Fact]
    public async Task A_deactivated_user_loses_every_permission_at_the_next_operation()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        Assert.Equal(AccountStatus.Succeeded, (await scene.Fixture.Services.Accounts.SetActiveAsync(scene.Admin, scene.UserId, false, Token)).Status);

        foreach (var permission in AuthorizationRules.Defined)
        {
            Assert.False((await scene.Fixture.Services.Authorization.CheckAsync(scene.User, permission, Token)).IsAllowed, permission.ToString());
        }

        await Assert.ThrowsAsync<NotAuthorizedException>(() => scene.Fixture.Services.Authorization.RequireAsync(scene.User, Permission.ApproveOrRejectInitialRequest, Token));
    }

    [Fact]
    public async Task A_reactivated_user_is_authorized_again_without_a_new_principal()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        await scene.Fixture.Services.Accounts.SetActiveAsync(scene.Admin, scene.UserId, false, Token);
        await scene.Fixture.Services.Accounts.SetActiveAsync(scene.Admin, scene.UserId, true, Token);

        // The database, not the object in the window, is the authority - in both directions.
        Assert.True((await scene.Fixture.Services.Authorization.CheckAsync(scene.User, Permission.ApproveOrRejectInitialRequest, Token)).IsAllowed);
    }

    [Fact]
    public async Task A_user_who_must_still_choose_a_password_has_no_operational_access()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        await scene.Fixture.Services.Accounts.ResetPasswordAsync(scene.Admin, scene.UserId, InitialPassword, Token);

        var decision = await scene.Fixture.Services.Authorization.CheckAsync(scene.User, Permission.ApproveOrRejectInitialRequest, Token);

        Assert.False(decision.IsAllowed);
        Assert.True(decision.Current!.MustChangePassword);
    }

    [Fact]
    public async Task No_principal_an_unknown_user_or_an_unknown_permission_is_denied_by_default()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        var authorization = scene.Fixture.Services.Authorization;

        Assert.False((await authorization.CheckAsync(null, Permission.ApproveOrRejectInitialRequest, Token)).IsAllowed);
        Assert.False((await authorization.CheckAsync(scene.User with { UserId = 987_654 }, Permission.ApproveOrRejectInitialRequest, Token)).IsAllowed);
        Assert.Null((await authorization.CheckAsync(scene.User with { UserId = 987_654 }, Permission.ApproveOrRejectInitialRequest, Token)).Current);
        Assert.False((await authorization.CheckAsync(scene.User, (Permission)9999, Token)).IsAllowed);
        Assert.False((await authorization.CheckAsync(scene.User, Permission.AdministerUsers, Token)).IsAllowed);
        await Assert.ThrowsAsync<NotAuthorizedException>(() => authorization.RequireAsync(null, Permission.ApproveOrRejectInitialRequest, Token));
    }

    [Fact]
    public async Task A_forged_principal_cannot_grant_what_the_database_does_not()
    {
        var scene = await StartAsync(DepartmentCodes.Planning, RoleCodes.Staff);
        using var _ = scene.Fixture;
        var forged = scene.User with { DepartmentCode = DepartmentCodes.Management, RoleCode = RoleCodes.Admin };

        Assert.True(AuthorizationRules.IsAllowed(forged, Permission.AdministerUsers), "the forged snapshot claims it");
        Assert.False((await scene.Fixture.Services.Authorization.CheckAsync(forged, Permission.AdministerUsers, Token)).IsAllowed);
        Assert.False((await scene.Fixture.Services.Authorization.CheckAsync(forged, Permission.ApproveOrRejectInitialRequest, Token)).IsAllowed);
    }

    [Fact]
    public async Task The_check_writes_nothing()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;
        var before = scene.Fixture.AuditActionsInOrder().Count;

        await scene.Fixture.Services.Authorization.CheckAsync(scene.User, Permission.ApproveOrRejectInitialRequest, Token);
        await scene.Fixture.Services.Authorization.CheckAsync(scene.User, Permission.AdministerUsers, Token);

        Assert.Equal(before, scene.Fixture.AuditActionsInOrder().Count);
    }

    [Fact]
    public async Task The_last_active_administrator_protection_is_unchanged()
    {
        var scene = await StartAsync();
        using var _ = scene.Fixture;

        Assert.Equal(AccountStatus.LastAdministrator, (await scene.Fixture.Services.Accounts.SetActiveAsync(scene.Admin, scene.Admin.UserId, false, Token)).Status);
        Assert.Equal(AccountStatus.LastAdministrator, (await scene.Fixture.Services.Accounts.ChangeRoleAsync(scene.Admin, scene.Admin.UserId, RoleCodes.Staff, Token)).Status);
    }
}
