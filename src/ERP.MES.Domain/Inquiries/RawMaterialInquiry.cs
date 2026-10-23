using ERP.MES.Domain.Quality;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Inquiries;

public enum InquiryStatus
{
    Draft = 1,
    Sent,
    PartiallyAnswered,
    Answered,
    Closed,
    Cancelled,
}

/// <summary>Warehouse's answer to one line (Decision Log #88): Yes or No only. Availability is information; it reserves and deducts nothing and does not decide feasibility.</summary>
public enum InquiryAnswer
{
    Yes = 1,
    No,
}

public enum InquiryAction
{
    Created = 1,
    LineAdded,
    LineRemoved,
    Sent,
    Answered,
    AnswerChanged,
    Closed,
    Cancelled,
}

/// <summary>Thrown when the request's status does not allow raw-material inquiries (they are raised while Drawing reviews, or after Drawing returned the request).</summary>
public sealed class InquiryRequestStateException(RequestStatus status) : InvalidOperationException("Raw-material inquiries are not possible while the request is " + status)
{
    public RequestStatus RequestStatus { get; } = status;
}

/// <summary>Thrown when an inquiry without lines is sent.</summary>
public sealed class InquiryNoLinesException : InvalidOperationException
{
    public InquiryNoLinesException()
        : base("An inquiry needs at least one line before it is sent.")
    {
    }
}

/// <summary>
/// A raw-material availability inquiry (docs/RAW_MATERIAL_INQUIRY_DESIGN.md; Decision Log #86, #88). Drawing creates it for a request, lists the lines, sends it; Warehouse answers each line Yes
/// or No with an optional note; Drawing closes or cancels it. It belongs to the request, changes no request status, reserves nothing and never decides technical feasibility. Lines are editable only
/// while the inquiry is a draft; to revise sent lines Drawing cancels the inquiry and opens a new round. Answers are append-only (a changed answer is a new row; the earlier one stays). What
/// happens after a "No" and the clarification thread are NOT modelled: they wait for owner decision D1. Callers check permissions first; persistence is outside this type.
/// </summary>
public sealed class RawMaterialInquiry
{
    public const int MaxNoteLength = 2000;

    private readonly List<RawMaterialInquiryLine> _lines = [];
    private readonly List<InquiryHistoryEntry> _history = [];

    private RawMaterialInquiry()
    {
    }

    public long Id { get; }

    public long RequestId { get; private set; }

    /// <summary>1 for the first inquiry of a request, then 2, 3 ... (a new round replaces a cancelled or closed one).</summary>
    public int RoundNo { get; private set; }

    public InquiryStatus Status { get; private set; }

    public string? Note { get; private set; }

    public long CreatedByUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyList<RawMaterialInquiryLine> Lines => _lines;

    public IReadOnlyList<InquiryHistoryEntry> History => _history;

    public bool IsOpen => Status is InquiryStatus.Sent or InquiryStatus.PartiallyAnswered or InquiryStatus.Answered;

    /// <summary>Lines that have a current answer, among all lines.</summary>
    public int AnsweredLineCount => _lines.Count(l => l.CurrentResponse is not null);

    public static RawMaterialInquiry Create(long requestId, int roundNo, RequestStatus requestStatus, string? note, WorkflowActor actor, DateTime atUtc)
    {
        RequireRequestState(requestStatus);
        if (roundNo < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(roundNo), roundNo, "The round starts at 1.");
        }

        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        var inquiry = new RawMaterialInquiry
        {
            RequestId = requestId,
            RoundNo = roundNo,
            Status = InquiryStatus.Draft,
            Note = CleanNote(note, required: false),
            CreatedByUserId = actor.UserId,
            CreatedAtUtc = utc,
            UpdatedAtUtc = utc,
        };
        inquiry.Add(InquiryAction.Created, null, InquiryStatus.Draft, null, null, null, actor, utc);
        return inquiry;
    }

    public RawMaterialInquiryLine AddLine(
        string materialGrade, string materialForm, decimal? diameterMm, decimal? lengthMm, string? otherDimension, decimal requiredQuantity, long quantityUnitId,
        string? standardOrSpec, string? remarks, WorkflowActor actor, DateTime atUtc)
    {
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        RequireStatus(InquiryStatus.Draft, "Lines can only be changed while the inquiry is a draft.");
        
        // Line numbers are never reused, also not after a draft line was removed: the history keeps every number that was handed out.
        var lineNo = (_history.Where(h => h.Action == InquiryAction.LineAdded).Select(h => h.LineNo ?? 0).DefaultIfEmpty(0).Max()) + 1;
        var line = new RawMaterialInquiryLine(lineNo, materialGrade, materialForm, diameterMm, lengthMm, otherDimension, requiredQuantity, quantityUnitId, standardOrSpec, remarks);
        _lines.Add(line);
        Add(InquiryAction.LineAdded, Status, Status, lineNo, null, null, actor, utc);
        return line;
    }

    public void RemoveLine(int lineNo, WorkflowActor actor, DateTime atUtc)
    {
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        RequireStatus(InquiryStatus.Draft, "Lines can only be changed while the inquiry is a draft.");
        var line = _lines.FirstOrDefault(l => l.LineNo == lineNo) ?? throw new InvalidOperationException("The inquiry has no such line.");
        _lines.Remove(line);
        Add(InquiryAction.LineRemoved, Status, Status, lineNo, null, null, actor, utc);
    }

    /// <summary>Drawing sends the draft to Warehouse. At least one line is needed.</summary>
    public void Send(RequestStatus requestStatus, WorkflowActor actor, DateTime atUtc)
    {
        RequireRequestState(requestStatus);
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        RequireStatus(InquiryStatus.Draft, "Only a draft can be sent.");
        if (_lines.Count == 0)
        {
            throw new InquiryNoLinesException();
        }

        var from = Status;
        Status = InquiryStatus.Sent;
        Add(InquiryAction.Sent, from, Status, null, null, null, actor, utc);
    }

    /// <summary>Warehouse answers a line Yes or No. A later answer to the same line is a new response; the earlier one stays. Allowed until the inquiry is closed or cancelled.</summary>
    public void Answer(int lineNo, InquiryAnswer answer, string? note, WorkflowActor actor, DateTime atUtc)
    {
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        if (!IsOpen)
        {
            throw new InvalidOperationException("Only a sent inquiry can be answered.");
        }

        if (!Enum.IsDefined(answer))
        {
            throw new ArgumentOutOfRangeException(nameof(answer), answer, "Unknown answer.");
        }

        var line = _lines.FirstOrDefault(l => l.LineNo == lineNo) ?? throw new InvalidOperationException("The inquiry has no such line.");
        var text = CleanNote(note, required: false);
        var changed = line.CurrentResponse is not null;
        line.AddResponse(answer, text, actor, utc);
        var from = Status;
        Status = _lines.All(l => l.CurrentResponse is not null) ? InquiryStatus.Answered : InquiryStatus.PartiallyAnswered;
        Add(changed ? InquiryAction.AnswerChanged : InquiryAction.Answered, from, Status, lineNo, answer, text, actor, utc);
    }

    /// <summary>Drawing reviews the answers and closes the inquiry. Only a fully answered inquiry can be closed. Closing does not decide anything about the request.</summary>
    public void Close(string? note, WorkflowActor actor, DateTime atUtc)
    {
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        RequireStatus(InquiryStatus.Answered, "Only an answered inquiry can be closed.");
        var text = CleanNote(note, required: false);
        var from = Status;
        Status = InquiryStatus.Closed;
        Add(InquiryAction.Closed, from, Status, null, null, text, actor, utc);
    }

    /// <summary>Drawing cancels an inquiry that is not closed; a reason is mandatory. Its lines and answers are kept.</summary>
    public void Cancel(string? reason, WorkflowActor actor, DateTime atUtc)
    {
        var utc = Customer.RequireUtc(atUtc, nameof(atUtc));
        if (Status is InquiryStatus.Closed or InquiryStatus.Cancelled)
        {
            throw new InvalidOperationException("The inquiry is already finished.");
        }

        var text = CleanNote(reason, required: true);
        var from = Status;
        Status = InquiryStatus.Cancelled;
        Add(InquiryAction.Cancelled, from, Status, null, null, text, actor, utc);
    }

    private static void RequireRequestState(RequestStatus status)
    {
        if (status is not (RequestStatus.UnderDrawingReview or RequestStatus.DrawingReturnedToPlanning))
        {
            throw new InquiryRequestStateException(status);
        }
    }

    private void RequireStatus(InquiryStatus required, string message)
    {
        if (Status != required)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void Add(InquiryAction action, InquiryStatus? from, InquiryStatus to, int? lineNo, InquiryAnswer? answer, string? note, WorkflowActor actor, DateTime atUtc)
    {
        UpdatedAtUtc = atUtc;
        _history.Add(new InquiryHistoryEntry(action, from, to, lineNo, answer, note, actor, atUtc));
    }

    internal static string? CleanNote(string? note, bool required)
    {
        var text = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (text is null && required)
        {
            throw new ArgumentException("A reason is required.", nameof(note));
        }

        return text is { Length: > MaxNoteLength } ? throw new ArgumentException("The text is limited to " + MaxNoteLength + " characters.", nameof(note)) : text;
    }
}

/// <summary>One requested material line. Immutable once created (a draft can remove it). Dimensions are in millimetres; a missing dimension means "not applicable" for that form.</summary>
public sealed class RawMaterialInquiryLine
{
    public const int MaxTextLength = 100;

    private readonly List<RawMaterialInquiryResponse> _responses = [];

    private RawMaterialInquiryLine()
    {
        MaterialGrade = string.Empty;
        MaterialForm = string.Empty;
    }

    internal RawMaterialInquiryLine(
        int lineNo, string materialGrade, string materialForm, decimal? diameterMm, decimal? lengthMm, string? otherDimension, decimal requiredQuantity, long quantityUnitId,
        string? standardOrSpec, string? remarks)
    {
        if (requiredQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredQuantity), requiredQuantity, "The required quantity must be greater than zero.");
        }

        if (diameterMm is <= 0 || lengthMm is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(diameterMm), "A dimension must be greater than zero, or left empty when it does not apply.");
        }

        LineNo = lineNo;
        MaterialGrade = Required(materialGrade, nameof(materialGrade));
        MaterialForm = Required(materialForm, nameof(materialForm));
        DiameterMm = diameterMm;
        LengthMm = lengthMm;
        OtherDimension = Optional(otherDimension);
        RequiredQuantity = requiredQuantity;
        QuantityUnitId = quantityUnitId;
        StandardOrSpec = Optional(standardOrSpec);
        Remarks = Optional(remarks);
    }

    public long Id { get; }

    public int LineNo { get; private set; }

    public string MaterialGrade { get; private set; }

    public string MaterialForm { get; private set; }

    public decimal? DiameterMm { get; private set; }

    public decimal? LengthMm { get; private set; }

    public string? OtherDimension { get; private set; }

    public decimal RequiredQuantity { get; private set; }

    public long QuantityUnitId { get; private set; }

    public string? StandardOrSpec { get; private set; }

    public string? Remarks { get; private set; }

    /// <summary>All responses, oldest first. Append-only.</summary>
    public IReadOnlyList<RawMaterialInquiryResponse> Responses => _responses;

    /// <summary>The latest response, or null while the line is unanswered.</summary>
    public RawMaterialInquiryResponse? CurrentResponse => _responses.LastOrDefault();

    internal void AddResponse(InquiryAnswer answer, string? note, WorkflowActor actor, DateTime atUtc) =>
        _responses.Add(new RawMaterialInquiryResponse(answer, note, actor, atUtc));

    private static string Required(string? value, string parameter)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return text is null or { Length: > MaxTextLength } ? throw new ArgumentException("A value of 1 to " + MaxTextLength + " characters is required.", parameter) : text;
    }

    private static string? Optional(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return text is { Length: > MaxTextLength * 5 } ? throw new ArgumentException("The text is too long.") : text;
    }
}

/// <summary>Warehouse's answer to a line. Append-only: never changed or deleted.</summary>
public sealed class RawMaterialInquiryResponse
{
    private RawMaterialInquiryResponse()
    {
        RespondedByDepartmentCode = string.Empty;
    }

    internal RawMaterialInquiryResponse(InquiryAnswer answer, string? note, WorkflowActor actor, DateTime atUtc)
    {
        Answer = answer;
        Note = note;
        RespondedByUserId = actor.UserId;
        RespondedByDepartmentCode = actor.DepartmentCode;
        RespondedAtUtc = atUtc;
    }

    public long Id { get; }

    public InquiryAnswer Answer { get; }

    public string? Note { get; }

    public long RespondedByUserId { get; }

    public string RespondedByDepartmentCode { get; }

    public DateTime RespondedAtUtc { get; }
}

/// <summary>One row of the inquiry history. Append-only.</summary>
public sealed class InquiryHistoryEntry
{
    private InquiryHistoryEntry()
    {
        ActionByDepartmentCode = string.Empty;
    }

    internal InquiryHistoryEntry(InquiryAction action, InquiryStatus? from, InquiryStatus to, int? lineNo, InquiryAnswer? answer, string? note, WorkflowActor actor, DateTime atUtc)
    {
        Action = action;
        FromStatus = from;
        ToStatus = to;
        LineNo = lineNo;
        Answer = answer;
        Note = note;
        ActionByUserId = actor.UserId;
        ActionByDepartmentCode = actor.DepartmentCode;
        ActionAtUtc = atUtc;
    }

    public long Id { get; }

    public InquiryAction Action { get; }

    public InquiryStatus? FromStatus { get; }

    public InquiryStatus ToStatus { get; }

    public int? LineNo { get; }

    public InquiryAnswer? Answer { get; }

    public string? Note { get; }

    public long ActionByUserId { get; }

    public string ActionByDepartmentCode { get; }

    public DateTime ActionAtUtc { get; }
}
