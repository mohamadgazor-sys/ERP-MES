using Microsoft.Data.SqlClient;

namespace ERP.MES.Infrastructure.SqlServer.Tests;

/// <summary>
/// A throw-away database on the SQL Server named by the environment variable <c>ERPMES_SQLSERVER</c> (a connection string with the right to create databases, supplied by the CI job; it is never
/// written to a file). Without the variable every test is skipped and says so - a developer machine without SQL Server does not fail, and a green result without a server is never reported as a pass.
/// </summary>
internal sealed class SqlServerFixture : IAsyncDisposable
{
    public const string Variable = "ERPMES_SQLSERVER";

    private readonly string _server;
    private readonly string _name;

    private SqlServerFixture(string server, string name)
    {
        _server = server;
        _name = name;
        ConnectionString = new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString;
    }

    public string ConnectionString { get; }

    public static async Task<SqlServerFixture> CreateAsync()
    {
        var server = Environment.GetEnvironmentVariable(Variable);
        Assert.SkipWhen(string.IsNullOrWhiteSpace(server), "No SQL Server available: set ERPMES_SQLSERVER (see docs/SQL_SERVER_PASS.md). This test did NOT run.");
        var name = "erpmes_it_" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(server!, $"CREATE DATABASE [{name}]");
        return new SqlServerFixture(server!, name);
    }

    public async Task<List<string>> QueryAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<string>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await ExecuteAsync(_server, $"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}]");
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
