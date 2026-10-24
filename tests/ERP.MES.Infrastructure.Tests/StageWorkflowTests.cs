using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// The persistence guarantees of stage changes (Decision Log #77, #79, #109) on a real SQLite file: a change, its expected row versions and its audit entries are one transaction, a failure saves
// nothing, and a competing write is a conflict. The stage ACTIONS (permissions, gates, versions, audit) are covered on stages planned in production units by StageActionTests; the unit-less
// demonstration flow is retired and its refusal is tested here. The Domain's demonstration transitions stay (the sample data is built with them).
public class StageWorkflowTests
{
    private const string InitialPassword = "Staff-Pass-Phrase-2";
    private const string OwnPassword = "My-Own-Pass-Phrase-3";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class World : IDisposable
    {
        public required SecurityFixture Fixture { get; init; }

        public required Principal Admin { get; init; }

        public required Principal Central { get; init; }

        public required Principal Planning { get; init; }

        public required Principal Drawing { get; init; }

        public required Principal Management { get; init; }

        public required Principal Production { get; init; }

        public required Principal Qc { get; init; }

        public required WorkflowServices Services { get; init; }

        public StageWorkService Stages => Services.StageWork;

        public void Dispose() => Fixture.Dispose();
    }

    private static async Task<Principal> UserAsync(SecurityFixture fixture, Principal admin, string name, string department)
    {
        await fixture.CreateUserAsync(admin, name, InitialPassword, department, RoleCodes.Staff, Token);
        var first = await fixture.SignInAsync(name, InitialPassword, Token);
        Assert.Equal(AccountStatus.Succeeded, (await fixture.Services.Accounts.ChangeOwnPasswordAsync(first, InitialPassword, OwnPassword, Token)).Status);
        return await fixture.SignInAsync(name, OwnPassword, Token);
    }

    private static async Task<World> StartAsync()
    {
        var fixture = await SecurityFixture.CreateFastAsync();
        var admin = await fixture.BootstrapAdminAsync(cancellationToken: Token);
        return new World
        {
            Fixture = fixture,
            Admin = admin,
            Central = await UserAsync(fixture, admin, "central.one", DepartmentCodes.Central),
            Planning = await UserAsync(fixture, admin, "planning.one", DepartmentCodes.Planning),
            Drawing = await UserAsync(fixture, admin, "drawing.one", DepartmentCodes.Drawing),
            Management = await UserAsync(fixture, admin, "management.one", DepartmentCodes.Management),
            Production = await UserAsync(fixture, admin, "production.one", DepartmentCodes.Production),
            Qc = await UserAsync(fixture, admin, "qc.one", DepartmentCodes.Qc),
            Services = WorkflowServices.Create(fixture.Db.ContextFactory, fixture.Services.Authorization, PersistenceTestData.Dates, fixture.Clock, new FixedAuditContext()),
        };
    }

    /// <summary>One request with one MAKE item "1.10" (quantity 10) whose fixed route has the given minutes per piece; the first stage is still PLANNED.</summary>
    private static async Task<long> SeedAsync(World w, params decimal[] minutesPerPiece)
    {
        var customer = (await w.Services.Customers.CreateAsync(w.Central, "Fictional Customer A", Token)).Value!.Value.Id;
        var request = (await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer, "R-1", WorkType.Make, "1405/07/20", null, null), Token)).Value!.Value.Id;
        // Fixture seeding through the repository (the gated definition workflow is tested in ItemDefinitionTests).
        await w.Fixture.Db.Requests().UpdateRowsAsync(request, RowVersionExpectations.None, r => r.AddItem(0, "1.10", "Ring A", 10m, w.Planning.UserId, w.Fixture.Clock.UtcNow, Priority.Normal), Token);
        await w.Fixture.Db.Requests().UpdateRowsAsync(
            request,
            RowVersionExpectations.None,
            r =>
            {
                var item = r.Items.Single();
                for (var i = 0; i < minutesPerPiece.Length; i++)
                {
                    item.DefineInitialStage("Stage " + (i + 1), minutesPerPiece[i]);
                }
            },
            Token);
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        return request;
    }

    private static async Task<VersionedRequest> StoredAsync(World w, long requestId) => (await w.Fixture.Db.Requests().FindWithVersionsAsync(requestId, Token))!;

    private static ItemStage StageOf(VersionedRequest stored, int stageNo) => stored.Value.Items.Single().Stages.Single(s => s.StageNo == stageNo);

    private static string[] Snapshot(World w) => w.Fixture.Db.Query(
        "SELECT 'R', Id, Version, UpdatedAtUtc FROM Requests UNION ALL SELECT 'I', Id, Version, UpdatedAtUtc FROM RequestItems "
        + "UNION ALL SELECT 'S', RequestItemId, StageNo, Version || '/' || Status FROM ItemStages "
        + "UNION ALL SELECT 'Q', RequestItemId, StageNo, ApprovedByUserId || '/' || ApprovedAtUtc FROM QcApprovals "
        + "UNION ALL SELECT 'A', Id, 0, ActionCode FROM AuditLog WHERE ActionCode LIKE 'Stage%' ORDER BY 1, 2, 3, 4").ToArray();

    private static StageTarget TargetOf(VersionedRequest stored, int stageNo = 1)
    {
        var item = stored.Value.Items.Single();
        return new StageTarget(stored.Value.Id, item.Id, stageNo, stored.Versions.Request, stored.Versions.ItemVersion(item.Id), stored.Versions.Stages.Where(p => p.Key.ItemId == item.Id).ToDictionary(p => p.Key.StageNo, p => p.Value));
    }

    // ---- the retired demonstration flow ----

    [Fact]
    public async Task A_stage_without_a_production_unit_can_no_longer_be_released_started_or_submitted_and_nothing_changes()
    {
        using var w = await StartAsync();
        var request = await SeedAsync(w, 5m, 3m);
        var before = Snapshot(w);
        var target = TargetOf(await StoredAsync(w, request));

        foreach (var (user, action) in new[] { (w.Planning, StageAction.Release), (w.Production, StageAction.Start), (w.Production, StageAction.SubmitForQc) })
        {
            var outcome = await w.Services.StageWork.PerformAsync(user, action, target, Token);
            Assert.Equal((OperationStatus.ValidationFailed, ValidationIssue.StageUnitRequired), (outcome.Status, outcome.Issue));
        }

        Assert.Equal(before, Snapshot(w));
        Assert.Equal(ItemStageStatus.Planned, StageOf(await StoredAsync(w, request), 1).Status);

        // The permission is still checked first, and an unknown item is still reported as such.
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.StageWork.PerformAsync(w.Qc, StageAction.Release, target, Token)).Status);
        Assert.Equal(OperationStatus.NotFound, (await w.Services.StageWork.PerformAsync(w.Planning, StageAction.Release, target with { ItemId = target.ItemId + 999 }, Token)).Status);
    }

    // ---- failures leave no partial changes ----

    [Fact]
    public async Task A_failure_while_saving_the_audit_entry_rolls_back_the_stage_change_too()
    {
        using var w = await StartAsync();
        var request = await SeedAsync(w, 5m);
        var stored = await StoredAsync(w, request);
        var item = stored.Value.Items.Single();
        var before = Snapshot(w);

        // The audit entry names a user that does not exist, so the database refuses it; the stage change in the same transaction must not survive.
        var failure = await Assert.ThrowsAsync<PersistenceException>(() => w.Fixture.Db.Requests().UpdateRowsAsync(
            request,
            RowVersionExpectations.ForItem(item.Id, stored.Versions.ItemVersion(item.Id)),
            r => r.Items.Single().ReleaseFirstStage(),
            _ => [new AuditLogEntry(w.Fixture.Clock.UtcNow, 424242, "PC", "user", null, AuditActions.StageReleased, "ItemStage", "x/1", "must not be saved")],
            Token));

        Assert.NotNull(failure);
        Assert.Equal(before, Snapshot(w));
        Assert.Equal(ItemStageStatus.Planned, StageOf(await StoredAsync(w, request), 1).Status);
    }

    [Fact]
    public async Task A_change_that_throws_or_an_audit_factory_that_throws_saves_nothing()
    {
        using var w = await StartAsync();
        var request = await SeedAsync(w, 5m);
        var before = Snapshot(w);

        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Fixture.Db.Requests().UpdateRowsAsync(
            request, RowVersionExpectations.None, r => { r.Items.Single().ReleaseFirstStage(); throw new InvalidOperationException("after the change"); }, _ => [], Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => w.Fixture.Db.Requests().UpdateRowsAsync(
            request, RowVersionExpectations.None, r => r.Items.Single().ReleaseFirstStage(), _ => throw new InvalidOperationException("audit failed"), Token));

        Assert.Equal(before, Snapshot(w));
        Assert.Equal(ItemStageStatus.Planned, StageOf(await StoredAsync(w, request), 1).Status);
    }

    [Fact]
    public async Task A_competing_write_between_loading_and_saving_gives_a_conflict_with_no_partial_change_and_no_audit_row()
    {
        using var w = await StartAsync();
        var request = await SeedAsync(w, 5m, 3m);
        await w.Fixture.Db.Requests().UpdateRowsAsync(
            request,
            RowVersionExpectations.None,
            r =>
            {
                var item = r.Items.Single();
                item.ReleaseFirstStage();
                item.StartStage(1);
                item.SubmitStageForQc(1);
            },
            Token);
        var stored = await StoredAsync(w, request);
        var item = stored.Value.Items.Single();
        var auditRowsBefore = w.Fixture.Db.Query("SELECT COUNT(*) FROM AuditLog").Single();

        // The approval is applied by a competing user from inside the change, i.e. after our versions were compared but before we save.
        var competing = w.Fixture.Db.Requests();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => w.Fixture.Db.Requests().UpdateRowsAsync(
            request,
            RowVersionExpectations.FromLoaded(stored.Versions),
            r =>
            {
                competing.UpdateRowsAsync(request, RowVersionExpectations.None, c => c.Items.Single().ApproveStageWholeQuantity(1, w.Qc.UserId, w.Fixture.Clock.UtcNow), Token).GetAwaiter().GetResult();
                r.Items.Single().ApproveStageWholeQuantity(1, w.Qc.UserId, w.Fixture.Clock.UtcNow.AddMinutes(5));
            },
            _ => [new AuditLogEntry(w.Fixture.Clock.UtcNow, w.Qc.UserId, "PC", "user", null, AuditActions.StageQcApproved, "ItemStage", item.Id + "/1", "must not be saved")],
            Token));

        Assert.Equal(auditRowsBefore, w.Fixture.Db.Query("SELECT COUNT(*) FROM AuditLog").Single());
        Assert.Single((await StoredAsync(w, request)).Value.Items.Single().QcApprovals);
    }
}
