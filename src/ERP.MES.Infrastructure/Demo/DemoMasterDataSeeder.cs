using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Materials;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Infrastructure.Demo;

/// <summary>The demonstration accounts the master data is attributed to (real users; the platform's authorization still refuses them until their one-time password is replaced).</summary>
public sealed record DemoMasterActors(long PlanningUserId, long WarehouseUserId, long ProductionUserId, long SupervisorUserId);

/// <summary>What one master-data seeding run did. Running it again on a seeded database adds nothing.</summary>
public sealed record DemoMasterSummary(int Added, int Existing);

/// <summary>
/// Fictional master data for the end-to-end demonstration (docs/DEMO_WALKTHROUGH.md): two production units with a machine and a rate each, the day shift, a material with a price, an operator linked to
/// the demonstration production account and that account's designation to the milling unit. Like the sample requests it is written through the repositories and the Domain with real users and an audit
/// entry for each row (the application services refuse a demonstration account until it has replaced its one-time password, and the seed does not go around that rule). The time is fixed, so a reset gives
/// the same rows. Everything that is a decision of the owner stays empty on purpose: no overtime limits (Planning must configure them), no unit supervisor (Planning reviews reports as the flagged
/// fallback), no worker schedule.
/// </summary>
public sealed class DemoMasterDataSeeder(
    IPlanningMasterRepository planning,
    ISchedulingRepository scheduling,
    IMaterialRepository materials,
    IOperatorRepository operators,
    IUnitQueueRepository unitQueue,
    IJalaliDateService dates,
    DemoMasterActors actors,
    DateTime atUtc)
{
    public const string MillingCode = "MILLING";

    public const string TurningCode = "TURNING";

    public const string MaterialCode = "ST37-R30";

    public const string OperatorCode = "OP-DEMO";

    private int _added;
    private int _existing;

    public async Task<DemoMasterSummary> SeedAsync(CancellationToken cancellationToken = default)
    {
        var effectiveFrom = dates.TryParseDate("1405/01/01", out var jalali, out _) ? dates.ToGregorian(jalali) : throw new InvalidOperationException("The demonstration rate date is invalid.");

        var storedUnits = await planning.ListUnitsAsync(cancellationToken);
        var units = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (code, name) in new[] { (MillingCode, "فرزکاری " + DemoDataSeeder.DemoNameSuffix), (TurningCode, "تراشکاری " + DemoDataSeeder.DemoNameSuffix) })
        {
            var existing = storedUnits.FirstOrDefault(u => string.Equals(u.Value.Code, code, StringComparison.Ordinal));
            units[code] = existing?.Value.Id ?? (await planning.AddUnitAsync(new ProductionUnit(0, code, name, actors.PlanningUserId, atUtc), [Audit(actors.PlanningUserId, AuditActions.ProductionUnitAdded, "ProductionUnit", code, $"Production unit {code} registered (demonstration)")], cancellationToken)).Value.Id;
            Count(existing is not null);
        }

        var storedMachines = await planning.ListMachinesAsync(cancellationToken);
        var rates = await planning.ListRatesAsync(cancellationToken);
        foreach (var (code, name, unit, rate) in new[] { ("FCC-06", "فرز CNC " + DemoDataSeeder.DemoNameSuffix, MillingCode, 1_200_000m), ("TCC-12", "تراش CNC " + DemoDataSeeder.DemoNameSuffix, TurningCode, 900_000m) })
        {
            var existing = storedMachines.FirstOrDefault(m => string.Equals(m.Value.Code, code, StringComparison.Ordinal));
            var machineId = existing?.Value.Id
                ?? (await planning.AddMachineAsync(new Machine(0, code, name, units[unit], actors.PlanningUserId, atUtc), [Audit(actors.PlanningUserId, AuditActions.MachineAdded, "Machine", code, $"Machine {code} registered (demonstration)")], cancellationToken)).Value.Id;
            Count(existing is not null);
            if (rates.Any(r => r.MachineId == machineId))
            {
                Count(true);
                continue;
            }

            await planning.AddRateAsync(
                new MachineHourlyRate(0, machineId, rate, effectiveFrom, "نرخ نمونه", actors.PlanningUserId, atUtc),
                [Audit(actors.PlanningUserId, AuditActions.MachineRateChanged, "MachineHourlyRate", code, $"Rate of {code} set to {rate:0} Rial per hour from the 1405/01/01 (demonstration)")],
                cancellationToken);
            Count(false);
        }

        var calendar = await scheduling.LoadConfigurationAsync(cancellationToken);
        var template = calendar.Templates.FirstOrDefault(t => string.Equals(t.Value.Code, "D", StringComparison.Ordinal));
        var templateId = template?.Value.Id
            ?? (await scheduling.AddTemplateAsync(
                new ShiftTemplate(0, "D", "روزکار " + DemoDataSeeder.DemoNameSuffix, new TimeOnly(8, 0), new TimeOnly(17, 0), new TimeOnly(12, 0), new TimeOnly(13, 0), actors.PlanningUserId, atUtc),
                Audit(actors.PlanningUserId, AuditActions.ShiftTemplateAdded, "ShiftTemplate", "D", "Shift D registered (demonstration)"),
                cancellationToken)).Value.Id;
        Count(template is not null);
        foreach (var (code, unitId) in units)
        {
            if (calendar.UnitShifts.Any(u => u.Value.ProductionUnitId == unitId && u.Value.ShiftTemplateId == templateId))
            {
                Count(true);
                continue;
            }

            await scheduling.AddUnitShiftAsync(
                new UnitShift(0, unitId, templateId, 3, 1, false, actors.PlanningUserId, atUtc),
                Audit(actors.PlanningUserId, AuditActions.UnitShiftAdded, "UnitShift", $"{code}/D", $"Unit {code} runs shift D with capacity 1..3 (demonstration)"),
                cancellationToken);
            Count(false);
        }

        var storedMaterials = await materials.ListMaterialsAsync(cancellationToken);
        var material = storedMaterials.FirstOrDefault(m => string.Equals(m.Value.Code, MaterialCode, StringComparison.Ordinal));
        var materialId = material?.Value.Id
            ?? (await materials.AddMaterialAsync(
                new Material(0, MaterialCode, "میلگرد ST37 " + DemoDataSeeder.DemoNameSuffix, "ST37", "میلگرد", actors.WarehouseUserId, atUtc),
                [Audit(actors.WarehouseUserId, AuditActions.MaterialAdded, "Material", MaterialCode, $"Material {MaterialCode} registered (demonstration)")],
                cancellationToken)).Value.Id;
        Count(material is not null);
        if ((await materials.ListPricesAsync(cancellationToken)).Any(p => p.MaterialId == materialId))
        {
            Count(true);
        }
        else
        {
            await materials.AddPriceAsync(
                new MaterialPrice(0, materialId, 90_000m, effectiveFrom, "قیمت نمونه", actors.WarehouseUserId, atUtc),
                [Audit(actors.WarehouseUserId, AuditActions.MaterialPriceChanged, "MaterialPrice", MaterialCode, $"Price of {MaterialCode} set to 90000 Rial per kg from the 1405/01/01 (demonstration)")],
                cancellationToken);
            Count(false);
        }

        var person = (await operators.ListOperatorsAsync(cancellationToken)).FirstOrDefault(o => string.Equals(o.Value.Code, OperatorCode, StringComparison.Ordinal));
        var operatorId = person?.Value.Id;
        if (person is null)
        {
            operatorId = (await operators.AddOperatorAsync(
                new Operator(0, OperatorCode, "اپراتور نمونه " + DemoDataSeeder.DemoNameSuffix, actors.ProductionUserId, actors.PlanningUserId, atUtc),
                [Audit(actors.PlanningUserId, AuditActions.OperatorRegistered, "Operator", OperatorCode, $"Operator {OperatorCode} registered and linked to the demonstration production account")],
                cancellationToken)).Value.Id;
        }

        Count(person is not null);
        if ((await unitQueue.ListDesignationsAsync(cancellationToken)).Any(d => d.Value.IsActive && d.Value.UserId == actors.ProductionUserId && d.Value.ProductionUnitId == units[MillingCode]))
        {
            Count(true);
        }
        else
        {
            await unitQueue.AssignDesignationAsync(
                actors.ProductionUserId, units[MillingCode], actors.PlanningUserId, atUtc,
                Audit(actors.PlanningUserId, AuditActions.UnitDesignationAssigned, "UserUnitDesignation", $"{actors.ProductionUserId}/{MillingCode}", "The demonstration production account designated to the milling unit"),
                cancellationToken);
            Count(false);
        }

        // The work-session demonstration (docs/DEMO_WALKTHROUGH.md): the operator is assigned to and qualified on the milling machine, and a separate demonstration account is the milling unit's supervisor, who
        // dispatches. The supervisor is a demonstration choice; who supervises a real unit is the owner's data. The worker schedule stays empty (Planning schedules the day, or the supervisor records the reason).
        var milling = units[MillingCode];
        var machine = (await planning.ListMachinesAsync(cancellationToken)).First(m => string.Equals(m.Value.Code, "FCC-06", StringComparison.Ordinal)).Value.Id;
        if ((await operators.ListAssignmentsAsync(cancellationToken)).Any(a => a.Value.OperatorId == operatorId && a.Value.MachineId == machine))
        {
            Count(true);
        }
        else
        {
            await operators.AddAssignmentAsync(
                new OperatorMachineAssignment(0, operatorId!.Value, machine, effectiveFrom, true, "تخصیص نمونه", actors.PlanningUserId, atUtc),
                [Audit(actors.PlanningUserId, AuditActions.OperatorMachineAssigned, "OperatorMachineAssignment", $"{OperatorCode}/FCC-06", "Operator OP-DEMO assigned to FCC-06 (demonstration)")],
                cancellationToken);
            Count(false);
        }

        if ((await operators.ListQualificationsAsync(cancellationToken)).Any(q => q.Value.OperatorId == operatorId && q.Value.MachineId == machine))
        {
            Count(true);
        }
        else
        {
            await operators.AddQualificationAsync(
                new OperatorMachineQualification(0, operatorId!.Value, machine, effectiveFrom, "صلاحیت نمونه", actors.PlanningUserId, atUtc),
                [Audit(actors.PlanningUserId, AuditActions.OperatorQualificationRecorded, "OperatorMachineQualification", $"{OperatorCode}/FCC-06", "Operator OP-DEMO qualified on FCC-06 (demonstration)")],
                cancellationToken);
            Count(false);
        }

        var supervisorRow = (await unitQueue.ListDesignationsAsync(cancellationToken)).FirstOrDefault(d => d.Value.IsActive && d.Value.UserId == actors.SupervisorUserId && d.Value.ProductionUnitId == milling);
        if (supervisorRow is null)
        {
            supervisorRow = await unitQueue.AssignDesignationAsync(
                actors.SupervisorUserId, milling, actors.PlanningUserId, atUtc,
                Audit(actors.PlanningUserId, AuditActions.UnitDesignationAssigned, "UserUnitDesignation", $"{actors.SupervisorUserId}/{MillingCode}", "The demonstration supervisor account designated to the milling unit"),
                cancellationToken);
            Count(false);
        }
        else
        {
            Count(true);
        }

        if (supervisorRow.Value.IsSupervisor)
        {
            Count(true);
        }
        else
        {
            await unitQueue.SetSupervisorAsync(
                supervisorRow.Value.Id, supervisorRow.Version, true,
                Audit(actors.PlanningUserId, AuditActions.UnitSupervisorChanged, "UserUnitDesignation", $"{actors.SupervisorUserId}/{MillingCode}", "The demonstration supervisor account marked as supervisor of the milling unit"),
                cancellationToken);
            Count(false);
        }

        return new DemoMasterSummary(_added, _existing);
    }

    private AuditLogEntry Audit(long userId, string code, string targetType, string targetId, string summary) =>
        new(atUtc, userId, "DEMO-SEED", "demo.seed", null, code, targetType, targetId, summary);

    private void Count(bool existed)
    {
        if (existed)
        {
            _existing++;
        }
        else
        {
            _added++;
        }
    }
}
