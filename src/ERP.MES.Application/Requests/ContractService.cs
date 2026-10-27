using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Documents;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

/// <summary>What the signed-in user may do with contracts, for enabling buttons (the Application's answer; every command checks again).</summary>
public sealed record ContractCapabilities(bool CanView, bool CanPrepare, bool CanDecide)
{
    public static ContractCapabilities None { get; } = new(false, false, false);
}

/// <summary>Raised inside a contract change when the request already has a contract record (the decision is made once).</summary>
public sealed class ContractAlreadyExistsException : InvalidOperationException
{
    public ContractAlreadyExistsException()
        : base("The request already has a contract decision.")
    {
    }
}

/// <summary>Raised inside an item contract decision when a rule refuses it.</summary>
public sealed class ItemContractRefusedException(ValidationIssue issue) : InvalidOperationException("The item contract decision was refused: " + issue)
{
    public ValidationIssue Issue { get; } = issue;
}

/// <summary>A contract PDF with its content.</summary>
public sealed record ContractPdfFile(string FileName, long ContentLength, string ContentHash, byte[] Content);

/// <summary>The gate state of one request: what blocks production definition among the gates that exist, and which required gates are not built yet.</summary>
public sealed record RequestGateReadiness(DefinitionBlock Block, ContractStatus? ContractStatus)
{
    public bool ManagementDrawingAndContractGatesSatisfied => Block == DefinitionBlock.None;

    /// <summary>Gates that production release will need but that are not implemented yet; a request is NOT fully released while these are unchecked.</summary>
    public static IReadOnlyList<string> GatesNotYetEnforced { get; } = ["RawMaterialDisposition", "ItemDefinition"];
}

/// <summary>
/// The MAKE contract gate (Decision Log #85; docs/WORKFLOW_REDESIGN.md section 3). Planning decides whether a contract is required, prepares and submits it with its PDF; Management approves
/// or rejects (a rejection needs a reason); Warehouse, Planning and Management read. The request status is not changed: the contract has its own status and the request's readiness is derived
/// from persisted state. Every command checks the permission against the database (Decision Log #74), passes the contract's row version, and runs in one transaction that re-reads the
/// request facts, so the Domain refuses a REPAIR request, a request not returned from Drawing, or a result Drawing did not accept. Rejection routing (return to Planning for revision and
/// resubmission) is a PROVISIONAL default, not an owner-approved permanent rule.
/// </summary>
public sealed class ContractService(IContractRepository contracts, CurrentAuthorization authorization, IClock clock, IJalaliDateService dates, IAuditContext auditContext, DocumentPolicy policy)
{
    private static readonly Permission[] AllPermissions = [Permission.ViewContracts, Permission.CreateOrSubmitContract, Permission.ApproveOrRejectContract];

    public async Task<ContractCapabilities> GetCapabilitiesAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, AllPermissions, cancellationToken);
        return set.Current is null
            ? ContractCapabilities.None
            : new ContractCapabilities(set.Has(Permission.ViewContracts), set.Has(Permission.CreateOrSubmitContract), set.Has(Permission.ApproveOrRejectContract));
    }

    public async Task<Outcome<IReadOnlyList<ContractListItem>>> ListAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewContracts, cancellationToken)).IsAllowed)
        {
            return Outcome<IReadOnlyList<ContractListItem>>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<IReadOnlyList<ContractListItem>>.Ok(await contracts.ListAsync(cancellationToken)));
    }

    public async Task<Outcome<ContractDetail>> GetAsync(Principal? principal, long requestId, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewContracts, cancellationToken)).IsAllowed)
        {
            return Outcome<ContractDetail>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var detail = await contracts.FindAsync(requestId, cancellationToken);
            return detail is null ? Outcome<ContractDetail>.Fail(OperationStatus.NotFound) : Outcome<ContractDetail>.Ok(detail);
        });
    }

    /// <summary>The gate state of a request, derived from persisted state: Management approval and Drawing's result first, then (MAKE only) the contract.</summary>
    public async Task<Outcome<RequestGateReadiness>> GetReadinessAsync(Principal? principal, long requestId, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewContracts, cancellationToken)).IsAllowed)
        {
            return Outcome<RequestGateReadiness>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var detail = await contracts.FindAsync(requestId, cancellationToken);
            if (detail is null)
            {
                return Outcome<RequestGateReadiness>.Fail(OperationStatus.NotFound);
            }

            var status = detail.Contract?.Value.Status;
            var block = DefinitionReadiness.Evaluate(detail.WorkType, detail.Eligibility.RequestStatus, detail.Eligibility.LastDrawingOutcome, status);
            return Outcome<RequestGateReadiness>.Ok(new RequestGateReadiness(block, status));
        });
    }

    // ---- Planning ----

    /// <summary>Planning records whether a contract is required for a MAKE request that Drawing returned. The decision is made once; a change needs <see cref="ChangeRequirementAsync"/> with a reason.</summary>
    public Task<Outcome<Stored<RequestContract>>> DecideRequirementAsync(Principal? principal, long requestId, ContractRequirement requirement, string? note, CancellationToken cancellationToken = default) =>
        RunAsync(principal, Permission.CreateOrSubmitContract, requestId, expectedVersion: null, AuditActions.ContractRequirementDecided, "requirement decided", note,
            validate: () => !Enum.IsDefined(requirement) ? ValidationIssue.ContractNotApplicable : NoteIssue(note, required: false),
            (existing, facts, actor, now, requestIdArg) => existing is not null
                ? throw new ContractAlreadyExistsException()
                : RequestContract.Decide(requestIdArg, facts, requirement, note, actor, now),
            alreadyExistsIssue: ValidationIssue.ContractAlreadyDecided, cancellationToken);

    public Task<Outcome<Stored<RequestContract>>> ChangeRequirementAsync(Principal? principal, long requestId, long expectedVersion, ContractRequirement requirement, string? reason, CancellationToken cancellationToken = default) =>
        RunAsync(principal, Permission.CreateOrSubmitContract, requestId, expectedVersion, AuditActions.ContractRequirementChanged, "requirement changed", reason,
            validate: () => NoteIssue(reason, required: true),
            (existing, facts, actor, now, _) => { existing!.ChangeRequirement(facts, requirement, reason, actor, now); return existing; }, null, cancellationToken);

    /// <summary>The item-level contract decisions of a request, oldest first (Decision Log #111).</summary>
    public async Task<Outcome<IReadOnlyList<ItemContractDecisionRecord>>> ListItemDecisionsAsync(Principal? principal, long requestId, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewContracts, cancellationToken)).IsAllowed)
        {
            return Outcome<IReadOnlyList<ItemContractDecisionRecord>>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<IReadOnlyList<ItemContractDecisionRecord>>.Ok(await contracts.ListItemDecisionsAsync(requestId, cancellationToken)));
    }

    /// <summary>
    /// Planning decides whether ONE item of a MAKE request needs the request's approved contract (Decision Log #111). Needs a reason (every decision is history). "Required" is possible only when the request
    /// itself has a contract decision of "required"; "not required" releases the item from the contract gate. Refused once a stage of the item has been released or started, and a repeated decision is refused.
    /// </summary>
    public async Task<Outcome<ItemContractDecisionRecord>> DecideItemAsync(
        Principal? principal, long requestId, long itemId, ContractRequirement requirement, string? reason, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.CreateOrSubmitContract, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<ItemContractDecisionRecord>.Fail(OperationStatus.NotAuthorized);
        }

        var text = reason?.Trim();
        if (!Enum.IsDefined(requirement))
        {
            return Outcome<ItemContractDecisionRecord>.Invalid(ValidationIssue.ContractNotApplicable);
        }

        if (string.IsNullOrEmpty(text))
        {
            return Outcome<ItemContractDecisionRecord>.Invalid(ValidationIssue.ReasonRequired);
        }

        if (text.Length > ItemContractDecision.MaxReasonLength)
        {
            return Outcome<ItemContractDecisionRecord>.Invalid(ValidationIssue.ReasonTooLong);
        }

        var userId = decision.Current!.UserId;
        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var saved = await contracts.DecideItemAsync(
                    requestId,
                    itemId,
                    context =>
                    {
                        var reasonIneligible = context.Eligibility.Check();
                        if (reasonIneligible != ContractIneligibility.None)
                        {
                            throw new ContractGateException(reasonIneligible);
                        }

                        if (!context.ItemExists)
                        {
                            throw new ItemContractRefusedException(ValidationIssue.ItemNotFound);
                        }

                        if (context.RequestRequirement is null)
                        {
                            throw new ItemContractRefusedException(ValidationIssue.ItemContractRequestUndecided);
                        }

                        if (requirement == ContractRequirement.Required && context.RequestRequirement != ContractRequirement.Required)
                        {
                            throw new ItemContractRefusedException(ValidationIssue.ItemContractRequestNotRequired);
                        }

                        if (context.ItemHasReleasedStage)
                        {
                            throw new ItemContractRefusedException(ValidationIssue.ItemContractInProduction);
                        }

                        // The item's current decision is its own latest row, or the request's decision when it has none.
                        if ((context.LatestItemRequirement ?? context.RequestRequirement) == requirement)
                        {
                            throw new ItemContractRefusedException(ValidationIssue.ItemContractUnchanged);
                        }

                        return new ItemContractDecision(requestId, itemId, requirement, text, userId, now);
                    },
                    (row, requestNo, itemNo) =>
                    [
                        new AuditLogEntry(
                            now, userId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, AuditActions.ItemContractDecided, "RequestItem",
                            itemId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            $"Request {requestNo}, item {itemNo}: contract {(row.Requirement == ContractRequirement.Required ? "required" : "not required")}; reason: {row.Reason}"),
                    ],
                    cancellationToken);
                return Outcome<ItemContractDecisionRecord>.Ok(saved);
            });
        }
        catch (ItemContractRefusedException refused)
        {
            return Outcome<ItemContractDecisionRecord>.Invalid(refused.Issue);
        }
        catch (ContractGateException exception)
        {
            return Outcome<ItemContractDecisionRecord>.Invalid(exception.Reason switch
            {
                ContractIneligibility.NotMake => ValidationIssue.ContractNotApplicable,
                ContractIneligibility.RequestNotReturnedFromDrawing => ValidationIssue.ContractRequestNotReturned,
                _ => ValidationIssue.ContractDrawingNotAccepted,
            });
        }
    }

    public async Task<Outcome<Stored<RequestContract>>> SaveDraftAsync(
        Principal? principal, long requestId, long expectedVersion, string? contractNo, string? contractDateJalali, string? title, string? note, CancellationToken cancellationToken = default)
    {
        DateOnly? date = null;
        if (!string.IsNullOrWhiteSpace(contractDateJalali))
        {
            if (!dates.TryParseDate(contractDateJalali, out var jalali, out var dateError))
            {
                return await DeniedOr(principal, Permission.CreateOrSubmitContract, Outcome<Stored<RequestContract>>.Invalid(ValidationIssue.DateInvalid, dateError), cancellationToken);
            }

            date = dates.ToGregorian(jalali);
        }

        return await RunAsync(principal, Permission.CreateOrSubmitContract, requestId, expectedVersion, AuditActions.ContractDraftSaved, "draft saved", null,
            validate: () => TextIssue(contractNo, title) is { } issue && issue != ValidationIssue.None ? issue : NoteIssue(note, required: false),
            (existing, facts, actor, now, _) => { existing!.SaveDraft(facts, contractNo, date, title, note, actor, now); return existing; }, null, cancellationToken);
    }

    public async Task<Outcome<Stored<RequestContract>>> AttachPdfAsync(
        Principal? principal, long requestId, long expectedVersion, string? fileName, byte[]? content, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.CreateOrSubmitContract, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<RequestContract>>.Fail(OperationStatus.NotAuthorized);
        }

        var userId = decision.Current!.UserId;
        var now = clock.UtcNow;
        if (!StoredDocument.TryCreateContractPdf(fileName, content, content is null ? new string('0', 64) : DocumentHash.Sha256Hex(content), policy.MaxRequestPdfBytes, userId, now, out var document, out var error))
        {
            return Outcome<Stored<RequestContract>>.Invalid(error switch
            {
                PdfError.Empty => ValidationIssue.PdfEmpty,
                PdfError.TooLarge => ValidationIssue.PdfTooLarge,
                PdfError.NotPdf => ValidationIssue.PdfNotPdf,
                _ => ValidationIssue.PdfFileNameInvalid,
            });
        }

        return await RunAsync(principal, Permission.CreateOrSubmitContract, requestId, expectedVersion, AuditActions.ContractPdfAttached,
            $"PDF '{document!.FileName}' attached ({document.ContentLength} bytes, SHA-256 {document.ContentHash})", null, validate: () => ValidationIssue.None,
            (existing, facts, actor, at, _) => { existing!.AttachPdf(facts, document, actor, at); return existing; }, null, cancellationToken);
    }

    public Task<Outcome<Stored<RequestContract>>> SubmitAsync(Principal? principal, long requestId, long expectedVersion, CancellationToken cancellationToken = default) =>
        RunAsync(principal, Permission.CreateOrSubmitContract, requestId, expectedVersion, AuditActions.ContractSubmitted, "submitted to Management", null, validate: () => ValidationIssue.None,
            (existing, facts, actor, now, _) => { existing!.Submit(facts, actor, now); return existing; }, null, cancellationToken);

    public Task<Outcome<Stored<RequestContract>>> StartRevisionAsync(Principal? principal, long requestId, long expectedVersion, CancellationToken cancellationToken = default) =>
        RunAsync(principal, Permission.CreateOrSubmitContract, requestId, expectedVersion, AuditActions.ContractRevisionStarted, "new version started after rejection", null, validate: () => ValidationIssue.None,
            (existing, facts, actor, now, _) => { existing!.StartRevision(facts, actor, now); return existing; }, null, cancellationToken);

    // ---- Management ----

    public Task<Outcome<Stored<RequestContract>>> ApproveAsync(Principal? principal, long requestId, long expectedVersion, string? note, CancellationToken cancellationToken = default) =>
        RunAsync(principal, Permission.ApproveOrRejectContract, requestId, expectedVersion, AuditActions.ContractApproved, "approved by Management", note, validate: () => NoteIssue(note, required: false),
            (existing, facts, actor, now, _) => { existing!.Approve(facts, note, actor, now); return existing; }, null, cancellationToken);

    public Task<Outcome<Stored<RequestContract>>> RejectAsync(Principal? principal, long requestId, long expectedVersion, string? reason, CancellationToken cancellationToken = default) =>
        RunAsync(principal, Permission.ApproveOrRejectContract, requestId, expectedVersion, AuditActions.ContractRejected, "rejected by Management; returned to Planning (provisional routing)", reason,
            validate: () => NoteIssue(reason, required: true),
            (existing, facts, actor, now, _) => { existing!.Reject(facts, reason, actor, now); return existing; }, null, cancellationToken);

    // ---- reading the PDF ----

    public async Task<Outcome<ContractPdfFile>> GetPdfAsync(Principal? principal, long requestId, int versionNo, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewContracts, cancellationToken)).IsAllowed)
        {
            return Outcome<ContractPdfFile>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var document = await contracts.FindPdfAsync(requestId, versionNo, cancellationToken);
            return document is null
                ? Outcome<ContractPdfFile>.Fail(OperationStatus.NotFound)
                : Outcome<ContractPdfFile>.Ok(new ContractPdfFile(document.FileName, document.ContentLength, document.ContentHash, document.Content));
        });
    }

    // ---- shared ----

    private async Task<Outcome<Stored<RequestContract>>> DeniedOr(Principal? principal, Permission permission, Outcome<Stored<RequestContract>> otherwise, CancellationToken cancellationToken) =>
        (await authorization.CheckAsync(principal, permission, cancellationToken)).IsAllowed ? otherwise : Outcome<Stored<RequestContract>>.Fail(OperationStatus.NotAuthorized);

    private static ValidationIssue NoteIssue(string? note, bool required)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return required ? ValidationIssue.ReasonRequired : ValidationIssue.None;
        }

        return note.Trim().Length > RequestContract.MaxNoteLength ? ValidationIssue.ReasonTooLong : ValidationIssue.None;
    }

    private static ValidationIssue? TextIssue(string? contractNo, string? title) =>
        contractNo?.Trim().Length > ContractVersion.MaxContractNoLength || title?.Trim().Length > ContractVersion.MaxTitleLength ? ValidationIssue.ContractTextTooLong : null;

    private async Task<Outcome<Stored<RequestContract>>> RunAsync(
        Principal? principal,
        Permission permission,
        long requestId,
        long? expectedVersion,
        string auditCode,
        string description,
        string? note,
        Func<ValidationIssue> validate,
        Func<RequestContract?, ContractEligibility, WorkflowActor, DateTime, long, RequestContract> change,
        ValidationIssue? alreadyExistsIssue,
        CancellationToken cancellationToken)
    {
        var decision = await authorization.CheckAsync(principal, permission, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<RequestContract>>.Fail(OperationStatus.NotAuthorized);
        }

        var invalid = validate();
        if (invalid != ValidationIssue.None)
        {
            return Outcome<Stored<RequestContract>>.Invalid(invalid);
        }

        var user = decision.Current!;
        var actor = new WorkflowActor(user.UserId, user.DepartmentCode);
        var now = clock.UtcNow;

        IReadOnlyList<AuditLogEntry> Audit(RequestContract contract, string requestNo) =>
        [
            new AuditLogEntry(
                now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, auditCode, "Request",
                requestId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Request {requestNo} contract: {description}; contract status {contract.Status}" + (string.IsNullOrWhiteSpace(note) ? string.Empty : "; note recorded in the contract history")),
        ];

        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var saved = await contracts.ApplyAsync(requestId, expectedVersion, (existing, facts) => change(existing, facts, actor, now, requestId), Audit, cancellationToken);
                return Outcome<Stored<RequestContract>>.Ok(saved);
            });
        }
        catch (ContractGateException exception)
        {
            return Outcome<Stored<RequestContract>>.Invalid(exception.Reason switch
            {
                ContractIneligibility.NotMake => ValidationIssue.ContractNotApplicable,
                ContractIneligibility.RequestNotReturnedFromDrawing => ValidationIssue.ContractRequestNotReturned,
                _ => ValidationIssue.ContractDrawingNotAccepted,
            });
        }
        catch (ContractAlreadyExistsException) when (alreadyExistsIssue is not null)
        {
            return Outcome<Stored<RequestContract>>.Invalid(alreadyExistsIssue.Value);
        }
        catch (ContractSubmissionException exception)
        {
            return Outcome<Stored<RequestContract>>.Invalid(exception.Problem switch
            {
                ContractSubmissionProblem.ContractNoMissing => ValidationIssue.ContractNoRequired,
                ContractSubmissionProblem.ContractDateMissing => ValidationIssue.ContractDateRequired,
                _ => ValidationIssue.ContractPdfRequired,
            });
        }
        catch (InvalidOperationException)
        {
            // The contract is not in the status this action needs (for example somebody else already decided it).
            return Outcome<Stored<RequestContract>>.Fail(OperationStatus.InvalidState);
        }
    }
}
