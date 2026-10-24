using ERP.MES.Application.Persistence;
using ERP.MES.Domain.Reporting;
using ERP.MES.Domain.Requests;
using ERP.MES.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Read-only facts for the unit metrics on <see cref="ErpMesDbContext"/> (provider-neutral). Everything is read with no tracking in one context; the calculator filters the widened periods exactly.</summary>
public sealed class EfKpiRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IKpiRepository
{
    public async Task<KpiFacts> LoadAsync(DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var units = await context.ProductionUnits.AsNoTracking().Select(u => new KpiUnit(u.Id, u.Code, u.Name)).ToListAsync(cancellationToken);
        var operators = await context.Operators.AsNoTracking().Select(o => new KpiOperator(o.Id, o.Code, o.FullName)).ToListAsync(cancellationToken);

        var reports = await context.DailyJobReports.Include(r => r.Lines).AsNoTracking().Where(r => r.WorkDate >= from && r.WorkDate <= toInclusive && r.Status == JobReportStatus.Approved).ToListAsync(cancellationToken);
        var lines = reports.SelectMany(r => r.Lines.Select(l => new KpiApprovedLine(r.Id, r.OperatorId, r.ProductionUnitId, r.WorkDate, l.StartUtc, l.EndUtc, l.ProductiveMinutes))).ToList();
        var pending = (await context.DailyJobReports.AsNoTracking().Where(r => r.WorkDate >= from && r.WorkDate <= toInclusive && r.Status == JobReportStatus.Submitted).Select(r => r.ProductionUnitId).ToListAsync(cancellationToken))
            .GroupBy(id => id).Select(g => new KpiPending(g.Key, g.Count())).ToList();

        // QC decisions of the widened period, attributed to the unit that performed the stage.
        var windowStart = DateTime.SpecifyKind(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var windowEnd = DateTime.SpecifyKind(toInclusive.AddDays(2).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var recent = await context.QcInspectionLines.AsNoTracking().Where(l => l.DecidedAtUtc >= windowStart && l.DecidedAtUtc < windowEnd).ToListAsync(cancellationToken);
        var itemIds = recent.Select(l => l.RequestItemId).Distinct().ToList();
        var stages = (await context.ItemStages.AsNoTracking()
            .Where(s => s.ProductionUnitId != null && itemIds.Contains(EF.Property<long>(s, "RequestItemId")))
            .Select(s => new { ItemId = EF.Property<long>(s, "RequestItemId"), s.StageNo, UnitId = s.ProductionUnitId!.Value, s.Status, s.PlannedMinutesPerPiece, s.PlannedQuantity })
            .ToListAsync(cancellationToken)).ToDictionary(s => (s.ItemId, s.StageNo));
        var inspections = recent.Where(l => stages.ContainsKey((l.RequestItemId, l.StageNo)))
            .Select(l => new KpiInspection(stages[(l.RequestItemId, l.StageNo)].UnitId, l.Disposition, l.BaseUnits, l.DecidedAtUtc)).ToList();

        // Stages that are QC-approved and had a decision in the widened period: last decision time over all their decisions, and the approved minutes reported for them.
        var allLines = await context.QcInspectionLines.AsNoTracking().Where(l => itemIds.Contains(l.RequestItemId)).Select(l => new { l.RequestItemId, l.StageNo, l.DecidedAtUtc }).ToListAsync(cancellationToken);
        var approved = await context.DailyJobReports.Include(r => r.Lines).AsNoTracking()
            .Where(r => r.Status == JobReportStatus.Approved && r.Lines.Any(l => itemIds.Contains(l.RequestItemId))).ToListAsync(cancellationToken);
        var actual = approved.SelectMany(r => r.Lines).Where(l => itemIds.Contains(l.RequestItemId)).GroupBy(l => (l.RequestItemId, l.StageNo)).ToDictionary(g => g.Key, g => g.Sum(l => l.ProductiveMinutes));
        var completed = stages.Values.Where(s => s.Status == ItemStageStatus.QcApproved && allLines.Any(l => l.RequestItemId == s.ItemId && l.StageNo == s.StageNo))
            .Select(s => new KpiCompletedStage(
                s.UnitId, s.ItemId, s.StageNo, s.PlannedMinutesPerPiece * s.PlannedQuantity, actual.GetValueOrDefault((s.ItemId, s.StageNo)),
                allLines.Where(l => l.RequestItemId == s.ItemId && l.StageNo == s.StageNo).Max(l => l.DecidedAtUtc)))
            .ToList();

        var templates = await context.ShiftTemplates.AsNoTracking().ToListAsync(cancellationToken);
        var working = templates.ToDictionary(t => t.Id, t => t.WorkingMinutes);
        var scheduled = (await context.WorkerSchedules.AsNoTracking().Where(w => w.Status == WorkerScheduleStatus.Planned && w.Date >= from && w.Date <= toInclusive).ToListAsync(cancellationToken))
            .Select(w => new KpiScheduledRow(w.ProductionUnitId, w.OperatorId, w.Date, working.GetValueOrDefault(w.ShiftTemplateId))).ToList();

        var overtime = (await context.OvertimeAssignments.AsNoTracking().Where(a => a.Status == OvertimeStatus.Approved && a.WorkDate >= from.AddDays(-1) && a.WorkDate <= toInclusive.AddDays(1)).ToListAsync(cancellationToken))
            .Select(a => new KpiOvertimeWindow(a.OperatorId, a.StartLocal, a.EndLocal)).ToList();

        return new KpiFacts(units, operators, lines, pending, inspections, completed, scheduled, overtime);
    }
}
