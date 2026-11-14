using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Warehouse;

namespace ERP.MES.Application.Costing;

public sealed record CompletionCapabilities(bool CanView, bool CanReceive, bool CanMarkFinished, bool CanDispatch, bool CanProposeReversal = false, bool CanDecideReversal = false);

public sealed record CompletionList(IReadOnlyList<CompletionItem> Items, CompletionCapabilities Capabilities);

/// <summary>
/// Warehouse completion of an item (Decision Log #95 D7, #105): three distinct, append-only records - (1) the final warehouse receipt after the last QC, (2) the finished/sent mark, allowed only after the
/// receipt and Management's price approval of the item's latest cost snapshot, and (3) an optional customer dispatch (date and document reference). One action never stands for the others. Warehouse
/// alone records them; Planning and Management read. Duplicates are refused; every step is one transaction with its audit entry.
/// </summary>
public sealed class WarehouseCompletionService(
    IWarehouseCompletionRepository store,
    IJalaliDateService dates,
    CurrentAuthorization authorization,
    IClock clock,
    IAuditContext auditContext)
{
    private static readonly Permission[] All = [Permission.ViewWarehouseCompletion, Permission.RecordWarehouseReceipt, Permission.MarkItemFinishedSent, Permission.RecordCustomerDispatch, Permission.ProposeWarehouseReversal, Permission.DecideWarehouseReversal];

    public async Task<Outcome<CompletionList>> LoadAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, All, cancellationToken);
        if (!set.Has(Permission.ViewWarehouseCompletion) || set.Current is null)
        {
            return Outcome<CompletionList>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<CompletionList>.Ok(new CompletionList(
            await store.ListAsync(cancellationToken),
            new CompletionCapabilities(true, set.Has(Permission.RecordWarehouseReceipt), set.Has(Permission.MarkItemFinishedSent), set.Has(Permission.RecordCustomerDispatch), set.Has(Permission.ProposeWarehouseReversal), set.Has(Permission.DecideWarehouseReversal)))));
    }

    /// <summary>Records the final warehouse receipt of the item's QC-accepted base units; a second receipt is refused.</summary>
    public Task<Outcome<WarehouseReceipt>> ReceiveAsync(Principal? principal, long requestItemId, string? note, CancellationToken cancellationToken = default) =>
        RecordAsync(principal, Permission.RecordWarehouseReceipt, requestItemId, AuditActions.WarehouseReceiptRecorded, $"Warehouse receipt of item {requestItemId}", (facts, user, now) =>
        {
            if (!facts.RouteComplete || facts.AcceptedBaseUnits <= 0)
            {
                return new CompletionRecord(ValidationIssue.ReceiptNotReady);
            }

            if (facts.Receipt is not null)
            {
                return new CompletionRecord(ValidationIssue.ReceiptDuplicate);
            }

            try
            {
                return new CompletionRecord(ValidationIssue.None, Receipt: new WarehouseReceipt(requestItemId, facts.AcceptedBaseUnits, note, user.UserId, now, facts.NextReceiptSequence));
            }
            catch (ArgumentException)
            {
                return new CompletionRecord(ValidationIssue.CompletionInputInvalid);
            }
        }, record => record.Receipt!, cancellationToken);

    /// <summary>Marks the received item finished/sent; the item's latest cost snapshot must have a price approved by Management.</summary>
    public Task<Outcome<ItemFinishedSent>> MarkFinishedSentAsync(Principal? principal, long requestItemId, CancellationToken cancellationToken = default) =>
        RecordAsync(principal, Permission.MarkItemFinishedSent, requestItemId, AuditActions.ItemFinishedSent, $"Item {requestItemId} marked finished/sent", (facts, user, now) =>
        {
            if (facts.FinishedSent)
            {
                return new CompletionRecord(ValidationIssue.FinishedSentDuplicate);
            }

            if (facts.Receipt is null)
            {
                return new CompletionRecord(ValidationIssue.ReceiptRequired);
            }

            if (facts.LatestDecisionStatus != PriceDecisionStatus.Approved || facts.LatestDecisionId is not { } decisionId || facts.ApprovedPriceRial is not { } price)
            {
                return new CompletionRecord(ValidationIssue.FinishedSentNeedsPrice);
            }

            return new CompletionRecord(ValidationIssue.None, FinishedSent: new ItemFinishedSent(requestItemId, facts.Receipt.Id, decisionId, price, user.UserId, now, facts.NextFinishedSentSequence));
        }, record => record.FinishedSent!, cancellationToken);

    /// <summary>Records a customer dispatch of a received item: a Jalali date that is not in the future and a document reference.</summary>
    public Task<Outcome<CustomerDispatch>> RecordDispatchAsync(
        Principal? principal, long requestItemId, string? dispatchDateJalali, string? documentReference, string? note, CancellationToken cancellationToken = default) =>
        RecordAsync(principal, Permission.RecordCustomerDispatch, requestItemId, AuditActions.CustomerDispatchRecorded, $"Customer dispatch of item {requestItemId}", (facts, user, now) =>
        {
            if (!dates.TryParseDate(dispatchDateJalali, out var jalali, out _))
            {
                return new CompletionRecord(ValidationIssue.CompletionInputInvalid);
            }

            if (facts.Receipt is null)
            {
                return new CompletionRecord(ValidationIssue.ReceiptRequired);
            }

            var date = dates.ToGregorian(jalali);
            if (date > dates.UtcToLocal(now).Date)
            {
                return new CompletionRecord(ValidationIssue.CompletionInputInvalid);
            }

            try
            {
                return new CompletionRecord(ValidationIssue.None, Dispatch: new CustomerDispatch(requestItemId, date, documentReference, note, user.UserId, now));
            }
            catch (ArgumentException)
            {
                return new CompletionRecord(ValidationIssue.CompletionInputInvalid);
            }
        }, record => record.Dispatch!, cancellationToken);

    /// <summary>
    /// The Warehouse proposes the reversal of one of its records with a mandatory reason (owner decision B9, Decision Log #142). Nothing changes until Management approves: the original stays current and is
    /// never deleted. <paramref name="nature"/> says whether the record was entered by mistake or a physical movement really happened and is being corrected by a compensating transaction.
    /// </summary>
    public async Task<Outcome<WarehouseReversal>> ProposeReversalAsync(
        Principal? principal, long requestItemId, InvalidatedRecordKind kind, long targetId, ReversalNature nature, string? reason, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ProposeWarehouseReversal, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<WarehouseReversal>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var record = await store.ProposeReversalAsync(
                    requestItemId, kind, targetId,
                    facts =>
                    {
                        WarehouseReversal proposal;
                        try
                        {
                            proposal = new WarehouseReversal(requestItemId, kind, targetId, nature, reason, user.UserId, now);
                        }
                        catch (ArgumentException)
                        {
                            return new ReversalRecord(ValidationIssue.ReversalInvalid);
                        }

                        return Refusal(facts, kind) is { } refusal && refusal != ValidationIssue.None
                            ? new ReversalRecord(refusal)
                            : new ReversalRecord(ValidationIssue.None, proposal);
                    },
                    Audit(user, now, AuditActions.WarehouseReversalProposed, requestItemId, $"Reversal of {kind} {targetId} of item {requestItemId} proposed"),
                    cancellationToken);
                return record.Refusal == ValidationIssue.None ? Outcome<WarehouseReversal>.Ok(record.Reversal!) : Outcome<WarehouseReversal>.Invalid(record.Refusal);
            });
        }
        catch (InvalidOperationException)
        {
            return Outcome<WarehouseReversal>.Fail(OperationStatus.InvalidState);
        }
    }

    /// <summary>
    /// Management approves or rejects a proposed reversal (owner decision B9). Approval marks the original record reversed (the row itself is kept) and links the reversal to it with proposer, approver,
    /// timestamps and reasons; rejection needs a reason and leaves the record current. The approver's reason is optional on approval.
    /// </summary>
    public async Task<Outcome<WarehouseReversal>> DecideReversalAsync(
        Principal? principal, long reversalId, bool approve, string? reason, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.DecideWarehouseReversal, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<WarehouseReversal>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var record = await store.DecideReversalAsync(
                    reversalId,
                    (reversal, facts) =>
                    {
                        if (reversal.Status != ReversalStatus.Proposed)
                        {
                            throw new InvalidOperationException("The reversal was already decided.");
                        }

                        if (!approve)
                        {
                            try
                            {
                                reversal.Reject(user.UserId, reason, now);
                            }
                            catch (ArgumentException)
                            {
                                return new ReversalRecord(ValidationIssue.ReversalInvalid);
                            }

                            return new ReversalRecord(ValidationIssue.None, reversal);
                        }

                        // The proposal itself is not an "open proposal" for this check.
                        if (Refusal(facts with { OpenProposalExists = false }, reversal.TargetKind) is { } refusal && refusal != ValidationIssue.None)
                        {
                            return new ReversalRecord(refusal);
                        }

                        try
                        {
                            reversal.Approve(user.UserId, reason, now);
                        }
                        catch (ArgumentException)
                        {
                            return new ReversalRecord(ValidationIssue.ReversalInvalid);
                        }

                        var marker = new RecordInvalidation(
                            reversal.RequestItemId, reversal.TargetKind, reversal.TargetId, InvalidationEffect.Reversed, reversal.ProposalReason, user.UserId, now, warehouseReversal: reversal);
                        return new ReversalRecord(ValidationIssue.None, reversal, marker);
                    },
                    reversal => Audit(user, now, approve ? AuditActions.WarehouseReversalApproved : AuditActions.WarehouseReversalRejected, reversal.RequestItemId,
                        $"Reversal {reversal.Id} of {reversal.TargetKind} {reversal.TargetId} " + (approve ? "approved" : "rejected")),
                    cancellationToken);
                return record.Refusal == ValidationIssue.None ? Outcome<WarehouseReversal>.Ok(record.Reversal!) : Outcome<WarehouseReversal>.Invalid(record.Refusal);
            });
        }
        catch (InvalidOperationException)
        {
            return Outcome<WarehouseReversal>.Fail(OperationStatus.InvalidState);
        }
    }

    private static ValidationIssue Refusal(ReversalFacts facts, InvalidatedRecordKind kind)
    {
        if (!facts.TargetExists)
        {
            return ValidationIssue.ReversalTargetMissing;
        }

        if (facts.RequestClosed)
        {
            return ValidationIssue.ReversalRequestClosed;
        }

        if (!facts.TargetCurrent)
        {
            return ValidationIssue.ReversalTargetNotCurrent;
        }

        if (facts.OpenProposalExists)
        {
            return ValidationIssue.ReversalAlreadyProposed;
        }

        return kind == InvalidatedRecordKind.WarehouseReceipt && facts.CurrentFinishedMarkExists ? ValidationIssue.ReversalBlockedByFinishedMark : ValidationIssue.None;
    }

    private AuditLogEntry Audit(Principal user, DateTime now, string code, long requestItemId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, "WarehouseReversal", requestItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), summary);

    private async Task<Outcome<T>> RecordAsync<T>(
        Principal? principal, Permission permission, long requestItemId, string code, string summary, Func<CompletionFacts, Principal, DateTime, CompletionRecord> decide, Func<CompletionRecord, T> pick,
        CancellationToken cancellationToken)
    {
        var decision = await authorization.CheckAsync(principal, permission, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<T>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var record = await store.RecordAsync(
                    requestItemId,
                    facts => decide(facts, user, now),
                    new AuditLogEntry(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, "WarehouseCompletion", requestItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), summary),
                    cancellationToken);
                return record.Refusal == ValidationIssue.None ? Outcome<T>.Ok(pick(record)) : Outcome<T>.Invalid(record.Refusal);
            });
        }
        catch (InvalidOperationException)
        {
            return Outcome<T>.Fail(OperationStatus.InvalidState);
        }
    }
}
