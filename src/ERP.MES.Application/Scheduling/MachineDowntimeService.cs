using System.Globalization;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Application.Scheduling;

/// <summary>A downtime record with the names the screen shows and what the signed-in user may do with it.</summary>
public sealed record DowntimeItem(Stored<MachineDowntime> Downtime, string MachineCode, string MachineName, string UnitName, bool CanDecide, bool CanChange);

public sealed record DowntimeCapabilities(bool CanView, bool CanReport, bool CanDecide);

/// <summary>A machine that downtime can be reported for (active machines only), with its unit.</summary>
public sealed record DowntimeMachine(long MachineId, string Code, string Name, long UnitId, string UnitName);

public sealed record DowntimeView(IReadOnlyList<DowntimeItem> Items, IReadOnlyList<DowntimeMachine> Machines, DowntimeCapabilities Capabilities);

/// <summary>
/// Machine downtime (Master Specification section 21, Decision Log #14, #128). Maintenance/PM reports that a machine cannot work, with a stop reason and a local start and end; Planning accepts or
/// rejects the report (rejection needs a reason); Maintenance/PM corrects the end or cancels the record with a reason. Reported and accepted downtime makes the machine unavailable to the
/// planned-end forecast. A machine has at most one counting record at a time, so time is never subtracted twice. Every write is audited in the same transaction.
/// </summary>
public sealed class MachineDowntimeService(
    IMachineDowntimeRepository store,
    IPlanningMasterRepository planning,
    CurrentAuthorization authorization,
    IClock clock,
    IAuditContext auditContext)
{
    /// <summary>The longest period one screen load covers.</summary>
    public const int MaxListDays = 400;

    private static readonly Permission[] All = [Permission.ViewDowntime, Permission.RegisterDowntime, Permission.DecideDowntime];

    public async Task<Outcome<DowntimeView>> LoadAsync(Principal? principal, DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, All, cancellationToken);
        if (!set.Has(Permission.ViewDowntime) || set.Current is null)
        {
            return Outcome<DowntimeView>.Fail(OperationStatus.NotAuthorized);
        }

        if (toInclusive < from || toInclusive.DayNumber - from.DayNumber > MaxListDays)
        {
            return Outcome<DowntimeView>.Invalid(ValidationIssue.EndBeforeStart);
        }

        var canReport = set.Has(Permission.RegisterDowntime);
        var canDecide = set.Has(Permission.DecideDowntime);
        return await CustomerService.GuardAsync(async () =>
        {
            var units = (await planning.ListUnitsAsync(cancellationToken)).ToDictionary(u => u.Value.Id, u => u.Value.Name);
            var machines = (await planning.ListMachinesAsync(cancellationToken)).Select(m => m.Value).ToDictionary(m => m.Id);
            var items = (await store.ListAsync(from.ToDateTime(TimeOnly.MinValue), toInclusive.AddDays(1).ToDateTime(TimeOnly.MinValue), cancellationToken))
                .Select(d => new DowntimeItem(
                    d,
                    machines.TryGetValue(d.Value.MachineId, out var m) ? m.Code : string.Empty,
                    m?.Name ?? string.Empty,
                    units.GetValueOrDefault(d.Value.ProductionUnitId, string.Empty),
                    canDecide && d.Value.Status == DowntimeStatus.Reported,
                    canReport && d.Value.Counts))
                .OrderByDescending(i => i.Downtime.Value.StartLocal).ThenBy(i => i.MachineCode, StringComparer.Ordinal).ToList();
            var usable = machines.Values.Where(m => m.IsActive)
                .Select(m => new DowntimeMachine(m.Id, m.Code, m.Name, m.ProductionUnitId, units.GetValueOrDefault(m.ProductionUnitId, string.Empty)))
                .OrderBy(m => m.Code, StringComparer.Ordinal).ToList();
            return Outcome<DowntimeView>.Ok(new DowntimeView(items, usable, new DowntimeCapabilities(true, canReport, canDecide)));
        });
    }

    /// <summary>Maintenance/PM reports downtime of an active machine in local wall-clock times. The unit is taken from the machine.</summary>
    public async Task<Outcome<Stored<MachineDowntime>>> ReportAsync(
        Principal? principal, long machineId, DowntimeReason reason, DateTime startLocal, DateTime endLocal, string? note, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.RegisterDowntime, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<MachineDowntime>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        return await Guard(async () =>
        {
            var machine = (await planning.ListMachinesAsync(cancellationToken)).Select(m => m.Value).FirstOrDefault(m => m.Id == machineId);
            if (machine is null || !machine.IsActive)
            {
                return Outcome<Stored<MachineDowntime>>.Invalid(ValidationIssue.DowntimeMachineNotUsable);
            }

            MachineDowntime row;
            try
            {
                row = new MachineDowntime(0, machineId, machine.ProductionUnitId, reason, startLocal, endLocal, note, user.UserId, now);
            }
            catch (ArgumentException)
            {
                return Outcome<Stored<MachineDowntime>>.Invalid(ValidationIssue.DowntimeInvalid);
            }

            var (stored, refusal) = await store.AddAsync(
                row,
                context => CheckUsable(row, context) is var issue && issue != ValidationIssue.None ? issue : CheckNoOverlap(row, context),
                Audit(user, now, AuditActions.DowntimeReported, "new", string.Create(
                    CultureInfo.InvariantCulture, $"Downtime reported for machine {machine.Code} (id {machineId}, unit {machine.ProductionUnitId}), reason {reason}, {startLocal:yyyy-MM-dd HH:mm}-{endLocal:yyyy-MM-dd HH:mm}")),
                cancellationToken);
            return refusal == ValidationIssue.None ? Outcome<Stored<MachineDowntime>>.Ok(stored!) : Outcome<Stored<MachineDowntime>>.Invalid(refusal);
        });
    }

    /// <summary>Planning accepts (optional note) or rejects (mandatory reason) a reported downtime.</summary>
    public async Task<Outcome<Stored<MachineDowntime>>> DecideAsync(
        Principal? principal, long id, long expectedVersion, bool accept, string? note, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.DecideDowntime, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<MachineDowntime>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        return await Guard(async () =>
        {
            var (stored, refusal) = await store.ChangeAsync(
                id,
                expectedVersion,
                (row, _) => Apply(() =>
                {
                    if (accept)
                    {
                        row.Accept(note, user.UserId, now);
                    }
                    else
                    {
                        row.Reject(note, user.UserId, now);
                    }
                }),
                _ => Audit(user, now, AuditActions.DowntimeDecided, Id(id), $"Downtime {Id(id)} {(accept ? "accepted" : "rejected")} by Planning"),
                cancellationToken);
            return refusal == ValidationIssue.None ? Outcome<Stored<MachineDowntime>>.Ok(stored!) : Outcome<Stored<MachineDowntime>>.Invalid(refusal);
        });
    }

    /// <summary>Maintenance/PM corrects the end of reported or accepted downtime; an accepted record goes back to Planning. The earlier end is kept in the audit entry.</summary>
    public async Task<Outcome<Stored<MachineDowntime>>> ChangeEndAsync(
        Principal? principal, long id, long expectedVersion, DateTime endLocal, string? reason, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.RegisterDowntime, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<MachineDowntime>>.Fail(OperationStatus.NotAuthorized);
        }

        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > MachineDowntime.MaxNoteLength)
        {
            return Outcome<Stored<MachineDowntime>>.Invalid(ValidationIssue.DowntimeInvalid);
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
                    var changed = Apply(() => row.ChangeEnd(endLocal));
                    return changed != ValidationIssue.None ? changed : CheckNoOverlap(row, context);
                },
                before => Audit(user, now, AuditActions.DowntimeEndChanged, Id(id), string.Create(
                    CultureInfo.InvariantCulture, $"Downtime {id} end changed from {before.EndLocal:yyyy-MM-dd HH:mm} to {endLocal:yyyy-MM-dd HH:mm} (status was {before.Status}); reason: {text}")),
                cancellationToken);
            return refusal == ValidationIssue.None ? Outcome<Stored<MachineDowntime>>.Ok(stored!) : Outcome<Stored<MachineDowntime>>.Invalid(refusal);
        });
    }

    /// <summary>Maintenance/PM cancels reported or accepted downtime with a reason; it stops counting and stays visible.</summary>
    public async Task<Outcome<Stored<MachineDowntime>>> CancelAsync(Principal? principal, long id, long expectedVersion, string? reason, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.RegisterDowntime, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<MachineDowntime>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        return await Guard(async () =>
        {
            var (stored, refusal) = await store.ChangeAsync(
                id,
                expectedVersion,
                (row, _) => Apply(() => row.Cancel(reason, user.UserId, now)),
                _ => Audit(user, now, AuditActions.DowntimeCancelled, Id(id), $"Downtime {Id(id)} cancelled"),
                cancellationToken);
            return refusal == ValidationIssue.None ? Outcome<Stored<MachineDowntime>>.Ok(stored!) : Outcome<Stored<MachineDowntime>>.Invalid(refusal);
        });
    }

    private static ValidationIssue CheckUsable(MachineDowntime row, DowntimeContext context) =>
        context.Machine is { IsActive: true } machine && machine.ProductionUnitId == row.ProductionUnitId ? ValidationIssue.None : ValidationIssue.DowntimeMachineNotUsable;

    private static ValidationIssue CheckNoOverlap(MachineDowntime row, DowntimeContext context) =>
        context.OtherCountingOfMachine.Any(d => d.Id != row.Id && d.Counts && d.Overlaps(row.StartLocal, row.EndLocal)) ? ValidationIssue.DowntimeOverlap : ValidationIssue.None;

    private static ValidationIssue Apply(Action change)
    {
        try
        {
            change();
            return ValidationIssue.None;
        }
        catch (ArgumentException)
        {
            return ValidationIssue.DowntimeInvalid;
        }
    }

    private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);

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
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, "MachineDowntime", targetId, summary);
}
