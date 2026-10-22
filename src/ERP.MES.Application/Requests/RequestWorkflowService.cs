using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

/// <summary>Which request-level workflow actions the current user may perform, for enabling buttons (the Application's answer, from the database; every command checks again).</summary>
public sealed record RequestActionCapabilities(bool CanDecide, bool CanReopen, bool CanForwardToDrawing, bool CanRecordDrawingResult)
{
    public static RequestActionCapabilities None { get; } = new(false, false, false, false);
}

/// <summary>
/// The request lifecycle after submission (Decision Log #29, #82; docs/WORKFLOW_REDESIGN.md section 3): Management approves or rejects, a rejected request is reopened, Planning
/// forwards the approved request to Drawing, Drawing returns its technical result, Planning may send a returned request back. Permissions (default deny, checked against the database
/// at the time of the call): approve/reject = Management with the Manager role; reopen = Planning or Management; forward and send back = Planning (the department that
/// "receives" the request); Drawing result = Drawing. Each action passes the version the request was loaded at, changes the status through the Domain method, adds one history entry and
/// one audit entry in ONE transaction, and refuses a request that is not in the required status. Cancelling, contract and item steps are not part of this service.
/// </summary>
public sealed class RequestWorkflowService(IRequestRepository requests, CurrentAuthorization authorization, IClock clock, IAuditContext auditContext)
{
    private static readonly Permission[] AllPermissions =
    [
        Permission.ApproveOrRejectInitialRequest,
        Permission.ReopenRejectedRequest,
        Permission.ReceiveAndEditRequestDetails,
        Permission.RecordDrawingDoabilityDecision,
    ];

    public async Task<RequestActionCapabilities> GetCapabilitiesAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, AllPermissions, cancellationToken);
        return set.Current is null
            ? RequestActionCapabilities.None
            : new RequestActionCapabilities(
                set.Has(Permission.ApproveOrRejectInitialRequest),
                set.Has(Permission.ReopenRejectedRequest),
                set.Has(Permission.ReceiveAndEditRequestDetails),
                set.Has(Permission.RecordDrawingDoabilityDecision));
    }

    public Task<Outcome<Stored<Request>>> ApproveAsync(Principal? principal, long requestId, long expectedVersion, CancellationToken cancellationToken = default) =>
        RunAsync(principal, Permission.ApproveOrRejectInitialRequest, requestId, expectedVersion, AuditActions.RequestApproved, "approved by Management", null,
            (request, actor, now) => request.Approve(actor, now), cancellationToken);

    public async Task<Outcome<Stored<Request>>> RejectAsync(Principal? principal, long requestId, long expectedVersion, string? reason, CancellationToken cancellationToken = default)
    {
        var invalid = CheckNote(reason, required: true);
        return invalid is not ValidationIssue.None && await IsAllowedAsync(principal, Permission.ApproveOrRejectInitialRequest, cancellationToken)
            ? Outcome<Stored<Request>>.Invalid(invalid)
            : await RunAsync(principal, Permission.ApproveOrRejectInitialRequest, requestId, expectedVersion, AuditActions.RequestRejected, "rejected by Management", reason,
                (request, actor, now) => request.Reject(actor, reason!, now), cancellationToken);
    }

    public async Task<Outcome<Stored<Request>>> ReopenAsync(Principal? principal, long requestId, long expectedVersion, string? reason, CancellationToken cancellationToken = default)
    {
        var invalid = CheckNote(reason, required: true);
        return invalid is not ValidationIssue.None && await IsAllowedAsync(principal, Permission.ReopenRejectedRequest, cancellationToken)
            ? Outcome<Stored<Request>>.Invalid(invalid)
            : await RunAsync(principal, Permission.ReopenRejectedRequest, requestId, expectedVersion, AuditActions.RequestReopened, "reopened for a new Management decision", reason,
                (request, actor, now) => request.Reopen(actor, reason!, now), cancellationToken);
    }

    public Task<Outcome<Stored<Request>>> ForwardToDrawingAsync(Principal? principal, long requestId, long expectedVersion, CancellationToken cancellationToken = default) =>
        RunAsync(principal, Permission.ReceiveAndEditRequestDetails, requestId, expectedVersion, AuditActions.RequestForwardedToDrawing, "forwarded to Drawing", null,
            (request, actor, now) => request.ForwardToDrawing(actor, now), cancellationToken);

    public async Task<Outcome<Stored<Request>>> RecordDrawingResultAsync(
        Principal? principal, long requestId, long expectedVersion, DrawingOutcome outcome, string? note, CancellationToken cancellationToken = default)
    {
        if (!await IsAllowedAsync(principal, Permission.RecordDrawingDoabilityDecision, cancellationToken))
        {
            return Outcome<Stored<Request>>.Fail(OperationStatus.NotAuthorized);
        }

        if (!Enum.IsDefined(outcome))
        {
            return Outcome<Stored<Request>>.Invalid(ValidationIssue.DrawingOutcomeInvalid);
        }

        var invalid = CheckNote(note, required: outcome != DrawingOutcome.FullyAccepted);
        if (invalid is not ValidationIssue.None)
        {
            return Outcome<Stored<Request>>.Invalid(invalid);
        }

        return await RunAsync(principal, Permission.RecordDrawingDoabilityDecision, requestId, expectedVersion, AuditActions.RequestDrawingResultRecorded,
            "Drawing result recorded (" + outcome.Code() + ")", note, (request, actor, now) => request.RecordDrawingResult(actor, outcome, note, now), cancellationToken);
    }

    public async Task<Outcome<Stored<Request>>> SendBackToDrawingAsync(Principal? principal, long requestId, long expectedVersion, string? reason, CancellationToken cancellationToken = default)
    {
        var invalid = CheckNote(reason, required: true);
        return invalid is not ValidationIssue.None && await IsAllowedAsync(principal, Permission.ReceiveAndEditRequestDetails, cancellationToken)
            ? Outcome<Stored<Request>>.Invalid(invalid)
            : await RunAsync(principal, Permission.ReceiveAndEditRequestDetails, requestId, expectedVersion, AuditActions.RequestSentBackToDrawing, "sent back to Drawing", reason,
                (request, actor, now) => request.SendBackToDrawing(actor, reason!, now), cancellationToken);
    }

    private async Task<bool> IsAllowedAsync(Principal? principal, Permission permission, CancellationToken cancellationToken) =>
        (await authorization.CheckAsync(principal, permission, cancellationToken)).IsAllowed;

    private static ValidationIssue CheckNote(string? note, bool required)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return required ? ValidationIssue.ReasonRequired : ValidationIssue.None;
        }

        return note.Trim().Length > RequestWorkflowEntry.MaxNoteLength ? ValidationIssue.ReasonTooLong : ValidationIssue.None;
    }

    private async Task<Outcome<Stored<Request>>> RunAsync(
        Principal? principal,
        Permission permission,
        long requestId,
        long expectedVersion,
        string auditCode,
        string description,
        string? note,
        Action<Request, WorkflowActor, DateTime> change,
        CancellationToken cancellationToken)
    {
        var decision = await authorization.CheckAsync(principal, permission, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<Request>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var actor = new WorkflowActor(user.UserId, user.DepartmentCode);
        var now = clock.UtcNow;
        string? before = null;

        IReadOnlyList<AuditLogEntry> Audit(Request request) =>
        [
            new AuditLogEntry(
                now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, auditCode, "Request",
                requestId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Request {request.RequestNo.Value} {description}: {before} -> {request.Status.Code()}" + (string.IsNullOrWhiteSpace(note) ? string.Empty : "; note recorded in the workflow history")),
        ];

        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var saved = await requests.UpdateRowsAsync(
                    requestId,
                    RowVersionExpectations.ForRequest(expectedVersion),
                    request =>
                    {
                        before = request.Status.Code();
                        change(request, actor, now);
                    },
                    Audit,
                    cancellationToken);
                return Outcome<Stored<Request>>.Ok(new Stored<Request>(saved.Value, saved.Versions.Request));
            });
        }
        catch (InvalidOperationException)
        {
            // The request is not in the status this action needs (for example somebody else already decided it).
            return Outcome<Stored<Request>>.Fail(OperationStatus.InvalidState);
        }
    }
}
