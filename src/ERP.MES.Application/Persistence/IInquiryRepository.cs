using ERP.MES.Application.Security;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>A request that can have or already has raw-material inquiries: in Drawing review, returned from Drawing, or with an inquiry.</summary>
public sealed record InquiryRequestItem(long RequestId, string RequestNo, string CustomerName, RequestStatus RequestStatus, int InquiryCount, InquiryStatus? LatestStatus);

/// <summary>The request facts a new inquiry is created with.</summary>
public sealed record InquiryContext(RequestStatus RequestStatus, int NextRoundNo);

/// <summary>An inquiry with its request, as loaded.</summary>
public sealed record InquiryDetail(string RequestNo, string CustomerName, RequestStatus RequestStatus, Stored<RawMaterialInquiry> Inquiry);

/// <summary>
/// Saves and loads raw-material inquiries (Decision Log #86, #88). A change runs in ONE transaction with its audit entries; the request's current status is read in the same transaction.
/// Responses and history are appended, never changed or deleted.
/// </summary>
public interface IInquiryRepository
{
    Task<IReadOnlyList<InquiryRequestItem>> ListRequestsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Stored<RawMaterialInquiry>>> ListForRequestAsync(long requestId, CancellationToken cancellationToken = default);

    Task<InquiryDetail?> FindAsync(long inquiryId, CancellationToken cancellationToken = default);

    /// <summary>Creates an inquiry for <paramref name="requestId"/> (the delegate builds it from the request status and the next round number).</summary>
    Task<Stored<RawMaterialInquiry>> CreateAsync(
        long requestId, Func<InquiryContext, RawMaterialInquiry> create, Func<RawMaterialInquiry, string, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default);

    /// <summary>Applies <paramref name="change"/> to a stored inquiry under its expected row version.</summary>
    Task<Stored<RawMaterialInquiry>> UpdateAsync(
        long inquiryId, long expectedVersion, Action<RawMaterialInquiry, RequestStatus> change, Func<RawMaterialInquiry, string, IReadOnlyList<AuditLogEntry>> audit, CancellationToken cancellationToken = default);
}
