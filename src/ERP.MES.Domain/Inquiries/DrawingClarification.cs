using ERP.MES.Domain.Quality;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Inquiries;

public enum ClarificationStatus
{
    /// <summary>Drawing asked; Planning has not answered.</summary>
    Open = 1,

    /// <summary>Planning answered; Drawing has not closed the question yet.</summary>
    Answered,

    /// <summary>Closed by Drawing (the party that asked). Final.</summary>
    Closed,
}

/// <summary>Thrown when a clarification is asked for a request that is not under Drawing review.</summary>
public sealed class ClarificationRequestStateException(RequestStatus status) : InvalidOperationException("A clarification can only be asked while Drawing reviews the request, not while it is " + status)
{
    public RequestStatus RequestStatus { get; } = status;
}

/// <summary>
/// A question from Drawing to Planning while Drawing reviews a request (owner decision D1, Decision Log #95). Drawing asks; Planning answers (or obtains the information outside the
/// application and records the answer); Drawing, the party that asked, closes it. The request stays <c>UNDER_DRAWING_REVIEW</c> meanwhile; Drawing may still return its result and the open
/// question is then flagged, but production release waits until the question is closed. Each fact (question, answer, close) is written once with user, department and time, so the history is
/// the row itself and nothing is overwritten. Callers check permissions first; persistence is outside this type.
/// </summary>
public sealed class DrawingClarification
{
    public const int MaxTextLength = 2000;

    private DrawingClarification()
    {
        Question = string.Empty;
        AskedByDepartmentCode = string.Empty;
    }

    public long Id { get; }

    public long RequestId { get; private set; }

    public string Question { get; private set; }

    public ClarificationStatus Status { get; private set; }

    public long AskedByUserId { get; private set; }

    public string AskedByDepartmentCode { get; private set; }

    public DateTime AskedAtUtc { get; private set; }

    public string? Answer { get; private set; }

    public long? AnsweredByUserId { get; private set; }

    public string? AnsweredByDepartmentCode { get; private set; }

    public DateTime? AnsweredAtUtc { get; private set; }

    public string? CloseNote { get; private set; }

    public long? ClosedByUserId { get; private set; }

    public DateTime? ClosedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public bool IsResolved => Status == ClarificationStatus.Closed;

    public static DrawingClarification Ask(long requestId, RequestStatus requestStatus, string? question, WorkflowActor actor, DateTime atUtc)
    {
        if (requestStatus != RequestStatus.UnderDrawingReview)
        {
            throw new ClarificationRequestStateException(requestStatus);
        }

        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        return new DrawingClarification
        {
            RequestId = requestId,
            Question = Clean(question) ?? throw new ArgumentException("The question is required.", nameof(question)),
            Status = ClarificationStatus.Open,
            AskedByUserId = actor.UserId,
            AskedByDepartmentCode = actor.DepartmentCode,
            AskedAtUtc = utc,
            UpdatedAtUtc = utc,
        };
    }

    /// <summary>Planning answers. The answer is required, even when Planning obtained the information outside the application. Allowed once, while open.</summary>
    public void RecordAnswer(string? answer, WorkflowActor actor, DateTime atUtc)
    {
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        if (Status != ClarificationStatus.Open)
        {
            throw new InvalidOperationException("Only an open question can be answered.");
        }

        Answer = Clean(answer) ?? throw new ArgumentException("The answer is required.", nameof(answer));
        AnsweredByUserId = actor.UserId;
        AnsweredByDepartmentCode = actor.DepartmentCode;
        AnsweredAtUtc = utc;
        Status = ClarificationStatus.Answered;
        UpdatedAtUtc = utc;
    }

    /// <summary>The party that asked (Drawing) closes the question, answered or withdrawn. A note is optional.</summary>
    public void Close(string? note, WorkflowActor actor, DateTime atUtc)
    {
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        if (Status == ClarificationStatus.Closed)
        {
            throw new InvalidOperationException("The question is already closed.");
        }

        if (!string.Equals(actor.DepartmentCode, AskedByDepartmentCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only the party that asked can close the question.");
        }

        CloseNote = Clean(note);
        ClosedByUserId = actor.UserId;
        ClosedAtUtc = utc;
        Status = ClarificationStatus.Closed;
        UpdatedAtUtc = utc;
    }

    private static string? Clean(string? text)
    {
        var value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        return value is { Length: > MaxTextLength } ? throw new ArgumentException("The text is limited to " + MaxTextLength + " characters.") : value;
    }
}
