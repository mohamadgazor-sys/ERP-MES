using ERP.MES.Application.Security;
using Microsoft.EntityFrameworkCore;
using ERP.MES.Infrastructure.Persistence;

namespace ERP.MES.Infrastructure.Security;

/// <summary>
/// The security unit on <see cref="ErpMesDbContext"/> (provider-neutral). One unit is one short-lived context; <see cref="SaveAsync"/> is one transaction. A row changed by
/// someone else after it was loaded fails the save through the row-version concurrency token, which is the conditional update the bootstrap, recovery and last-administrator rules rely on.
/// </summary>
internal sealed class EfSecurityUnit(ErpMesDbContext context) : ISecurityUnit
{
    public Task<SystemSetup> GetSetupAsync(CancellationToken cancellationToken) =>
        context.SystemSetups.SingleAsync(s => s.Id == SystemSetup.SingleRowId, cancellationToken);

    public Task<UserAccount?> FindUserByUsernameAsync(string username, CancellationToken cancellationToken) =>
        context.Users.Include(u => u.Department).Include(u => u.Role).FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

    public Task<UserAccount?> FindUserByIdAsync(long userId, CancellationToken cancellationToken) =>
        context.Users.Include(u => u.Department).Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public Task<Department> GetDepartmentAsync(string code, CancellationToken cancellationToken) =>
        context.Departments.SingleAsync(d => d.Code == code, cancellationToken);

    public Task<Role> GetRoleAsync(string code, CancellationToken cancellationToken) =>
        context.Roles.SingleAsync(r => r.Code == code, cancellationToken);

    public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken) =>
        context.Users.CountAsync(u => u.IsActive && u.Role.Code == RoleCodes.Admin, cancellationToken);

    public void AddUser(UserAccount user) => context.Users.Add(user);

    public void AddAudit(AuditLogEntry entry) => context.AuditLog.Add(entry);

    public Task SaveAsync(CancellationToken cancellationToken) => PersistenceSave.SaveAsync(context, cancellationToken);
}

public sealed class EfSecurityUnitRunner(IDbContextFactory<ErpMesDbContext> contextFactory) : ISecurityUnitRunner
{
    public async Task<T> RunAsync<T>(Func<ISecurityUnit, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await work(new EfSecurityUnit(context));
    }
}
