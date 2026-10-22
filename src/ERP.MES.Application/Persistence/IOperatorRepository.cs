using ERP.MES.Application.Security;
using ERP.MES.Domain.Planning;

namespace ERP.MES.Application.Persistence;

/// <summary>
/// Saves and loads operators, their master assignments to machines and their qualifications (Decision Log #82 R2, #89). Every method is one transaction; loaded objects are detached;
/// a change goes through the Domain methods under the row version; audit entries passed to a write method are saved in the same transaction. Assignments and qualifications are history:
/// they are closed or ended, never deleted.
/// </summary>
public interface IOperatorRepository
{
    Task<IReadOnlyList<Stored<Operator>>> ListOperatorsAsync(CancellationToken cancellationToken = default);

    Task<Stored<Operator>> AddOperatorAsync(Operator entity, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default);

    Task<Stored<Operator>> UpdateOperatorAsync(
        long operatorId, long expectedVersion, Action<Operator> change, IReadOnlyList<AuditLogEntry>? audit = null, CancellationToken cancellationToken = default);

    /// <summary>The display name and activity of the user account, or null when no such user exists.</summary>
    Task<LinkedUserInfo?> FindUserAsync(long userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Stored<OperatorMachineAssignment>>> ListAssignmentsAsync(CancellationToken cancellationToken = default);

    Task<Stored<OperatorMachineAssignment>> AddAssignmentAsync(OperatorMachineAssignment assignment, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default);

    Task<Stored<OperatorMachineAssignment>> UpdateAssignmentAsync(
        long assignmentId, long expectedVersion, Action<OperatorMachineAssignment> change, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Stored<OperatorMachineQualification>>> ListQualificationsAsync(CancellationToken cancellationToken = default);

    Task<Stored<OperatorMachineQualification>> AddQualificationAsync(OperatorMachineQualification qualification, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default);

    Task<Stored<OperatorMachineQualification>> UpdateQualificationAsync(
        long qualificationId, long expectedVersion, Action<OperatorMachineQualification> change, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default);
}

/// <summary>A user account an operator can be linked to.</summary>
public sealed record LinkedUserInfo(long UserId, string DisplayName, bool IsActive);
