using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Planning;

/// <summary>
/// A machine (Domain and Database Specification 14.3; Decision Log #82 R2, #89). Planning owns the registration. The machine code (for example FCC-06, TCC-12, HUB-01) is a STABLE
/// business identifier: unique, never reused and never changed, because job reports, stage assignments and cost snapshots refer to it. The production unit it belongs to is fixed too
/// (moving a machine would change the meaning of every record that names it; a move needs its own history-keeping operation, which is not built). Only the Persian name and the
/// active flag change. Machines are retired, never deleted.
/// </summary>
public sealed class Machine
{
    public Machine(long id, string code, string name, long productionUnitId, long createdByUserId, DateTime createdAtUtc)
    {
        Id = id;
        Code = UnitOfMeasure.RequireCode(code);
        Name = UnitOfMeasure.RequireName(name);
        ProductionUnitId = productionUnitId;
        IsActive = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Requirements.Utc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
    }

    public long Id { get; }

    public string Code { get; }

    public string Name { get; private set; }

    public long ProductionUnitId { get; }

    public bool IsActive { get; private set; }

    public long CreatedByUserId { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void Rename(string name, DateTime changedAtUtc)
    {
        Name = UnitOfMeasure.RequireName(name);
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    public void SetActive(bool isActive, DateTime changedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }
}
