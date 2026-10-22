using ERP.MES.Application.Security;
using ERP.MES.Domain.Planning;

namespace ERP.MES.Application.Persistence;

/// <summary>
/// Saves and loads the Planning resource master data: production units, machines and their hourly-rate history (Decision Log #82 R2, #89). Every method is one transaction; loaded objects
/// are detached; a change is applied through the Domain methods to a freshly loaded object under its row version. Audit entries passed to an add method are saved in the SAME
/// transaction as the record. Codes are unique (also enforced by the database). Rate entries are append-only.
/// </summary>
public interface IPlanningMasterRepository
{
    Task<IReadOnlyList<Stored<ProductionUnit>>> ListUnitsAsync(CancellationToken cancellationToken = default);

    Task<Stored<ProductionUnit>> AddUnitAsync(ProductionUnit unit, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default);

    Task<Stored<ProductionUnit>> UpdateUnitAsync(long unitId, long expectedVersion, Action<ProductionUnit> change, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Stored<Machine>>> ListMachinesAsync(CancellationToken cancellationToken = default);

    Task<Stored<Machine>> AddMachineAsync(Machine machine, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default);

    Task<Stored<Machine>> UpdateMachineAsync(long machineId, long expectedVersion, Action<Machine> change, CancellationToken cancellationToken = default);

    /// <summary>All rate entries of all machines, oldest first by machine and effective date.</summary>
    Task<IReadOnlyList<MachineHourlyRate>> ListRatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a rate entry and its audit entries in one transaction. The latest-entry rule and the machine check are made inside the same transaction, so two people adding a rate
    /// for one machine at the same moment cannot both pass: the later one gets <see cref="ConcurrencyConflictException"/>.
    /// </summary>
    Task<MachineHourlyRate> AddRateAsync(MachineHourlyRate rate, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default);
}
