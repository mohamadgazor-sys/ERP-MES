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

    Task<Stored<DailyJobReport>> UpdateAsync(
        long id, long expectedVersion, Action<DailyJobReport> change, AuditLogEntry audit, CancellationToken cancellationToken = default, StaleCostMarking? staleCost = null);

    /// <summary>
    /// Reopens an approved report (owner decision B2): <paramref name="reopen"/> appends the event to the original and returns the new amendment row, both saved with the audit entry in one transaction.
    /// Refused with <see cref="InvalidOperationException"/> when the original already has an amendment (open or approved).
    /// </summary>
    Task<Stored<DailyJobReport>> ReopenAsync(long originalId, long expectedVersion, Func<DailyJobReport, DailyJobReport> reopen, AuditLogEntry audit, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cost data changed by an approved amendment (owner decision B2, Decision Log #142): the items whose latest cost snapshot (and its price approval) become stale in the same transaction that approves the amendment.
/// Never touches a Warehouse record.
/// </summary>
public sealed record StaleCostMarking(IReadOnlyCollection<long> ItemIds, string Reason, long ByUserId, DateTime AtUtc);

/// <summary>The operator already has a report for that date and shift.</summary>
public sealed class DuplicateJobReportException(string message) : Exception(message);
