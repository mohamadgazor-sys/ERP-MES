using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

/// <summary>One stage waiting for a QC decision (or a shortfall decision), with its base-unit balance and the row versions it was read with.</summary>
public sealed record InspectionEntry2(
    long RequestId,
    string RequestNo,
    string CustomerName,
    long ItemId,
    string ItemNo,
    string PartName,
    int StageNo,
    int StageCount,
    string StageTitle,
    ItemStageStatus Status,
    int BaseUnitsPerUnit,
    long InputBaseUnits,
    long AwaitingQcBaseUnits,
    long AcceptedBaseUnits,
    long ReworkBaseUnits,
    long ScrapBaseUnits,
    long RemainingBaseUnits,
    long? ProductionUnitId)
{
    /// <summary>Units of an external stage that the contractor never returned (Decision Log #136): part of the shortfall to decide, but not scrap and never QC approved.</summary>
    public long LostAtContractorBaseUnits { get; init; }

    public long RequestVersion { get; init; }

    public long ItemVersion { get; init; }

    public IReadOnlyDictionary<int, long> StageVersions { get; init; } = new Dictionary<int, long>();

    public StageTarget ToStageTarget() => new(RequestId, ItemId, StageNo, RequestVersion, ItemVersion, StageVersions);
}

public sealed record InspectionCapabilities(bool CanView, bool CanInspect, bool CanAcceptShortfall, bool CanOrderReplacement)
{
    public static InspectionCapabilities None { get; } = new(false, false, false, false);
}

public sealed record InspectionWorklist(IReadOnlyList<InspectionEntry2> Entries, InspectionCapabilities Capabilities);

public sealed record InspectionHistory(IReadOnlyList<QcInspectionLine> Lines, IReadOnlyList<ShortfallDecision> Decisions, IReadOnlyList<QcCorrection>? Corrections = null);

/// <summary>
/// Quantity-level QC (Decision Log #92, #95 D2/D3, #101): QC decides base units of a submitted stage as accepted, rework or scrap (no per-piece identity); rework returns the units to the stage once;
/// scrap leaves a shortfall that Management may accept or Planning may answer with a recorded replacement order; the accepted base units are all the next stage receives and the produced quantity.
/// Every command reloads the user from the database, names the row versions it relies on, lets the Domain validate and saves the change, the append-only lines or decision and the audit in ONE transaction.
/// </summary>
public sealed class QcInspectionService(IRequestRepository requests, IQcInspectionRepository store, CurrentAuthorization authorization, IClock clock, IAuditContext auditContext, IWorkSessionRepository? workSessions = null)
{
    private static readonly Permission[] All = [Permission.ViewQcInspections, Permission.InspectOrCorrectQc, Permission.AcceptShortfall, Permission.OrderShortfallReplacement];

    public async Task<InspectionCapabilities> GetCapabilitiesAsync(Principal? principal, CancellationToken cancellationToken = default) =>
        CapabilitiesOf(await authorization.EvaluateAsync(principal, All, cancellationToken));

    /// <summary>The stages waiting for QC or for a shortfall decision, and the stages in rework, oldest request first.</summary>
    public async Task<Outcome<InspectionWorklist>> LoadAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, All, cancellationToken);
        var caps = CapabilitiesOf(set);
        if (!caps.CanView)
        {
            return Outcome<InspectionWorklist>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var entries = new List<InspectionEntry2>();
            foreach (var summary in await requests.ListAsync(customerId: null, cancellationToken))
            {
                var loaded = await requests.FindWithVersionsAsync(summary.RequestId, cancellationToken);
                if (loaded is null)
                {
                    continue;
                }

                foreach (var item in loaded.Value.Items)
                {
                    var versions = item.Stages.ToDictionary(s => s.StageNo, s => loaded.Versions.StageVersion(item.Id, s.StageNo));
                    foreach (var stage in item.Stages.Where(s => s.InputBaseUnits is not null && (s.Status is ItemStageStatus.WaitingQc or ItemStageStatus.WaitingShortfallDecision || (s.Status == ItemStageStatus.InProgress && s.ReworkBaseUnits > 0))))
                    {
                        entries.Add(new InspectionEntry2(
                            summary.RequestId, summary.RequestNo.Value, summary.CustomerName, item.Id, item.ItemNo, item.PartName, stage.StageNo, item.Stages.Count, stage.StageTitle, stage.Status,
                            item.BaseUnitsPerUnit, stage.InputBaseUnits!.Value, stage.AwaitingQcBaseUnits, stage.AcceptedBaseUnits, stage.ReworkBaseUnits, stage.ScrapBaseUnits, stage.RemainingBaseUnits ?? 0, stage.ProductionUnitId)
                        {
                            LostAtContractorBaseUnits = stage.ExternalMissingBaseUnits,
                            RequestVersion = loaded.Versions.Request,
                            ItemVersion = loaded.Versions.ItemVersion(item.Id),
                            StageVersions = versions,
                        });
                    }
                }
            }

            return Outcome<InspectionWorklist>.Ok(new InspectionWorklist(entries.OrderBy(e => e.RequestId).ThenBy(e => e.ItemId).ThenBy(e => e.StageNo).ToList(), caps));
        });
    }

    public async Task<Outcome<InspectionHistory>> HistoryAsync(Principal? principal, long itemId, int stageNo, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewQcInspections, cancellationToken)).IsAllowed)
        {
            return Outcome<InspectionHistory>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
            Outcome<InspectionHistory>.Ok(new InspectionHistory(await store.ListLinesAsync(itemId, stageNo, cancellationToken), await store.ListDecisionsAsync(itemId, stageNo, cancellationToken),
                (await store.ListCorrectionsAsync(itemId, cancellationToken)).Where(c => c.StageNo == stageNo).ToList())));
    }

    /// <summary>QC decides base units of a submitted stage. At least one entry; never more than what is awaiting QC; a reason for rework and scrap.</summary>
    public async Task<Outcome<InspectionOutcome>> RecordInspectionAsync(Principal? principal, StageTarget target, IReadOnlyList<InspectionEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entries);
        var decision = await authorization.CheckAsync(principal, Permission.InspectOrCorrectQc, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<InspectionOutcome>.Fail(OperationStatus.NotAuthorized);
        }

        if (!TryExpectations(target, out var expected))
        {
            return Outcome<InspectionOutcome>.Invalid(ValidationIssue.VersionsMissing);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        var inspectionId = Guid.NewGuid().ToString("N");
        InspectionOutcome? result = null;
        var title = string.Empty;
        var itemNo = string.Empty;
        var requestNo = string.Empty;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                await store.CommitAsync(
                    target.RequestId,
                    expected,
                    request =>
                    {
                        var item = request.Items.FirstOrDefault(i => i.Id == target.ItemId) ?? throw new InvalidOperationException("The request has no such item.");
                        requestNo = request.RequestNo.Value;
                        itemNo = item.ItemNo;
                        title = item.Stages.FirstOrDefault(s => s.StageNo == target.StageNo)?.StageTitle ?? string.Empty;
                        result = item.RecordInspection(target.StageNo, entries, user.UserId, now, inspectionId);
                        return new QcAppended(result.Lines, null);
                    },
                    request =>
                    {
                        var accepted = result!.Lines.Where(l => l.Disposition == InspectionDisposition.Accepted).Sum(l => l.BaseUnits);
                        var rework = result.Lines.Where(l => l.Disposition == InspectionDisposition.Rework).Sum(l => l.BaseUnits);
                        var scrap = result.Lines.Where(l => l.Disposition == InspectionDisposition.Scrap).Sum(l => l.BaseUnits);
                        return [Audit(user, now, AuditActions.QcInspectionRecorded, $"{target.ItemId}/{target.StageNo}",
                            $"Request {requestNo}, item {itemNo}, stage {target.StageNo} ({title}): QC accepted {accepted}, rework {rework}, scrap {scrap} base units; stage is now {result.NewStatus.Code()}{(result.Completed ? " (QC approved)" : string.Empty)}")];
                    },
                    cancellationToken);
                return Outcome<InspectionOutcome>.Ok(result!);
            });
        }
        catch (ArgumentOutOfRangeException)
        {
            return Outcome<InspectionOutcome>.Invalid(ValidationIssue.InspectionEntriesInvalid);
        }
        catch (ArgumentException)
        {
            return Outcome<InspectionOutcome>.Invalid(ValidationIssue.InspectionEntriesInvalid);
        }
        catch (InspectionExceedsAwaitingException)
        {
            return Outcome<InspectionOutcome>.Invalid(ValidationIssue.InspectionExceedsAwaiting);
        }
        catch (StageNotCountableException)
        {
            return Outcome<InspectionOutcome>.Invalid(ValidationIssue.StageNotCountable);
        }
        catch (InvalidOperationException)
        {
            return Outcome<InspectionOutcome>.Fail(OperationStatus.InvalidState);
        }
    }

    /// <summary>
    /// QC corrects the final decision of a QC_APPROVED stage (Master Specification 1.10, 15; owner decisions Q1-Q8, Decision Log #142): the quantity of previously accepted units that are now rework and/or scrap, with a
    /// mandatory reason. QC alone (the permission is checked here, not in the screen), any time before the request is closed, no separate Management approval. In ONE transaction: the stage returns as executable
    /// work or waits for the shortfall decision (nothing is deleted or reset), the append-only correction is recorded with the earlier decisions it supersedes, the latest cost snapshot, its price approval, the
    /// Warehouse receipt and the finished/sent mark are invalidated, a customer dispatch is only flagged, downstream stages that had progressed are marked affected, and an assembly parent that has not started
    /// returns to the waiting state (one already in work is marked, not reset).
    /// </summary>
    public async Task<Outcome<QcCorrection>> CorrectAsync(
        Principal? principal, StageTarget target, long reworkBaseUnits, long scrapBaseUnits, string? reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var decision = await authorization.CheckAsync(principal, Permission.InspectOrCorrectQc, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<QcCorrection>.Fail(OperationStatus.NotAuthorized);
        }

        if (!TryExpectations(target, out var expected))
        {
            return Outcome<QcCorrection>.Invalid(ValidationIssue.VersionsMissing);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        var requestNo = string.Empty;
        var itemNo = string.Empty;
        var title = string.Empty;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var loaded = await requests.FindAsync(target.RequestId, cancellationToken);
                if (loaded is null)
                {
                    return Outcome<QcCorrection>.Fail(OperationStatus.NotFound);
                }

                if (loaded.Value.Status is RequestStatus.Cancelled or RequestStatus.Closed)
                {
                    return Outcome<QcCorrection>.Invalid(ValidationIssue.StageGateNotSatisfied);
                }

                // The assembly parents above the corrected item: whether the first stage of each has work sessions decides between "back to waiting" and "only marked" (owner decision Q7).
                var sessionsOfAncestors = new Dictionary<long, bool>();
                var cursor = loaded.Value.Items.FirstOrDefault(i => i.Id == target.ItemId)?.ParentItemId;
                while (cursor is { } parentId && loaded.Value.Items.FirstOrDefault(i => i.Id == parentId) is { } parent && !sessionsOfAncestors.ContainsKey(parentId))
                {
                    sessionsOfAncestors[parentId] = workSessions is not null && await workSessions.StageHasSessionsAsync(parentId, 1, cancellationToken);
                    cursor = parent.ParentItemId;
                }

                await store.CorrectAsync(
                    target.RequestId,
                    target.ItemId,
                    target.StageNo,
                    expected,
                    (request, facts) =>
                    {
                        var item = request.Items.FirstOrDefault(i => i.Id == target.ItemId) ?? throw new InvalidOperationException("The request has no such item.");
                        requestNo = request.RequestNo.Value;
                        itemNo = item.ItemNo;
                        title = item.Stages.FirstOrDefault(s => s.StageNo == target.StageNo)?.StageTitle ?? string.Empty;

                        if (item.Stages.FirstOrDefault(s => s.StageNo == target.StageNo)?.Status != ItemStageStatus.QcApproved)
                        {
                            throw new InvalidOperationException("Only a final (QC_APPROVED) stage can be corrected.");
                        }

                        // The newest accepted decisions are superseded first; only the still-valid part of a line can be taken again.
                        var needed = reworkBaseUnits + scrapBaseUnits;
                        var sources = new List<QcCorrectionSource>();
                        foreach (var line in facts.AcceptedLines.OrderByDescending(l => l.LineId))
                        {
                            var take = Math.Min(line.ValidBaseUnits, needed);
                            if (take > 0)
                            {
                                sources.Add(new QcCorrectionSource(line.LineId, take));
                                needed -= take;
                            }
                        }

                        if (needed > 0)
                        {
                            throw new CorrectionExceedsAcceptedException("The accepted decisions of the stage do not cover " + (reworkBaseUnits + scrapBaseUnits) + " base units.");
                        }

                        var correction = item.CorrectQcDecision(target.StageNo, reworkBaseUnits, scrapBaseUnits, reason, sources, facts.CustomerDispatchIds.Count > 0, user.UserId, now);
                        foreach (var (ancestorId, hasSessions) in sessionsOfAncestors)
                        {
                            request.Items.First(i => i.Id == ancestorId).ApplyChildQcCorrection(correction, hasSessions);
                        }

                        var why = "QC correction of stage " + target.StageNo + " of item " + itemNo + ": " + correction.Reason;
                        var marks = new List<RecordInvalidation>();
                        void Mark(InvalidatedRecordKind kind, long? id, InvalidationEffect effect)
                        {
                            if (id is { } value)
                            {
                                marks.Add(new RecordInvalidation(target.ItemId, kind, value, effect, why.Length > RecordInvalidation.MaxReasonLength ? why[..RecordInvalidation.MaxReasonLength] : why, user.UserId, now, correction));
                            }
                        }

                        Mark(InvalidatedRecordKind.CostSnapshot, facts.CostSnapshotId, InvalidationEffect.Invalidated);
                        Mark(InvalidatedRecordKind.PriceDecision, facts.PriceDecisionId, InvalidationEffect.Invalidated);
                        Mark(InvalidatedRecordKind.WarehouseReceipt, facts.ReceiptId, InvalidationEffect.RequiresReconciliation);
                        Mark(InvalidatedRecordKind.FinishedSent, facts.FinishedSentId, InvalidationEffect.Invalidated);
                        foreach (var dispatchId in facts.CustomerDispatchIds)
                        {
                            Mark(InvalidatedRecordKind.CustomerDispatch, dispatchId, InvalidationEffect.Flagged);
                        }

                        return new CorrectionAppended(correction, marks);
                    },
                    (request, appended) =>
                    {
                        var c = appended.Correction;
                        var entries = new List<AuditLogEntry>
                        {
                            Audit(user, now, AuditActions.QcCorrectionRecorded, $"{target.ItemId}/{target.StageNo}",
                                $"Request {requestNo}, item {itemNo}, stage {target.StageNo} ({title}): QC corrected {c.CorrectedBaseUnits} accepted base units (rework {c.ReworkBaseUnits}, scrap {c.ScrapBaseUnits}); accepted {c.AcceptedBeforeBaseUnits} -> {c.AcceptedAfterBaseUnits}; stage is now {c.ResultingStatus.Code()}; reason: {c.Reason}"
                                + (c.AfterCustomerDispatch ? "; a customer dispatch of the item already exists (flagged, not undone)" : string.Empty)),
                        };
                        entries.AddRange(appended.Invalidations.Select(i => Audit(user, now, AuditActions.RecordInvalidatedByCorrection, $"{i.Kind}/{i.TargetId}",
                            $"{i.Kind} {i.TargetId} of item {itemNo} is no longer current ({i.Effect}) because of the QC correction of stage {target.StageNo}")));
                        return entries;
                    },
                    cancellationToken);
                var saved = (await store.ListCorrectionsAsync(target.ItemId, cancellationToken)).Where(c => c.StageNo == target.StageNo).OrderBy(c => c.Id).Last();
                return Outcome<QcCorrection>.Ok(saved);
            });
        }
        catch (CorrectionExceedsAcceptedException)
        {
            return Outcome<QcCorrection>.Invalid(ValidationIssue.CorrectionExceedsAccepted);
        }
        catch (StageNotCountableException)
        {
            return Outcome<QcCorrection>.Invalid(ValidationIssue.StageNotCountable);
        }
        catch (ArgumentException)
        {
            return Outcome<QcCorrection>.Invalid(ValidationIssue.CorrectionInvalid);
        }
        catch (InvalidOperationException)
        {
            return Outcome<QcCorrection>.Fail(OperationStatus.InvalidState);
        }
    }

    /// <summary>
    /// Closes the shortfall scrap created: Management accepts it, or Planning records that replacement production was ordered (no replacement is created). Each kind has its own permission.
    /// </summary>
    public async Task<Outcome<ShortfallDecision>> DecideShortfallAsync(Principal? principal, StageTarget target, ShortfallKind kind, string? note, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(kind))
        {
            return Outcome<ShortfallDecision>.Invalid(ValidationIssue.StageActionInvalid);
        }

        var permission = kind == ShortfallKind.ManagementAccepted ? Permission.AcceptShortfall : Permission.OrderShortfallReplacement;
        var decision = await authorization.CheckAsync(principal, permission, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<ShortfallDecision>.Fail(OperationStatus.NotAuthorized);
        }

        if (!TryExpectations(target, out var expected))
        {
            return Outcome<ShortfallDecision>.Invalid(ValidationIssue.VersionsMissing);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        ShortfallDecision? result = null;
        var itemNo = string.Empty;
        var requestNo = string.Empty;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                await store.CommitAsync(
                    target.RequestId,
                    expected,
                    request =>
                    {
                        var item = request.Items.FirstOrDefault(i => i.Id == target.ItemId) ?? throw new InvalidOperationException("The request has no such item.");
                        requestNo = request.RequestNo.Value;
                        itemNo = item.ItemNo;
                        result = item.DecideShortfall(target.StageNo, kind, note, user.UserId, now);
                        return new QcAppended([], result);
                    },
                    request => [Audit(user, now, AuditActions.ShortfallDecided, $"{target.ItemId}/{target.StageNo}",
                        $"Request {requestNo}, item {itemNo}, stage {target.StageNo}: shortfall of {result!.ShortfallBaseUnits} base units {(kind == ShortfallKind.ManagementAccepted ? "accepted by Management" : "answered by a replacement order recorded by Planning")}: {result.Note}")],
                    cancellationToken);
                return Outcome<ShortfallDecision>.Ok(result!);
            });
        }
        catch (ArgumentException)
        {
            return Outcome<ShortfallDecision>.Invalid(ValidationIssue.ShortfallNoteRequired);
        }
        catch (InvalidOperationException)
        {
            return Outcome<ShortfallDecision>.Fail(OperationStatus.InvalidState);
        }
    }

    // The rows the command changes or relies on: the request and item, the stage itself, the previous stage (it must be approved) and the next stage (it becomes executable).
    private static bool TryExpectations(StageTarget target, out RowVersionExpectations expected)
    {
        expected = RowVersionExpectations.ForRequest(target.RequestVersion).WithItem(target.ItemId, target.ItemVersion);
        var number = target.StageNo;
        if (number < 1 || !target.StageVersions.TryGetValue(number, out var own))
        {
            return false;
        }

        expected = expected.WithStage(target.ItemId, number, own);
        if (number > 1)
        {
            if (!target.StageVersions.TryGetValue(number - 1, out var previous))
            {
                return false;
            }

            expected = expected.WithStage(target.ItemId, number - 1, previous);
        }

        if (target.StageVersions.TryGetValue(number + 1, out var following))
        {
            expected = expected.WithStage(target.ItemId, number + 1, following);
        }

        return true;
    }

    private static InspectionCapabilities CapabilitiesOf(PermissionSet set) =>
        set.Current is null
            ? InspectionCapabilities.None
            : new InspectionCapabilities(set.Has(Permission.ViewQcInspections), set.Has(Permission.InspectOrCorrectQc), set.Has(Permission.AcceptShortfall), set.Has(Permission.OrderShortfallReplacement));

    private AuditLogEntry Audit(Principal user, DateTime now, string code, string targetId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, "ItemStage", targetId, summary);
}
