using ERP.MES.Application.Security;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>
/// The rows one session command works on, read and changed inside ONE transaction (Decision Log #112). Nothing here exposes a database type. <see cref="CheckpointAsync"/> saves what was
/// changed so far inside the same transaction (a handover must close the old interval before it opens the new one); the final save, the audit entries and the commit happen when the command returns.
/// </summary>
public interface ISessionWorkspace
{
    /// <summary>A session with its intervals, pauses, events and corrections (tracked), or null.</summary>
    Task<WorkSession?> FindSessionAsync(long sessionId, CancellationToken cancellationToken = default);

    /// <summary>Fails with a concurrency conflict when the session's row version is not the one the caller read.</summary>
    void ExpectVersion(WorkSession session, long expectedVersion);

    /// <summary>Every session of the stage, in dispatch order (tracked).</summary>
    Task<IReadOnlyList<WorkSession>> StageSessionsAsync(long itemId, int stageNo, CancellationToken cancellationToken = default);

    /// <summary>The stage with its item and request (tracked), or null.</summary>
    Task<(Request Request, RequestItem Item, ItemStage Stage)?> FindStageAsync(long itemId, int stageNo, CancellationToken cancellationToken = default);

    /// <summary>Fails with a concurrency conflict when the stage's row version is not the one the caller read; and raises the stage's version with this command so that a concurrent stage action (QC submission) conflicts.</summary>
    void ExpectAndFenceStage(ItemStage stage, long? expectedVersion);

    /// <summary>True when an open interval exists on the machine / for the operator, other than in <paramref name="exceptSessionId"/>.</summary>
    Task<bool> IsMachineBusyAsync(long machineId, long exceptSessionId, CancellationToken cancellationToken = default);

    Task<bool> IsOperatorBusyAsync(long operatorId, long exceptSessionId, CancellationToken cancellationToken = default);

    /// <summary>The unit's active (not paused) sessions on the given machines, or on all machines of the unit when none are named.</summary>
    Task<IReadOnlyList<WorkSession>> ActiveSessionsAsync(long? unitId, IReadOnlyCollection<long> machineIds, CancellationToken cancellationToken = default);

    Task<Machine?> FindMachineAsync(long machineId, CancellationToken cancellationToken = default);

    Task<PauseReason?> FindReasonAsync(long reasonId, CancellationToken cancellationToken = default);

    Task<OutageEvent?> FindOutageAsync(long outageId, CancellationToken cancellationToken = default);

    /// <summary>The sessions currently paused by the outage.</summary>
    Task<IReadOnlyList<WorkSession>> SessionsPausedByOutageAsync(long outageId, CancellationToken cancellationToken = default);

    /// <summary>True when any daily job report line (any status) records time for the stage.</summary>
    Task<bool> StageHasReportLinesAsync(long itemId, int stageNo, CancellationToken cancellationToken = default);

    /// <summary>True when the session's recorded intervals overlap, in time, an interval of another session on the same machine or of the same operator (touching ends do not overlap).</summary>
    Task<bool> OverlapsOtherSessionsAsync(WorkSession session, DateTime nowUtc, CancellationToken cancellationToken = default);

    void Add(WorkSession session);

    void Add(OutageEvent outage);

    void AddAudit(AuditLogEntry entry);

    /// <summary>Saves the changes so far inside the surrounding transaction (ids of new rows become available).</summary>
    Task CheckpointAsync(CancellationToken cancellationToken = default);
}

/// <summary>What a session command returns: the sessions it touched with their new row versions.</summary>
public sealed record SessionCommitResult(IReadOnlyList<Stored<WorkSession>> Sessions, OutageEvent? Outage = null);

/// <summary>A session with the names the screens show.</summary>
public sealed record SessionRow(
    Stored<WorkSession> Session, string RequestNo, string ItemNo, string PartName, string StageTitle, string UnitCode, string MachineCode, string OperatorName, string OperatorCode, decimal PlannedMinutes = 0m);

/// <summary>Persistence of work sessions, pause reasons and outage events (Decision Log #112).</summary>
public interface IWorkSessionRepository
{
    Task<IReadOnlyList<Stored<PauseReason>>> ListReasonsAsync(CancellationToken cancellationToken = default);

    Task<Stored<PauseReason>> AddReasonAsync(PauseReason reason, AuditLogEntry audit, CancellationToken cancellationToken = default);

    Task<Stored<PauseReason>> UpdateReasonAsync(long id, long expectedVersion, Action<PauseReason> change, AuditLogEntry audit, CancellationToken cancellationToken = default);

    /// <summary>The unit's sessions: every unfinished one and those finished since <paramref name="finishedSince"/>.</summary>
    Task<IReadOnlyList<SessionRow>> ListForUnitAsync(long unitId, DateTime finishedSince, CancellationToken cancellationToken = default);

    /// <summary>Every session of a stage (history included).</summary>
    Task<IReadOnlyList<SessionRow>> ListForStageAsync(long itemId, int stageNo, CancellationToken cancellationToken = default);

    /// <summary>The sessions of one operator (unfinished ones and those finished since <paramref name="finishedSince"/>).</summary>
    Task<IReadOnlyList<SessionRow>> ListForOperatorAsync(long operatorId, DateTime finishedSince, CancellationToken cancellationToken = default);

    Task<SessionRow?> FindAsync(long sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutageEvent>> ListOpenOutagesAsync(long? unitId, CancellationToken cancellationToken = default);

    /// <summary>True when the stage has at least one session that is not cancelled.</summary>
    /// <summary>The production unit planned for the stage, or null when the stage does not exist or has none.</summary>
    Task<long?> FindStageUnitAsync(long itemId, int stageNo, CancellationToken cancellationToken = default);

    Task<bool> StageHasSessionsAsync(long itemId, int stageNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="apply"/> in one transaction. The delegate reads and changes the workspace's rows and returns the sessions to report (or throws to refuse); the changes, the audit entries
    /// and the new row versions are committed together, or nothing is saved. A concurrency conflict, including a database refusal of a second open interval, surfaces as <see cref="ConcurrencyConflictException"/>.
    /// </summary>
    Task<SessionCommitResult> CommitAsync(Func<ISessionWorkspace, Task<SessionCommitOutput>> apply, CancellationToken cancellationToken = default);
}

/// <summary>What the delegate of <see cref="IWorkSessionRepository.CommitAsync"/> hands back.</summary>
public sealed record SessionCommitOutput(IReadOnlyList<WorkSession> Sessions, OutageEvent? Outage = null);
