using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Warehouse;

namespace ERP.MES.Application.Costing;

public sealed record CompletionCapabilities(bool CanView, bool CanReceive, bool CanMarkFinished, bool CanDispatch);

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
    private static readonly Permission[] All = [Permission.ViewWarehouseCompletion, Permission.RecordWarehouseReceipt, Permission.MarkItemFinishedSent, Permission.RecordCustomerDispatch];

    public async Task<Outcome<CompletionList>> LoadAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, All, cancellationToken);
        if (!set.Has(Permission.ViewWarehouseCompletion) || set.Current is null)
        {
            return Outcome<CompletionList>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<CompletionList>.Ok(new CompletionList(
            await store.ListAsync(cancellationToken),
            new CompletionCapabilities(true, set.Has(Permission.RecordWarehouseReceipt), set.Has(Permission.MarkItemFinishedSent), set.Has(Permission.RecordCustomerDispatch)))));
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
                return new CompletionRecord(ValidationIssue.None, Receipt: new WarehouseReceipt(requestItemId, facts.AcceptedBaseUnits, note, user.UserId, now));
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

            return new CompletionRecord(ValidationIssue.None, FinishedSent: new ItemFinishedSent(requestItemId, facts.Receipt.Id, decisionId, price, user.UserId, now));
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
