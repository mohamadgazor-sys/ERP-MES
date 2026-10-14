namespace ERP.MES.Domain.Requests;

/// <summary>
/// The approved initial request status set (Decision Log #29). Transitions between statuses are NOT modelled: the
/// transition table is an unapproved draft (docs/REQUEST_STATUS_TRANSITIONS_DRAFT.md).
/// </summary>
public enum RequestStatus
{
    Created = 1,
    SentToManagement = 2,
    MgmtApprovedAtPlanning = 3,
    MgmtRejectedAtPlanning = 4,
    UnderDrawingReview = 5,
    DrawingReturnedToPlanning = 6,
    Cancelled = 7,
    Closed = 8,
}

public static class RequestStatusInfo
{
    public static string Code(this RequestStatus status) => status switch
    {
        RequestStatus.Created => "CREATED",
        RequestStatus.SentToManagement => "SENT_TO_MANAGEMENT",
        RequestStatus.MgmtApprovedAtPlanning => "MGMT_APPROVED_AT_PLANNING",
        RequestStatus.MgmtRejectedAtPlanning => "MGMT_REJECTED_AT_PLANNING",
        RequestStatus.UnderDrawingReview => "UNDER_DRAWING_REVIEW",
        RequestStatus.DrawingReturnedToPlanning => "DRAWING_RETURNED_TO_PLANNING",
        RequestStatus.Cancelled => "CANCELLED",
        RequestStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown request status."),
    };
}
