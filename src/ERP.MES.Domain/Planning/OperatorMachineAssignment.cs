namespace ERP.MES.Domain.Planning;

/// <summary>
/// The master assignment of an operator to a machine (Decision Log #82 R2, #89): who may be scheduled on which machine. Planning owns it; it keeps history - an assignment is closed, never
/// edited or deleted, and a new period is a new row. An assignment does NOT imply qualification (see <see cref="OperatorMachineQualification"/>). A supervisor cannot change it (#89).
/// </summary>
public sealed class OperatorMachineAssignment
{
    public const int MaxReasonLength = 500;

    public OperatorMachineAssignment(long id, long operatorId, long machineId, DateOnly effectiveFrom, bool isPrimary, string? reason, long assignedByUserId, DateTime assignedAtUtc)
    {
        var note = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (note is { Length: > MaxReasonLength })
        {
            throw new ArgumentException("The reason is limited to " + MaxReasonLength + " characters.", nameof(reason));
        }

        Id = id;
        OperatorId = operatorId;
        MachineId = machineId;
        EffectiveFrom = effectiveFrom;
        IsPrimary = isPrimary;
        Reason = note;
        AssignedByUserId = assignedByUserId;
        AssignedAtUtc = ERP.MES.Domain.Quality.Requirements.Utc(assignedAtUtc, nameof(assignedAtUtc));
    }

    public long Id { get; }

    public long OperatorId { get; }

    public long MachineId { get; }

    public DateOnly EffectiveFrom { get; }

    /// <summary>The last day of the assignment, or null while it is open.</summary>
    public DateOnly? EffectiveTo { get; private set; }

    public bool IsPrimary { get; }

    public string? Reason { get; }

    public long AssignedByUserId { get; }

    public DateTime AssignedAtUtc { get; }

    public long? ClosedByUserId { get; private set; }

    public DateTime? ClosedAtUtc { get; private set; }

    public bool IsOpen => EffectiveTo is null;

    /// <summary>True when the assignment covers <paramref name="date"/> (from the first day to the last day, inclusive; open assignments have no end).</summary>
    public bool CoversDate(DateOnly date) => date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);

    /// <summary>Closes an open assignment on <paramref name="lastDay"/>, which cannot be before its first day. The row is kept as history.</summary>
    public void Close(DateOnly lastDay, long closedByUserId, DateTime closedAtUtc)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("The assignment is already closed.");
        }

        if (lastDay < EffectiveFrom)
        {
            throw new ArgumentOutOfRangeException(nameof(lastDay), lastDay, "An assignment cannot end before it starts.");
        }

        EffectiveTo = lastDay;
        ClosedByUserId = closedByUserId;
        ClosedAtUtc = ERP.MES.Domain.Quality.Requirements.Utc(closedAtUtc, nameof(closedAtUtc));
    }
}
