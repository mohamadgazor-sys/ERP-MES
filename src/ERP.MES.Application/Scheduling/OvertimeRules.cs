using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Application.Scheduling;

/// <summary>How much overtime an operator would have on the day and in the week, and whether that is outside the limits.</summary>
public sealed record OvertimeUsage(int DayMinutes, int WeekMinutes, bool ExceedsLimits);

/// <summary>
/// The rules for overtime (Decision Log #89, #95 D5, #103), pure over a context. Overtime is outside the normal shift: it may not overlap the operator's planned shift or other overtime, the operator
/// must not be absent, and a machine has one operator at a time. Only approved assignments (and those waiting for Management) count against the limits; the week starts on Saturday
/// (a labelled default, matching the regular week of #13).
/// </summary>
public static class OvertimeRules
{
    public static ValidationIssue CheckBookable(OvertimeAssignment candidate, OvertimeContext context)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(context);
        if (!context.OperatorActive)
        {
            return ValidationIssue.OperatorInvalid;
        }

        if (!context.MachineUsable)
        {
            return ValidationIssue.MachineInvalid;
        }

        var lastDay = DateOnly.FromDateTime(candidate.EndLocal.AddTicks(-1));
        if (context.OperatorOffDays.Any(o => o.IsActive && o.OperatorId == candidate.OperatorId && o.FromDate <= lastDay && o.ToDate >= candidate.WorkDate))
        {
            return ValidationIssue.OvertimeOperatorAbsent;
        }

        var templates = context.Templates.ToDictionary(t => t.Id);
        foreach (var row in context.PlannedRows.Where(r => r.IsPlanned && templates.ContainsKey(r.ShiftTemplateId)))
        {
            var template = templates[row.ShiftTemplateId];
            var start = row.Date.ToDateTime(template.StartTime);
            var end = start.AddMinutes(template.TotalMinutes);
            if (!(start < candidate.EndLocal && candidate.StartLocal < end))
            {
                continue;
            }

            if (row.OperatorId == candidate.OperatorId)
            {
                return ValidationIssue.OvertimeOperatorOverlap;
            }

            if (row.MachineId == candidate.MachineId)
            {
                return ValidationIssue.OvertimeMachineBusy;
            }
        }

        foreach (var other in context.Assignments.Where(a => a.IsLive && a.Id != candidate.Id && a.Overlaps(candidate.StartLocal, candidate.EndLocal)))
        {
            if (other.OperatorId == candidate.OperatorId)
            {
                return ValidationIssue.OvertimeOperatorOverlap;
            }

            if (other.MachineId == candidate.MachineId)
            {
                return ValidationIssue.OvertimeMachineBusy;
            }
        }

        return ValidationIssue.None;
    }

    /// <summary>The usage if <paramref name="candidate"/> were approved; the limits must be configured.</summary>
    public static OvertimeUsage Usage(OvertimeAssignment candidate, OvertimeContext context)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(context);
        var limits = context.Limits ?? throw new InvalidOperationException("The overtime limits are not configured.");
        var weekStart = WeekStart(candidate.WorkDate);
        var weekEnd = weekStart.AddDays(6);
        var committed = context.Assignments.Where(a => a.Id != candidate.Id && a.OperatorId == candidate.OperatorId && a.Status is OvertimeStatus.Approved or OvertimeStatus.AwaitingManagement).ToList();
        var day = candidate.Minutes + committed.Where(a => a.WorkDate == candidate.WorkDate).Sum(a => a.Minutes);
        var week = candidate.Minutes + committed.Where(a => a.WorkDate >= weekStart && a.WorkDate <= weekEnd).Sum(a => a.Minutes);
        return new OvertimeUsage(day, week, day > limits.MaxMinutesPerDay || week > limits.MaxMinutesPerWeek);
    }

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek - (int)DayOfWeek.Saturday + 7) % 7));
}
