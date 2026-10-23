using ERP.MES.Application.Security;
using ERP.MES.Domain.Reporting;

namespace ERP.MES.Application.Persistence;

/// <summary>Persistence of daily job reports (Decision Log #102). Every write is one transaction with its audit entry and uses the row version.</summary>
public interface IJobReportRepository
{
    Task<IReadOnlyList<Stored<DailyJobReport>>> ListAsync(DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken = default);

    Task<Stored<DailyJobReport>?> FindAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Adds a report; a second report of the same operator, date and shift fails with <see cref="DuplicateJobReportException"/>.</summary>
    Task<Stored<DailyJobReport>> AddAsync(DailyJobReport report, AuditLogEntry audit, CancellationToken cancellationToken = default);

    Task<Stored<DailyJobReport>> UpdateAsync(long id, long expectedVersion, Action<DailyJobReport> change, AuditLogEntry audit, CancellationToken cancellationToken = default);
}

/// <summary>The operator already has a report for that date and shift.</summary>
public sealed class DuplicateJobReportException(string message) : Exception(message);
