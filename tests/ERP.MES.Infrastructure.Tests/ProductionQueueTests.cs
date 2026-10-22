using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// The read-only production overview for Management and Planning (docs/PRODUCTION_QUEUE.md, Decision Log #78) on a real SQLite file with the real authorization. It changes
// nothing; its display order is a provisional, deterministic order and not the authoritative operational queue order.
public class ProductionQueueTests
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

        public required Principal Qc { get; init; }

        public required WorkflowServices Services { get; init; }

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
        var fixture = await SecurityFixture.CreateAsync();
        var admin = await fixture.BootstrapAdminAsync(cancellationToken: Token);
        return new World
        {
            Fixture = fixture,
            Admin = admin,
            Central = await UserAsync(fixture, admin, "central.one", DepartmentCodes.Central),
            Planning = await UserAsync(fixture, admin, "planning.one", DepartmentCodes.Planning),
            Drawing = await UserAsync(fixture, admin, "drawing.one", DepartmentCodes.Drawing),
            Management = await UserAsync(fixture, admin, "management.one", DepartmentCodes.Management),
            Qc = await UserAsync(fixture, admin, "qc.one", DepartmentCodes.Qc),
            Services = WorkflowServices.Create(fixture.Db.ContextFactory, fixture.Services.Authorization, PersistenceTestData.Dates, fixture.Clock),
        };
    }

    private static async Task<long> CustomerAsync(World w, string name = "Fictional Customer A")
    {
        var outcome = await w.Services.Customers.CreateAsync(w.Central, name, Token);
        Assert.True(outcome.Succeeded, outcome.Status.ToString());
        return outcome.Value!.Value.Id;
    }

    private static async Task<long> RequestAsync(World w, long customerId, string requestNo, string date)
    {
        var outcome = await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customerId, requestNo, date, "Fictional request", null), Token);
        Assert.True(outcome.Succeeded, outcome.Status + " " + outcome.Issue);
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        return outcome.Value!.Value.Id;
    }

    private static async Task AddItemAsync(World w, long requestId, string itemNo, WorkType type, Priority priority, decimal quantity = 10m)
    {
        var current = (await w.Services.Requests.GetAsync(w.Planning, requestId, Token)).Value!;
        var added = await w.Services.Requests.AddItemAsync(w.Planning, requestId, current.Request.Version, new AddItemCommand(itemNo, "Part " + itemNo, type, quantity, priority), Token);
        Assert.True(added.Succeeded, added.Status + " " + added.Issue);
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
    }

    private static Task ChangeItemAsync(World w, long requestId, string itemNo, Action<RequestItem> change) =>
        w.Fixture.Db.Requests().UpdateRowsAsync(requestId, RowVersionExpectations.None, r => change(r.Items.Single(i => i.ItemNo == itemNo)), Token);

    private static async Task DefineRouteAsync(World w, long requestId, string itemNo, params decimal[] minutesPerPiece)
    {
        await ChangeItemAsync(w, requestId, itemNo, item =>
        {
            for (var i = 0; i < minutesPerPiece.Length; i++)
            {
                item.DefineInitialStage("Stage " + (i + 1), minutesPerPiece[i]);
            }

            item.ReleaseFirstStage();
        });
    }

    private static Task ApproveAsync(World w, long requestId, string itemNo, int stageNo) =>
        ChangeItemAsync(w, requestId, itemNo, item =>
        {
            item.StartStage(stageNo);
            item.SubmitStageForQc(stageNo);
            item.ApproveStageWholeQuantity(stageNo, w.Qc.UserId, w.Fixture.Clock.UtcNow);
        });

    private static async Task<IReadOnlyList<ProductionQueueEntry>> LoadAsync(World w, Principal? user = null)
    {
        var outcome = await w.Services.Queue.LoadAsync(user ?? w.Management, Token);
        Assert.True(outcome.Succeeded, outcome.Status.ToString());
        return outcome.Value!.Entries;
    }

    [Fact]
    public async Task Management_sees_every_item_with_request_customer_type_priority_and_status()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w, "Fictional Customer A");
        var request = await RequestAsync(w, customer, "wsj-898", "1405/07/20");
        await AddItemAsync(w, request, "1.10", WorkType.Make, Priority.Urgent, 10m);
        await AddItemAsync(w, request, "2.10", WorkType.Repair, Priority.Normal, 4m);

        var entries = await LoadAsync(w);

        Assert.Equal(2, entries.Count);
        var make = entries.Single(e => e.ItemNo == "1.10");
        Assert.Equal(request, make.RequestId);
        Assert.Equal("wsj-898", make.RequestNo);
        Assert.Equal(customer, make.CustomerId);
        Assert.Equal("Fictional Customer A", make.CustomerName);
        Assert.Equal(RequestStatus.Created, make.RequestStatus);
        Assert.Equal(WorkType.Make, make.WorkType);
        Assert.Equal(Priority.Urgent, make.Priority);
        Assert.Equal(10m, make.RequestedQuantity);
        Assert.Equal(WorkType.Repair, entries.Single(e => e.ItemNo == "2.10").WorkType);
        Assert.Equal(Priority.Normal, entries.Single(e => e.ItemNo == "2.10").Priority);
    }

    [Fact]
    public async Task The_current_stage_is_the_first_stage_not_yet_approved_and_the_qc_position_and_estimates_follow_from_the_route()
    {
        using var w = await StartAsync();
        var request = await RequestAsync(w, await CustomerAsync(w), "R-1", "1405/07/20");
        await AddItemAsync(w, request, "open", WorkType.Make, Priority.Normal, 10m);
        await AddItemAsync(w, request, "waiting-qc", WorkType.Make, Priority.Normal, 2m);
        await AddItemAsync(w, request, "done", WorkType.Repair, Priority.Normal, 1m);
        await AddItemAsync(w, request, "no-route", WorkType.Repair, Priority.Normal, 1m);

        await DefineRouteAsync(w, request, "open", 5m, 3m, 1.5m);
        await ApproveAsync(w, request, "open", 1);
        await DefineRouteAsync(w, request, "waiting-qc", 4m);
        await ChangeItemAsync(w, request, "waiting-qc", i => { i.StartStage(1); i.SubmitStageForQc(1); });
        await DefineRouteAsync(w, request, "done", 2m);
        await ApproveAsync(w, request, "done", 1);

        var entries = await LoadAsync(w);

        var open = entries.Single(e => e.ItemNo == "open");
        Assert.Equal(QueueRouteState.Open, open.RouteState);
        Assert.Equal(3, open.StageCount);
        Assert.Equal(1, open.ApprovedStageCount);
        Assert.Equal(2, open.CurrentStage!.StageNo);
        Assert.Equal("Stage 2", open.CurrentStage.Title);
        Assert.Equal(ItemStageStatus.Waiting, open.CurrentStage.Status);
        Assert.Equal(30m, open.CurrentStage.EstimatedMinutes);   // 3 minutes per piece x 10 pieces
        Assert.Equal(95m, open.TotalEstimatedMinutes);            // (5 + 3 + 1.5) x 10

        var waitingQc = entries.Single(e => e.ItemNo == "waiting-qc");
        Assert.Equal(ItemStageStatus.WaitingQc, waitingQc.CurrentStage!.Status);
        Assert.Equal(0, waitingQc.ApprovedStageCount);
        Assert.Equal(8m, waitingQc.CurrentStage.EstimatedMinutes);

        var done = entries.Single(e => e.ItemNo == "done");
        Assert.Equal(QueueRouteState.Completed, done.RouteState);
        Assert.Null(done.CurrentStage);
        Assert.Equal(1, done.ApprovedStageCount);
        Assert.Equal(2m, done.TotalEstimatedMinutes);

        var none = entries.Single(e => e.ItemNo == "no-route");
        Assert.Equal(QueueRouteState.NoRoute, none.RouteState);
        Assert.Null(none.CurrentStage);
        Assert.Null(none.TotalEstimatedMinutes);
    }

    [Fact]
    public async Task The_display_order_is_priority_then_older_request_date_then_request_then_item_and_is_the_same_on_every_load()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        var r1 = await RequestAsync(w, customer, "R-1", "1405/07/20");
        var r2 = await RequestAsync(w, customer, "R-2", "1405/07/10");
        var r3 = await RequestAsync(w, customer, "R-3", "1405/07/10");
        await AddItemAsync(w, r1, "a", WorkType.Make, Priority.Normal);
        await AddItemAsync(w, r1, "b", WorkType.Make, Priority.Urgent);
        await AddItemAsync(w, r1, "f", WorkType.Make, Priority.VeryUrgent);
        await AddItemAsync(w, r2, "c", WorkType.Make, Priority.Normal);
        await AddItemAsync(w, r2, "d", WorkType.Make, Priority.Urgent);
        await AddItemAsync(w, r2, "g", WorkType.Make, Priority.Urgent);
        await AddItemAsync(w, r3, "e", WorkType.Make, Priority.Urgent);

        var first = await LoadAsync(w);
        var second = await LoadAsync(w);

        // Very urgent first; among equal priorities the older request date first, then the request created first, then the item added first.
        Assert.Equal(new[] { "f", "d", "g", "e", "b", "c", "a" }, first.Select(e => e.ItemNo).ToArray());
        Assert.Equal(first.Select(e => e.ItemId), second.Select(e => e.ItemId));
    }

    [Fact]
    public void The_display_order_does_not_depend_on_the_order_the_rows_arrive_in()
    {
        ProductionQueueEntry Entry(long requestId, long itemId, Priority priority, DateOnly date) => new(
            requestId, "R", 1, "C", RequestStatus.Created, date, itemId, "i" + itemId, "p", WorkType.Make, 1m, priority, QueueRouteState.NoRoute, 0, 0, null, null, 1);

        var d1 = new DateOnly(2026, 10, 1);
        var d2 = new DateOnly(2026, 10, 2);
        var rows = new[]
        {
            Entry(2, 5, Priority.Normal, d1),
            Entry(1, 1, Priority.Urgent, d2),
            Entry(1, 2, Priority.Urgent, d2),
            Entry(3, 7, Priority.VeryUrgent, d2),
            Entry(2, 4, Priority.Urgent, d1),
        };
        var expected = new long[] { 7, 4, 1, 2, 5 };

        Assert.Equal(expected, ProductionQueueService.DisplayOrder(rows).Select(e => e.ItemId).ToArray());
        Assert.Equal(expected, ProductionQueueService.DisplayOrder(rows.Reverse()).Select(e => e.ItemId).ToArray());
        Assert.Equal(expected, ProductionQueueService.DisplayOrder(rows.OrderBy(r => r.ItemId % 3).ThenBy(r => r.ItemId)).Select(e => e.ItemId).ToArray());
    }

    [Fact]
    public async Task The_entries_carry_the_item_and_stage_row_versions_that_were_read()
    {
        using var w = await StartAsync();
        var request = await RequestAsync(w, await CustomerAsync(w), "R-1", "1405/07/20");
        await AddItemAsync(w, request, "1.10", WorkType.Make, Priority.Normal);
        await DefineRouteAsync(w, request, "1.10", 5m, 3m);

        var entry = (await LoadAsync(w)).Single();
        var stored = (await w.Fixture.Db.Requests().FindWithVersionsAsync(request, Token))!;
        var item = stored.Value.Items.Single();

        Assert.Equal(stored.Versions.ItemVersion(item.Id), entry.ItemVersion);
        Assert.Equal(stored.Versions.StageVersion(item.Id, 1), entry.CurrentStage!.Version);
    }

    [Fact]
    public async Task Management_and_planning_may_view_the_overview_and_every_other_department_is_denied_without_any_data()
    {
        using var w = await StartAsync();
        var request = await RequestAsync(w, await CustomerAsync(w), "R-1", "1405/07/20");
        await AddItemAsync(w, request, "1.10", WorkType.Make, Priority.Normal);

        foreach (var user in new[] { w.Management, w.Planning })
        {
            Assert.True(await w.Services.Queue.CanViewAsync(user, Token));
            var outcome = await w.Services.Queue.LoadAsync(user, Token);
            Assert.True(outcome.Succeeded);
            Assert.Single(outcome.Value!.Entries);
        }

        foreach (var user in new[] { w.Central, w.Drawing, w.Qc, w.Admin })
        {
            var outcome = await w.Services.Queue.LoadAsync(user, Token);
            Assert.Equal(OperationStatus.NotAuthorized, outcome.Status);
            Assert.Null(outcome.Value);
            Assert.False(await w.Services.Queue.CanViewAsync(user, Token));
        }

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.Queue.LoadAsync(null, Token)).Status);
    }

    [Fact]
    public async Task A_deactivated_management_or_planning_user_loses_access_at_the_next_load()
    {
        using var w = await StartAsync();
        Assert.True((await w.Services.Queue.LoadAsync(w.Management, Token)).Succeeded);
        Assert.True((await w.Services.Queue.LoadAsync(w.Planning, Token)).Succeeded);

        Assert.Equal(AccountStatus.Succeeded, (await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Management.UserId, false, Token)).Status);
        Assert.Equal(AccountStatus.Succeeded, (await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Planning.UserId, false, Token)).Status);

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.Queue.LoadAsync(w.Management, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.Queue.LoadAsync(w.Planning, Token)).Status);
        Assert.False(await w.Services.Queue.CanViewAsync(w.Planning, Token));
    }

    [Fact]
    public async Task A_forged_or_stale_principal_is_judged_by_the_persisted_department_not_by_what_it_claims()
    {
        using var w = await StartAsync();

        // The caller holds a principal claiming Management or Planning, but the persisted users are Drawing and QC users.
        var forgedManagement = new Principal(w.Drawing.UserId, w.Drawing.Username, w.Drawing.DisplayName, DepartmentCodes.Management, RoleCodes.Manager, false);
        var forgedPlanning = new Principal(w.Qc.UserId, w.Qc.Username, w.Qc.DisplayName, DepartmentCodes.Planning, RoleCodes.Staff, false);
        // And a stale principal that still says Drawing while the persisted user is Planning is granted what the DATABASE says, not what the snapshot says.
        var staleButNowPlanning = new Principal(w.Planning.UserId, w.Planning.Username, w.Planning.DisplayName, DepartmentCodes.Drawing, RoleCodes.Staff, false);

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.Queue.LoadAsync(forgedManagement, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.Queue.LoadAsync(forgedPlanning, Token)).Status);
        Assert.True((await w.Services.Queue.LoadAsync(staleButNowPlanning, Token)).Succeeded);
    }

    [Fact]
    public async Task A_user_whose_password_change_is_still_pending_sees_nothing()
    {
        using var w = await StartAsync();
        await w.Fixture.CreateUserAsync(w.Admin, "planning.pending", InitialPassword, DepartmentCodes.Planning, RoleCodes.Staff, Token);
        var pending = await w.Fixture.SignInAsync("planning.pending", InitialPassword, Token);

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.Queue.LoadAsync(pending, Token)).Status);
    }

    [Fact]
    public async Task An_empty_database_gives_an_empty_queue_not_an_error()
    {
        using var w = await StartAsync();
        await CustomerAsync(w);

        var outcome = await w.Services.Queue.LoadAsync(w.Management, Token);

        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Value!.Entries);
    }

    [Fact]
    public async Task Loading_the_queue_changes_nothing()
    {
        using var w = await StartAsync();
        var request = await RequestAsync(w, await CustomerAsync(w), "R-1", "1405/07/20");
        await AddItemAsync(w, request, "1.10", WorkType.Make, Priority.Urgent);
        await DefineRouteAsync(w, request, "1.10", 5m, 3m);
        string[] Snapshot() => w.Fixture.Db.Query(
            "SELECT 'R', Id, Version, UpdatedAtUtc FROM Requests UNION ALL SELECT 'I', Id, Version, UpdatedAtUtc FROM RequestItems "
            + "UNION ALL SELECT 'S', RequestItemId, StageNo, Version || '/' || Status FROM ItemStages "
            + "UNION ALL SELECT 'Q', RequestItemId, StageNo, ApprovedAtUtc FROM QcApprovals ORDER BY 1, 2, 3, 4").ToArray();
        var before = Snapshot();

        await LoadAsync(w);
        await LoadAsync(w);

        Assert.Equal(before, Snapshot());
    }

    // ---- failures of the persistence layer become statuses, not exceptions ----

    private sealed class FailingRepository(Exception failure) : IRequestRepository
    {
        public Task<Stored<Request>> AddAsync(Request request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Stored<Request>?> FindAsync(long requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> ExistsAsync(RequestKey key, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<RequestNo>> FindCaseInsensitiveNearDuplicatesAsync(long customerId, int jalaliRequestYear, RequestNo requestNo, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RequestSummary>> ListAsync(long? customerId, CancellationToken cancellationToken = default) => throw failure;

        public Task<Stored<Request>> UpdateAsync(long requestId, long? expectedVersion, Action<Request> change, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<VersionedRequest?> FindWithVersionsAsync(long requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<VersionedRequest> UpdateRowsAsync(long requestId, RowVersionExpectations expected, Action<Request> change, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    [Theory]
    [InlineData(typeof(PersistenceException), OperationStatus.Failed)]
    [InlineData(typeof(ConcurrencyConflictException), OperationStatus.ConcurrencyConflict)]
    public async Task A_persistence_failure_while_reading_becomes_a_status(Type failure, OperationStatus expected)
    {
        using var w = await StartAsync();
        var exception = failure == typeof(PersistenceException) ? new PersistenceException("database error") : (Exception)new ConcurrencyConflictException("conflict");
        var service = new ProductionQueueService(new FailingRepository(exception), w.Fixture.Services.Authorization);

        var outcome = await service.LoadAsync(w.Management, Token);

        Assert.Equal(expected, outcome.Status);
        Assert.Null(outcome.Value);
    }

    [Fact]
    public async Task A_denied_user_is_refused_before_the_repository_is_touched()
    {
        using var w = await StartAsync();
        var service = new ProductionQueueService(new FailingRepository(new InvalidOperationException("must not be reached")), w.Fixture.Services.Authorization);

        Assert.Equal(OperationStatus.NotAuthorized, (await service.LoadAsync(w.Drawing, Token)).Status);
    }
}
