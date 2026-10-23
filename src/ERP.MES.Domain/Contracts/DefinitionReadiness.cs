using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Contracts;

/// <summary>What currently blocks a request from moving on to production definition, among the gates that exist so far.</summary>
public enum DefinitionBlock
{
    None = 0,

    /// <summary>Management has not approved the request, or it has not passed Drawing: the request is not in <c>DRAWING_RETURNED_TO_PLANNING</c>.</summary>
    RequestNotReturnedFromDrawing,

    DrawingResultNotAccepted,

    /// <summary>MAKE: Planning has not yet decided whether a contract is required.</summary>
    ContractDecisionMissing,

    /// <summary>MAKE with a required contract: the contract is still a draft.</summary>
    ContractInPreparation,

    ContractPendingManagement,

    /// <summary>The submitted contract was rejected and has not been revised and approved.</summary>
    ContractRejected,
}

/// <summary>
/// The Management, Drawing and contract gates before production definition (docs/WORKFLOW_REDESIGN.md section 5). The raw-material disposition gate and the item-definition checks are
/// NOT part of this evaluation because they do not exist yet; a request that passes here is not thereby released for production.
/// </summary>
public static class DefinitionReadiness
{
    public static DefinitionBlock Evaluate(WorkType workType, RequestStatus requestStatus, DrawingOutcome? lastDrawingOutcome, ContractStatus? contractStatus)
    {
        if (requestStatus != RequestStatus.DrawingReturnedToPlanning)
        {
            return DefinitionBlock.RequestNotReturnedFromDrawing;
        }

        if (lastDrawingOutcome is not (DrawingOutcome.FullyAccepted or DrawingOutcome.PartiallyAccepted))
        {
            return DefinitionBlock.DrawingResultNotAccepted;
        }

        if (workType != WorkType.Make)
        {
            return DefinitionBlock.None;
        }

        return contractStatus switch
        {
            null => DefinitionBlock.ContractDecisionMissing,
            ContractStatus.NotRequired or ContractStatus.Approved => DefinitionBlock.None,
            ContractStatus.Draft => DefinitionBlock.ContractInPreparation,
            ContractStatus.PendingManagement => DefinitionBlock.ContractPendingManagement,
            _ => DefinitionBlock.ContractRejected,
        };
    }
}
