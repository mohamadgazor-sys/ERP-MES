using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

public sealed record KpiUnit(long UnitId, string Code, string Name);

public sealed record KpiOperator(long OperatorId, string Code, string Name);

/// <summary>An APPROVED job report line (the only time that counts): times are UTC instants, the unit is the report's unit.</summary>
public sealed record KpiApprovedLine(long ReportId, long OperatorId, long UnitId, DateOnly WorkDate, DateTime StartUtc, DateTime EndUtc, int ProductiveMinutes);

/// <summary>A QC decision on base units of a stage that was planned in a unit; the unit that performed the stage gets the figure (Decision Log #95 D6).</summary>
public sealed record KpiInspection(long UnitId, InspectionDisposition Disposition, long BaseUnits, DateTime DecidedAtUtc);

/// <summary>A QC-approved stage planned in a unit with its planned minutes, the approved actual minutes reported for it and the time of its last QC decision.</summary>
public sealed record KpiCompletedStage(long UnitId, long ItemId, int StageNo, decimal PlannedMinutes, int ActualApprovedMinutes, DateTime CompletedAtUtc);

/// <summary>An operator planned on a unit's shift on a date, with the working minutes of the shift (break excluded).</summary>
public sealed record KpiScheduledRow(long UnitId, long OperatorId, DateOnly Date, int WorkingMinutes);

/// <summary>An approved overtime window (local wall-clock times) of an operator.</summary>
public sealed record KpiOvertimeWindow(long OperatorId, DateTime StartLocal, DateTime EndLocal);

public sealed record KpiPending(long UnitId, int SubmittedReports);

/// <summary>The raw facts one KPI run is computed from, read in one pass; the periods are widened by a day around the range and filtered exactly in the calculator.</summary>
public sealed record KpiFacts(
    IReadOnlyList<KpiUnit> Units,
    IReadOnlyList<KpiOperator> Operators,
    IReadOnlyList<KpiApprovedLine> Lines,
    IReadOnlyList<KpiPending> Pending,
    IReadOnlyList<KpiInspection> Inspections,
    IReadOnlyList<KpiCompletedStage> CompletedStages,
    IReadOnlyList<KpiScheduledRow> Scheduled,
    IReadOnlyList<KpiOvertimeWindow> Overtime);

/// <summary>Read-only access to everything the unit metrics need (Decision Log #106); nothing is written.</summary>
public interface IKpiRepository
{
    Task<KpiFacts> LoadAsync(DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken = default);
}
