using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Costing;

namespace ERP.MES.Application.Costing;

public sealed record CostingCapabilities(bool CanView, bool CanCalculate, bool CanApprove);

public sealed record CostingList(IReadOnlyList<CostingItemSummary> Items, CostingCapabilities Capabilities);

public sealed record CostResult(Stored<ItemCostCalculation> Calculation, Stored<ItemPriceDecision> Decision);

/// <summary>
/// Item cost and price (docs/COSTING_DESIGN.md; Decision Log #91, #95 D3/D4, #104). Warehouse calculates an immutable snapshot from the item's approved report time, the machine rates in force and the
/// material price, and submits it; Management alone approves the price (or changes it with a mandatory reason; for an item of an approved contract it records the contract amount with its reference).
/// Planning, Warehouse and Management read. A failed calculation (route not complete, no approved time, a missing rate or price) saves nothing and names the gap. Every command reloads the user from
/// the database; the decision moves under its row version; snapshot, decision and audit entry are one transaction.
/// </summary>
public sealed class CostingService(
    ICostingRepository store,
    IJalaliDateService dates,
    CurrentAuthorization authorization,
    IClock clock,
    IAuditContext auditContext)
{
    private static readonly Permission[] All = [Permission.ViewItemCosts, Permission.CalculateItemCost, Permission.ApproveItemPrice];

    public async Task<Outcome<CostingList>> LoadAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, All, cancellationToken);
        if (!set.Has(Permission.ViewItemCosts) || set.Current is null)
        {
            return Outcome<CostingList>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
            Outcome<CostingList>.Ok(new CostingList(await store.ListItemsAsync(cancellationToken), new CostingCapabilities(true, set.Has(Permission.CalculateItemCost), set.Has(Permission.ApproveItemPrice)))));
    }

    public async Task<Outcome<CostingHistory>> HistoryAsync(Principal? principal, long requestItemId, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ViewItemCosts, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<CostingHistory>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<CostingHistory>.Ok(await store.HistoryAsync(requestItemId, cancellationToken)));
    }

    /// <summary>Calculates a new snapshot of the item (and its draft price decision). The refusal names missing rates or prices in the warnings as <c>RATE|machine|date</c> / <c>PRICE|material|date</c>.</summary>
    public async Task<Outcome<CostResult>> CalculateAsync(Principal? principal, long requestItemId, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.CalculateItemCost, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<CostResult>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        return await Guard(async () =>
        {
            var (calculation, price, build) = await store.CalculateAsync(
                requestItemId,
                inputs => CostCalculator.Build(inputs, user.UserId, now, utc => dates.UtcToLocal(utc).Date),
                Audit(user, now, AuditActions.ItemCostCalculated, requestItemId.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Cost of item {requestItemId} calculated"),
                cancellationToken);
            return build.Refusal == ValidationIssue.None
                ? Outcome<CostResult>.Ok(new CostResult(calculation!, price!))
                : new Outcome<CostResult>(OperationStatus.ValidationFailed, Issue: build.Refusal, Warnings: build.Gaps);
        });
    }

    /// <summary>Warehouse sends a calculated price to Management.</summary>
    public async Task<Outcome<Stored<ItemPriceDecision>>> SubmitAsync(Principal? principal, long decisionId, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.CalculateItemCost, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<ItemPriceDecision>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        return await Change(decisionId, expectedVersion, row =>
        {
            row.Submit(user.UserId, now);
            return ValidationIssue.None;
        }, Audit(user, now, AuditActions.ItemPriceSubmitted, decisionId.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Price decision {decisionId} submitted to Management"), cancellationToken);
    }

    /// <summary>
    /// Management approves the price. <paramref name="priceRial"/> null accepts the calculated proposal; a different price needs a reason; for a contract item the contract amount and reference are mandatory.
    /// </summary>
    public async Task<Outcome<Stored<ItemPriceDecision>>> ApproveAsync(
        Principal? principal, long decisionId, long expectedVersion, decimal? priceRial, string? reason, string? contractReference, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ApproveItemPrice, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<ItemPriceDecision>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        return await Change(decisionId, expectedVersion, row =>
        {
            try
            {
                row.Approve(priceRial, reason, contractReference, user.UserId, now);
                return ValidationIssue.None;
            }
            catch (ArgumentException)
            {
                return ValidationIssue.ItemPriceInvalid;
            }
        }, Audit(user, now, AuditActions.ItemPriceApproved, decisionId.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Price decision {decisionId} approved"), cancellationToken);
    }

    private async Task<Outcome<Stored<ItemPriceDecision>>> Change(
        long decisionId, long expectedVersion, Func<ItemPriceDecision, ValidationIssue> change, AuditLogEntry audit, CancellationToken cancellationToken) =>
        await Guard(async () =>
        {
            var (row, refusal) = await store.ChangeDecisionAsync(decisionId, expectedVersion, change, audit, cancellationToken);
            return refusal == ValidationIssue.None ? Outcome<Stored<ItemPriceDecision>>.Ok(row!) : Outcome<Stored<ItemPriceDecision>>.Invalid(refusal);
        });

    private static async Task<Outcome<T>> Guard<T>(Func<Task<Outcome<T>>> action)
    {
        try
        {
            return await CustomerService.GuardAsync(action);
        }
        catch (InvalidOperationException)
        {
            return Outcome<T>.Fail(OperationStatus.InvalidState);
        }
    }

    private AuditLogEntry Audit(Principal user, DateTime now, string code, string targetId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, "ItemCost", targetId, summary);
}
