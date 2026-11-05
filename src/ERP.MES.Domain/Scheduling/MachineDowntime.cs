using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Scheduling;

/// <summary>The documented stop reasons of Master Specification section 21. "Other" needs a note.</summary>
public enum DowntimeReason
{
    PowerOutage = 1,
    PreventiveMaintenance = 2,
    Breakdown = 3,
    NoOperator = 4,
    NoMaterial = 5,
    NoDrawing = 6,
    NoQcResult = 7,
    SetupChangeover = 8,
    Other = 9,
}

public enum DowntimeStatus
{
    /// <summary>Reported by Maintenance/PM; Planning has not reviewed it yet. It already counts as downtime (Decision Log #128, ASSUMPTION).</summary>
    Reported = 1,

    /// <summary>Planning reviewed and accepted it.</summary>
    Accepted = 2,

    /// <summary>Planning rejected it as wrong (reason kept); it does not count.</summary>
    Rejected = 3,

    /// <summary>Maintenance/PM withdrew it (reason kept); it does not count.</summary>
    Cancelled = 4,
}

/// <summary>
/// A period in which a machine cannot work (Master Specification section 21, Decision Log #14, #128). Maintenance/PM reports it to Planning with a reason; Planning accepts or rejects it.
/// It is separate from work on an item or stage and never deleted: a wrong record is rejected or cancelled with a reason, and a changed end keeps the earlier end in the audit log.
/// Times are local wall-clock (the calendar's own convention, as for overtime); conversion happens at the boundary. The production unit is the machine's unit when it was reported.
/// </summary>
public sealed class MachineDowntime
{
    public const int MaxNoteLength = 300;

    /// <summary>A single record covers at most 90 days; a longer stoppage is reported again (a labelled default of Decision Log #128).</summary>
    public const int MaxDays = 90;

    private MachineDowntime()
    {
    }

    public MachineDowntime(
        long id, long machineId, long productionUnitId, DowntimeReason reason, DateTime startLocal, DateTime endLocal, string? note, long reportedByUserId, DateTime reportedAtUtc)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown downtime reason.");
        }

        CheckInterval(startLocal, endLocal);
        Id = id;
        MachineId = machineId;
        ProductionUnitId = productionUnitId;
        Reason = reason;
        StartLocal = DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified);
        EndLocal = DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified);
        Note = CheckNote(note, required: reason == DowntimeReason.Other);
        Status = DowntimeStatus.Reported;
        ReportedByUserId = reportedByUserId;
        ReportedAtUtc = Requirements.Utc(reportedAtUtc, nameof(reportedAtUtc));
    }

    public long Id { get; private set; }

    public long MachineId { get; private set; }

    public long ProductionUnitId { get; private set; }

    public DowntimeReason Reason { get; private set; }

    public DateTime StartLocal { get; private set; }

    public DateTime EndLocal { get; private set; }

    public string? Note { get; private set; }

    public DowntimeStatus Status { get; private set; }

    public long ReportedByUserId { get; private set; }

    public DateTime ReportedAtUtc { get; private set; }

    /// <summary>The Planning user who accepted or rejected it (Master Specification section 21 "ApprovedByUserID").</summary>
    public long? DecidedByUserId { get; private set; }

    public DateTime? DecidedAtUtc { get; private set; }

    /// <summary>Planning's note on acceptance (optional), its reason for a rejection, or Maintenance/PM's reason for a cancellation.</summary>
    public string? DecisionNote { get; private set; }

    public long? CancelledByUserId { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public int DurationMinutes => (int)(EndLocal - StartLocal).TotalMinutes;

    /// <summary>Reported and accepted downtime makes the machine unavailable; rejected and cancelled downtime does not.</summary>
    public bool Counts => Status is DowntimeStatus.Reported or DowntimeStatus.Accepted;

    public bool Overlaps(DateTime otherStartLocal, DateTime otherEndLocal) => StartLocal < otherEndLocal && otherStartLocal < EndLocal;

    /// <summary>Planning accepts a reported downtime; the note is optional.</summary>
    public void Accept(string? note, long userId, DateTime atUtc)
    {
        RequireStatus(DowntimeStatus.Reported);
        DecisionNote = CheckNote(note, required: false);
        DecidedByUserId = userId;
        DecidedAtUtc = Requirements.Utc(atUtc, nameof(atUtc));
        Status = DowntimeStatus.Accepted;
    }

    /// <summary>Planning rejects a reported downtime as wrong; the reason is mandatory.</summary>
    public void Reject(string? reason, long userId, DateTime atUtc)
    {
        RequireStatus(DowntimeStatus.Reported);
        DecisionNote = CheckNote(reason, required: true);
        DecidedByUserId = userId;
        DecidedAtUtc = Requirements.Utc(atUtc, nameof(atUtc));
        Status = DowntimeStatus.Rejected;
    }

    /// <summary>Maintenance/PM withdraws a reported or accepted downtime; the reason is mandatory.</summary>
    public void Cancel(string? reason, long userId, DateTime atUtc)
    {
        if (!Counts)
        {
            throw new InvalidOperationException("Only reported or accepted downtime can be cancelled.");
        }

        DecisionNote = CheckNote(reason, required: true);
        CancelledByUserId = userId;
        CancelledAtUtc = Requirements.Utc(atUtc, nameof(atUtc));
        Status = DowntimeStatus.Cancelled;
    }

    /// <summary>
    /// Maintenance/PM corrects the end (a breakdown lasted longer or ended earlier). An accepted downtime goes back to Planning for review; the earlier end stays in the audit log.
    /// </summary>
    public void ChangeEnd(DateTime endLocal)
    {
        if (!Counts)
        {
            throw new InvalidOperationException("Only reported or accepted downtime can be changed.");
        }

        CheckInterval(StartLocal, endLocal);
        EndLocal = DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified);
        if (Status == DowntimeStatus.Accepted)
        {
            Status = DowntimeStatus.Reported;
            DecidedByUserId = null;
            DecidedAtUtc = null;
            DecisionNote = null;
        }
    }

    private static void CheckInterval(DateTime startLocal, DateTime endLocal)
    {
        if (endLocal <= startLocal || (endLocal - startLocal).TotalDays > MaxDays || startLocal.Second != 0 || endLocal.Second != 0 || startLocal.Millisecond != 0 || endLocal.Millisecond != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(endLocal), endLocal, "Downtime lasts from 1 minute to " + MaxDays + " days and is entered in whole minutes.");
        }
    }

    private static string? CheckNote(string? note, bool required)
    {
        var text = note?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return required ? throw new ArgumentException("A note of 1 to " + MaxNoteLength + " characters is required.", nameof(note)) : null;
        }

        return text.Length > MaxNoteLength ? throw new ArgumentException("The note is at most " + MaxNoteLength + " characters.", nameof(note)) : text;
    }

    private void RequireStatus(DowntimeStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException("The downtime is not in the state this action needs.");
        }
    }
}
