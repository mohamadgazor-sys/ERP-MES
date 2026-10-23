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
    public async Task<Outcome<ProductionGateStatus>> GetAsync(Principal? principal, long requestId, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewProductionGates, cancellationToken)).IsAllowed)
        {
            return Outcome<ProductionGateStatus>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var status = await EvaluateAsync(requestId, cancellationToken);
            return status is null ? Outcome<ProductionGateStatus>.Fail(OperationStatus.NotFound) : Outcome<ProductionGateStatus>.Ok(status);
        });
    }

    /// <summary>The gate state without a permission check, for use inside another use case that has already authorized its caller. Null when the request does not exist.</summary>
    public async Task<ProductionGateStatus?> EvaluateAsync(long requestId, CancellationToken cancellationToken = default)
    {
        var detail = await contracts.FindAsync(requestId, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var rounds = await inquiries.ListForRequestAsync(requestId, cancellationToken);
        var questions = await clarifications.ListForRequestAsync(requestId, cancellationToken);
        var contractStatus = detail.Contract?.Value.Status;
        var block = DefinitionReadiness.Evaluate(detail.WorkType, detail.Eligibility.RequestStatus, detail.Eligibility.LastDrawingOutcome, contractStatus);
        return new ProductionGateStatus(
            block,
            contractStatus,
            rounds.Sum(r => r.Value.UnansweredLineCount),
            rounds.Sum(r => r.Value.PendingDispositionCount),
            questions.Count(q => !q.Value.IsResolved),
            rounds.Count(r => r.Value.IsOpen));
    }
}
