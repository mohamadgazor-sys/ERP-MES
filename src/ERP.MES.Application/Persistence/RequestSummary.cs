using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>One row of a request list: enough to show and open a request without loading its items.</summary>
public sealed record RequestSummary(
    long RequestId,
    long CustomerId,
    string CustomerName,
    RequestNo RequestNo,
    WorkType WorkType,
    int JalaliRequestYear,
    DateOnly RequestDate,
    string? Title,
    RequestStatus Status,
    int ItemCount);
