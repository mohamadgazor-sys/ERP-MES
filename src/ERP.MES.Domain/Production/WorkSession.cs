using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Production;

public enum WorkSessionStatus
{
    /// <summary>Dispatched by the supervisor (machine and operator named); not started yet, no time recorded.</summary>
    Assigned = 1,

    Active,

    /// <summary>Stopped with a reason; no active interval is open. Elapsed work already recorded stays.</summary>
    Paused,

    Finished,

    /// <summary>Withdrawn before it started.</summary>
    Cancelled,
}

/// <summary>Why a finished session ended: the work of this session is done, or another operator took the machine over (a successor session continues it).</summary>
public enum SessionFinishKind
{
    Completed = 1,
    HandedOver = 2,
}

/// <summary>What closed an active interval.</summary>
public enum IntervalEnd
{
    Paused = 1,
    Finished = 2,
    HandedOver = 3,
}

public enum WorkSessionEventType
{
    Dispatched = 1,
    Started,
    Paused,
    Resumed,
    Finished,
    HandedOver,
    Cancelled,
    Corrected,
    TimeApproved,
}

/// <summary>Which recorded instant a correction moves.</summary>
public enum SessionBoundary
{
    /// <summary>The start of the first active interval.</summary>
    Start = 1,

    /// <summary>The instant a pause began (the end of the interval before it).</summary>
    PauseStart,

    /// <summary>The instant a pause ended (the start of the interval after it).</summary>
    PauseEnd,

    /// <summary>The end of the last active interval.</summary>
    Finish,
}

/// <summary>One continuous stretch of active work of a session. It carries the machine and the operator it ran with so that machine runtime and labour time can be added up independently.</summary>
public sealed class WorkSessionInterval
{
    private WorkSessionInterval()
    {
    }

    internal WorkSessionInterval(long machineId, long operatorId, DateTime startedAtUtc, long startedByUserId)
    {
        MachineId = machineId;
        OperatorId = operatorId;
        StartedAtUtc = startedAtUtc;
        StartedByUserId = startedByUserId;
    }

    public long Id { get; private set; }

    public long MachineId { get; private set; }

    public long OperatorId { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public DateTime? EndedAtUtc { get; private set; }

    public long StartedByUserId { get; private set; }

    public long? EndedByUserId { get; private set; }

    public IntervalEnd? EndedBy { get; private set; }

    public bool IsOpen => EndedAtUtc is null;

    /// <summary>The recorded length; an open interval counts up to <paramref name="asOfUtc"/>.</summary>
    public TimeSpan Duration(DateTime asOfUtc) => (EndedAtUtc ?? (asOfUtc < StartedAtUtc ? StartedAtUtc : asOfUtc)) - StartedAtUtc;

    internal void Close(DateTime atUtc, long userId, IntervalEnd end)
    {
        EndedAtUtc = atUtc;
        EndedByUserId = userId;
        EndedBy = end;
    }

    internal void MoveStart(DateTime atUtc) => StartedAtUtc = atUtc;

    internal void MoveEnd(DateTime atUtc) => EndedAtUtc = atUtc;
}

/// <summary>One pause of a session: why, scope, who, and its own pause and resume instants (a common outage gives every affected session its own row).</summary>
public sealed class WorkSessionPause
{
    private WorkSessionPause()
    {
    }

    internal WorkSessionPause(long reasonId, string? note, PauseScope scope, long? outageEventId, long pausedByUserId, DateTime pausedAtUtc)
    {
        ReasonId = reasonId;
        Note = note;
        Scope = scope;
        OutageEventId = outageEventId;
        PausedByUserId = pausedByUserId;
        PausedAtUtc = pausedAtUtc;
    }

    public long Id { get; private set; }

    public long ReasonId { get; private set; }

    public string? Note { get; private set; }

    public PauseScope Scope { get; private set; }

    public long? OutageEventId { get; private set; }

    public long PausedByUserId { get; private set; }

    public DateTime PausedAtUtc { get; private set; }

    public DateTime? ResumedAtUtc { get; private set; }

    public long? ResumedByUserId { get; private set; }

    public bool IsOpen => ResumedAtUtc is null;

    public TimeSpan Duration(DateTime asOfUtc) => (ResumedAtUtc ?? (asOfUtc < PausedAtUtc ? PausedAtUtc : asOfUtc)) - PausedAtUtc;

    internal void Resume(DateTime atUtc, long userId)
    {
        ResumedAtUtc = atUtc;
        ResumedByUserId = userId;
    }

    internal void MovePause(DateTime atUtc) => PausedAtUtc = atUtc;

    internal void MoveResume(DateTime atUtc) => ResumedAtUtc = atUtc;
}

/// <summary>An append-only history line of a session: dispatch, start, pause, resume, finish, handover, cancellation, correction, time approval.</summary>
public sealed class WorkSessionEvent
{
    private WorkSessionEvent()
    {
    }

    internal WorkSessionEvent(WorkSessionEventType type, DateTime atUtc, long userId, string detail)
    {
        Type = type;
        AtUtc = atUtc;
        ByUserId = userId;
        Detail = detail;
    }

    public long Id { get; private set; }

    public WorkSessionEventType Type { get; private set; }

    public DateTime AtUtc { get; private set; }

    public long ByUserId { get; private set; }

    public string Detail { get; private set; } = string.Empty;
}

/// <summary>An authorized correction of a recorded instant. The old and new values and the reason stay forever; the session shows the corrected instant.</summary>
public sealed class WorkSessionCorrection
{
    private WorkSessionCorrection()
    {
    }

    internal WorkSessionCorrection(SessionBoundary boundary, int pauseIndex, DateTime oldUtc, DateTime newUtc, string reason, long userId, DateTime atUtc)
    {
        Boundary = boundary;
        PauseIndex = pauseIndex;
        OldValueUtc = oldUtc;
        NewValueUtc = newUtc;
        Reason = reason;
        CorrectedByUserId = userId;
        CorrectedAtUtc = atUtc;
    }

    public long Id { get; private set; }

    public SessionBoundary Boundary { get; private set; }

    /// <summary>The 1-based number of the pause for pause boundaries; 0 otherwise.</summary>
    public int PauseIndex { get; private set; }

    public DateTime OldValueUtc { get; private set; }

    public DateTime NewValueUtc { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public long CorrectedByUserId { get; private set; }

    public DateTime CorrectedAtUtc { get; private set; }
}

/// <summary>
/// One machine/operator working on one stage of an item (owner direction of 2026-10-27, Decision Log #112). A stage may have many sessions, in sequence or at the same time on different machines
/// and by different operators. A session records its own active intervals, pauses and history; finishing it does NOT complete the stage (that is the responsible role's submission to QC). Nothing
/// is overwritten when another operator takes over: the session is finished as handed over and a successor session starts at the same instant. Active time is the sum of the intervals; paused time is
/// the sum of the pauses; neither is an editable number. Callers check permissions and the resource rules; this type keeps the session's own invariants.
/// </summary>
public sealed class WorkSession
{
    public const int MaxNoteLength = 500;

    private readonly List<WorkSessionInterval> _intervals = [];
    private readonly List<WorkSessionPause> _pauses = [];
    private readonly List<WorkSessionEvent> _events = [];
    private readonly List<WorkSessionCorrection> _corrections = [];

    private WorkSession()
    {
    }

    public long Id { get; private set; }

    public long RequestItemId { get; private set; }

    public int StageNo { get; private set; }

    public long ProductionUnitId { get; private set; }

    public long MachineId { get; private set; }

    public long OperatorId { get; private set; }

    public WorkSessionStatus Status { get; private set; }

    /// <summary>The session this one took over from (a handover), if any.</summary>
    public long? PredecessorSessionId { get; private set; }

    /// <summary>True when the operator had no current qualification for the machine at dispatch: the warning is kept on the session, never silently dropped.</summary>
    public bool QualificationWarning { get; private set; }

    /// <summary>The reason a supervisor gave for dispatching outside the operator's shift (null when inside it).</summary>
    public string? OutsideShiftReason { get; private set; }

    public long DispatchedByUserId { get; private set; }

    public DateTime DispatchedAtUtc { get; private set; }

    public long? StartedByUserId { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public long? FinishedByUserId { get; private set; }

    public DateTime? FinishedAtUtc { get; private set; }

    public SessionFinishKind? FinishKind { get; private set; }

    public string? FinishNote { get; private set; }

    /// <summary>Planning's approval of the session's actual time; only approved sessions feed machine-rate costing (Decision Log #91, #112).</summary>
    public long? TimeApprovedByUserId { get; private set; }

    public DateTime? TimeApprovedAtUtc { get; private set; }

    public bool IsTimeApproved => TimeApprovedAtUtc is not null;

    public IReadOnlyList<WorkSessionInterval> Intervals => _intervals;

    public IReadOnlyList<WorkSessionPause> Pauses => _pauses;

    public IReadOnlyList<WorkSessionEvent> Events => _events;

    public IReadOnlyList<WorkSessionCorrection> Corrections => _corrections;

    /// <summary>Assigned, active or paused: the session still has work that must be finished or withdrawn before the stage goes to QC.</summary>
    public bool IsUnfinished => Status is WorkSessionStatus.Assigned or WorkSessionStatus.Active or WorkSessionStatus.Paused;

    public WorkSessionInterval? OpenInterval => _intervals.LastOrDefault(i => i.IsOpen);

    public WorkSessionPause? OpenPause => _pauses.LastOrDefault(p => p.IsOpen);

    public static WorkSession Dispatch(
        long requestItemId, int stageNo, long productionUnitId, long machineId, long operatorId, long? predecessorSessionId, bool qualificationWarning, string? outsideShiftReason,
        long dispatchedByUserId, DateTime atUtc)
    {
        var utc = Requirements.Utc(atUtc, nameof(atUtc));
        var session = new WorkSession
        {
            RequestItemId = requestItemId,
            StageNo = stageNo,
            ProductionUnitId = productionUnitId,
            MachineId = machineId,
            OperatorId = operatorId,
            PredecessorSessionId = predecessorSessionId,
            QualificationWarning = qualificationWarning,
            OutsideShiftReason = Clean(outsideShiftReason),
            DispatchedByUserId = dispatchedByUserId,
            DispatchedAtUtc = utc,
            Status = WorkSessionStatus.Assigned,
        };
        session.Log(WorkSessionEventType.Dispatched, utc, dispatchedByUserId, predecessorSessionId is null ? "dispatched" : $"dispatched as the successor of session {predecessorSessionId}");
        return session;
    }

    public void Start(long userId, DateTime atUtc)
    {
        Require(WorkSessionStatus.Assigned);
        var utc = Requirements.Utc(atUtc, nameof(atUtc));
        if (utc < DispatchedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(atUtc), atUtc, "A session cannot start before it was dispatched.");
        }

        _intervals.Add(new WorkSessionInterval(MachineId, OperatorId, utc, userId));
        StartedByUserId = userId;
        StartedAtUtc = utc;
        Status = WorkSessionStatus.Active;
        Log(WorkSessionEventType.Started, utc, userId, "started");
    }

    /// <summary>Pauses the session: closes the active interval (the work already recorded stays) and records why. The note is required when <paramref name="reasonRequiresNote"/>.</summary>
    public WorkSessionPause Pause(long reasonId, bool reasonRequiresNote, string? note, PauseScope scope, long? outageEventId, long userId, DateTime atUtc)
    {
        Require(WorkSessionStatus.Active);
        var utc = Requirements.Utc(atUtc, nameof(atUtc));
        var text = Clean(note);
        if (reasonRequiresNote && text is null)
        {
            throw new ArgumentException("This pause reason needs a note.", nameof(note));
        }

        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unknown pause scope.");
        }

        var open = OpenInterval ?? throw new InvalidOperationException("The active session has no open interval.");
        if (utc <= open.StartedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(atUtc), atUtc, "A pause must come after the interval started.");
        }

        open.Close(utc, userId, IntervalEnd.Paused);
        var pause = new WorkSessionPause(reasonId, text, scope, outageEventId, userId, utc);
        _pauses.Add(pause);
        Status = WorkSessionStatus.Paused;
        Log(WorkSessionEventType.Paused, utc, userId, $"paused (reason {reasonId}, scope {scope})" + (text is null ? string.Empty : "; " + text));
        return pause;
    }

    /// <summary>Resumes the session: closes the pause and opens a NEW active interval. The caller has already checked authorization and the machine/operator rules again.</summary>
    public void Resume(long userId, DateTime atUtc)
    {
        Require(WorkSessionStatus.Paused);
        var utc = Requirements.Utc(atUtc, nameof(atUtc));
        var pause = OpenPause ?? throw new InvalidOperationException("The paused session has no open pause.");
        if (utc <= pause.PausedAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(atUtc), atUtc, "A session cannot resume before it was paused.");
        }

        pause.Resume(utc, userId);
        _intervals.Add(new WorkSessionInterval(MachineId, OperatorId, utc, userId));
        Status = WorkSessionStatus.Active;
        Log(WorkSessionEventType.Resumed, utc, userId, "resumed");
    }

    /// <summary>
    /// Finishes the session. From active the open interval is closed; from paused the open pause is closed at the same instant (the session was idle until now). This does not complete the stage.
    /// </summary>
    public void Finish(SessionFinishKind kind, string? note, long userId, DateTime atUtc)
    {
        if (Status is not (WorkSessionStatus.Active or WorkSessionStatus.Paused) || !Enum.IsDefined(kind))
        {
            throw new InvalidOperationException($"A {Status} session cannot be finished.");
        }

        var utc = Requirements.Utc(atUtc, nameof(atUtc));
        var last = _intervals[^1];
        if (utc < (last.EndedAtUtc ?? last.StartedAtUtc) || (last.IsOpen && utc <= last.StartedAtUtc))
        {
            throw new ArgumentOutOfRangeException(nameof(atUtc), atUtc, "A session cannot finish before its last recorded instant.");
        }

        if (OpenInterval is { } open)
        {
            open.Close(utc, userId, kind == SessionFinishKind.HandedOver ? IntervalEnd.HandedOver : IntervalEnd.Finished);
        }
        else if (OpenPause is { } pause)
        {

            // Finished while paused: the session was idle until now, so the pause ends here and the active time stays as recorded.
            pause.Resume(utc, userId);
        }

        FinishedByUserId = userId;
        FinishedAtUtc = utc;
        FinishKind = kind;
        FinishNote = Clean(note);
        Status = WorkSessionStatus.Finished;
        Log(kind == SessionFinishKind.HandedOver ? WorkSessionEventType.HandedOver : WorkSessionEventType.Finished, utc, userId, kind == SessionFinishKind.HandedOver ? "finished: handed over to a successor" : "finished" + (FinishNote is null ? string.Empty : "; " + FinishNote));
    }

    /// <summary>Withdraws a session that was dispatched but never started. A started session is finished, never cancelled.</summary>
    public void Cancel(string? reason, long userId, DateTime atUtc)
    {
        Require(WorkSessionStatus.Assigned);
        var utc = Requirements.Utc(atUtc, nameof(atUtc));
        var text = Clean(reason) ?? throw new ArgumentException("A reason is required.", nameof(reason));
        Status = WorkSessionStatus.Cancelled;
        Log(WorkSessionEventType.Cancelled, utc, userId, "cancelled before start; " + text);
    }

    /// <summary>Planning approves the actual time of a finished session (it may not be the person who dispatched or started it; the caller enforces that).</summary>
    public void ApproveTime(long userId, DateTime atUtc)
    {
        Require(WorkSessionStatus.Finished);
        if (IsTimeApproved)
        {
            throw new InvalidOperationException("The session's time is already approved.");
        }

        var utc = Requirements.Utc(atUtc, nameof(atUtc));
        TimeApprovedByUserId = userId;
        TimeApprovedAtUtc = utc;
        Log(WorkSessionEventType.TimeApproved, utc, userId, "actual time approved");
    }

    /// <summary>
    /// Moves one recorded instant (authorized correction with a mandatory reason). The original value is kept in <see cref="Corrections"/>. The instants must stay in order (start &lt; pause start &lt; pause end &lt; ... &lt; finish)
    /// and not lie in the future. A correction withdraws an existing time approval: the corrected time must be approved again.
    /// </summary>
    public WorkSessionCorrection Correct(SessionBoundary boundary, int pauseIndex, DateTime newUtc, string? reason, long userId, DateTime atUtc, DateTime nowUtc)
    {
        var utc = Requirements.Utc(atUtc, nameof(atUtc));
        var value = Requirements.Utc(newUtc, nameof(newUtc));
        var text = Clean(reason) ?? throw new ArgumentException("A reason is required.", nameof(reason));
        if (Status is WorkSessionStatus.Assigned or WorkSessionStatus.Cancelled)
        {
            throw new InvalidOperationException("A session that never started has no recorded time to correct.");
        }

        if (value > Requirements.Utc(nowUtc, nameof(nowUtc)))
        {
            throw new ArgumentOutOfRangeException(nameof(newUtc), newUtc, "A recorded instant cannot be moved into the future.");
        }

        DateTime old;
        switch (boundary)
        {
            case SessionBoundary.Start:
                old = _intervals[0].StartedAtUtc;
                if (_intervals[0].EndedAtUtc is { } firstEnd && value >= firstEnd)
                {
                    throw new ArgumentOutOfRangeException(nameof(newUtc), newUtc, "The start must stay before the first pause or finish.");
                }

                _intervals[0].MoveStart(value);
                StartedAtUtc = value;
                break;
            case SessionBoundary.PauseStart:
            {
                var pause = PauseAt(pauseIndex);
                var interval = _intervals[pauseIndex - 1];
                old = pause.PausedAtUtc;
                if (value <= interval.StartedAtUtc || (pause.ResumedAtUtc is { } resumed && value >= resumed))
                {
                    throw new ArgumentOutOfRangeException(nameof(newUtc), newUtc, "A pause must stay after the interval before it started and before it ended.");
                }

                pause.MovePause(value);
                interval.MoveEnd(value);
                break;
            }

            case SessionBoundary.PauseEnd:
            {
                var pause = PauseAt(pauseIndex);
                if (pause.ResumedAtUtc is null)
                {
                    throw new InvalidOperationException("The pause has not ended yet.");
                }

                if (pauseIndex >= _intervals.Count)
                {
                    throw new InvalidOperationException("This pause ended with the session's finish; correct the finish instead.");
                }

                var next = _intervals[pauseIndex];
                old = pause.ResumedAtUtc.Value;
                if (value <= pause.PausedAtUtc || (next.EndedAtUtc is { } nextEnd && value >= nextEnd))
                {
                    throw new ArgumentOutOfRangeException(nameof(newUtc), newUtc, "A resume must stay after its pause began and before the next interval ended.");
                }

                pause.MoveResume(value);
                next.MoveStart(value);
                break;
            }

            case SessionBoundary.Finish:
                if (FinishedAtUtc is null)
                {
                    throw new InvalidOperationException("The session is not finished.");
                }

                old = FinishedAtUtc.Value;
                var last = _intervals[^1];
                if (last.EndedBy == IntervalEnd.Paused)
                {
                    // Finished while paused: only the end of the idle pause moves; the active time is unchanged.
                    var tail = _pauses[^1];
                    if (value <= tail.PausedAtUtc)
                    {
                        throw new ArgumentOutOfRangeException(nameof(newUtc), newUtc, "The finish must come after the last pause began.");
                    }

                    tail.MoveResume(value);
                }
                else
                {
                    if (value <= last.StartedAtUtc)
                    {
                        throw new ArgumentOutOfRangeException(nameof(newUtc), newUtc, "The finish must come after the last interval started.");
                    }

                    last.MoveEnd(value);
                }

                FinishedAtUtc = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(boundary), boundary, "Unknown boundary.");
        }

        var correction = new WorkSessionCorrection(boundary, boundary is SessionBoundary.PauseStart or SessionBoundary.PauseEnd ? pauseIndex : 0, old, value, text, userId, utc);
        _corrections.Add(correction);
        var withdrawn = IsTimeApproved;
        TimeApprovedByUserId = null;
        TimeApprovedAtUtc = null;
        Log(WorkSessionEventType.Corrected, utc, userId, $"{boundary}{(pauseIndex > 0 ? " #" + pauseIndex : string.Empty)} moved by correction; {text}" + (withdrawn ? "; time approval withdrawn" : string.Empty));
        return correction;
    }

    private WorkSessionPause PauseAt(int pauseIndex) =>
        pauseIndex >= 1 && pauseIndex <= _pauses.Count ? _pauses[pauseIndex - 1] : throw new ArgumentOutOfRangeException(nameof(pauseIndex), pauseIndex, "No such pause.");

    private void Require(WorkSessionStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"The session is {Status}, not {expected}.");
        }
    }

    private void Log(WorkSessionEventType type, DateTime atUtc, long userId, string detail) => _events.Add(new WorkSessionEvent(type, atUtc, userId, detail));

    internal static string? Clean(string? note)
    {
        var text = note?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > MaxNoteLength ? throw new ArgumentException("The note is at most 500 characters.", nameof(note)) : text;
    }
}
