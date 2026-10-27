using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Application.Production;

/// <summary>Whether an operator may be put on a machine at a given local moment (Decision Log #112).</summary>
public enum OperatorAvailability
{
    /// <summary>Scheduled in a running shift of the unit that is on now, or covered by approved overtime.</summary>
    Available = 0,

    /// <summary>The operator record is inactive.</summary>
    Inactive,

    /// <summary>An off-day (leave, sickness...) covers the day: refused, there is no override.</summary>
    Absent,

    /// <summary>Not scheduled in a running shift or approved overtime now: allowed only with a recorded reason from the supervisor.</summary>
    OutsideShift,
}

/// <summary>
/// The availability rule, pure so that its midnight and shift-boundary behaviour can be tested exactly. Local time is the configured application time zone's wall clock (the calendar's own
/// convention). A shift that crosses midnight belongs to the date it STARTED on, so the rule looks at today's and yesterday's schedule rows.
/// </summary>
public static class OperatorAvailabilityRules
{
    public static OperatorAvailability Evaluate(
        DateTime localNow,
        Operator worker,
        long productionUnitId,
        IReadOnlyList<OperatorOffDay> offDays,
        IReadOnlyList<WorkerSchedule> plannedRows,
        IReadOnlyDictionary<long, ShiftTemplate> templates,
        WorkingCalendar calendar,
        IReadOnlyList<OvertimeAssignment> overtime)
    {
        if (!worker.IsActive)
        {
            return OperatorAvailability.Inactive;
        }

        var today = DateOnly.FromDateTime(localNow);
        if (offDays.Any(o => o.OperatorId == worker.Id && (o.Covers(today) || o.Covers(today.AddDays(-1)) && IsInPreviousDayShift(localNow, worker, productionUnitId, plannedRows, templates, today))))
        {
            return OperatorAvailability.Absent;
        }

        foreach (var row in plannedRows.Where(r => r.IsPlanned && r.OperatorId == worker.Id && r.ProductionUnitId == productionUnitId && (r.Date == today || r.Date == today.AddDays(-1))))
        {
            if (!templates.TryGetValue(row.ShiftTemplateId, out var template) || !calendar.IsWorking(productionUnitId, template.Id, row.Date))
            {
                continue;
            }

            var start = row.Date.ToDateTime(template.StartTime);
            if (localNow >= start && localNow < start.AddMinutes(template.TotalMinutes))
            {
                return OperatorAvailability.Available;
            }
        }

        if (overtime.Any(o => o.Status == OvertimeStatus.Approved && o.OperatorId == worker.Id && o.ProductionUnitId == productionUnitId && localNow >= o.StartLocal && localNow < o.EndLocal))
        {
            return OperatorAvailability.Available;
        }

        return OperatorAvailability.OutsideShift;
    }

    // An off-day that ended yesterday still covers the tail of a night shift that started yesterday.
    private static bool IsInPreviousDayShift(DateTime localNow, Operator worker, long unitId, IReadOnlyList<WorkerSchedule> rows, IReadOnlyDictionary<long, ShiftTemplate> templates, DateOnly today) =>
        rows.Any(r => r.IsPlanned && r.OperatorId == worker.Id && r.ProductionUnitId == unitId && r.Date == today.AddDays(-1) && templates.TryGetValue(r.ShiftTemplateId, out var t)
            && localNow < r.Date.ToDateTime(t.StartTime).AddMinutes(t.TotalMinutes));
}
