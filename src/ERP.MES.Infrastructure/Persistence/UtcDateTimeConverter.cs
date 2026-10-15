using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>
/// Timestamps are stored as UTC (Decision Log #24), but SQLite returns them with an unspecified kind. The domain requires
/// UTC timestamps, so values read from the database are marked as UTC. Nothing is converted: the stored text already is UTC.
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }
}
