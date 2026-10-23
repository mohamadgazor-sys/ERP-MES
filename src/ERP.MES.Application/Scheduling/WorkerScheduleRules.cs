using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Application.Scheduling;

/// <summary>
/// The D5 rules for planning a worker (Decision Log #95, #100), pure over a snapshot: (1) the unit runs the shift and it works that date; (2) the operator is active and not absent;
/// (3) a worker is never planned in two overlapping occurrences (a shift that crosses midnight is compared with the neighbouring days); (4) the unit-shift's maximum capacity is not exceeded;
/// (5) a machine belongs to the unit and has at most one operator at a time, except an explicit handover overlap whose stated length covers the overlap.
/// </summary>
public static class WorkerScheduleRules
{
    public static ValidationIssue Check(WorkerSchedule candidate, ScheduleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(snapshot);
        var config = snapshot.Configuration;
        var unitShift = config.UnitShifts.Select(u => u.Value).FirstOrDefault(u => u.IsActive && u.ProductionUnitId == candidate.ProductionUnitId && u.ShiftTemplateId == candidate.ShiftTemplateId);
        var template = config.Templates.Select(t => t.Value).FirstOrDefault(t => t.IsActive && t.Id == candidate.ShiftTemplateId);
        if (unitShift is null || template is null)
        {
            return ValidationIssue.ScheduleUnitShiftNotConfigured;
        }

        if (!config.ToCalendar().IsWorking(candidate.ProductionUnitId, candidate.ShiftTemplateId, candidate.Date))
        {
            return ValidationIssue.ScheduleDayOff;
        }

        if (!snapshot.OperatorActive)
        {
            return ValidationIssue.OperatorInvalid;
        }

        if (snapshot.OperatorOffDays.Any(o => o.Covers(candidate.Date)))
        {
            return ValidationIssue.ScheduleOperatorAbsent;
        }

        var templates = config.Templates.ToDictionary(t => t.Value.Id, t => t.Value);
        var own = Interval(candidate, template);
        var planned = snapshot.PlannedRows.Where(r => r.IsPlanned).ToList();

        foreach (var other in planned.Where(r => r.OperatorId == candidate.OperatorId))
        {
            if (templates.TryGetValue(other.ShiftTemplateId, out var otherTemplate) && Overlaps(own, Interval(other, otherTemplate)))
            {
                return ValidationIssue.ScheduleWorkerOverlap;
            }
        }

        if (planned.Count(r => r.Date == candidate.Date && r.ProductionUnitId == candidate.ProductionUnitId && r.ShiftTemplateId == candidate.ShiftTemplateId) >= unitShift.MaxOperators)
        {
            return ValidationIssue.ScheduleCapacityExceeded;
        }

        if (candidate.MachineId is null)
        {
            return candidate.HandoverMinutes is null ? ValidationIssue.None : ValidationIssue.HandoverInvalid;
        }

        if (!snapshot.MachineUsable)
        {
            return ValidationIssue.MachineInvalid;
        }

        foreach (var other in planned.Where(r => r.MachineId == candidate.MachineId && r.OperatorId != candidate.OperatorId))
        {
            if (!templates.TryGetValue(other.ShiftTemplateId, out var otherTemplate))
            {
                continue;
            }

            var overlap = OverlapMinutes(own, Interval(other, otherTemplate));
            if (overlap <= 0)
            {
                continue;
            }

            var stated = Math.Max(candidate.HandoverMinutes ?? 0, other.HandoverMinutes ?? 0);
            if (stated == 0)
            {
                return ValidationIssue.ScheduleMachineBusy;
            }

            if (overlap > stated)
            {
                return ValidationIssue.HandoverInvalid;
            }
        }

        return ValidationIssue.None;
    }

    /// <summary>Flags for rows that no longer satisfy the calendar (a day marked off, or an off-day recorded, after the row was planned): nothing is cancelled silently, Planning resolves them.</summary>
    public static ScheduleFlags Flags(WorkerSchedule row, CalendarConfiguration config, IReadOnlyList<OperatorOffDay> offDays)
    {
        if (!row.IsPlanned)
        {
            return ScheduleFlags.None;
        }

        var flags = ScheduleFlags.None;
        var configured = config.UnitShifts.Any(u => u.Value.IsActive && u.Value.ProductionUnitId == row.ProductionUnitId && u.Value.ShiftTemplateId == row.ShiftTemplateId)
            && config.Templates.Any(t => t.Value.IsActive && t.Value.Id == row.ShiftTemplateId);
        if (!configured)
        {
            flags |= ScheduleFlags.ShiftNoLongerRun;
        }
        else if (!config.ToCalendar().IsWorking(row.ProductionUnitId, row.ShiftTemplateId, row.Date))
        {
            flags |= ScheduleFlags.DayNowOff;
        }

        if (offDays.Any(o => o.OperatorId == row.OperatorId && o.Covers(row.Date)))
        {
            flags |= ScheduleFlags.OperatorAbsent;
        }

        return flags;
    }

    private static (DateTime Start, DateTime End) Interval(WorkerSchedule row, ShiftTemplate template)
    {
        var start = row.Date.ToDateTime(template.StartTime);
        return (start, start.AddMinutes(template.TotalMinutes));
    }

    private static bool Overlaps((DateTime Start, DateTime End) a, (DateTime Start, DateTime End) b) => a.Start < b.End && b.Start < a.End;

    private static int OverlapMinutes((DateTime Start, DateTime End) a, (DateTime Start, DateTime End) b)
    {
        var start = a.Start > b.Start ? a.Start : b.Start;
        var end = a.End < b.End ? a.End : b.End;
        return end > start ? (int)(end - start).TotalMinutes : 0;
    }
}

[Flags]
public enum ScheduleFlags
{
    None = 0,
    DayNowOff = 1,
    OperatorAbsent = 2,
    ShiftNoLongerRun = 4,
}
