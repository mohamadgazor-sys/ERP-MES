namespace ERP.MES.Domain.Requests;

/// <summary>The request-level actions that change a request's status (Decision Log #29, #75, #82; docs/WORKFLOW_REDESIGN.md section 3).</summary>
public enum RequestAction
{
    /// <summary>Central submits the request to Management (CREATED to SENT_TO_MANAGEMENT).</summary>
    Submit = 1,

    /// <summary>Management (Manager role) approves the request.</summary>
    Approve,

    /// <summary>Management (Manager role) rejects the request; a reason is mandatory.</summary>
    Reject,

    /// <summary>Planning or Management reopens a rejected request for a new Management decision; a reason is mandatory.</summary>
    Reopen,

    /// <summary>Planning sends an approved request to Drawing.</summary>
    ForwardToDrawing,

    /// <summary>Drawing returns its technical result to Planning.</summary>
    RecordDrawingResult,

    /// <summary>Planning sends a request that Drawing returned back to Drawing (more information or correction).</summary>
    SendBackToDrawing,
}

/// <summary>
/// The controlled outcome of the Drawing review (Master Specification 5.2). It is data on the workflow entry, not a separate request status. Whether Drawing's technical result is
/// "feasible" is decided by Drawing alone; material availability never decides it (Decision Log #82, R1).
/// </summary>
public enum DrawingOutcome
{
    FullyAccepted = 1,
    PartiallyAccepted,
    Rejected,
    MoreInformationNeeded,
    ReturnedForCorrection,
}

/// <summary>Who performed a request action: the user and the code of their department at that moment (a snapshot, so the history stays readable if the user later moves).</summary>
public readonly record struct WorkflowActor(long UserId, string DepartmentCode);

public static class RequestActionInfo
{
    public static string Code(this RequestAction action) => action switch
    {
        RequestAction.Submit => "SUBMIT",
        RequestAction.Approve => "APPROVE",
        RequestAction.Reject => "REJECT",
        RequestAction.Reopen => "REOPEN",
        RequestAction.ForwardToDrawing => "FORWARD_TO_DRAWING",
        RequestAction.RecordDrawingResult => "RECORD_DRAWING_RESULT",
        RequestAction.SendBackToDrawing => "SEND_BACK_TO_DRAWING",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown request action."),
    };

    public static string Code(this DrawingOutcome outcome) => outcome switch
    {
        DrawingOutcome.FullyAccepted => "FULLY_ACCEPTED",
        DrawingOutcome.PartiallyAccepted => "PARTIALLY_ACCEPTED",
        DrawingOutcome.Rejected => "REJECTED",
        DrawingOutcome.MoreInformationNeeded => "MORE_INFORMATION_NEEDED",
        DrawingOutcome.ReturnedForCorrection => "RETURNED_FOR_CORRECTION",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown Drawing outcome."),
    };
}

/// <summary>
/// One row of the request's workflow history (Domain and Database Specification 4.4): what happened, from which status to which, by whom, when, with the reason or note and, for
/// the Drawing result, the outcome. Entries are append-only; a state-changing action always creates exactly one, in the same transaction as the status change.
/// </summary>
public sealed class RequestWorkflowEntry
{
    public const int MaxNoteLength = 2000;

    private RequestWorkflowEntry()
    {
        ActionByDepartmentCode = string.Empty;
    }

    internal RequestWorkflowEntry(RequestAction action, RequestStatus from, RequestStatus to, DrawingOutcome? outcome, string? note, WorkflowActor actor, DateTime atUtc)
    {
        Action = action;
        FromStatus = from;
        ToStatus = to;
        Outcome = outcome;
        Note = note;
        ActionByUserId = actor.UserId;
        ActionByDepartmentCode = actor.DepartmentCode;
        ActionAtUtc = atUtc;
    }

    public long Id { get; }

    public RequestAction Action { get; }

    public RequestStatus FromStatus { get; }

    public RequestStatus ToStatus { get; }

    public DrawingOutcome? Outcome { get; }

    /// <summary>The reason (rejection, reopening, sending back) or the technical note of the Drawing result; null when none was given.</summary>
    public string? Note { get; }

    public long ActionByUserId { get; }

    public string ActionByDepartmentCode { get; }

    public DateTime ActionAtUtc { get; }
}
