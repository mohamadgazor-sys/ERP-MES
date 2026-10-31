using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

/// <summary>
/// The stage actions of operation (Decision Log #58, #79, #99, #109). The whole-quantity QC approval of the demonstration is retired: QC decides by quantity (<see cref="QcInspectionService"/>, Decision Log #101).
/// </summary>
public enum StageAction
{
    /// <summary>PLANNED to WAITING for the first stage (Planning).</summary>
    Release = 1,

    /// <summary>WAITING to IN_PROGRESS (Production).</summary>
    Start,

    /// <summary>IN_PROGRESS to WAITING_QC (Production).</summary>
    SubmitForQc,
}

/// <summary>The two corrections Planning may make to an erroneous operator action.</summary>
public enum StageCorrection
{
    UndoStart = 1,
    UndoSubmit,
}

/// <summary>The loaded state an action is based on: the ids and every row version the entry was read with. Build it from the entry the user saw (<see cref="From"/>).</summary>
public sealed record StageTarget(long RequestId, long ItemId, int StageNo, long RequestVersion, long ItemVersion, IReadOnlyDictionary<int, long> StageVersions)
{
    /// <summary>The target of the entry's current stage, with the versions the entry was read at. The entry must have a current stage.</summary>
    public static StageTarget From(ProductionQueueEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var stage = entry.CurrentStage ?? throw new ArgumentException("The item has no open stage.", nameof(entry));
        return new StageTarget(entry.RequestId, entry.ItemId, stage.StageNo, entry.RequestVersion, entry.ItemVersion, entry.StageVersions);
    }
}

/// <summary>What a successful stage action did: the new status of the stage, and of the next stage when QC approval released it.</summary>
public sealed record StageActionResult(StageAction Action, int StageNo, ItemStageStatus NewStatus, int? NextStageNo, ItemStageStatus? NextStageNewStatus);


/// <summary>
/// The limited, DEMONSTRATION-ONLY stage progression and QC approval (Decision Log #58, #79): release the first stage, start it, submit it for QC, record whole-quantity QC
/// approval, and the next stage becomes available only after the preceding stage is approved. It uses the Domain's existing demonstration-only transitions and the existing
/// status model and adds no production transition: there is no QC rejection, rework, scrap, external supply or route editing here.
///
/// Every action: (1) reloads the user from the database and checks the permission (<see cref="Permission.ReleaseStage"/> Planning, <see cref="Permission.RunStage"/> Production,
/// <see cref="Permission.InspectOrCorrectQc"/> QC - the demo-only mapping approved in Decision Log #80); (2) names the expected versions of the request row, the item row
/// and every stage row the action changes or relies on, and passes them to <see cref="IRequestRepository.UpdateRowsAsync(long, RowVersionExpectations, Action{Request}, Func{Request, IReadOnlyList{AuditLogEntry}}, CancellationToken)"/>,
/// which compares them BEFORE the Domain method runs; (3) lets the Domain validate the transition and the QC gate; (4) saves the change, the append-only QC approval and the audit
/// entry in ONE transaction. A stale version, an invalid transition or a failure leaves everything unchanged.
///
/// DEMO-ONLY for stages WITHOUT a production unit (seeded demonstration routes): there is no request-status, contract or Management-approval gate (Decision Log #80). A stage that Planning
/// planned in a production unit (phase 4a onward) follows the operational rules instead (Decision Log #99): releasing or starting needs the production-release gates (Management, Drawing, contract,
/// inquiry, clarification) and a request that is not cancelled or closed; a stopped stage cannot proceed; a Production user acts only on the units they are designated to.
///
/// GUARANTEE (per row, not per aggregate - Decision Log #77): the named rows are protected against a stale view, and every row the change touches is protected until it is saved.
/// A row of the same request that the action neither names nor changes is not compared.
/// </summary>
public sealed class StageWorkService(IRequestRepository requests, CurrentAuthorization authorization, IClock clock, IAuditContext auditContext, RequestGateService gates, IUnitQueueRepository unitStore, IWorkSessionRepository workSessions, IItemDrawingRepository? drawings = null)
{
    /// <summary>The warning text carried in <see cref="Outcome{T}.Warnings"/> when the first stage of an item without a technical drawing is released.</summary>
    public const string NoDrawingWarning = "NoDrawing";

    /// <summary>
    /// Planning corrects an erroneous stage start (back to waiting) or submission (back to in progress) with a mandatory reason, audited (Decision Log #95 D8, #102). It is refused once QC has decided
    /// anything about the stage. Nobody else can change a start or submission made by another operator.
    /// </summary>
    public async Task<Outcome<StageActionResult>> CorrectAsync(Principal? principal, StageCorrection correction, StageTarget target, string? reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var decision = await authorization.CheckAsync(principal, Permission.CorrectStageAction, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<StageActionResult>.Fail(OperationStatus.NotAuthorized);
        }

        var text = reason?.Trim();
        if (!Enum.IsDefined(correction) || string.IsNullOrEmpty(text) || text.Length > 300)
        {
            return Outcome<StageActionResult>.Invalid(ValidationIssue.QueueReasonRequired);
        }

        var expected = RowVersionExpectations.ForRequest(target.RequestVersion).WithItem(target.ItemId, target.ItemVersion);
        if (!target.StageVersions.TryGetValue(target.StageNo, out var own))
        {
            return Outcome<StageActionResult>.Invalid(ValidationIssue.VersionsMissing);
        }

        expected = expected.WithStage(target.ItemId, target.StageNo, own);
        var user = decision.Current!;
        var now = clock.UtcNow;
        var before = ItemStageStatus.Planned;
        var after = ItemStageStatus.Planned;
        var summary = string.Empty;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                await requests.UpdateRowsAsync(
                    target.RequestId,
                    expected,
                    request =>
                    {
                        var item = request.Items.FirstOrDefault(i => i.Id == target.ItemId) ?? throw new InvalidOperationException("The request has no such item.");
                        var stage = item.Stages.FirstOrDefault(s => s.StageNo == target.StageNo) ?? throw new InvalidOperationException("The item has no such stage.");
                        before = stage.Status;
                        if (correction == StageCorrection.UndoStart)
                        {
                            item.UndoStart(target.StageNo);
                        }
                        else
                        {
                            item.UndoSubmit(target.StageNo);
                        }

                        after = stage.Status;
                        summary = $"Request {request.RequestNo.Value}, item {item.ItemNo}, stage {target.StageNo} ({stage.StageTitle}) corrected by Planning: {before.Code()} -> {after.Code()}; reason: {text}";
                    },
                    _ => [new AuditLogEntry(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, AuditActions.StageCorrected, "ItemStage", target.ItemId + "/" + target.StageNo, summary)],
                    cancellationToken);
                return Outcome<StageActionResult>.Ok(new StageActionResult(StageAction.Start, target.StageNo, after, null, null));
            });
        }
        catch (InvalidOperationException)
        {
            return Outcome<StageActionResult>.Fail(OperationStatus.InvalidState);
        }
    }

    public async Task<Outcome<StageActionResult>> PerformAsync(Principal? principal, StageAction action, StageTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(action))
        {
            return Outcome<StageActionResult>.Invalid(ValidationIssue.StageActionInvalid);
        }

        var permission = action == StageAction.Release ? Permission.ReleaseStage : Permission.RunStage;
        var decision = await authorization.CheckAsync(principal, permission, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<StageActionResult>.Fail(OperationStatus.NotAuthorized);
        }

        if (!TryExpectations(action, target, out var expected))
        {
            return Outcome<StageActionResult>.Invalid(ValidationIssue.VersionsMissing);
        }

        var userId = decision.Current!.UserId;
        var now = clock.UtcNow;
        var number = target.StageNo;
        var before = ItemStageStatus.Planned;
        var after = ItemStageStatus.Planned;
        string title = string.Empty;
        string itemNo = string.Empty;
        string requestNo = string.Empty;

        void Change(Request request)
        {
            var item = request.Items.FirstOrDefault(i => i.Id == target.ItemId) ?? throw new InvalidOperationException("The request has no such item.");
            var stage = item.Stages.FirstOrDefault(s => s.StageNo == number) ?? throw new InvalidOperationException("The item has no such stage.");
            requestNo = request.RequestNo.Value;
            itemNo = item.ItemNo;
            title = stage.StageTitle;
            before = stage.Status;

            // The Domain methods validate the transition and the QC gate; they throw InvalidOperationException for anything else.
            switch (action)
            {
                case StageAction.Release:
                    if (number != 1)
                    {
                        throw new InvalidOperationException("Only the first stage is released by Planning.");
                    }

                    item.ReleaseFirstStage(now);
                    break;
                case StageAction.Start:
                    item.StartStage(number);
                    break;
                default:
                    item.SubmitStageForQc(number);
                    break;
            }

            after = stage.Status;
        }

        IReadOnlyList<AuditLogEntry> Audit(Request request)
        {
            var (code, text) = action switch
            {
                StageAction.Release => (AuditActions.StageReleased, "released"),
                StageAction.Start => (AuditActions.StageStarted, "started"),
                _ => (AuditActions.StageSubmittedForQc, "submitted for QC"),
            };
            var summary = $"Request {requestNo}, item {itemNo}, stage {number} ({title}) {text}: {before.Code()} -> {after.Code()}";

            return [new AuditLogEntry(now, userId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, "ItemStage", target.ItemId + "/" + number, summary)];
        }

        try
        {
            var current = decision.Current!;
            return await CustomerService.GuardAsync(async () =>
            {
                if (await CheckUnitScopeAsync(action, target, current, cancellationToken) is { } refused)
                {
                    return refused;
                }

                await requests.UpdateRowsAsync(target.RequestId, expected, Change, Audit, cancellationToken);

                // A missing technical drawing warns when the first stage is released but never blocks it (Decision Log #125 Q14).
                IReadOnlyList<string>? warnings = null;
                if (action == StageAction.Release && target.StageNo == 1 && drawings is not null && (await drawings.ListCurrentAsync([target.ItemId], cancellationToken)).Count == 0)
                {
                    warnings = [NoDrawingWarning];
                }

                return Outcome<StageActionResult>.Ok(new StageActionResult(action, number, after, null, null), warnings);
            });
        }
        catch (InvalidOperationException)
        {
            // An invalid transition or a QC-gate refusal from the Domain (or a missing item or stage). Nothing was saved.
            return Outcome<StageActionResult>.Fail(OperationStatus.InvalidState);
        }
    }

    // Operational rules (Decision Log #99, #109); null = allowed here (the Domain still validates the transition). A stage without a production unit can no longer be released, started or submitted:
    // the demonstration flow that acted on such stages without a unit designation is retired.
    private async Task<Outcome<StageActionResult>?> CheckUnitScopeAsync(StageAction action, StageTarget target, Principal user, CancellationToken cancellationToken)
    {
        var loaded = await requests.FindAsync(target.RequestId, cancellationToken);
        if (loaded is null)
        {
            return Outcome<StageActionResult>.Fail(OperationStatus.NotFound);
        }

        var stage = loaded.Value.Items.FirstOrDefault(i => i.Id == target.ItemId)?.Stages.FirstOrDefault(s => s.StageNo == target.StageNo);
        if (stage is null)
        {
            return Outcome<StageActionResult>.Fail(OperationStatus.NotFound);
        }

        if (stage.ProductionUnitId is not { } unitId)
        {
            return Outcome<StageActionResult>.Invalid(ValidationIssue.StageUnitRequired);
        }

        var closed = loaded.Value.Status is RequestStatus.Cancelled or RequestStatus.Closed;
        if (closed)
        {
            return Outcome<StageActionResult>.Invalid(ValidationIssue.StageGateNotSatisfied);
        }

        // The first stage of an assembly is released only when every child and descendant has finished its route with every QC approval (Decision Log #121 Q5).
        if (action == StageAction.Release
            && loaded.Value.Items.FirstOrDefault(i => i.Id == target.ItemId) is { IsAssembly: true } assembly
            && !loaded.Value.AllDescendantsRouteCompleted(assembly))
        {
            return Outcome<StageActionResult>.Invalid(ValidationIssue.AssemblyChildrenNotComplete);
        }

        // An existing REPAIR item without its delivery date or warehouse code stays visible but cannot be released until completed (Decision Log #124 B5).
        if (action == StageAction.Release
            && loaded.Value.Items.FirstOrDefault(i => i.Id == target.ItemId) is { } repair
            && repair.IsRepairIncomplete(loaded.Value.WorkType))
        {
            return Outcome<StageActionResult>.Invalid(ValidationIssue.RepairFieldsMissing);
        }

        if (action is StageAction.Release or StageAction.Start)
        {
            var gate = await gates.EvaluateAsync(target.RequestId, target.ItemId, cancellationToken);
            if (gate is not { Satisfied: true })
            {
                return Outcome<StageActionResult>.Invalid(ValidationIssue.StageGateNotSatisfied);
            }
        }

        // Work sessions (Decision Log #112): the legacy whole-stage Start cannot be used on a stage that sessions track, and a stage that sessions track goes to QC only when every session is finished or withdrawn.
        if (action == StageAction.Start && await workSessions.StageHasSessionsAsync(target.ItemId, target.StageNo, cancellationToken))
        {
            return Outcome<StageActionResult>.Invalid(ValidationIssue.SessionTimeBasisConflict);
        }

        if (action == StageAction.SubmitForQc)
        {
            var rows = await workSessions.ListForStageAsync(target.ItemId, target.StageNo, cancellationToken);
            if (rows.Any(r => r.Session.Value.IsUnfinished))
            {
                return Outcome<StageActionResult>.Invalid(ValidationIssue.SubmitBlockedBySessions);
            }

            if (rows.Count > 0 && rows.All(r => r.Session.Value.Status == Domain.Production.WorkSessionStatus.Cancelled))
            {
                return Outcome<StageActionResult>.Invalid(ValidationIssue.SubmitNoFinishedSession);
            }
        }

        if (action != StageAction.Release && stage.IsStopped)
        {
            return Outcome<StageActionResult>.Invalid(ValidationIssue.StageStopped);
        }

        if (action is StageAction.Start or StageAction.SubmitForQc && user.DepartmentCode == DepartmentCodes.Production)
        {
            var designated = (await unitStore.ListDesignationsAsync(cancellationToken)).Any(d => d.Value.UserId == user.UserId && d.Value.ProductionUnitId == unitId && d.Value.IsActive);
            if (!designated)
            {
                return Outcome<StageActionResult>.Invalid(ValidationIssue.StageUnitNotDesignated);
            }
        }

        return null;
    }

    // The rows the action changes or relies on: the request and the item (the approval changes the item's update time; every action relies on the item's route), the stage itself, the
    // previous stage (starting relies on it being approved). Rows the caller did not read are not invented.
    private static bool TryExpectations(StageAction action, StageTarget target, out RowVersionExpectations expected)
    {
        expected = RowVersionExpectations.ForRequest(target.RequestVersion).WithItem(target.ItemId, target.ItemVersion);
        var number = target.StageNo;
        if (number < 1 || !target.StageVersions.TryGetValue(number, out var own))
        {
            return false;
        }

        expected = expected.WithStage(target.ItemId, number, own);
        if (action == StageAction.Start && number > 1)
        {
            if (!target.StageVersions.TryGetValue(number - 1, out var previous))
            {
                return false;
            }

            expected = expected.WithStage(target.ItemId, number - 1, previous);
        }

        return true;
    }
}
