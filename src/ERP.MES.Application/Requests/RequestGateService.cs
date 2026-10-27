using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;

namespace ERP.MES.Application.Requests;

/// <summary>Why a request cannot move on to production definition and release.</summary>
public enum GateBlocker
{
    /// <summary>Management approval, Drawing's accepted result or (MAKE) the contract gate is not satisfied; see <see cref="ProductionGateStatus.DefinitionBlock"/>.</summary>
    DefinitionGate = 1,

    /// <summary>A sent inquiry still has lines Warehouse has not answered.</summary>
    InquiryLinesUnanswered,

    /// <summary>A line answered No has no valid disposition (owner decision D1).</summary>
    NoAnswerWithoutDisposition,

    /// <summary>A Drawing clarification question is not closed yet.</summary>
    ClarificationUnresolved,
}

/// <summary>
/// The production-gate state of one request, derived from persisted state only (Decision Log #93, #95). <see cref="Satisfied"/> means the Management, Drawing, contract, inquiry and clarification
/// gates hold. It does NOT mean production may run: the item-definition checks come with phase 4a and the stage release rules with their phases.
/// </summary>
public sealed record ProductionGateStatus(
    DefinitionBlock DefinitionBlock, ContractStatus? ContractStatus, int UnansweredInquiryLines, int PendingDispositions, int UnresolvedClarifications, int OpenInquiries)
{
    public bool Satisfied => Blockers.Count == 0;

    /// <summary>
    /// True when items may be defined or imported (Decision Log #111): the contract is decided by Planning per item AFTER the items exist, so a contract that is still in preparation, pending or rejected
    /// does not hold item definition - it holds the release and start of the stages of the items that need it. Management, Drawing, the contract decision itself, inquiries and clarifications still do.
    /// </summary>
    public bool DefinitionAllowed =>
        DefinitionBlock is DefinitionBlock.None or DefinitionBlock.ContractInPreparation or DefinitionBlock.ContractPendingManagement or DefinitionBlock.ContractRejected
        && Blockers.All(b => b == GateBlocker.DefinitionGate);

    /// <summary>True when material or clarification matters are unresolved - the flag shown beside a Drawing result that was returned with them open.</summary>
    public bool HasUnresolvedMaterialState => UnansweredInquiryLines > 0 || PendingDispositions > 0 || UnresolvedClarifications > 0 || OpenInquiries > 0;

    public IReadOnlyList<GateBlocker> Blockers
    {
        get
        {
            var list = new List<GateBlocker>();
            if (DefinitionBlock != DefinitionBlock.None)
            {
                list.Add(GateBlocker.DefinitionGate);
            }

            if (UnansweredInquiryLines > 0)
            {
                list.Add(GateBlocker.InquiryLinesUnanswered);
            }

            if (PendingDispositions > 0)
            {
                list.Add(GateBlocker.NoAnswerWithoutDisposition);
            }

            if (UnresolvedClarifications > 0)
            {
                list.Add(GateBlocker.ClarificationUnresolved);
            }

            return list;
        }
    }
}

/// <summary>
/// Evaluates the gates a request must pass before Planning defines its items and releases production: Management approval and an accepted Drawing result, the MAKE contract, every sent
/// inquiry line answered, every No answer dispositioned, and every Drawing clarification closed (owner decisions D1, #93, #95). Read-only; the commands that depend on it call
/// <see cref="EvaluateAsync"/> inside their own use case so the gate is enforced by the Application, not by a button.
/// </summary>
public sealed class RequestGateService(IContractRepository contracts, IInquiryRepository inquiries, IClarificationRepository clarifications, CurrentAuthorization authorization)
{
    public Task<Outcome<ProductionGateStatus>> GetAsync(Principal? principal, long requestId, CancellationToken cancellationToken = default) => GetAsync(principal, requestId, null, cancellationToken);

    public async Task<Outcome<ProductionGateStatus>> GetAsync(Principal? principal, long requestId, long? itemId, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewProductionGates, cancellationToken)).IsAllowed)
        {
            return Outcome<ProductionGateStatus>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var status = await EvaluateAsync(requestId, itemId, cancellationToken);
            return status is null ? Outcome<ProductionGateStatus>.Fail(OperationStatus.NotFound) : Outcome<ProductionGateStatus>.Ok(status);
        });
    }

    /// <summary>
    /// The gate state without a permission check, for use inside another use case that has already authorized its caller. Null when the request does not exist. With an <paramref name="itemId"/> the
    /// contract gate is that item's (Decision Log #111): an item decided "not required" is not held by the contract. Without one, the request-level state is returned, and the contract gate no longer
    /// holds when every item has been decided "not required".
    /// </summary>
    public Task<ProductionGateStatus?> EvaluateAsync(long requestId, CancellationToken cancellationToken = default) => EvaluateAsync(requestId, null, cancellationToken);

    /// <inheritdoc cref="EvaluateAsync(long, CancellationToken)"/>
    public async Task<ProductionGateStatus?> EvaluateAsync(long requestId, long? itemId, CancellationToken cancellationToken = default)
    {
        var gates = await EvaluateItemsAsync(requestId, cancellationToken);
        return gates is null ? null : itemId is { } id ? gates.For(id) : gates.Request;
    }

    /// <summary>The request-level state and a cheap per-item view of it, from one read. Null when the request does not exist.</summary>
    public async Task<RequestGates?> EvaluateItemsAsync(long requestId, CancellationToken cancellationToken = default)
    {
        var detail = await contracts.FindAsync(requestId, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var rounds = await inquiries.ListForRequestAsync(requestId, cancellationToken);
        var questions = await clarifications.ListForRequestAsync(requestId, cancellationToken);
        var requestStatus = detail.Contract?.Value.Status;
        var unanswered = rounds.Sum(r => r.Value.UnansweredLineCount);
        var pending = rounds.Sum(r => r.Value.PendingDispositionCount);
        var unresolved = questions.Count(q => !q.Value.IsResolved);
        var open = rounds.Count(r => r.Value.IsOpen);

        ProductionGateStatus Build(ContractStatus? contractStatus) => new(
            DefinitionReadiness.Evaluate(detail.WorkType, detail.Eligibility.RequestStatus, detail.Eligibility.LastDrawingOutcome, contractStatus), contractStatus, unanswered, pending, unresolved, open);

        var requestLevel = requestStatus;
        if (detail.Contract is not null && detail.LatestItemDecisions.Count > 0)
        {
            var itemCount = await contracts.CountItemsAsync(requestId, cancellationToken);
            if (itemCount > 0 && detail.LatestItemDecisions.Count == itemCount && detail.LatestItemDecisions.Values.All(r => r == ContractRequirement.NotRequired))
            {
                requestLevel = ContractStatus.NotRequired;
            }
        }

        return new RequestGates(Build(requestLevel), itemId => Build(detail.StatusForItem(itemId)));
    }
}

/// <summary>The gate state of a request and, from the same read, of each of its items (Decision Log #111).</summary>
public sealed class RequestGates(ProductionGateStatus request, Func<long, ProductionGateStatus> forItem)
{
    public ProductionGateStatus Request { get; } = request;

    public ProductionGateStatus For(long itemId) => forItem(itemId);
}
