using ERP.MES.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.SqlServer;

/// <summary>
/// SQL Server wiring for <see cref="ErpMesDbContext"/> (Decision Log #117): connection options and a context factory. The business logic and every repository are provider-neutral and unchanged;
/// the only provider-specific model detail (the binary collation of the case-sensitive business keys) is applied in <see cref="ErpMesDbContext"/> when the provider is SQL Server.
/// </summary>
public static class SqlServerDatabase
{
    public static DbContextOptions<ErpMesDbContext> CreateOptions(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new DbContextOptionsBuilder<ErpMesDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(SqlServerDatabase).Assembly.GetName().Name))
            .Options;
    }

    /// <summary>A factory that creates a new context for each operation; the repositories use it, one context per call.</summary>
    public static IDbContextFactory<ErpMesDbContext> CreateContextFactory(string connectionString) => new SqlServerContextFactory(CreateOptions(connectionString));

    private sealed class SqlServerContextFactory(DbContextOptions<ErpMesDbContext> options) : IDbContextFactory<ErpMesDbContext>
    {
        public ErpMesDbContext CreateDbContext() => new(options);
    }
}
