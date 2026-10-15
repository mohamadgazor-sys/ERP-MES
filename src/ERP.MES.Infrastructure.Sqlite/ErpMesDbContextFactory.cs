using ERP.MES.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace ERP.MES.Infrastructure.Sqlite;

/// <summary>Design-time factory used by the dotnet-ef tool to create migrations. It never opens or creates a real database file.</summary>
public sealed class ErpMesDbContextFactory : IDesignTimeDbContextFactory<ErpMesDbContext>
{
    public ErpMesDbContext CreateDbContext(string[] args) =>
        new(SqliteDatabase.CreateOptions(Path.Combine(Path.GetTempPath(), "erpmes-design-time.db")));
}
