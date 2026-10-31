using ERP.MES.Infrastructure.Demo;
using ERP.MES.Infrastructure.Persistence;
using ERP.MES.Infrastructure.Sqlite.Demo;

namespace ERP.MES.Infrastructure.Tests;

// The fictional master data of the demonstration database (docs/DEMO_WALKTHROUGH.md; Decision Log #108): complete, repeatable, reset-stable, attributed to real users and free of owner decisions.
public class DemoMasterDataTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static long Count(TempDatabase db, string tableAndWhere) => long.Parse(db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);

    private static Task<DemoDatabaseResult> Init(TempDatabase db) =>
        DemoDatabase.InitializeAsync(db.Path, PersistenceTestData.Dates, db.NextCredentialPath(), SecurityFixture.FastPolicy, Token);

    private static List<string> MasterSnapshot(TempDatabase db)
    {
        var rows = new List<string>();
        foreach (var table in new[] { "StageTitles", "ProductionUnits", "Machines", "MachineHourlyRates", "ShiftTemplates", "UnitShifts", "Materials", "MaterialPrices", "Operators", "UserUnitDesignations" })
        {
            rows.Add("# " + table);
            rows.AddRange(db.Query("SELECT * FROM " + table + " ORDER BY 1, 2"));
        }

        rows.AddRange(db.Query("SELECT ActionCode || '|' || TargetType || '|' || TargetId || '|' || OccurredAtUtc FROM AuditLog WHERE MachineName = 'DEMO-SEED' ORDER BY Id"));
        return rows;
    }

    [Fact]
    public async Task A_new_demonstration_database_has_the_units_machines_rates_shift_material_operator_and_designation_the_walkthrough_needs()
    {
        using var db = new TempDatabase();

        await Init(db);

        Assert.Equal(new[] { "MILLING", "TURNING" }, db.Query("SELECT Code FROM ProductionUnits ORDER BY Code"));
        Assert.Equal(new[] { "FCC-06|MILLING", "TCC-12|TURNING" }, db.Query("SELECT m.Code || '|' || u.Code FROM Machines m JOIN ProductionUnits u ON u.Id = m.ProductionUnitId ORDER BY m.Code"));
        Assert.Equal(new[] { "FCC-06|1200000.0|2026-03-21", "TCC-12|900000.0|2026-03-21" }, db.Query("SELECT m.Code || '|' || r.RatePerHourRial || '|' || r.EffectiveFrom FROM MachineHourlyRates r JOIN Machines m ON m.Id = r.MachineId ORDER BY m.Code"));
        Assert.Equal(new[] { "D|08:00:00|17:00:00|12:00:00|13:00:00" }, db.Query("SELECT Code || '|' || StartTime || '|' || EndTime || '|' || BreakStart || '|' || BreakEnd FROM ShiftTemplates"));
        Assert.Equal(2, Count(db, "UnitShifts"));
        Assert.Equal(new[] { "ST37-R30|90000.0" }, db.Query("SELECT m.Code || '|' || p.PricePerKgRial FROM MaterialPrices p JOIN Materials m ON m.Id = p.MaterialId"));
        Assert.Equal(new[] { "OP-DEMO|demo.production" }, db.Query("SELECT o.Code || '|' || u.Username FROM Operators o JOIN Users u ON u.Id = o.UserId"));
        Assert.Equal(new[] { "demo.production|MILLING|0", "demo.supervisor|MILLING|1" }, db.Query("SELECT u.Username || '|' || p.Code || '|' || d.IsSupervisor FROM UserUnitDesignations d JOIN Users u ON u.Id = d.UserId JOIN ProductionUnits p ON p.Id = d.ProductionUnitId WHERE d.RevokedAtUtc IS NULL ORDER BY u.Username"));
        Assert.Equal(new[] { "OP-DEMO|FCC-06" }, db.Query("SELECT o.Code || '|' || m.Code FROM OperatorMachineAssignments a JOIN Operators o ON o.Id = a.OperatorId JOIN Machines m ON m.Id = a.MachineId"));
        Assert.Equal(new[] { "OP-DEMO|FCC-06" }, db.Query("SELECT o.Code || '|' || m.Code FROM OperatorMachineQualifications q JOIN Operators o ON o.Id = q.OperatorId JOIN Machines m ON m.Id = q.MachineId"));
        Assert.All(db.Query("SELECT Name FROM ProductionUnits UNION ALL SELECT Name FROM Machines UNION ALL SELECT FullName FROM Operators"), name => Assert.EndsWith("(نمونه آزمایشی)", name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_choices_that_belong_to_the_owner_stay_empty_and_every_seeded_row_is_audited_under_a_real_user()
    {
        using var db = new TempDatabase();

        await Init(db);

        Assert.Equal(0, Count(db, "OvertimeLimits")); // Planning must configure the limits (D5)
        Assert.Equal(0, Count(db, "OvertimeAssignments"));
        Assert.Equal(0, Count(db, "WorkerSchedules"));
        Assert.Equal(1, Count(db, "UserUnitDesignations WHERE IsSupervisor = 1")); // the demonstration supervisor of MILLING (a demonstration choice; real supervisors are the owner's data)
        Assert.Equal(0, Count(db, "DailyJobReports"));
        Assert.Equal(0, Count(db, "ItemCostCalculations"));

        var seeded = db.Query("SELECT a.ActionCode || '|' || u.Username FROM AuditLog a JOIN Users u ON u.Id = a.UserId WHERE a.MachineName = 'DEMO-SEED' ORDER BY a.Id");
        Assert.Equal(
            new[]
            {
                "StageTitleAdded|demo.planning", "StageTitleAdded|demo.planning", "StageTitleAdded|demo.planning", "StageTitleAdded|demo.planning", "StageTitleAdded|demo.planning", "StageTitleAdded|demo.planning", "StageTitleAdded|demo.planning",
                "ProductionUnitAdded|demo.planning", "ProductionUnitAdded|demo.planning", "MachineAdded|demo.planning", "MachineRateChanged|demo.planning", "MachineAdded|demo.planning", "MachineRateChanged|demo.planning",
                "ShiftTemplateAdded|demo.planning", "UnitShiftAdded|demo.planning", "UnitShiftAdded|demo.planning", "MaterialAdded|demo.warehouse", "MaterialPriceChanged|demo.warehouse",
                "OperatorRegistered|demo.planning", "UnitDesignationAssigned|demo.planning", "OperatorMachineAssigned|demo.planning", "OperatorQualificationRecorded|demo.planning", "UnitDesignationAssigned|demo.planning", "UnitSupervisorChanged|demo.planning",
            },
            seeded);
    }

    [Fact]
    public async Task Seeding_the_master_data_again_adds_nothing_and_a_reset_gives_exactly_the_same_rows()
    {
        using var db = new TempDatabase();
        await Init(db);
        var before = MasterSnapshot(db);

        var services = ERP.MES.Infrastructure.Security.SecurityServices.Create(db.ContextFactory, SecurityFixture.FastPolicy);
        var provisioner = new DemoAccountProvisioner(services.Bootstrap, services.Accounts, db.ContextFactory);
        var actors = await provisioner.ResolveMasterActorsAsync(Token);
        var again = await new DemoMasterDataSeeder(
            new EfPlanningMasterRepository(db.ContextFactory), new EfSchedulingRepository(db.ContextFactory), new EfMaterialRepository(db.ContextFactory), new EfOperatorRepository(db.ContextFactory),
            new EfUnitQueueRepository(db.ContextFactory), PersistenceTestData.Dates, actors, new DateTime(2026, 10, 23, 5, 0, 0, DateTimeKind.Utc)).SeedAsync(Token);

        Assert.Equal(0, again.Added);
        Assert.Equal(24, again.Existing);
        Assert.Equal(before, MasterSnapshot(db));

        await DemoDatabase.ResetAsync(db.Path, PersistenceTestData.Dates, db.NextCredentialPath(), SecurityFixture.FastPolicy, Token);
        Assert.Equal(before, MasterSnapshot(db));
    }
}
