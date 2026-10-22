namespace ERP.MES.Domain.Planning;

/// <summary>
/// A record that an operator is qualified on a machine (Decision Log #82 R2: assignment is not qualification). Planning records it. Like an assignment it keeps history: it is ended,
/// never edited or deleted. A missing qualification is a WARNING when scheduling, not a block (a labelled default, docs/PLANNING_RESOURCES_AND_SCHEDULING_DESIGN.md section 3).
/// </summary>
public sealed class OperatorMachineQualification
{
    public const int MaxNoteLength = 500;

    public OperatorMachineQualification(long id, long operatorId, long machineId, DateOnly qualifiedFrom, string? note, long recordedByUserId, DateTime recordedAtUtc)
    {
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (text is { Length: > MaxNoteLength })
        {
            throw new ArgumentException("The note is limited to " + MaxNoteLength + " characters.", nameof(note));
        }

        Id = id;
        OperatorId = operatorId;
        MachineId = machineId;
        QualifiedFrom = qualifiedFrom;
        Note = text;
        RecordedByUserId = recordedByUserId;
        RecordedAtUtc = ERP.MES.Domain.Quality.Requirements.Utc(recordedAtUtc, nameof(recordedAtUtc));
    }

    public long Id { get; }

    public long OperatorId { get; }

    public long MachineId { get; }

    public DateOnly QualifiedFrom { get; }

    /// <summary>The last day the qualification holds, or null while it holds.</summary>
    public DateOnly? QualifiedTo { get; private set; }

    public string? Note { get; }

    public long RecordedByUserId { get; }

    public DateTime RecordedAtUtc { get; }

    public bool IsOpen => QualifiedTo is null;

    public bool CoversDate(DateOnly date) => date >= QualifiedFrom && (QualifiedTo is null || date <= QualifiedTo);

    public void End(DateOnly lastDay)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("The qualification has already ended.");
        }

        if (lastDay < QualifiedFrom)
        {
            throw new ArgumentOutOfRangeException(nameof(lastDay), lastDay, "A qualification cannot end before it starts.");
        }

        QualifiedTo = lastDay;
    }
}

public static class OperatorMachineRules
{
    /// <summary>True when an assignment of this operator to this machine covers <paramref name="date"/>.</summary>
    public static bool IsAssigned(IEnumerable<OperatorMachineAssignment> assignments, long operatorId, long machineId, DateOnly date) =>
        assignments.Any(a => a.OperatorId == operatorId && a.MachineId == machineId && a.CoversDate(date));

    /// <summary>True when the operator holds a qualification on the machine that covers <paramref name="date"/>. Assignment alone never answers yes.</summary>
    public static bool IsQualified(IEnumerable<OperatorMachineQualification> qualifications, long operatorId, long machineId, DateOnly date) =>
        qualifications.Any(q => q.OperatorId == operatorId && q.MachineId == machineId && q.CoversDate(date));
}
