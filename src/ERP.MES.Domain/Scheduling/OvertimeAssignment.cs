using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Scheduling;

public enum OvertimeStatus
{
    /// <summary>Created by a unit supervisor; waiting for Planning.</summary>
    Proposed = 1,

    /// <summary>Planning found it outside the configured limits; Management must approve it.</summary>
    AwaitingManagement = 2,

    Approved = 3,

    Rejected = 4,

    Cancelled = 5,
}

/// <summary>
/// The maximum overtime per operator per day and per week (Decision Log #95 D5). One row; it does not exist until Planning configures it, and overtime cannot be approved before that:
/// no numeric limit is invented. Values are minutes.
/// </summary>
public sealed class OvertimeLimits
{
    public const int MaxMinutesPerDayCeiling = 1440;

    public const int MaxMinutesPerWeekCeiling = 10080;

    private OvertimeLimits()
    {
    }

    public OvertimeLimits(int maxMinutesPerDay, int maxMinutesPerWeek, long changedByUserId, DateTime changedAtUtc)
    {
        Id = 1;
        Set(maxMinutesPerDay, maxMinutesPerWeek, changedByUserId, changedAtUtc);
    }

    public long Id { get; private set; }

    public int MaxMinutesPerDay { get; private set; }

    public int MaxMinutesPerWeek { get; private set; }

    public long ChangedByUserId { get; private set; }

    public DateTime ChangedAtUtc { get; private set; }

    public void Set(int maxMinutesPerDay, int maxMinutesPerWeek, long changedByUserId, DateTime changedAtUtc)
    {
        if (maxMinutesPerDay is < 1 or > MaxMinutesPerDayCeiling)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMinutesPerDay), maxMinutesPerDay, "The daily limit is between 1 and 1440 minutes.");
        }

        if (maxMinutesPerWeek < maxMinutesPerDay || maxMinutesPerWeek > MaxMinutesPerWeekCeiling)
        {
            throw new ArgumentOutOfRangeException(nameof(maxMinutesPerWeek), maxMinutesPerWeek, "The weekly limit is at least the daily limit and at most 10080 minutes.");
        }

        MaxMinutesPerDay = maxMinutesPerDay;
        MaxMinutesPerWeek = maxMinutesPerWeek;
        ChangedByUserId = changedByUserId;
        ChangedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }
}

/// <summary>
/// Extra hours an operator is to work on a machine of a unit, outside the normal shift (Decision Log #89, #95 D5, #103). A unit supervisor proposes it, Planning approves it, and Management
/// approves it when it is outside the configured limits. The assignment authorizes hours; it never proves work: hours reach reports, costs and KPIs only through approved job report lines.
/// Times are local wall-clock (the calendar's own convention); the work date is the date of the start. Approval, rejection and cancellation keep who, when and why on the row and in the audit log.
/// </summary>
public sealed class OvertimeAssignment
{
    public const int MaxReasonLength = 300;

    public const int MaxMinutes = 720;

    private OvertimeAssignment()
    {
    }

    public OvertimeAssignment(
        long id, long operatorId, long productionUnitId, long machineId, DateTime startLocal, DateTime endLocal, string? reason, long proposedByUserId, DateTime proposedAtUtc)
    {
        if (endLocal <= startLocal || (endLocal - startLocal).TotalMinutes > MaxMinutes || startLocal.Second != 0 || endLocal.Second != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(endLocal), endLocal, "Overtime lasts from 1 minute to 12 hours and is entered in whole minutes.");
        }

        Id = id;
        OperatorId = operatorId;
        ProductionUnitId = productionUnitId;
        MachineId = machineId;
        StartLocal = DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified);
        EndLocal = DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified);
        WorkDate = DateOnly.FromDateTime(startLocal);
        Reason = CalendarException.RequireReason(reason);
        Status = OvertimeStatus.Proposed;
        ProposedByUserId = proposedByUserId;
        ProposedAtUtc = Requirements.Utc(proposedAtUtc, nameof(proposedAtUtc));
    }

    public long Id { get; private set; }

    public long OperatorId { get; private set; }

    public long ProductionUnitId { get; private set; }

    public long MachineId { get; private set; }

    public DateOnly WorkDate { get; private set; }

    public DateTime StartLocal { get; private set; }

    public DateTime EndLocal { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public OvertimeStatus Status { get; private set; }

    public long ProposedByUserId { get; private set; }

    public DateTime ProposedAtUtc { get; private set; }

    /// <summary>True when Planning found the assignment outside the configured limits (so Management decides).</summary>
    public bool ExceedsLimits { get; private set; }

    public long? PlanningUserId { get; private set; }

    public DateTime? PlanningAtUtc { get; private set; }

    public long? ManagementUserId { get; private set; }

    public DateTime? ManagementAtUtc { get; private set; }

    /// <summary>The reason of a rejection or cancellation (an approval's note is optional and kept here too).</summary>
    public string? DecisionNote { get; private set; }

    public int Minutes => (int)(EndLocal - StartLocal).TotalMinutes;

    /// <summary>Counts against the limits and blocks other bookings: proposed, waiting for Management or approved.</summary>
    public bool IsLive => Status is OvertimeStatus.Proposed or OvertimeStatus.AwaitingManagement or OvertimeStatus.Approved;

    public bool Overlaps(DateTime otherStartLocal, DateTime otherEndLocal) => StartLocal < otherEndLocal && otherStartLocal < EndLocal;

    /// <summary>
    /// The first decision on a proposal (Decision Log #122 Q11): a deciding user approves it. Within the limits it is approved. Outside them it is approved at once when Management itself decides
    /// (<paramref name="byManagement"/>; the same user is recorded as the Management decider) and otherwise (the Production Manager) it goes to Management, because beyond the limits only
    /// Management approves. The first-decision columns keep their historical "Planning" names to avoid a schema rename; they now hold the Management or Production Manager decision.
    /// </summary>
    public void FirstApprove(bool exceedsLimits, string? note, long userId, DateTime atUtc, bool byManagement = false)
    {
        Require(OvertimeStatus.Proposed);
        PlanningUserId = userId;
        PlanningAtUtc = Requirements.Utc(atUtc, nameof(atUtc));
        ExceedsLimits = exceedsLimits;
        DecisionNote = Clean(note);
        if (exceedsLimits && byManagement)
        {
            ManagementUserId = userId;
            ManagementAtUtc = PlanningAtUtc;
        }

        Status = exceedsLimits && !byManagement ? OvertimeStatus.AwaitingManagement : OvertimeStatus.Approved;
    }

    public void ManagementApprove(string? note, long userId, DateTime atUtc)
    {
        Require(OvertimeStatus.AwaitingManagement);
        ManagementUserId = userId;
        ManagementAtUtc = Requirements.Utc(atUtc, nameof(atUtc));
        DecisionNote = Clean(note) ?? DecisionNote;
        Status = OvertimeStatus.Approved;
    }

    /// <summary>A deciding user (Management or Production Manager) rejects a proposal; Management rejects one waiting for it. The reason is mandatory.</summary>
    public void Reject(string? reason, long userId, DateTime atUtc, bool byManagement)
    {
        Require(byManagement ? OvertimeStatus.AwaitingManagement : OvertimeStatus.Proposed);
        DecisionNote = CalendarException.RequireReason(reason);
        if (byManagement)
        {
            ManagementUserId = userId;
            ManagementAtUtc = Requirements.Utc(atUtc, nameof(atUtc));
        }
        else
        {
            PlanningUserId = userId;
            PlanningAtUtc = Requirements.Utc(atUtc, nameof(atUtc));
        }

        Status = OvertimeStatus.Rejected;
    }

    public void Cancel(string? reason, DateTime atUtc)
    {
        if (!IsLive)
        {
            throw new InvalidOperationException("The overtime assignment is already closed.");
        }

        DecisionNote = CalendarException.RequireReason(reason);
        _ = Requirements.Utc(atUtc, nameof(atUtc));
        Status = OvertimeStatus.Cancelled;
    }

    private void Require(OvertimeStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"The overtime assignment is {Status}, not {expected}.");
        }
    }

    private static string? Clean(string? note)
    {
        var text = note?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > MaxReasonLength ? throw new ArgumentException("The note is at most 300 characters.", nameof(note)) : text;
    }
}
