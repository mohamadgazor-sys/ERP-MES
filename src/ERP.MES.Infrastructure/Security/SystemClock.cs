using ERP.MES.Application.Security;

namespace ERP.MES.Infrastructure.Security;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>Machine name and operating-system user of the running process. SQLite reports no database login.</summary>
public sealed class EnvironmentAuditContext : IAuditContext
{
    public string MachineName => Environment.MachineName;

    public string OsUserName => Environment.UserName;

    public string? DatabaseLogin => null;
}
