using ERP.MES.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ERP.MES.Infrastructure.SqlServer.Design;

/// <summary>Lets the dotnet-ef tool build the model for SQL Server. The connection string is a placeholder: generating a migration never connects to a database.</summary>
public sealed class ErpMesSqlServerDesignTimeFactory : IDesignTimeDbContextFactory<ErpMesDbContext>
{
    public ErpMesDbContext CreateDbContext(string[] args) =>
        new(SqlServerDatabase.CreateOptions("Server=design-time-placeholder;Database=erpmes_design;Integrated Security=true;TrustServerCertificate=true"));
}

internal static class Program
{
    private static void Main()
    {
    }
}
