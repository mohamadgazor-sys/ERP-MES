using ERP.MES.Application.Security;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>A Production-department user a unit can be designated to.</summary>
public sealed record ProductionUserInfo(long UserId, string Username, string DisplayName, bool IsActive);

/// <summary>
/// The rows one queue command works on, inside one transaction: the requests it changes (with their stages), the overrides of the units it touches and the append-only history and audit
/// entries it adds. Implemented by the persistence layer; no database type appears here.
/// </summary>
public interface IQueueWorkspace
{
    Request Request(long requestId);

    /// <summary>An item of any request loaded into the workspace (the requests the command names and the owners of the overrides of the units it touches), with its request.</summary>
    (Request Request, RequestItem Item)? FindItem(long itemId);

    /// <summary>The override row of the stage (active or cleared), or null when none exists.</summary>
    QueueOverride? FindOverride(long itemId, int stageNo);

    /// <summary>Every override row (active or cleared) of the unit, so ranks can be renumbered.</summary>
    IReadOnlyList<QueueOverride> OverridesOfUnit(long unitId);

    void AddOverride(QueueOverride value);

    void AddHistory(QueueOverrideHistoryEntry entry);

    void AddAudit(AuditLogEntry entry);
}

/// <summary>
/// One queue command. <see cref="StageVersions"/> and <see cref="ItemVersions"/> are the row versions the caller read; <see cref="OverrideVersions"/> maps a stage to the version of its
/// override row (0 = the caller saw none, so none may exist). Every named row is compared BEFORE <see cref="Apply"/> runs; one mismatch rejects the whole command.
/// </summary>
public sealed record QueueCommit(
    IReadOnlyCollection<long> RequestIds,
    IReadOnlyCollection<long> UnitIds,
    IReadOnlyDictionary<(long RequestId, StageRef Stage), long> StageVersions,
    IReadOnlyDictionary<(long RequestId, long ItemId), long> ItemVersions,
    IReadOnlyDictionary<StageRef, long> OverrideVersions,
    Action<IQueueWorkspace> Apply);

/// <summary>Persistence of the operational unit queue: Planning positions and stage holds (on the stage rows), Management overrides and their history, and user-to-unit designations.</summary>
public interface IUnitQueueRepository
{
    Task<IReadOnlyList<Stored<QueueOverride>>> ListOverridesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Stored<UserUnitDesignation>>> ListDesignationsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QueueOverrideHistoryEntry>> ListOverrideHistoryAsync(long unitId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductionUserInfo>> ListProductionUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies a queue command in ONE transaction. A stale version, a missing row, a rule of the Domain or a database failure saves nothing.</summary>
    Task CommitAsync(QueueCommit commit, CancellationToken cancellationToken = default);

    /// <summary>Designates a user to a unit, or reinstates a withdrawn designation, with its audit entry, in one transaction.</summary>
    Task<Stored<UserUnitDesignation>> AssignDesignationAsync(long userId, long productionUnitId, long assignedByUserId, DateTime assignedAtUtc, AuditLogEntry audit, CancellationToken cancellationToken = default);

    /// <summary>Withdraws a designation (kept as history) under its expected version, with its audit entry.</summary>
    Task<Stored<UserUnitDesignation>> RevokeDesignationAsync(long designationId, long expectedVersion, long revokedByUserId, DateTime revokedAtUtc, AuditLogEntry audit, CancellationToken cancellationToken = default);
}
