using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

/// <summary>What the signed-in user may do with clarification questions, for enabling buttons (the Application's answer; every command checks again).</summary>
public sealed record ClarificationCapabilities(bool CanView, bool CanAsk, bool CanAnswer)
{
    public static ClarificationCapabilities None { get; } = new(false, false, false);
}

/// <summary>
/// Drawing's clarification questions (owner decision D1, Decision Log #95). Drawing asks Planning while it reviews a request; Planning answers (also when it obtained the information outside the
/// application); Drawing, the party that asked, closes the question. The request status never changes: it stays <c>UNDER_DRAWING_REVIEW</c>. An open question is shown to Planning, kept with its full
/// history, and blocks production release until it is closed (see <see cref="RequestGateService"/>). Every command reloads the user from the database, passes the row version and is audited in the
/// same transaction.
/// </summary>
public sealed class ClarificationService(IClarificationRepository clarifications, CurrentAuthorization authorization, IClock clock, IAuditContext auditContext)
{
    private static readonly Permission[] AllPermissions = [Permission.ViewDrawingClarifications, Permission.AskDrawingClarification, Permission.AnswerDrawingClarification];

    public async Task<ClarificationCapabilities> GetCapabilitiesAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, AllPermissions, cancellationToken);
        return set.Current is null
            ? ClarificationCapabilities.None
            : new ClarificationCapabilities(set.Has(Permission.ViewDrawingClarifications), set.Has(Permission.AskDrawingClarification), set.Has(Permission.AnswerDrawingClarification));
    }

    public async Task<Outcome<IReadOnlyList<ClarificationItem>>> ListUnresolvedAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewDrawingClarifications, cancellationToken)).IsAllowed)
        {
            return Outcome<IReadOnlyList<ClarificationItem>>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<IReadOnlyList<ClarificationItem>>.Ok(await clarifications.ListUnresolvedAsync(cancellationToken)));
    }

    /// <summary>Requests Drawing is reviewing, to choose from when asking a question. Readable by whoever may ask (Drawing).</summary>
    public async Task<Outcome<IReadOnlyList<ReviewRequestItem>>> ListUnderReviewAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.AskDrawingClarification, cancellationToken)).IsAllowed)
        {
            return Outcome<IReadOnlyList<ReviewRequestItem>>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<IReadOnlyList<ReviewRequestItem>>.Ok(await clarifications.ListUnderReviewAsync(cancellationToken)));
    }

    public async Task<Outcome<IReadOnlyList<Stored<DrawingClarification>>>> ListForRequestAsync(Principal? principal, long requestId, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewDrawingClarifications, cancellationToken)).IsAllowed)
        {
            return Outcome<IReadOnlyList<Stored<DrawingClarification>>>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<IReadOnlyList<Stored<DrawingClarification>>>.Ok(await clarifications.ListForRequestAsync(requestId, cancellationToken)));
    }

    public async Task<Outcome<Stored<DrawingClarification>>> AskAsync(Principal? principal, long requestId, string? question, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.AskDrawingClarification, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<DrawingClarification>>.Fail(OperationStatus.NotAuthorized);
        }

        if (TextIssue(question) is var issue && issue != ValidationIssue.None)
        {
            return Outcome<Stored<DrawingClarification>>.Invalid(issue);
        }

        var (actor, now) = Who(decision);
        return await RunAsync(() => clarifications.AskAsync(
            requestId, status => DrawingClarification.Ask(requestId, status, question, actor, now),
            (c, requestNo) => [Audit(decision.Current!, now, AuditActions.ClarificationAsked, requestId, $"Request {requestNo}: Drawing asked Planning a clarification question")]));
    }

    public async Task<Outcome<Stored<DrawingClarification>>> AnswerAsync(Principal? principal, long clarificationId, long expectedVersion, string? answer, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.AnswerDrawingClarification, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<DrawingClarification>>.Fail(OperationStatus.NotAuthorized);
        }

        if (TextIssue(answer) is var issue && issue != ValidationIssue.None)
        {
            return Outcome<Stored<DrawingClarification>>.Invalid(issue);
        }

        var (actor, now) = Who(decision);
        return await RunAsync(() => clarifications.UpdateAsync(
            clarificationId, expectedVersion, c => c.RecordAnswer(answer, actor, now),
            (c, requestNo) => [Audit(decision.Current!, now, AuditActions.ClarificationAnswered, c.RequestId, $"Request {requestNo}: Planning answered Drawing's clarification question")],
            cancellationToken));
    }

    public async Task<Outcome<Stored<DrawingClarification>>> CloseAsync(Principal? principal, long clarificationId, long expectedVersion, string? note, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.AskDrawingClarification, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<DrawingClarification>>.Fail(OperationStatus.NotAuthorized);
        }

        if (!string.IsNullOrWhiteSpace(note) && note.Trim().Length > DrawingClarification.MaxTextLength)
        {
            return Outcome<Stored<DrawingClarification>>.Invalid(ValidationIssue.ReasonTooLong);
        }

        var (actor, now) = Who(decision);
        return await RunAsync(() => clarifications.UpdateAsync(
            clarificationId, expectedVersion, c => c.Close(note, actor, now),
            (c, requestNo) => [Audit(decision.Current!, now, AuditActions.ClarificationClosed, c.RequestId, $"Request {requestNo}: Drawing closed its clarification question")],
            cancellationToken));
    }

    private static ValidationIssue TextIssue(string? text) =>
        string.IsNullOrWhiteSpace(text) || text.Trim().Length > DrawingClarification.MaxTextLength ? ValidationIssue.ClarificationTextInvalid : ValidationIssue.None;

    private (WorkflowActor Actor, DateTime Now) Who(AuthorizationDecision decision) => (new WorkflowActor(decision.Current!.UserId, decision.Current.DepartmentCode), clock.UtcNow);

    private AuditLogEntry Audit(Principal user, DateTime now, string action, long requestId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, action, "Request", requestId.ToString(System.Globalization.CultureInfo.InvariantCulture), summary);

    private static async Task<Outcome<Stored<DrawingClarification>>> RunAsync(Func<Task<Stored<DrawingClarification>>> work)
    {
        try
        {
            return await CustomerService.GuardAsync(async () => Outcome<Stored<DrawingClarification>>.Ok(await work()));
        }
        catch (ClarificationRequestStateException)
        {
            return Outcome<Stored<DrawingClarification>>.Invalid(ValidationIssue.ClarificationRequestState);
        }
        catch (ArgumentException)
        {
            return Outcome<Stored<DrawingClarification>>.Invalid(ValidationIssue.ClarificationTextInvalid);
        }
        catch (InvalidOperationException)
        {
            return Outcome<Stored<DrawingClarification>>.Fail(OperationStatus.InvalidState);
        }
    }
}
