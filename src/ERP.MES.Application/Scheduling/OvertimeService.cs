using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Application.Scheduling;

/// <summary>An assignment with the names the screen shows and what the signed-in user may do with it.</summary>
public sealed record OvertimeItem(Stored<OvertimeAssignment> Assignment, string OperatorName, string MachineCode, bool CanPlanningDecide, bool CanManagementDecide, bool CanCancel);

public sealed record OvertimeCapabilities(bool CanView, bool CanPropose, bool CanPlanningDecide, bool CanManagementDecide, bool CanConfigureLimits);

public sealed record OvertimeView(IReadOnlyList<OvertimeItem> Items, Stored<OvertimeLimits>? Limits, OvertimeCapabilities Capabilities, IReadOnlyList<long> SupervisedUnitIds);

/// <summary>
/// Overtime assignments (Decision Log #89, #95 D5, #103). The designated supervisor of a unit proposes overtime for an operator on a machine of that unit (never for themselves); Planning approves or
/// rejects it; when it is outside the limits Planning configured, Management decides. No numeric limit is invented: until Planning configures the daily and weekly limits, no overtime can be approved.
/// The rules (outside the normal shift, no absence, one operator per machine) are checked inside the transaction that saves the change. An assignment authorizes hours, it never proves work.
/// </summary>
public sealed class OvertimeService(
    IOvertimeRepository store,
    IOperatorRepository operators,
    IPlanningMasterRepository planning,
    IUnitQueueRepository unitQueue,
    CurrentAuthorization authorization,
    IClock clock,
    IAuditContext auditContext)
{
    private static readonly Permission[] All = [Permission.ViewOvertime, Permission.ProposeOvertime, Permission.DecideOvertime, Permission.DecideOvertimeBeyondLimits];

    public async Task<Outcome<OvertimeView>> LoadAsync(Principal? principal, DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, All, cancellationToken);
        if (!set.Has(Permission.ViewOvertime) || set.Current is null)
        {
            return Outcome<OvertimeView>.Fail(OperationStatus.NotAuthorized);
        }

        if (toInclusive < from || toInclusive.DayNumber - from.DayNumber > 400)
        {
            return Outcome<OvertimeView>.Invalid(ValidationIssue.EndBeforeStart);
        }

        var user = set.Current;
        return await CustomerService.GuardAsync(async () =>
        {
            var people = await operators.ListOperatorsAsync(cancellationToken);
            var own = people.FirstOrDefault(o => o.Value.UserId == user.UserId && o.Value.IsActive)?.Value.Id;
            var supervised = await SupervisedUnitsAsync(user.UserId, cancellationToken);
            var machines = (await planning.ListMachinesAsync(cancellationToken)).ToDictionary(m => m.Value.Id, m => m.Value.Code);
            var names = people.ToDictionary(o => o.Value.Id, o => o.Value.FullName);
            var broad = user.DepartmentCode is DepartmentCodes.Planning or DepartmentCodes.Management;
            var canPropose = set.Has(Permission.ProposeOvertime) && supervised.Count > 0;
            var items = (await store.ListAsync(from, toInclusive, cancellationToken))
                .Where(a => broad || a.Value.OperatorId == own || supervised.Contains(a.Value.ProductionUnitId))
                .Select(a => new OvertimeItem(
                    a,
                    names.GetValueOrDefault(a.Value.OperatorId, string.Empty),
                    machines.GetValueOrDefault(a.Value.MachineId, string.Empty),
                    set.Has(Permission.DecideOvertime) && a.Value.Status == OvertimeStatus.Proposed,
                    set.Has(Permission.DecideOvertimeBeyondLimits) && a.Value.Status == OvertimeStatus.AwaitingManagement,
                    a.Value.IsLive && (set.Has(Permission.DecideOvertime) || (canPropose && a.Value.ProposedByUserId == user.UserId && supervised.Contains(a.Value.ProductionUnitId)))))
                .OrderByDescending(i => i.Assignment.Value.WorkDate).ThenBy(i => i.OperatorName, StringComparer.Ordinal).ToList();
            var limits = await store.GetLimitsAsync(cancellationToken);
            return Outcome<OvertimeView>.Ok(new OvertimeView(
                items,
                limits,
                new OvertimeCapabilities(true, canPropose, set.Has(Permission.DecideOvertime), set.Has(Permission.DecideOvertimeBeyondLimits), set.Has(Permission.DecideOvertime)),
                supervised.ToList()));
        });
    }

    /// <summary>The supervisor of <paramref name="unitId"/> proposes overtime for an operator on a machine of the unit, in local wall-clock times.</summary>
    public async Task<Outcome<Stored<OvertimeAssignment>>> ProposeAsync(
        Principal? principal, long operatorId, long unitId, long machineId, DateTime startLocal, DateTime endLocal, string? reason, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ProposeOvertime, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<OvertimeAssignment>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        return await Guard(async () =>
        {
            if (!(await SupervisedUnitsAsync(user.UserId, cancellationToken)).Contains(unitId))
            {
                return Outcome<Stored<OvertimeAssignment>>.Invalid(ValidationIssue.OvertimeNotSupervisor);
            }

            var people = await operators.ListOperatorsAsync(cancellationToken);
            if (people.Any(o => o.Value.Id == operatorId && o.Value.UserId == user.UserId))
            {
                return Outcome<Stored<OvertimeAssignment>>.Invalid(ValidationIssue.OvertimeNotSupervisor);
            }

            OvertimeAssignment row;
            try
            {
                row = new OvertimeAssignment(0, operatorId, unitId, machineId, startLocal, endLocal, reason, user.UserId, now);
            }
            catch (ArgumentException)
            {
                return Outcome<Stored<OvertimeAssignment>>.Invalid(ValidationIssue.OvertimeInvalid);
            }

            var (stored, refusal) = await store.AddAsync(
                row,
                context => OvertimeRules.CheckBookable(row, context),
                Audit(user, now, AuditActions.OvertimeProposed, "new", $"Overtime proposed for operator {operatorId} on machine {machineId} (unit {unitId}), {startLocal:yyyy-MM-dd HH:mm}-{endLocal:yyyy-MM-dd HH:mm}"),
                cancellationToken);
            return refusal == ValidationIssue.None ? Outcome<Stored<OvertimeAssignment>>.Ok(stored!) : Outcome<Stored<OvertimeAssignment>>.Invalid(refusal);
        });
    }

    /// <summary>Planning approves (within the limits it is final, outside them Management must decide) or rejects a proposal with a mandatory reason.</summary>
    public async Task<Outcome<Stored<OvertimeAssignment>>> PlanningDecideAsync(
        Principal? principal, long id, long expectedVersion, bool approve, string? note, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.DecideOvertime, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<OvertimeAssignment>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        return await Guard(async () =>
        {
            var (stored, refusal) = await store.ChangeAsync(
                id,
                expectedVersion,
                (row, context) =>
                {
                    if (row.Status != OvertimeStatus.Proposed)
                    {
                        throw new InvalidOperationException("Only a proposed assignment is decided by Planning.");
                    }

                    if (!approve)
                    {
                        return Reject(row, note, user.UserId, now, byManagement: false);
                    }

                    if (context.Limits is null)
                    {
                        return ValidationIssue.OvertimeLimitsNotConfigured;
                    }

                    var bookable = OvertimeRules.CheckBookable(row, context);
                    if (bookable != ValidationIssue.None)
                    {
                        return bookable;
                    }

                    try
                    {
                        row.PlanningApprove(OvertimeRules.Usage(row, context).ExceedsLimits, note, user.UserId, now);
                    }
                    catch (ArgumentException)
                    {
                        return ValidationIssue.OvertimeInvalid;
                    }

                    return ValidationIssue.None;
                },
                Audit(user, now, AuditActions.OvertimePlanningDecided, id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Overtime {id} {(approve ? "approved by Planning (or sent to Management when outside the limits)" : "rejected by Planning")}"),
                cancellationToken);
            return refusal == ValidationIssue.None ? Outcome<Stored<OvertimeAssignment>>.Ok(stored!) : Outcome<Stored<OvertimeAssignment>>.Invalid(refusal);
        });
    }

    /// <summary>Management decides an assignment that Planning found outside the limits.</summary>
    public async Task<Outcome<Stored<OvertimeAssignment>>> ManagementDecideAsync(
        Principal? principal, long id, long expectedVersion, bool approve, string? note, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.DecideOvertimeBeyondLimits, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<OvertimeAssignment>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        return await Guard(async () =>
        {
            var (stored, refusal) = await store.ChangeAsync(
                id,
                expectedVersion,
                (row, context) =>
                {
                    if (row.Status != OvertimeStatus.AwaitingManagement)
                    {
                        throw new InvalidOperationException("Only an assignment waiting for Management is decided by Management.");
                    }

                    if (!approve)
                    {
                        return Reject(row, note, user.UserId, now, byManagement: true);
                    }

                    var bookable = OvertimeRules.CheckBookable(row, context);
                    if (bookable != ValidationIssue.None)
                    {
                        return bookable;
                    }

                    try
                    {
                        row.ManagementApprove(note, user.UserId, now);
                    }
                    catch (ArgumentException)
                    {
                        return ValidationIssue.OvertimeInvalid;
                    }

                    return ValidationIssue.None;
                },
                Audit(user, now, AuditActions.OvertimeManagementDecided, id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Overtime {id} {(approve ? "approved" : "rejected")} by Management"),
                cancellationToken);
            return refusal == ValidationIssue.None ? Outcome<Stored<OvertimeAssignment>>.Ok(stored!) : Outcome<Stored<OvertimeAssignment>>.Invalid(refusal);
        });
    }

    /// <summary>Planning, or the supervisor who proposed it, cancels a live assignment with a reason.</summary>
    public async Task<Outcome<Stored<OvertimeAssignment>>> CancelAsync(Principal? principal, long id, long expectedVersion, string? reason, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, All, cancellationToken);
        if (set.Current is null || !(set.Has(Permission.DecideOvertime) || set.Has(Permission.ProposeOvertime)))
        {
            return Outcome<Stored<OvertimeAssignment>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = set.Current;
        var now = clock.UtcNow;
        return await Guard(async () =>
        {
            var supervised = set.Has(Permission.ProposeOvertime) ? await SupervisedUnitsAsync(user.UserId, cancellationToken) : [];
            var (stored, refusal) = await store.ChangeAsync(
                id,
                expectedVersion,
                (row, _) =>
                {
                    if (!set.Has(Permission.DecideOvertime) && !(row.ProposedByUserId == user.UserId && supervised.Contains(row.ProductionUnitId)))
                    {
                        throw new UnauthorizedAccessException("Only Planning or the proposing supervisor cancels an assignment.");
                    }

                    try
                    {
                        row.Cancel(reason, now);
                    }
                    catch (ArgumentException)
                    {
                        return ValidationIssue.OvertimeInvalid;
                    }

                    return ValidationIssue.None;
                },
                Audit(user, now, AuditActions.OvertimeCancelled, id.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Overtime {id} cancelled"),
                cancellationToken);
            return refusal == ValidationIssue.None ? Outcome<Stored<OvertimeAssignment>>.Ok(stored!) : Outcome<Stored<OvertimeAssignment>>.Invalid(refusal);
        });
    }

    /// <summary>Planning sets the daily and weekly overtime limits (minutes). <paramref name="expectedVersion"/> is 0 while none are configured.</summary>
    public async Task<Outcome<Stored<OvertimeLimits>>> SetLimitsAsync(
        Principal? principal, long expectedVersion, int maxMinutesPerDay, int maxMinutesPerWeek, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.DecideOvertime, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<OvertimeLimits>>.Fail(OperationStatus.NotAuthorized);
        }

        if (maxMinutesPerDay is < 1 or > OvertimeLimits.MaxMinutesPerDayCeiling || maxMinutesPerWeek < maxMinutesPerDay || maxMinutesPerWeek > OvertimeLimits.MaxMinutesPerWeekCeiling)
        {
            return Outcome<Stored<OvertimeLimits>>.Invalid(ValidationIssue.OvertimeLimitsInvalid);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () => Outcome<Stored<OvertimeLimits>>.Ok(await store.SetLimitsAsync(
                expectedVersion, maxMinutesPerDay, maxMinutesPerWeek, user.UserId, now,
                Audit(user, now, AuditActions.OvertimeLimitsChanged, "limits", $"Overtime limits set: {maxMinutesPerDay} minutes per day, {maxMinutesPerWeek} minutes per week"), cancellationToken)));
        }
        catch (InvalidOperationException)
        {
            return Outcome<Stored<OvertimeLimits>>.Fail(OperationStatus.InvalidState);
        }
    }

    private static ValidationIssue Reject(OvertimeAssignment row, string? note, long userId, DateTime now, bool byManagement)
    {
        try
        {
            row.Reject(note, userId, now, byManagement);
            return ValidationIssue.None;
        }
        catch (ArgumentException)
        {
            return ValidationIssue.OvertimeInvalid;
        }
    }

    private async Task<HashSet<long>> SupervisedUnitsAsync(long userId, CancellationToken cancellationToken) =>
        (await unitQueue.ListDesignationsAsync(cancellationToken)).Where(d => d.Value.IsActive && d.Value.IsSupervisor && d.Value.UserId == userId).Select(d => d.Value.ProductionUnitId).ToHashSet();

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
        catch (UnauthorizedAccessException)
        {
            return Outcome<T>.Fail(OperationStatus.NotAuthorized);
        }
    }

    private AuditLogEntry Audit(Principal user, DateTime now, string code, string targetId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, "OvertimeAssignment", targetId, summary);
}
