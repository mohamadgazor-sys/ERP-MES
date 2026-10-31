using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Materials;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>One approved report line that may enter a cost: reported time on a machine for the item.</summary>
public sealed record ApprovedTimeLine(long JobReportLineId, long MachineId, DateOnly WorkDate, int ProductiveMinutes);

/// <summary>Everything a cost is computed from, read INSIDE the transaction that saves the snapshot.</summary>
public sealed record CostInputs(
    long ItemId,
    long RequestId,
    string RequestNo,
    string ItemNo,
    WorkType WorkType,
    bool ContractApproved,
    bool RouteComplete,
    DateTime? FirstReleaseUtc,
    long? InputBaseUnits,
    long AcceptedBaseUnits,
    long? MaterialId,
    string? MaterialCode,
    decimal? WeightKgPerBaseUnit,
    IReadOnlyList<ApprovedTimeLine> Lines,
    IReadOnlyList<MachineHourlyRate> Rates,
    IReadOnlyDictionary<long, string> MachineCodes,
    IReadOnlyList<MaterialPrice> Prices,
    int NextSequenceNo,
    IReadOnlyList<ERP.MES.Domain.Production.WorkSession>? Sessions = null,
    int UnapprovedFinishedSessions = 0)
{
    /// <summary>The item's time-approved work sessions (Decision Log #114); machine runtime of these, counted once per machine, is costed beside the approved job report lines.</summary>
    public IReadOnlyList<ERP.MES.Domain.Production.WorkSession> ApprovedSessions { get; } = Sessions ?? [];
}

/// <summary>The outcome of building a snapshot: the calculation and its decision, or the refusal and the named gaps.</summary>
public sealed record CostBuild(ItemCostCalculation? Calculation, ItemPriceDecision? Decision, ValidationIssue Refusal, IReadOnlyList<string> Gaps);

/// <summary>An item whose route is complete with its latest snapshot and price decision, for the costing list.</summary>
public sealed record CostingItemSummary(
    long RequestId, string RequestNo, long ItemId, string ItemNo, string PartName, WorkType WorkType, bool ContractApproved, long AcceptedBaseUnits,
    Stored<ItemCostCalculation>? LatestCalculation, Stored<ItemPriceDecision>? LatestDecision, string CustomerName = "");

public sealed record CostingHistory(IReadOnlyList<Stored<ItemCostCalculation>> Calculations, IReadOnlyList<Stored<ItemPriceDecision>> Decisions);

/// <summary>Persistence of cost snapshots and price decisions (Decision Log #104). Snapshots are append-only; a decision moves Draft, Submitted, Approved under its row version.</summary>
public interface ICostingRepository
{
    Task<IReadOnlyList<CostingItemSummary>> ListItemsAsync(CancellationToken cancellationToken = default);

    Task<CostingHistory> HistoryAsync(long requestItemId, CancellationToken cancellationToken = default);

    /// <summary>Reads the inputs, lets <paramref name="build"/> compute the snapshot, and saves it with its draft decision and the audit entry; a refusal saves nothing.</summary>
    Task<(Stored<ItemCostCalculation>? Calculation, Stored<ItemPriceDecision>? Decision, CostBuild Build)> CalculateAsync(
        long requestItemId, Func<CostInputs, CostBuild> build, AuditLogEntry audit, CancellationToken cancellationToken = default);

    /// <summary>Loads the decision under <paramref name="expectedVersion"/> and lets <paramref name="change"/> apply a transition; a refusal saves nothing.</summary>
    Task<(Stored<ItemPriceDecision>? Row, ValidationIssue Refusal)> ChangeDecisionAsync(
        long decisionId, long expectedVersion, Func<ItemPriceDecision, ValidationIssue> change, AuditLogEntry audit, CancellationToken cancellationToken = default);
}
