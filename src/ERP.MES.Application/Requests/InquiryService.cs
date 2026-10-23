using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

/// <summary>What the signed-in user may do with raw-material inquiries, for enabling buttons (the Application's answer; every command checks again).</summary>
public sealed record InquiryCapabilities(bool CanView, bool CanManage, bool CanAnswer)
{
    public static InquiryCapabilities None { get; } = new(false, false, false);
}

/// <summary>The values of one inquiry line as entered. Dimensions in millimetres; null = not applicable for this form.</summary>
public sealed record InquiryLineInput(
    string? MaterialGrade, string? MaterialForm, decimal? DiameterMm, decimal? LengthMm, string? OtherDimension, decimal RequiredQuantity, long QuantityUnitId, string? StandardOrSpec, string? Remarks);

/// <summary>
/// Raw-material availability inquiries (docs/RAW_MATERIAL_INQUIRY_DESIGN.md; Decision Log #86, #88). Drawing creates, fills, sends, closes and cancels; Warehouse answers each line Yes or No; both
/// read. The inquiry belongs to the request, changes no request status, reserves nothing and does not decide feasibility or block anything. NOT built, because they wait for owner decision D1:
/// the clarification thread, what a "No" triggers and any release gate. Planning and Management read access waits for D8 and stays denied. Every command reloads the user from the database
/// (Decision Log #74), passes the inquiry's expected row version and is audited in the same transaction.
/// </summary>
public sealed class InquiryService(IInquiryRepository inquiries, IQcMasterRepository qc, CurrentAuthorization authorization, IClock clock, IAuditContext auditContext)
{
    private static readonly Permission[] AllPermissions = [Permission.ViewRawMaterialInquiries, Permission.ManageRawMaterialInquiries, Permission.AnswerRawMaterialInquiry];

    public async Task<InquiryCapabilities> GetCapabilitiesAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, AllPermissions, cancellationToken);
        return set.Current is null
            ? InquiryCapabilities.None
            : new InquiryCapabilities(set.Has(Permission.ViewRawMaterialInquiries), set.Has(Permission.ManageRawMaterialInquiries), set.Has(Permission.AnswerRawMaterialInquiry));
    }

    public async Task<Outcome<IReadOnlyList<InquiryRequestItem>>> ListRequestsAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewRawMaterialInquiries, cancellationToken)).IsAllowed)
        {
            return Outcome<IReadOnlyList<InquiryRequestItem>>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<IReadOnlyList<InquiryRequestItem>>.Ok(await inquiries.ListRequestsAsync(cancellationToken)));
    }

    public async Task<Outcome<IReadOnlyList<Stored<RawMaterialInquiry>>>> ListForRequestAsync(Principal? principal, long requestId, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewRawMaterialInquiries, cancellationToken)).IsAllowed)
        {
            return Outcome<IReadOnlyList<Stored<RawMaterialInquiry>>>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<IReadOnlyList<Stored<RawMaterialInquiry>>>.Ok(await inquiries.ListForRequestAsync(requestId, cancellationToken)));
    }

    public async Task<Outcome<InquiryDetail>> GetAsync(Principal? principal, long inquiryId, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewRawMaterialInquiries, cancellationToken)).IsAllowed)
        {
            return Outcome<InquiryDetail>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var detail = await inquiries.FindAsync(inquiryId, cancellationToken);
            return detail is null ? Outcome<InquiryDetail>.Fail(OperationStatus.NotFound) : Outcome<InquiryDetail>.Ok(detail);
        });
    }

    // ---- Drawing ----

    public async Task<Outcome<Stored<RawMaterialInquiry>>> CreateAsync(Principal? principal, long requestId, string? note, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ManageRawMaterialInquiries, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Fail(OperationStatus.NotAuthorized);
        }

        if (NoteIssue(note, required: false) is var issue && issue != ValidationIssue.None)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Invalid(issue);
        }

        var (actor, now) = Who(decision);
        return await RunAsync(async () => await inquiries.CreateAsync(
            requestId,
            context => RawMaterialInquiry.Create(requestId, context.NextRoundNo, context.RequestStatus, note, actor, now),
            (inquiry, requestNo) => [Audit(decision.Current!, now, AuditActions.InquiryCreated, requestId, $"Request {requestNo}: raw-material inquiry round {inquiry.RoundNo} created")]));
    }

    public async Task<Outcome<Stored<RawMaterialInquiry>>> AddLineAsync(Principal? principal, long inquiryId, long expectedVersion, InquiryLineInput line, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(line);
        var decision = await authorization.CheckAsync(principal, Permission.ManageRawMaterialInquiries, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Fail(OperationStatus.NotAuthorized);
        }

        if (line.RequiredQuantity <= 0)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Invalid(ValidationIssue.QuantityInvalid);
        }

        if (!ValidText(line.MaterialGrade) || !ValidText(line.MaterialForm) || line.DiameterMm is <= 0 || line.LengthMm is <= 0
            || TooLong(line.OtherDimension) || TooLong(line.StandardOrSpec) || TooLong(line.Remarks))
        {
            return Outcome<Stored<RawMaterialInquiry>>.Invalid(ValidationIssue.InquiryLineInvalid);
        }

        var (actor, now) = Who(decision);
        return await RunOutcomeAsync(async () =>
        {
            var unit = await qc.FindUnitAsync(line.QuantityUnitId, cancellationToken);
            if (unit is null || !unit.Value.IsActive)
            {
                return Outcome<Stored<RawMaterialInquiry>>.Invalid(ValidationIssue.UnitNotFound);
            }

            return Outcome<Stored<RawMaterialInquiry>>.Ok(await inquiries.UpdateAsync(
                inquiryId, expectedVersion,
                (inquiry, _) => inquiry.AddLine(line.MaterialGrade!, line.MaterialForm!, line.DiameterMm, line.LengthMm, line.OtherDimension, line.RequiredQuantity, line.QuantityUnitId, line.StandardOrSpec, line.Remarks, actor, now),
                (inquiry, requestNo) => [Audit(decision.Current!, now, AuditActions.InquiryLineAdded, inquiry.RequestId, $"Request {requestNo}: inquiry round {inquiry.RoundNo}: line added")],
                cancellationToken));
        });
    }

    public async Task<Outcome<Stored<RawMaterialInquiry>>> RemoveLineAsync(Principal? principal, long inquiryId, long expectedVersion, int lineNo, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ManageRawMaterialInquiries, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Fail(OperationStatus.NotAuthorized);
        }

        var (actor, now) = Who(decision);
        return await RunAsync(() => inquiries.UpdateAsync(
            inquiryId, expectedVersion, (inquiry, _) => inquiry.RemoveLine(lineNo, actor, now),
            (inquiry, requestNo) => [Audit(decision.Current!, now, AuditActions.InquiryLineRemoved, inquiry.RequestId, $"Request {requestNo}: inquiry round {inquiry.RoundNo}: line {lineNo} removed from the draft")],
            cancellationToken));
    }

    public async Task<Outcome<Stored<RawMaterialInquiry>>> SendAsync(Principal? principal, long inquiryId, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ManageRawMaterialInquiries, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Fail(OperationStatus.NotAuthorized);
        }

        var (actor, now) = Who(decision);
        return await RunAsync(() => inquiries.UpdateAsync(
            inquiryId, expectedVersion, (inquiry, requestStatus) => inquiry.Send(requestStatus, actor, now),
            (inquiry, requestNo) => [Audit(decision.Current!, now, AuditActions.InquirySent, inquiry.RequestId, $"Request {requestNo}: inquiry round {inquiry.RoundNo} sent to Warehouse ({inquiry.Lines.Count} lines)")],
            cancellationToken));
    }

    public async Task<Outcome<Stored<RawMaterialInquiry>>> CloseAsync(Principal? principal, long inquiryId, long expectedVersion, string? note, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ManageRawMaterialInquiries, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Fail(OperationStatus.NotAuthorized);
        }

        if (NoteIssue(note, required: false) is var issue && issue != ValidationIssue.None)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Invalid(issue);
        }

        var (actor, now) = Who(decision);
        return await RunAsync(() => inquiries.UpdateAsync(
            inquiryId, expectedVersion, (inquiry, _) => inquiry.Close(note, actor, now),
            (inquiry, requestNo) => [Audit(decision.Current!, now, AuditActions.InquiryClosed, inquiry.RequestId, $"Request {requestNo}: inquiry round {inquiry.RoundNo} closed after review")],
            cancellationToken));
    }

    public async Task<Outcome<Stored<RawMaterialInquiry>>> CancelAsync(Principal? principal, long inquiryId, long expectedVersion, string? reason, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ManageRawMaterialInquiries, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Fail(OperationStatus.NotAuthorized);
        }

        if (NoteIssue(reason, required: true) is var issue && issue != ValidationIssue.None)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Invalid(issue);
        }

        var (actor, now) = Who(decision);
        return await RunAsync(() => inquiries.UpdateAsync(
            inquiryId, expectedVersion, (inquiry, _) => inquiry.Cancel(reason, actor, now),
            (inquiry, requestNo) => [Audit(decision.Current!, now, AuditActions.InquiryCancelled, inquiry.RequestId, $"Request {requestNo}: inquiry round {inquiry.RoundNo} cancelled; reason recorded in the history")],
            cancellationToken));
    }

    // ---- Warehouse ----

    public async Task<Outcome<Stored<RawMaterialInquiry>>> AnswerAsync(
        Principal? principal, long inquiryId, long expectedVersion, int lineNo, InquiryAnswer answer, string? note, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.AnswerRawMaterialInquiry, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Fail(OperationStatus.NotAuthorized);
        }

        if (!Enum.IsDefined(answer))
        {
            return Outcome<Stored<RawMaterialInquiry>>.Invalid(ValidationIssue.InquiryAnswerInvalid);
        }

        if (NoteIssue(note, required: false) is var issue && issue != ValidationIssue.None)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Invalid(issue);
        }

        var (actor, now) = Who(decision);
        return await RunAsync(() => inquiries.UpdateAsync(
            inquiryId, expectedVersion, (inquiry, _) => inquiry.Answer(lineNo, answer, note, actor, now),
            (inquiry, requestNo) => [Audit(decision.Current!, now, AuditActions.InquiryAnswered, inquiry.RequestId, $"Request {requestNo}: inquiry round {inquiry.RoundNo}, line {lineNo}: answered {answer}")],
            cancellationToken));
    }

    // ---- shared ----

    private (WorkflowActor Actor, DateTime Now) Who(AuthorizationDecision decision) => (new WorkflowActor(decision.Current!.UserId, decision.Current.DepartmentCode), clock.UtcNow);

    private static bool ValidText(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= RawMaterialInquiryLine.MaxTextLength;

    private static bool TooLong(string? value) => value is not null && value.Trim().Length > RawMaterialInquiryLine.MaxTextLength * 5;

    private static ValidationIssue NoteIssue(string? note, bool required)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return required ? ValidationIssue.ReasonRequired : ValidationIssue.None;
        }

        return note.Trim().Length > RawMaterialInquiry.MaxNoteLength ? ValidationIssue.ReasonTooLong : ValidationIssue.None;
    }

    private AuditLogEntry Audit(Principal user, DateTime now, string action, long requestId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, action, "Request", requestId.ToString(System.Globalization.CultureInfo.InvariantCulture), summary);

    private static async Task<Outcome<Stored<RawMaterialInquiry>>> RunAsync(Func<Task<Stored<RawMaterialInquiry>>> work) =>
        await RunOutcomeAsync(async () => Outcome<Stored<RawMaterialInquiry>>.Ok(await work()));

    private static async Task<Outcome<Stored<RawMaterialInquiry>>> RunOutcomeAsync(Func<Task<Outcome<Stored<RawMaterialInquiry>>>> work)
    {
        try
        {
            return await CustomerService.GuardAsync(work);
        }
        catch (InquiryRequestStateException)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Invalid(ValidationIssue.InquiryRequestState);
        }
        catch (ArgumentException)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Invalid(ValidationIssue.InquiryLineInvalid);
        }
        catch (InquiryNoLinesException)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Invalid(ValidationIssue.InquiryNoLines);
        }
        catch (InvalidOperationException)
        {
            return Outcome<Stored<RawMaterialInquiry>>.Fail(OperationStatus.InvalidState);
        }
    }
}
