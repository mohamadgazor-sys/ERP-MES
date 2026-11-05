using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Application.Persistence;

/// <summary>The facts read INSIDE the transaction that adds or changes a downtime record, so the rules are checked against current data.</summary>
public sealed record DowntimeContext(Machine? Machine, IReadOnlyList<MachineDowntime> OtherCountingOfMachine);

/// <summary>Persistence of machine downtime (Master Specification section 21, Decision Log #128). Every write is one transaction with its audit entry and uses row versions.</summary>
public interface IMachineDowntimeRepository
{
    /// <summary>Every record (any status) that overlaps the local interval [fromLocal, toLocal), ordered by start.</summary>
    Task<IReadOnlyList<Stored<MachineDowntime>>> ListAsync(DateTime fromLocal, DateTime toLocal, CancellationToken cancellationToken = default);

    /// <summary>Adds the record after <paramref name="validate"/> accepted it against a context read in the same transaction; a refusal saves nothing.</summary>
    Task<(Stored<MachineDowntime>? Row, ValidationIssue Refusal)> AddAsync(
        MachineDowntime row, Func<DowntimeContext, ValidationIssue> validate, AuditLogEntry audit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the record under <paramref name="expectedVersion"/> with the current context and lets <paramref name="change"/> apply a transition; a refusal saves nothing.
    /// <paramref name="audit"/> builds the audit entry from the record as loaded, BEFORE the change, so it can name the earlier values. A domain rule violation inside
    /// <paramref name="change"/> surfaces as <see cref="InvalidOperationException"/>.
    /// </summary>
    Task<(Stored<MachineDowntime>? Row, ValidationIssue Refusal)> ChangeAsync(
        long id, long expectedVersion, Func<MachineDowntime, DowntimeContext, ValidationIssue> change, Func<MachineDowntime, AuditLogEntry> audit, CancellationToken cancellationToken = default);
}
