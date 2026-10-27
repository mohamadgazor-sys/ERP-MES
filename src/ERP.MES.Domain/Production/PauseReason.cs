using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Production;

/// <summary>How far a pause reaches by default (Decision Log #112). A machine breakdown stops one machine's session; an outage can stop every session of a unit; none of them stops the STAGE unless all its work is stopped.</summary>
public enum PauseScope
{
    /// <summary>One work session.</summary>
    Session = 1,

    /// <summary>One machine (breakdown, planned preventive maintenance).</summary>
    Machine = 2,

    /// <summary>One operator (absence; usually followed by a handover to another operator).</summary>
    Operator = 3,

    /// <summary>A production unit or the plant (power outage): recorded as one common <see cref="OutageEvent"/> linked to the affected sessions.</summary>
    Unit = 4,
}

/// <summary>
/// The controlled, configurable catalogue of pause reasons (owner direction of 2026-10-27, Decision Log #112). The seven system reasons the owner listed exist from the first migration;
/// Planning may add reasons and deactivate any (a deactivated reason stays on the pauses that used it). A reason never changes its code.
/// </summary>
public sealed class PauseReason
{
    public const string PowerOutage = "POWER_OUTAGE";
    public const string PlannedMaintenance = "PLANNED_MAINTENANCE";
    public const string MachineBreakdown = "MACHINE_BREAKDOWN";
    public const string OperatorAbsenceOrHandover = "OPERATOR_ABSENCE_HANDOVER";
    public const string MaterialUnavailable = "MATERIAL_UNAVAILABLE";
    public const string WaitingAssistance = "WAITING_ASSISTANCE";
    public const string Other = "OTHER";

    public const int MaxNameLength = 100;

    private PauseReason()
    {
    }

    public PauseReason(long id, string code, string name, PauseScope defaultScope, bool requiresNote, bool isSystem, long? createdByUserId, DateTime createdAtUtc)
    {
        if (!Enum.IsDefined(defaultScope))
        {
            throw new ArgumentOutOfRangeException(nameof(defaultScope), defaultScope, "Unknown pause scope.");
        }

        Id = id;
        Code = RequireCode(code);
        Name = RequireName(name);
        DefaultScope = defaultScope;
        RequiresNote = requiresNote;
        IsSystem = isSystem;
        IsActive = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Requirements.Utc(createdAtUtc, nameof(createdAtUtc));
    }

    public long Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public PauseScope DefaultScope { get; private set; }

    /// <summary>True when a pause with this reason must carry an explanatory note ("other").</summary>
    public bool RequiresNote { get; private set; }

    /// <summary>One of the owner's listed reasons; it can be deactivated but its code and note rule stay.</summary>
    public bool IsSystem { get; private set; }

    public bool IsActive { get; private set; }

    public long? CreatedByUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public void Rename(string name) => Name = RequireName(name);

    public void SetActive(bool isActive) => IsActive = isActive;

    private static string RequireCode(string code) =>
        !string.IsNullOrEmpty(code) && code.Length <= 40 && code.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_')
            ? code
            : throw new ArgumentException("A reason code is 1 to 40 characters of A-Z, 0-9 or '_'.", nameof(code));

    private static string RequireName(string name)
    {
        var text = name?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > MaxNameLength ? throw new ArgumentException("A reason name of at most 100 characters is required.", nameof(name)) : text;
    }
}

/// <summary>
/// One common event that stopped several sessions at once (power outage, a unit-wide stop). The pauses of the affected sessions point at it (each with its OWN pause and resume time and duration);
/// the event itself says when it began and ended and why. It never pauses a stage.
/// </summary>
public sealed class OutageEvent
{
    public const int MaxNoteLength = 500;

    private OutageEvent()
    {
    }

    public OutageEvent(long reasonId, long? productionUnitId, string? note, long recordedByUserId, DateTime startedAtUtc)
    {
        ReasonId = reasonId;
        ProductionUnitId = productionUnitId;
        Note = CleanNote(note);
        RecordedByUserId = recordedByUserId;
        StartedAtUtc = Requirements.Utc(startedAtUtc, nameof(startedAtUtc));
    }

    public long Id { get; private set; }

    public long ReasonId { get; private set; }

    /// <summary>The unit the outage hit; null for the whole plant.</summary>
    public long? ProductionUnitId { get; private set; }

    public string? Note { get; private set; }

    public long RecordedByUserId { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public DateTime? EndedAtUtc { get; private set; }

    public long? ClosedByUserId { get; private set; }

    public bool IsOpen => EndedAtUtc is null;

    public void Close(long userId, DateTime atUtc)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("The outage is already closed.");
        }

        var utc = Requirements.Utc(atUtc, nameof(atUtc));
        if (utc < StartedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(atUtc), atUtc, "An outage cannot end before it began.");
        }

        EndedAtUtc = utc;
        ClosedByUserId = userId;
    }

    internal static string? CleanNote(string? note)
    {
        var text = note?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > MaxNoteLength ? throw new ArgumentException("The note is at most 500 characters.", nameof(note)) : text;
    }
}
