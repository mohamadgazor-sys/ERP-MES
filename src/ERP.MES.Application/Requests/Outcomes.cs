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
