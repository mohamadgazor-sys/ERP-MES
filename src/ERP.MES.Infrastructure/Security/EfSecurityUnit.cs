using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
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

    // The user, its department and its role are loaded with the credential. Lookup is by the canonical key of the Domain (Decision Log #62); a text that is not a
    // valid user name has no key and so finds nothing.
    private IQueryable<UserCredential> Accounts =>
        context.UserCredentials.Include(c => c.User).ThenInclude(u => u.Department).Include(c => c.User).ThenInclude(u => u.Role);

    public Task<UserCredential?> FindAccountByUsernameAsync(string? username, CancellationToken cancellationToken)
    {
        var key = Username.KeyOfValid(username);
        return key is null
            ? Task.FromResult<UserCredential?>(null)
            : Accounts.FirstOrDefaultAsync(c => c.User.UsernameKey == key, cancellationToken);
    }

    public Task<UserCredential?> FindAccountByUserIdAsync(long userId, CancellationToken cancellationToken) =>
        Accounts.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public Task<Department> GetDepartmentAsync(string code, CancellationToken cancellationToken) =>
        context.Departments.SingleAsync(d => d.Code == code, cancellationToken);

    public Task<Role> GetRoleAsync(string code, CancellationToken cancellationToken) =>
        context.Roles.SingleAsync(r => r.Code == code, cancellationToken);

    public Task<int> CountActiveAdministratorsAsync(CancellationToken cancellationToken) =>
        context.Users.CountAsync(u => u.IsActive && u.Role.Code == RoleCodes.Admin, cancellationToken);

    public void AddAccount(UserCredential account) => context.UserCredentials.Add(account);

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
