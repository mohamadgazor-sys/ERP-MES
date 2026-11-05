using System.Globalization;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

/// <summary>A dispatch with what is still outstanding from it and when it is expected back (dispatch time + lead time in calendar days; Decision Log #129 Q3).</summary>
public sealed record OpenDispatch(ExternalShipment Dispatch, long OutstandingBaseUnits, DateTime ExpectedReturnUtc, bool Overdue);

/// <summary>One external stage in the external-work view (Master Specification §16.7): request, item, stage, route, status, quantities, open dispatches and the full shipment history.</summary>
public sealed record ExternalWorkEntry(
    long RequestId, string RequestNo, string CustomerName, long ItemId, string ItemNo, string PartName, int StageNo, int StageCount, string StageTitle,
    long UnitId, string UnitCode, string UnitName, string UnitCategory, ItemStageStatus Status, int ExpectedLeadTimeDays,
    long InputBaseUnits, long DispatchedBaseUnits, long ReturnedBaseUnits, long AtContractorBaseUnits, long StillToSendBaseUnits, long AwaitingQcBaseUnits, long AcceptedBaseUnits,
    long ReworkBaseUnits, long ScrapBaseUnits, IReadOnlyList<OpenDispatch> OpenDispatches, IReadOnlyList<ExternalShipment> History)
{
    public long RequestVersion { get; init; }

    public long ItemVersion { get; init; }

    public IReadOnlyDictionary<int, long> StageVersions { get; init; } = new Dictionary<int, long>();

    public StageTarget ToStageTarget() => new(RequestId, ItemId, StageNo, RequestVersion, ItemVersion, StageVersions);
}

public sealed record ExternalWorkView(IReadOnlyList<ExternalWorkEntry> Entries, bool CanRecord);

/// <summary>
/// External supply and processing stages (Master Specification §16, Decision Log #129): the Warehouse records each dispatch to and each return from the external route as a separate historical
/// transaction (quantity, transaction time, user); partial dispatches and returns are allowed; a return never exceeds what its dispatch has outstanding; a mistaken row is cancelled with a
/// reason and stays in the history; returned units go to the existing quantity QC. Warehouse, Planning, Management, the Production Manager and QC see the status and outstanding quantities.
/// </summary>
public sealed class ExternalWorkService(
    IRequestRepository requests,
    IExternalShipmentRepository store,
    IPlanningMasterRepository planning,
    CurrentAuthorization authorization,
    IClock clock,
    IAuditContext auditContext)
{
    private static readonly Permission[] All = [Permission.ViewExternalWork, Permission.RecordExternalShipment];

    /// <summary>The external stages that are executable or waiting for QC / a shortfall decision; with <paramref name="includeCompleted"/> also the QC-approved and not yet released ones.</summary>
    public async Task<Outcome<ExternalWorkView>> LoadAsync(Principal? principal, bool includeCompleted = false, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, All, cancellationToken);
        if (!set.Has(Permission.ViewExternalWork) || set.Current is null)
        {
            return Outcome<ExternalWorkView>.Fail(OperationStatus.NotAuthorized);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var units = (await planning.ListUnitsAsync(cancellationToken)).ToDictionary(u => u.Value.Id, u => u.Value);
            var found = new List<(RequestSummary Summary, VersionedRequest Loaded, RequestItem Item, ItemStage Stage)>();
            foreach (var summary in await requests.ListAsync(customerId: null, cancellationToken))
            {
                var loaded = await requests.FindWithVersionsAsync(summary.RequestId, cancellationToken);
                foreach (var item in loaded?.Value.Items ?? [])
                {
                    foreach (var stage in item.Stages.Where(s => s.IsExternal && (includeCompleted || Open(s))))
                    {
                        found.Add((summary, loaded!, item, stage));
                    }
                }
            }

            var shipments = (await store.ListAsync(found.Select(f => f.Item.Id).Distinct().ToList(), cancellationToken)).ToLookup(s => (s.RequestItemId, s.StageNo));
            var entries = found.Select(f =>
            {
                var history = shipments[(f.Item.Id, f.Stage.StageNo)].OrderBy(s => s.RecordedAtUtc).ThenBy(s => s.Id).ToList();
                var open = history.Where(s => s.IsActive && s.Kind == ExternalShipmentKind.Dispatch)
                    .Select(d => (Dispatch: d, Outstanding: d.BaseUnits - history.Where(r => r.IsActive && r.Kind == ExternalShipmentKind.Return && r.DispatchId == d.Id).Sum(r => r.BaseUnits)))
                    .Where(d => d.Outstanding > 0)
                    .Select(d =>
                    {
                        var expected = d.Dispatch.RecordedAtUtc.AddDays(f.Stage.ExpectedLeadTimeDays!.Value);
                        return new OpenDispatch(d.Dispatch, d.Outstanding, expected, now > expected);
                    })
                    .ToList();
                var unit = f.Stage.ProductionUnitId is { } id && units.TryGetValue(id, out var u) ? u : null;
                return new ExternalWorkEntry(
                    f.Summary.RequestId, f.Summary.RequestNo.Value, f.Summary.CustomerName, f.Item.Id, f.Item.ItemNo, f.Item.PartName, f.Stage.StageNo, f.Item.Stages.Count, f.Stage.StageTitle,
                    f.Stage.ProductionUnitId ?? 0, unit?.Code ?? string.Empty, unit?.Name ?? string.Empty, unit?.Category ?? string.Empty, f.Stage.Status, f.Stage.ExpectedLeadTimeDays!.Value,
                    f.Stage.InputBaseUnits ?? 0, f.Stage.ExternalDispatchedBaseUnits, f.Stage.ExternalReturnedBaseUnits, f.Stage.AtContractorBaseUnits, f.Stage.StillToSendBaseUnits ?? 0,
                    f.Stage.AwaitingQcBaseUnits, f.Stage.AcceptedBaseUnits, f.Stage.ReworkBaseUnits, f.Stage.ScrapBaseUnits, open, history)
                {
                    RequestVersion = f.Loaded.Versions.Request,
                    ItemVersion = f.Loaded.Versions.ItemVersion(f.Item.Id),
                    StageVersions = f.Item.Stages.ToDictionary(s => s.StageNo, s => f.Loaded.Versions.StageVersion(f.Item.Id, s.StageNo)),
                };
            })
            .OrderByDescending(e => e.OpenDispatches.Any(d => d.Overdue)).ThenBy(e => e.RequestId).ThenBy(e => e.ItemId).ThenBy(e => e.StageNo).ToList();
            return Outcome<ExternalWorkView>.Ok(new ExternalWorkView(entries, set.Has(Permission.RecordExternalShipment)));
        });
    }

    /// <summary>The Warehouse sends <paramref name="baseUnits"/> of an external stage (partial dispatches allowed).</summary>
    public Task<Outcome<ExternalShipment>> DispatchAsync(Principal? principal, StageTarget target, long baseUnits, string? pieceDescription, string? note, CancellationToken cancellationToken = default) =>
        RecordAsync(principal, target, AuditActions.ExternalDispatchRecorded, (item, _, user, now) => item.DispatchExternal(target.StageNo, baseUnits, pieceDescription, note, user, now),
            (s, no, title) => $"Request {no}: {s.BaseUnits} base units of stage {target.StageNo} ({title}) dispatched to the external route", cancellationToken);

    /// <summary>The Warehouse records <paramref name="baseUnits"/> back from dispatch <paramref name="dispatchId"/>; they go to QC (partial returns allowed, never more than outstanding).</summary>
    public Task<Outcome<ExternalShipment>> ReturnAsync(Principal? principal, StageTarget target, long dispatchId, long baseUnits, string? note, CancellationToken cancellationToken = default) =>
        RecordAsync(principal, target, AuditActions.ExternalReturnRecorded, (item, shipments, user, now) =>
            {
                var dispatch = shipments.FirstOrDefault(s => s.Id == dispatchId) ?? throw new InvalidOperationException("No such dispatch of the stage.");
                return item.ReturnExternal(target.StageNo, dispatch, shipments, baseUnits, note, user, now);
            },
            (s, no, title) => $"Request {no}: {s.BaseUnits} base units of stage {target.StageNo} ({title}) returned from dispatch {dispatchId} and handed to QC", cancellationToken);

    /// <summary>The Warehouse cancels a mistaken dispatch (no returns yet) or return (QC has not decided its units) with a reason; the row stays in the history.</summary>
    public Task<Outcome<ExternalShipment>> CancelAsync(Principal? principal, StageTarget target, long shipmentId, string? reason, CancellationToken cancellationToken = default)
    {
        ExternalShipment? cancelled = null;
        return RecordAsync(principal, target, AuditActions.ExternalShipmentCancelled, (item, shipments, user, now) =>
            {
                cancelled = shipments.FirstOrDefault(s => s.Id == shipmentId) ?? throw new InvalidOperationException("No such shipment of the stage.");
                if (cancelled.Kind == ExternalShipmentKind.Dispatch)
                {
                    item.CancelExternalDispatch(target.StageNo, cancelled, shipments, reason, user, now);
                }
                else
                {
                    item.CancelExternalReturn(target.StageNo, cancelled, reason, user, now);
                }

                return null;
            },
            (_, no, title) => string.Create(CultureInfo.InvariantCulture, $"Request {no}: {cancelled!.Kind} {shipmentId} of {cancelled.BaseUnits} base units of stage {target.StageNo} ({title}) cancelled; reason: {cancelled.CancelReason}"),
            cancellationToken,
            () => cancelled);
    }

    private static bool Open(ItemStage stage) =>
        stage.Status is ItemStageStatus.WaitingExternalSend or ItemStageStatus.ExternalSent or ItemStageStatus.WaitingQc or ItemStageStatus.WaitingShortfallDecision;

    private async Task<Outcome<ExternalShipment>> RecordAsync(
        Principal? principal,
        StageTarget target,
        string action,
        Func<RequestItem, IReadOnlyList<ExternalShipment>, long, DateTime, ExternalShipment?> change,
        Func<ExternalShipment, string, string, string> summary,
        CancellationToken cancellationToken,
        Func<ExternalShipment?>? result = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        var decision = await authorization.CheckAsync(principal, Permission.RecordExternalShipment, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<ExternalShipment>.Fail(OperationStatus.NotAuthorized);
        }

        if (!target.StageVersions.TryGetValue(target.StageNo, out var own))
        {
            return Outcome<ExternalShipment>.Invalid(ValidationIssue.VersionsMissing);
        }

        var expected = RowVersionExpectations.ForRequest(target.RequestVersion).WithItem(target.ItemId, target.ItemVersion).WithStage(target.ItemId, target.StageNo, own);
        var user = decision.Current!;
        var now = clock.UtcNow;
        ExternalShipment? added = null;
        string requestNo = string.Empty, title = string.Empty;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                await store.CommitAsync(
                    target.RequestId,
                    expected,
                    target.ItemId,
                    target.StageNo,
                    (request, shipments) =>
                    {
                        var item = request.Items.FirstOrDefault(i => i.Id == target.ItemId) ?? throw new InvalidOperationException("The request has no such item.");
                        requestNo = request.RequestNo.Value;
                        title = item.Stages.FirstOrDefault(s => s.StageNo == target.StageNo)?.StageTitle ?? string.Empty;
                        added = change(item, shipments, user.UserId, now);
                        return added;
                    },
                    _ =>
                    [
                        new AuditLogEntry(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, action, "ItemStage", $"{target.ItemId}/{target.StageNo}",
                            summary((added ?? result?.Invoke())!, requestNo, title)),
                    ],
                    cancellationToken);
                return Outcome<ExternalShipment>.Ok((added ?? result?.Invoke())!);
            });
        }
        catch (ExternalQuantityException)
        {
            return Outcome<ExternalShipment>.Invalid(ValidationIssue.ExternalQuantityInvalid);
        }
        catch (ArgumentException)
        {
            return Outcome<ExternalShipment>.Invalid(ValidationIssue.ExternalTextInvalid);
        }
        catch (InvalidOperationException)
        {
            return Outcome<ExternalShipment>.Fail(OperationStatus.InvalidState);
        }
    }
}
