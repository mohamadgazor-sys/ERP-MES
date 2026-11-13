using ERP.MES.Infrastructure.Sqlite.Demo;

namespace ERP.MES.Infrastructure.Tests;

// The management-demonstration scenario (owner request of 2026-11-13) is created by the real services during demo-init: it must complete, be coherent and leave the accounts' password state as it was.
public class DemoScenarioTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static long Count(TempDatabase db, string tableAndWhere) => long.Parse(db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Demo_init_creates_the_management_scenario_through_the_real_services_and_a_second_init_adds_nothing()
    {
        using var db = new TempDatabase();
        var result = await DemoDatabase.InitializeAsync(db.Path, PersistenceTestData.Dates, db.NextCredentialPath(), SecurityFixture.FastPolicy, Token);

        Assert.True(result.Seed.ScenarioWarning is null, result.Seed.ScenarioWarning);
        foreach (var number in new[] { "DEMO-1405-101", "DEMO-1405-102", "DEMO-1405-103" })
        {
            Assert.Equal(1, Count(db, $"Requests WHERE RequestNo = '{number}'"));
        }

        // The MAKE and the REPAIR request were released with the single action: it is in the workflow history with its actor, and audited.
        Assert.Equal(2, Count(db, "RequestWorkflowEntries WHERE Action = 8"));
        Assert.Equal(2, Count(db, "AuditLog WHERE ActionCode = 'RequestReleasedToProduction'"));
        Assert.True(Count(db, "AuditLog WHERE ActionCode = 'StageReleased'") >= 4);

        // Work session with a pause, QC acceptance, external dispatch/return/missing, downtime, calendar closure and overtime are all present.
        Assert.Equal(1, Count(db, "AuditLog WHERE ActionCode = 'WorkSessionPaused'"));
        Assert.True(Count(db, "AuditLog WHERE ActionCode = 'WorkSessionDispatched'") >= 1);
        Assert.Equal(1, Count(db, "ExternalShipments WHERE Kind = 1"));
        Assert.Equal(1, Count(db, "ExternalShipments WHERE Kind = 2"));
        Assert.Equal(1, Count(db, "ExternalShipments WHERE Kind = 3"));
        Assert.Equal(1, Count(db, "ItemStages WHERE Status = 'WAITING_SHORTFALL_DECISION' OR Status = 11 OR ExternalMissingBaseUnits = 3"));
        Assert.Equal(2, Count(db, "MachineDowntimes"));
        Assert.True(Count(db, "CalendarExceptions") >= 1);
        Assert.Equal(1, Count(db, "OvertimeAssignments"));

        // The accounts still must change their one-time password (the scenario lifted the flag only while it ran).
        Assert.Equal(Count(db, "UserCredentials"), Count(db, "UserCredentials WHERE MustChangePassword = 1"));

        var requests = Count(db, "Requests");
        var again = await DemoDatabase.InitializeAsync(db.Path, PersistenceTestData.Dates, db.NextCredentialPath(), SecurityFixture.FastPolicy, Token);
        Assert.True(again.Seed.ScenarioWarning is null, again.Seed.ScenarioWarning);
        Assert.Equal(requests, Count(db, "Requests"));
    }
}
