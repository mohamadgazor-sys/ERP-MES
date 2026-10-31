using ERP.MES.Application.Security;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Reads the user list (no credential column is touched) on <see cref="ErpMesDbContext"/>.</summary>
public sealed class EfUserDirectoryRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IUserDirectoryRepository
{
    public async Task<IReadOnlyList<UserDirectoryEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var users = await context.Users.AsNoTracking().Include(u => u.Department).Include(u => u.Role).ToListAsync(cancellationToken);
        return users.Select(u => new UserDirectoryEntry(u.Id, u.Username.Value, u.DisplayName, u.Department.Code, u.Role.Code, u.IsActive, u.LastLoginAtUtc)).ToList();
    }
}
