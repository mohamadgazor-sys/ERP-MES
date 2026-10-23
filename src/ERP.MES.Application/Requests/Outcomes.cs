using ERP.MES.Domain.Calendar;

namespace ERP.MES.Application.Requests;

/// <summary>How a customer or request command ended. The Desktop shows a Persian message for each; nothing here is a UI decision.</summary>
public enum OperationStatus
{
    Succeeded,

    /// <summary>The user's CURRENT status, department or role (read from the database now) does not allow it, or the user must still choose a password. Nothing was changed.</summary>
    NotAuthorized,

    /// <summary>An input was refused; <see cref="Outcome{T}.Issue"/> says which. Nothing was changed.</summary>
    ValidationFailed,

    /// <summary>The customer or request does not exist (any more).</summary>
    NotFound,

    /// <summary>Someone else changed the record after it was loaded (Decision Log #23). Nothing was saved; reload and try again.</summary>
    ConcurrencyConflict,

    /// <summary>The business key Customer + RequestNo + Jalali Request Year is taken (exact, case-sensitive).</summary>
    DuplicateRequest,

    /// <summary>The request is in a state in which the operation is not allowed (for example submitting a request that is not <c>CREATED</c>).</summary>
    InvalidState,

    /// <summary>The data could not be saved or read (database failure). Nothing was changed.</summary>
    Failed,
}

/// <summary>The input a validation failure is about. Codes, not texts: the Desktop owns the Persian wording.</summary>
public enum ValidationIssue
{
    None = 0,
    CustomerNameRequired,
    CustomerRequired,

    /// <summary>The chosen customer does not exist (any more). Nothing was created and no other customer was substituted.</summary>
    CustomerNotFound,

    /// <summary>The chosen customer is inactive NOW (Decision Log #76). Nothing was created; the customer was not reactivated or changed.</summary>
    CustomerInactive,
    RequestNoRequired,
    RequestNoWhitespace,
    RequestDateRequired,

    /// <summary>The Jalali date text is not valid; <see cref="Outcome{T}.DateError"/> says why.</summary>
    RequestDateInvalid,
    ItemNoRequired,
    ItemNoDuplicate,
    PartNameRequired,
    QuantityInvalid,
    WorkTypeInvalid,
    PriorityInvalid,

    /// <summary>A cancelled or closed request accepts no new items.</summary>
    RequestClosedForItems,

    /// <summary>The stage action is not one of the demonstration actions.</summary>
    StageActionInvalid,

    /// <summary>A reason (rejection, reopening, sending back) or the Drawing note is required.</summary>
    ReasonRequired,

    /// <summary>The reason or note is longer than 2000 characters.</summary>
    ReasonTooLong,

    /// <summary>The Drawing outcome is not one of the controlled outcomes.</summary>
    DrawingOutcomeInvalid,

    /// <summary>The unit or quantity-type code is missing or not 1 to 20 characters of A-Z, 0-9, '_' or '-'.</summary>
    CodeInvalid,

    /// <summary>A unit or quantity type with this code already exists.</summary>
    CodeDuplicate,

    /// <summary>A Persian name is required.</summary>
    NameRequired,

    /// <summary>The unit kind is not one of the controlled kinds.</summary>
    UnitKindInvalid,

    /// <summary>A unit chosen for a conversion or as a base unit does not exist.</summary>
    UnitNotFound,

    /// <summary>A conversion needs two different units.</summary>
    ConversionSameUnit,

    /// <summary>The conversion factor must be positive and not zero.</summary>
    FactorInvalid,

    /// <summary>A conversion between these two units already exists (the opposite direction is derived automatically).</summary>
    ConversionDuplicate,

    /// <summary>The base counting unit of a quantity type must be an active unit of kind Count.</summary>
    BaseUnitInvalid,

    /// <summary>A piece-like quantity type counts at least one base unit.</summary>
    BaseUnitsInvalid,

    /// <summary>A set needs at least two members, each with a description and at least one base unit.</summary>
    SetMembersInvalid,

    /// <summary>The production unit chosen for a machine does not exist or is not active.</summary>
    ProductionUnitInvalid,

    /// <summary>The machine does not exist or is not active.</summary>
    MachineInvalid,

    /// <summary>The hourly rate must be a number greater than zero (Rial per hour).</summary>
    RateInvalid,

    /// <summary>The date from which a rate applies is required.</summary>
    EffectiveDateRequired,

    /// <summary>The date from which a rate applies is not a valid Jalali date; the date error says why.</summary>
    EffectiveDateInvalid,

    /// <summary>A new rate must apply from a date later than the machine's latest rate (history is never rewritten).</summary>
    RateNotLater,

    /// <summary>The Management, Drawing, contract, inquiry or clarification gates are not satisfied, so items cannot be defined yet.</summary>
    ItemGateNotSatisfied,

    /// <summary>A MAKE item needs a quantity type, a material and a weight.</summary>
    ItemSpecificationIncomplete,

    /// <summary>The weight must be greater than zero, at most 99,999,999.9999 kg and have at most 4 decimals.</summary>
    WeightInvalid,

    /// <summary>A length or diameter must be greater than zero, at most 99,999,999.99 mm and have at most 2 decimals.</summary>
    DimensionInvalid,

    /// <summary>The chosen quantity type does not exist or is not active.</summary>
    QuantityTypeInvalid,

    /// <summary>The item does not exist in this request.</summary>
    ItemNotFound,

    /// <summary>The stage title is required (at most 200 characters).</summary>
    StageTitleRequired,

    /// <summary>The planned minutes per piece must be greater than zero.</summary>
    StageMinutesInvalid,

    /// <summary>The chosen machine does not belong to the chosen production unit.</summary>
    MachineUnitMismatch,

    /// <summary>The question or answer text is required (at most 2000 characters).</summary>
    ClarificationTextInvalid,

    /// <summary>A clarification can only be asked while Drawing reviews the request.</summary>
    ClarificationRequestState,

    /// <summary>The disposition needs a note, or the chosen new round is not a later round of the same request.</summary>
    DispositionInvalid,

    /// <summary>The supply-route note is required.</summary>
    SupplyRouteInvalid,

    /// <summary>The request is not in Drawing review or returned from Drawing, so raw-material inquiries are not possible.</summary>
    InquiryRequestState,

    /// <summary>A material line needs a grade and a form (at most 100 characters each); dimensions, when given, must be greater than zero.</summary>
    InquiryLineInvalid,

    /// <summary>An inquiry needs at least one line before it is sent.</summary>
    InquiryNoLines,

    /// <summary>The answer must be Yes or No.</summary>
    InquiryAnswerInvalid,

    /// <summary>A REPAIR request has no contract workflow (Decision Log #85).</summary>
    ContractNotApplicable,

    /// <summary>The request is not in "returned from Drawing" status: Management approval and Drawing's result come first.</summary>
    ContractRequestNotReturned,

    /// <summary>Drawing's latest result is not fully or partially accepted.</summary>
    ContractDrawingNotAccepted,

    /// <summary>The contract number is required to submit.</summary>
    ContractNoRequired,

    /// <summary>The contract date is required to submit.</summary>
    ContractDateRequired,

    /// <summary>The contract PDF is required to submit.</summary>
    ContractPdfRequired,

    /// <summary>The contract number or title is longer than allowed.</summary>
    ContractTextTooLong,

    /// <summary>The contract decision is already recorded for this request.</summary>
    ContractAlreadyDecided,

    /// <summary>The material does not exist or is not active.</summary>
    MaterialInvalid,

    /// <summary>The price must be greater than zero (Rial per kg) and within the sanity bound.</summary>
    PriceInvalid,

    /// <summary>The price must apply from a date later than the material's latest price.</summary>
    PriceNotLater,

    /// <summary>The grade or form text is longer than allowed.</summary>
    MaterialTextTooLong,

    /// <summary>The operator's full name is required (at most 200 characters).</summary>
    OperatorNameInvalid,

    /// <summary>The chosen user account does not exist or is not active.</summary>
    UserInvalid,

    /// <summary>The user account is already linked to another operator.</summary>
    UserAlreadyLinked,

    /// <summary>The operator does not exist or is not active.</summary>
    OperatorInvalid,

    /// <summary>An open assignment of this operator to this machine already exists.</summary>
    AssignmentExists,

    /// <summary>The assignment is already closed.</summary>
    AssignmentClosed,

    /// <summary>The last day cannot be before the first day.</summary>
    EndBeforeStart,

    /// <summary>An open qualification of this operator on this machine already exists.</summary>
    QualificationExists,

    /// <summary>The qualification has already ended.</summary>
    QualificationEnded,

    /// <summary>The date is required.</summary>
    DateRequired,

    /// <summary>The date is not a valid Jalali date; the date error says why.</summary>
    DateInvalid,

    /// <summary>The request cannot be submitted to Management until Central has attached the request PDF (Decision Log #87).</summary>
    RequestPdfRequired,

    /// <summary>The chosen file is empty.</summary>
    PdfEmpty,

    /// <summary>The file is larger than the configured limit (Decision Log #54: 20 MiB by default).</summary>
    PdfTooLarge,

    /// <summary>The file is not a PDF (the content has no PDF signature).</summary>
    PdfNotPdf,

    /// <summary>The file name is missing or is not a ".pdf" name.</summary>
    PdfFileNameInvalid,

    /// <summary>The caller did not supply the loaded row versions the action relies on; nothing was changed (Decision Log #77).</summary>
    VersionsMissing,
}

/// <summary>The result of a command or query: a status, the value on success, and the details of a refusal.</summary>
public sealed record Outcome<T>(
    OperationStatus Status,
    T? Value = default,
    ValidationIssue Issue = ValidationIssue.None,
    JalaliDateError DateError = JalaliDateError.None,
    IReadOnlyList<string>? Warnings = null)
{
    public bool Succeeded => Status == OperationStatus.Succeeded;

    public IReadOnlyList<string> WarningList => Warnings ?? [];

    public static Outcome<T> Ok(T value, IReadOnlyList<string>? warnings = null) => new(OperationStatus.Succeeded, value, Warnings: warnings);

    public static Outcome<T> Fail(OperationStatus status) => new(status);

    public static Outcome<T> Invalid(ValidationIssue issue, JalaliDateError dateError = JalaliDateError.None) =>
        new(OperationStatus.ValidationFailed, Issue: issue, DateError: dateError);
}
