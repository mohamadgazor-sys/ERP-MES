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
