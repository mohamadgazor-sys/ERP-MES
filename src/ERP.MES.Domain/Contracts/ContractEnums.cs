using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Contracts;

/// <summary>Planning's decision for a MAKE request (Decision Log #85): is a contract required or not. A REPAIR request has no such decision.</summary>
public enum ContractRequirement
{
    NotRequired = 1,
    Required = 2,
}

/// <summary>
/// Where a request's contract stands. <c>NotRequired</c> is Planning's decision; the others apply when a contract is required. <c>RejectedAtPlanning</c> is the provisional default
/// routing (docs/WORKFLOW_REDESIGN.md section 3.1, not an owner-approved permanent rule): a rejection returns the contract to Planning for revision and resubmission.
/// </summary>
public enum ContractStatus
{
    NotRequired = 1,
    Draft,
    PendingManagement,
    Approved,
    RejectedAtPlanning,
}

public enum ContractVersionState
{
    /// <summary>Being prepared by Planning; the only state in which the version can change.</summary>
    Draft = 1,

    /// <summary>Submitted to Management. Immutable from here on: fields, PDF link and decision history are never overwritten.</summary>
    Submitted,

    /// <summary>A draft that was never submitted and was abandoned because Planning decided no contract is required. Kept for history.</summary>
    Withdrawn,
}

public enum ContractDecision
{
    Approved = 1,
    Rejected,
}

public enum ContractAction
{
    DecidedNotRequired = 1,
    DecidedRequired,
    RequirementChanged,
    DraftSaved,
    PdfAttached,
    Submitted,
    Approved,
    Rejected,
    RevisionStarted,
}

/// <summary>Why a request cannot take part in the contract workflow right now.</summary>
public enum ContractIneligibility
{
    None = 0,

    /// <summary>A REPAIR request skips the contract determination (Decision Log #85).</summary>
    NotMake,

    /// <summary>The request is not in <c>DRAWING_RETURNED_TO_PLANNING</c>: Management approval and Drawing's technical result come first.</summary>
    RequestNotReturnedFromDrawing,

    /// <summary>Drawing's latest result is not fully or partially accepted (labelled default, matching the approved production gate of docs/WORKFLOW_REDESIGN.md section 5).</summary>
    DrawingResultNotAccepted,
}

/// <summary>The facts about a request that decide whether the contract workflow may run. Read from the persisted request in the same transaction as the contract change.</summary>
public sealed record ContractEligibility(WorkType WorkType, RequestStatus RequestStatus, DrawingOutcome? LastDrawingOutcome)
{
    public ContractIneligibility Check() =>
        WorkType != WorkType.Make ? ContractIneligibility.NotMake
        : RequestStatus != RequestStatus.DrawingReturnedToPlanning ? ContractIneligibility.RequestNotReturnedFromDrawing
        : LastDrawingOutcome is not (DrawingOutcome.FullyAccepted or DrawingOutcome.PartiallyAccepted) ? ContractIneligibility.DrawingResultNotAccepted
        : ContractIneligibility.None;
}

/// <summary>Thrown when the request is not eligible for the contract workflow; carries the reason.</summary>
public sealed class ContractGateException(ContractIneligibility reason) : InvalidOperationException("The request cannot take part in the contract workflow: " + reason)
{
    public ContractIneligibility Reason { get; } = reason;
}

/// <summary>Thrown when a draft is submitted without what a submission needs.</summary>
public sealed class ContractSubmissionException(ContractSubmissionProblem problem) : InvalidOperationException("The contract cannot be submitted: " + problem)
{
    public ContractSubmissionProblem Problem { get; } = problem;
}

/// <summary>What stops a draft from being submitted.</summary>
public enum ContractSubmissionProblem
{
    None = 0,
    ContractNoMissing,
    ContractDateMissing,
    PdfMissing,
}
