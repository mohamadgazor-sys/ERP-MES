using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

/// <summary>The four demonstration-only stage actions (Decision Log #58, #79). Each acts on the CURRENT stage of an item: the first stage QC has not approved.</summary>
public enum StageAction
{
    /// <summary>PLANNED to WAITING for the first stage (Planning).</summary>
    Release = 1,

    /// <summary>WAITING to IN_PROGRESS (Production).</summary>
    Start,

    /// <summary>IN_PROGRESS to WAITING_QC (Production).</summary>
    SubmitForQc,

    /// <summary>WAITING_QC to QC_APPROVED for the stage's whole planned quantity, which makes the next stage WAITING (QC).</summary>
    ApproveWholeQuantity,
}

/// <summary>Which stage actions the current user may perform, as the Application answers it now (from the database). A convenience for enabling buttons; every action checks again.</summary>
public sealed record StageWorkCapabilities(bool CanView, bool CanRelease, bool CanRun, bool CanApprove)
{
    public static StageWorkCapabilities None { get; } = new(false, false, false, false);
}

/// <summary>The stage actions that are possible for one queue entry for this user right now (permission AND stage state). The Domain and the use case enforce the same rules again.</summary>
public sealed record AvailableStageActions(bool CanRelease, bool CanStart, bool CanSubmitForQc, bool CanApprove)
{
    public static AvailableStageActions None { get; } = new(false, false, false, false);
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

/// <summary>The stage work screen's data: the items in the display order and what the user may do.</summary>
public sealed record StageWorkView(IReadOnlyList<ProductionQueueEntry> Entries, StageWorkCapabilities Capabilities);

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
/// DEMO-ONLY: there is no request-status, contract or Management-approval gate here. The request-status transition table and the relationship between contract / Management approval
/// and production release are unresolved (Decision Log #80), so the production eligibility rules are neither implemented nor approved by this service.
///
/// GUARANTEE (per row, not per aggregate - Decision Log #77): the named rows are protected against a stale view, and every row the change touches is protected until it is saved.
/// A row of the same request that the action neither names nor changes is not compared.
/// </summary>
public sealed class StageWorkService(IRequestRepository requests, CurrentAuthorization authorization, IClock clock, IAuditContext auditContext)
{
    private static readonly Permission[] AllPermissions = [Permission.ViewProductionQueue, Permission.ReleaseStage, Permission.RunStage, Permission.InspectOrCorrectQc];

    public async Task<StageWorkCapabilities> GetCapabilitiesAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, AllPermissions, cancellationToken);
        return CapabilitiesOf(set);
    }

    /// <summary>The items with their current stage and what the user may do. Allowed for anyone who may view the overview or perform one of the stage actions.</summary>
    public async Task<Outcome<StageWorkView>> LoadAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, AllPermissions, cancellationToken);
        var capabilities = CapabilitiesOf(set);
        if (!capabilities.CanView)
        {
            return Outcome<StageWorkView>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
            Outcome<StageWorkView>.Ok(new StageWorkView(await ProductionQueueService.ReadEntriesAsync(requests, cancellationToken), capabilities)));
    }

    /// <summary>The actions possible for an entry: the user's permission AND the stage's state. Pure; the same rules are enforced again when an action runs.</summary>
    public static AvailableStageActions AvailableFor(StageWorkCapabilities capabilities, ProductionQueueEntry entry)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.CurrentStage is not { } stage)
        {
            return AvailableStageActions.None;
        }

        return new AvailableStageActions(
            capabilities.CanRelease && stage is { StageNo: 1, Status: ItemStageStatus.Planned },
            capabilities.CanRun && stage.Status == ItemStageStatus.Waiting,
            capabilities.CanRun && stage.Status == ItemStageStatus.InProgress,
            capabilities.CanApprove && stage.Status == ItemStageStatus.WaitingQc);
    }

    public async Task<Outcome<StageActionResult>> PerformAsync(Principal? principal, StageAction action, StageTarget target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!Enum.IsDefined(action))
        {
            return Outcome<StageActionResult>.Invalid(ValidationIssue.StageActionInvalid);
        }

        var permission = action switch
        {
            StageAction.Release => Permission.ReleaseStage,
            StageAction.Start or StageAction.SubmitForQc => Permission.RunStage,
            _ => Permission.InspectOrCorrectQc,
        };
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
        ItemStageStatus? nextBefore = null;
        ItemStageStatus? nextAfter = null;
        var approvedQuantity = 0m;
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

                    item.ReleaseFirstStage();
                    break;
                case StageAction.Start:
                    item.StartStage(number);
                    break;
                case StageAction.SubmitForQc:
                    item.SubmitStageForQc(number);
                    break;
                default:
                    var next = item.Stages.FirstOrDefault(s => s.StageNo == number + 1);
                    nextBefore = next?.Status;
                    approvedQuantity = item.ApproveStageWholeQuantity(number, userId, now).ApprovedQuantity;
                    nextAfter = next?.Status;
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
                StageAction.SubmitForQc => (AuditActions.StageSubmittedForQc, "submitted for QC"),
                _ => (AuditActions.StageQcApproved, "approved by QC (whole quantity " + approvedQuantity.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture) + ")"),
            };
            var summary = $"Request {requestNo}, item {itemNo}, stage {number} ({title}) {text}: {before.Code()} -> {after.Code()}";
            if (action == StageAction.ApproveWholeQuantity)
            {
                summary += nextBefore is { } nb && nextAfter is { } na
                    ? $"; stage {number + 1}: {nb.Code()} -> {na.Code()}"
                    : "; no further stage";
            }

            return [new AuditLogEntry(now, userId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, "ItemStage", target.ItemId + "/" + number, summary)];
        }

        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                await requests.UpdateRowsAsync(target.RequestId, expected, Change, Audit, cancellationToken);
                int? nextNo = nextAfter is null ? null : number + 1;
                return Outcome<StageActionResult>.Ok(new StageActionResult(action, number, after, nextNo, nextAfter));
            });
        }
        catch (InvalidOperationException)
        {
            // An invalid transition or a QC-gate refusal from the Domain (or a missing item or stage). Nothing was saved.
            return Outcome<StageActionResult>.Fail(OperationStatus.InvalidState);
        }
    }

    // The rows the action changes or relies on: the request and the item (the approval changes the item's update time; every action relies on the item's route), the stage itself, the
    // previous stage (starting or approving relies on it being approved) and, for an approval, the next stage (it becomes WAITING). Rows the caller did not read are not invented.
    private static bool TryExpectations(StageAction action, StageTarget target, out RowVersionExpectations expected)
    {
        expected = RowVersionExpectations.ForRequest(target.RequestVersion).WithItem(target.ItemId, target.ItemVersion);
        var number = target.StageNo;
        if (number < 1 || !target.StageVersions.TryGetValue(number, out var own))
        {
            return false;
        }

        expected = expected.WithStage(target.ItemId, number, own);
        if (action is StageAction.Start or StageAction.ApproveWholeQuantity && number > 1)
        {
            if (!target.StageVersions.TryGetValue(number - 1, out var previous))
            {
                return false;
            }

            expected = expected.WithStage(target.ItemId, number - 1, previous);
        }

        if (action == StageAction.ApproveWholeQuantity && target.StageVersions.TryGetValue(number + 1, out var following))
        {
            expected = expected.WithStage(target.ItemId, number + 1, following);
        }

        return true;
    }

    private static StageWorkCapabilities CapabilitiesOf(PermissionSet set)
    {
        if (set.Current is null)
        {
            return StageWorkCapabilities.None;
        }

        var release = set.Has(Permission.ReleaseStage);
        var run = set.Has(Permission.RunStage);
        var approve = set.Has(Permission.InspectOrCorrectQc);
        return new StageWorkCapabilities(set.Has(Permission.ViewProductionQueue) || release || run || approve, release, run, approve);
    }
}
