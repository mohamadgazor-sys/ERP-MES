using ERP.MES.Application.Security;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>A clarification with the request it belongs to.</summary>
public sealed record ClarificationItem(string RequestNo, string CustomerName, RequestStatus RequestStatus, Stored<DrawingClarification> Clarification);

/// <summary>A request currently under Drawing review, which Drawing may ask a question about.</summary>
public sealed record ReviewRequestItem(long RequestId, string RequestNo, string CustomerName);

/// <summary>Saves and loads Drawing's clarification questions (Decision Log #95). Each change is one transaction with its audit entry; the request status is read in the same transaction.</summary>
public interface IClarificationRepository
{
    /// <summary>All questions that are not closed yet, oldest first, with their requests.</summary>
    Task<IReadOnlyList<ClarificationItem>> ListUnresolvedAsync(CancellationToken cancellationToken = default);

    /// <summary>Requests in <c>UNDER_DRAWING_REVIEW</c>.</summary>
    Task<IReadOnlyList<ReviewRequestItem>> ListUnderReviewAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Stored<DrawingClarification>>> ListForRequestAsync(long requestId, CancellationToken cancellationToken = default);

    Task<Stored<DrawingClarification>> AskAsync(
        long requestId, Func<RequestStatus, DrawingClarification> create, Func<DrawingClarification, string, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default);

    Task<Stored<DrawingClarification>> UpdateAsync(
        long clarificationId, long expectedVersion, Action<DrawingClarification> change, Func<DrawingClarification, string, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default);
}
