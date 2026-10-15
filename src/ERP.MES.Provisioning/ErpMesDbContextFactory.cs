using ERP.MES.Infrastructure.Persistence;
using ERP.MES.Infrastructure.Sqlite;
using Microsoft.EntityFrameworkCore.Design;

namespace ERP.MES.Provisioning;

/// <summary>
/// Design-time factory used by the dotnet-ef tool to create migrations. It lives in the startup project (Provisioning), where the tool
/// discovers it; the migrations themselves are in ERP.MES.Infrastructure.Sqlite. It never opens or creates a real database file.
/// </summary>
public sealed class ErpMesDbContextFactory : IDesignTimeDbContextFactory<ErpMesDbContext>
{
    public ErpMesDbContext CreateDbContext(string[] args) =>
        new(SqliteDatabase.CreateOptions(Path.Combine(Path.GetTempPath(), "erpmes-design-time.db")));
}
