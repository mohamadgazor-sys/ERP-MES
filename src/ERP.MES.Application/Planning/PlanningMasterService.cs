using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Quality;

namespace ERP.MES.Application.Planning;

/// <summary>The Planning resource master data as loaded at one moment. <see cref="Rates"/> is empty and <see cref="RatesVisible"/> false for a user who may not read rates.</summary>
public sealed record PlanningMasterData(
    IReadOnlyList<Stored<ProductionUnit>> Units, IReadOnlyList<Stored<Machine>> Machines, IReadOnlyList<MachineHourlyRate> Rates, bool RatesVisible);

/// <summary>What the signed-in user may do on the Planning master screen, for enabling buttons (the Application's answer; every command checks again).</summary>
public sealed record PlanningMasterCapabilities(bool CanMaintainResources, bool CanChangeRates, bool CanViewRates)
{
    public static PlanningMasterCapabilities None { get; } = new(false, false, false);
}

/// <summary>
/// Planning resource master data: production units, machines with stable codes and the hourly-rate history (Decision Log #10, #82 R2, #89). Planning registers units and machines;
/// Planning and Management change rates; Warehouse may not (it only reads). Every command reloads the user from the database (Decision Log #74); updates pass the row version; a rate is
/// an append-only history entry that applies from a date, must be later than the machine's latest entry, and is audited with its reason in the same transaction.
/// </summary>
public sealed class PlanningMasterService(IPlanningMasterRepository repository, CurrentAuthorization authorization, IClock clock, IJalaliDateService dates, IAuditContext auditContext)
{
    private static readonly Permission[] AllPermissions = [Permission.MaintainProductionResources, Permission.ChangeMachineHourlyRates, Permission.ViewMachineRates];

    public async Task<PlanningMasterCapabilities> GetCapabilitiesAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, AllPermissions, cancellationToken);
        return set.Current is null
            ? PlanningMasterCapabilities.None
            : new PlanningMasterCapabilities(set.Has(Permission.MaintainProductionResources), set.Has(Permission.ChangeMachineHourlyRates), set.Has(Permission.ViewMachineRates));
    }

    public async Task<Outcome<PlanningMasterData>> LoadAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, [Permission.ViewPlanningMasterData, Permission.ViewMachineRates], cancellationToken);
        if (!set.Has(Permission.ViewPlanningMasterData))
        {
            return Outcome<PlanningMasterData>.Fail(OperationStatus.NotAuthorized);
        }

        var ratesVisible = set.Has(Permission.ViewMachineRates);
        return await CustomerService.GuardAsync(async () => Outcome<PlanningMasterData>.Ok(new PlanningMasterData(
            await repository.ListUnitsAsync(cancellationToken),
            await repository.ListMachinesAsync(cancellationToken),
            ratesVisible ? await repository.ListRatesAsync(cancellationToken) : [],
            ratesVisible)));
    }

    // ---- production units ----

    public async Task<Outcome<Stored<ProductionUnit>>> AddUnitAsync(Principal? principal, string? code, string? name, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<ProductionUnit>>.Fail(OperationStatus.NotAuthorized);
        }

        if (!UnitOfMeasure.IsValidCode(code))
        {
            return Outcome<Stored<ProductionUnit>>.Invalid(ValidationIssue.CodeInvalid);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Outcome<Stored<ProductionUnit>>.Invalid(ValidationIssue.NameRequired);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            if ((await repository.ListUnitsAsync(cancellationToken)).Any(u => string.Equals(u.Value.Code, code, StringComparison.Ordinal)))
            {
                return Outcome<Stored<ProductionUnit>>.Invalid(ValidationIssue.CodeDuplicate);
            }

            var unit = new ProductionUnit(0, code!, name, decision.Current!.UserId, now);
            return Outcome<Stored<ProductionUnit>>.Ok(await repository.AddUnitAsync(unit, [Audit(decision.Current, now, AuditActions.ProductionUnitAdded, "ProductionUnit", code!, $"Production unit {code} registered")], cancellationToken));
        });
    }

    public async Task<Outcome<Stored<ProductionUnit>>> RenameUnitAsync(Principal? principal, long unitId, long expectedVersion, string? name, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken)).IsAllowed)
        {
            return Outcome<Stored<ProductionUnit>>.Fail(OperationStatus.NotAuthorized);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Outcome<Stored<ProductionUnit>>.Invalid(ValidationIssue.NameRequired);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () => Outcome<Stored<ProductionUnit>>.Ok(await repository.UpdateUnitAsync(unitId, expectedVersion, u => u.Rename(name, now), cancellationToken)));
    }

    public async Task<Outcome<Stored<ProductionUnit>>> SetUnitActiveAsync(Principal? principal, long unitId, long expectedVersion, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken)).IsAllowed)
        {
            return Outcome<Stored<ProductionUnit>>.Fail(OperationStatus.NotAuthorized);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () => Outcome<Stored<ProductionUnit>>.Ok(await repository.UpdateUnitAsync(unitId, expectedVersion, u => u.SetActive(isActive, now), cancellationToken)));
    }

    // ---- machines ----

    public async Task<Outcome<Stored<Machine>>> AddMachineAsync(Principal? principal, string? code, string? name, long productionUnitId, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<Machine>>.Fail(OperationStatus.NotAuthorized);
        }

        if (!UnitOfMeasure.IsValidCode(code))
        {
            return Outcome<Stored<Machine>>.Invalid(ValidationIssue.CodeInvalid);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Outcome<Stored<Machine>>.Invalid(ValidationIssue.NameRequired);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var unit = (await repository.ListUnitsAsync(cancellationToken)).FirstOrDefault(u => u.Value.Id == productionUnitId);
            if (unit is null || !unit.Value.IsActive)
            {
                return Outcome<Stored<Machine>>.Invalid(ValidationIssue.ProductionUnitInvalid);
            }

            if ((await repository.ListMachinesAsync(cancellationToken)).Any(m => string.Equals(m.Value.Code, code, StringComparison.Ordinal)))
            {
                return Outcome<Stored<Machine>>.Invalid(ValidationIssue.CodeDuplicate);
            }

            var machine = new Machine(0, code!, name, productionUnitId, decision.Current!.UserId, now);
            return Outcome<Stored<Machine>>.Ok(await repository.AddMachineAsync(
                machine, [Audit(decision.Current, now, AuditActions.MachineAdded, "Machine", code!, $"Machine {code} registered in production unit {unit.Value.Code}")], cancellationToken));
        });
    }

    public async Task<Outcome<Stored<Machine>>> RenameMachineAsync(Principal? principal, long machineId, long expectedVersion, string? name, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken)).IsAllowed)
        {
            return Outcome<Stored<Machine>>.Fail(OperationStatus.NotAuthorized);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Outcome<Stored<Machine>>.Invalid(ValidationIssue.NameRequired);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () => Outcome<Stored<Machine>>.Ok(await repository.UpdateMachineAsync(machineId, expectedVersion, m => m.Rename(name, now), cancellationToken)));
    }

    public async Task<Outcome<Stored<Machine>>> SetMachineActiveAsync(Principal? principal, long machineId, long expectedVersion, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken)).IsAllowed)
        {
            return Outcome<Stored<Machine>>.Fail(OperationStatus.NotAuthorized);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () => Outcome<Stored<Machine>>.Ok(await repository.UpdateMachineAsync(machineId, expectedVersion, m => m.SetActive(isActive, now), cancellationToken)));
    }

    // ---- hourly rates ----

    /// <summary>
    /// Adds a rate entry for a machine: Rial per hour, applying from the typed Jalali date. The date must be later than the machine's latest entry (the history is never rewritten), the
    /// machine must be active, and the change is audited with its reason. Planning and Management only; Warehouse and everyone else are refused.
    /// </summary>
    public async Task<Outcome<MachineHourlyRate>> AddRateAsync(
        Principal? principal, long machineId, decimal ratePerHourRial, string? effectiveFromJalali, string? reason, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ChangeMachineHourlyRates, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<MachineHourlyRate>.Fail(OperationStatus.NotAuthorized);
        }

        if (ratePerHourRial <= 0 || ratePerHourRial > MachineHourlyRate.MaxRatePerHourRial)
        {
            return Outcome<MachineHourlyRate>.Invalid(ValidationIssue.RateInvalid);
        }

        if (string.IsNullOrWhiteSpace(effectiveFromJalali))
        {
            return Outcome<MachineHourlyRate>.Invalid(ValidationIssue.EffectiveDateRequired);
        }

        if (!dates.TryParseDate(effectiveFromJalali, out var jalali, out var dateError))
        {
            return Outcome<MachineHourlyRate>.Invalid(ValidationIssue.EffectiveDateInvalid, dateError);
        }

        if (reason is not null && reason.Trim().Length > MachineHourlyRate.MaxReasonLength)
        {
            return Outcome<MachineHourlyRate>.Invalid(ValidationIssue.ReasonTooLong);
        }

        var effectiveFrom = dates.ToGregorian(jalali);
        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var machine = (await repository.ListMachinesAsync(cancellationToken)).FirstOrDefault(m => m.Value.Id == machineId);
            if (machine is null || !machine.Value.IsActive)
            {
                return Outcome<MachineHourlyRate>.Invalid(ValidationIssue.MachineInvalid);
            }

            var latest = (await repository.ListRatesAsync(cancellationToken)).Where(r => r.MachineId == machineId).MaxBy(r => r.EffectiveFrom);
            if (latest is not null && effectiveFrom <= latest.EffectiveFrom)
            {
                return Outcome<MachineHourlyRate>.Invalid(ValidationIssue.RateNotLater);
            }

            var rate = new MachineHourlyRate(0, machineId, ratePerHourRial, effectiveFrom, reason, decision.Current!.UserId, now);
            var rial = ratePerHourRial.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
            var summary = $"Machine {machine.Value.Code}: hourly rate {(latest is null ? "set" : latest.RatePerHourRial.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture) + " ->")} {rial} Rial from {effectiveFrom:yyyy-MM-dd}"
                + (string.IsNullOrWhiteSpace(reason) ? string.Empty : "; reason recorded with the rate");
            return Outcome<MachineHourlyRate>.Ok(await repository.AddRateAsync(rate, [Audit(decision.Current, now, AuditActions.MachineRateChanged, "Machine", machine.Value.Code, summary)], cancellationToken));
        });
    }

    private AuditLogEntry Audit(Principal user, DateTime now, string action, string targetType, string targetId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, action, targetType, targetId, summary);
}
