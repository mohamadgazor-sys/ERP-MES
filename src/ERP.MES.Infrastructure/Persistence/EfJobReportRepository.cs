using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Reporting;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Daily job reports on <see cref="ErpMesDbContext"/> (provider-neutral). A report, its lines, its append-only events and the audit entry are one SaveChanges.</summary>
public sealed class EfJobReportRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IJobReportRepository
{
    public async Task<IReadOnlyList<Stored<DailyJobReport>>> ListAsync(DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await Load(context).Where(r => r.WorkDate >= from && r.WorkDate <= toInclusive).OrderBy(r => r.WorkDate).ThenBy(r => r.Id).ToListAsync(cancellationToken);
        return list.Select(r => new Stored<DailyJobReport>(r, context.GetRowVersion(r))).ToList();
    }

    public async Task<Stored<DailyJobReport>?> FindAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var report = await Load(context).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        return report is null ? null : new Stored<DailyJobReport>(report, context.GetRowVersion(report));
    }

    public async Task<Stored<DailyJobReport>> AddAsync(DailyJobReport report, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await context.DailyJobReports.AnyAsync(r => r.OperatorId == report.OperatorId && r.WorkDate == report.WorkDate && r.ShiftTemplateId == report.ShiftTemplateId, cancellationToken))
        {
            throw new DuplicateJobReportException("A report of this operator for this date and shift already exists.");
        }

        context.DailyJobReports.Add(report);
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<DailyJobReport>(report, context.GetRowVersion(report));
    }

    public async Task<Stored<DailyJobReport>> UpdateAsync(long id, long expectedVersion, Action<DailyJobReport> change, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var report = await Load(context).SingleOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new RecordNotFoundException("daily job report", id);
        PersistenceSave.CheckExpectedVersion(context, report, expectedVersion);

        // Lines of a draft are replaced as a whole: the old rows go, the new ones are added (events are only ever added).
        var oldLines = report.Lines.ToList();
        change(report);
        foreach (var line in oldLines.Where(l => !report.Lines.Contains(l)))
        {
            context.Remove(line);
        }

        context.AuditLog.Add(audit);

        // The report row itself must change so its version moves even when only children changed.
        context.Entry(report).Property(r => r.RevisionNo).IsModified = true;
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<DailyJobReport>(report, context.GetRowVersion(report));
    }

    private static IQueryable<DailyJobReport> Load(ErpMesDbContext context) =>
        context.DailyJobReports.Include(r => r.Lines.OrderBy(l => l.LineNo)).Include(r => r.Events.OrderBy(e => e.Id)).AsSplitQuery();
}
