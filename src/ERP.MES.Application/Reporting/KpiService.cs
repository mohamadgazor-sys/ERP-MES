using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Identity;

namespace ERP.MES.Application.Reporting;

/// <summary>
/// The unit metrics screen's use case (Decision Log #95 D6/D8, #106). Planning and Management read any unit; a unit supervisor reads their own units only; every other user is refused. The run is as-of:
/// the report carries its run time and period, and later approvals may change the figures. Nothing is written.
/// </summary>
public sealed class KpiService(IKpiRepository store, IUnitQueueRepository unitQueue, IJalaliDateService dates, CurrentAuthorization authorization, IClock clock)
{
    public async Task<Outcome<KpiReport>> ReportAsync(Principal? principal, DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ViewKpi, cancellationToken);
        if (!decision.IsAllowed || decision.Current is null)
        {
            return Outcome<KpiReport>.Fail(OperationStatus.NotAuthorized);
        }

        if (toInclusive < from || toInclusive.DayNumber - from.DayNumber > 400)
        {
            return Outcome<KpiReport>.Invalid(ValidationIssue.EndBeforeStart);
        }

        var user = decision.Current;
        return await CustomerService.GuardAsync(async () =>
        {
            IReadOnlySet<long>? visible = null;
            if (user.DepartmentCode is not (DepartmentCodes.Planning or DepartmentCodes.Management))
            {
                visible = (await unitQueue.ListDesignationsAsync(cancellationToken)).Where(d => d.Value.IsActive && d.Value.IsSupervisor && d.Value.UserId == user.UserId).Select(d => d.Value.ProductionUnitId).ToHashSet();
                if (visible.Count == 0)
                {
                    return Outcome<KpiReport>.Fail(OperationStatus.NotAuthorized);
                }
            }

            var facts = await store.LoadAsync(from, toInclusive, cancellationToken);
            return Outcome<KpiReport>.Ok(KpiCalculator.Build(facts, from, toInclusive, clock.UtcNow, ToLocal, visible));
        });
    }

    private DateTime ToLocal(DateTime utc)
    {
        var (date, time) = dates.UtcToLocal(utc);
        return date.ToDateTime(time);
    }
}
