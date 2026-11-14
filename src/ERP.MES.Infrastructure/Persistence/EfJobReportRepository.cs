using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Costing;
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
        if (await context.DailyJobReports.AnyAsync(r => r.OperatorId == report.OperatorId && r.WorkDate == report.WorkDate && r.ShiftTemplateId == report.ShiftTemplateId && r.AmendmentNo == report.AmendmentNo, cancellationToken))
        {
            throw new DuplicateJobReportException("A report of this operator for this date and shift already exists.");
        }

        context.DailyJobReports.Add(report);
        context.AuditLog.Add(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<DailyJobReport>(report, context.GetRowVersion(report));
    }

    public async Task<Stored<DailyJobReport>> UpdateAsync(long id, long expectedVersion, Action<DailyJobReport> change, AuditLogEntry audit, CancellationToken cancellationToken = default, StaleCostMarking? staleCost = null)
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
        if (staleCost is not null)
        {
            await MarkCostStaleAsync(context, report.Id, staleCost, cancellationToken);
        }

        // The report row itself must change so its version moves even when only children changed.
        context.Entry(report).Property(r => r.RevisionNo).IsModified = true;
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<DailyJobReport>(report, context.GetRowVersion(report));
    }

    public async Task<Stored<DailyJobReport>> ReopenAsync(long originalId, long expectedVersion, Func<DailyJobReport, DailyJobReport> reopen, AuditLogEntry audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reopen);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var original = await Load(context).SingleOrDefaultAsync(r => r.Id == originalId, cancellationToken) ?? throw new RecordNotFoundException("daily job report", originalId);
        PersistenceSave.CheckExpectedVersion(context, original, expectedVersion);
        if (await context.DailyJobReports.AnyAsync(r => r.PreviousRevisionReportId == originalId, cancellationToken))
        {
            throw new InvalidOperationException("The report already has an amendment.");
        }

        var revision = reopen(original);
        context.DailyJobReports.Add(revision);
        context.AuditLog.Add(audit);

        // The original is not changed except for the appended event; its row version moves so concurrent reopening is detected.
        context.Entry(original).Property(r => r.RevisionNo).IsModified = true;
        try
        {
            await PersistenceSave.SaveAsync(context, cancellationToken);
        }
        catch (PersistenceException)
        {
            // Two reopenings at once: the unique key of operator, date, shift and amendment number refuses the second.
            throw new InvalidOperationException("The report already has an amendment.");
        }

        return new Stored<DailyJobReport>(revision, context.GetRowVersion(revision));
    }

    /// <summary>Marks the latest cost snapshot of each item, and its price approval, as no longer current because of the approved amendment (owner decision B2). Warehouse records are never touched.</summary>
    private static async Task MarkCostStaleAsync(ErpMesDbContext context, long revisionReportId, StaleCostMarking marking, CancellationToken cancellationToken)
    {
        foreach (var itemId in marking.ItemIds.Distinct())
        {
            var latest = await context.ItemCostCalculations.AsNoTracking().Where(c => c.RequestItemId == itemId).OrderByDescending(c => c.SequenceNo).Select(c => (long?)c.Id).FirstOrDefaultAsync(cancellationToken);
            if (latest is not { } calculationId)
            {
                continue;
            }

            if (!await context.RecordInvalidations.AnyAsync(i => i.Kind == InvalidatedRecordKind.CostSnapshot && i.TargetId == calculationId, cancellationToken))
            {
                context.RecordInvalidations.Add(new RecordInvalidation(itemId, InvalidatedRecordKind.CostSnapshot, calculationId, InvalidationEffect.Invalidated, marking.Reason, marking.ByUserId, marking.AtUtc, jobReportRevisionId: revisionReportId));
            }

            var decisionId = await context.ItemPriceDecisions.AsNoTracking().Where(d => d.CalculationId == calculationId).Select(d => (long?)d.Id).FirstOrDefaultAsync(cancellationToken);
            if (decisionId is { } decision && !await context.RecordInvalidations.AnyAsync(i => i.Kind == InvalidatedRecordKind.PriceDecision && i.TargetId == decision, cancellationToken))
            {
                context.RecordInvalidations.Add(new RecordInvalidation(itemId, InvalidatedRecordKind.PriceDecision, decision, InvalidationEffect.Invalidated, marking.Reason, marking.ByUserId, marking.AtUtc, jobReportRevisionId: revisionReportId));
            }
        }
    }

    private static IQueryable<DailyJobReport> Load(ErpMesDbContext context) =>
        context.DailyJobReports.Include(r => r.Lines.OrderBy(l => l.LineNo)).Include(r => r.Events.OrderBy(e => e.Id)).AsSplitQuery();
}
