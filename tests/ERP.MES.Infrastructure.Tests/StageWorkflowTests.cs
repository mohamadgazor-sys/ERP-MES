using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// The limited, demonstration-only stage progression and QC approval (Decision Log #79) on a real SQLite file with the real authorization: release, start, submit for QC,
// whole-quantity QC approval, the QC gate, expected row versions, and the audit trail written in the same transaction.
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

    private static async Task<ProductionQueueEntry> EntryAsync(World w)
    {
        var view = await w.Stages.LoadAsync(w.Management, Token);
        Assert.True(view.Succeeded, view.Status.ToString());
        return view.Value!.Entries.Single();
    }

    private static async Task<Outcome<StageActionResult>> ActAsync(World w, Principal? user, StageAction action) =>
        await w.Stages.PerformAsync(user, action, StageTarget.From(await EntryAsync(w)), Token);

    private static async Task<StageActionResult> MustSucceedAsync(World w, Principal user, StageAction action)
    {
        var outcome = await ActAsync(w, user, action);
        Assert.True(outcome.Succeeded, action + ": " + outcome.Status + " " + outcome.Issue);
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        return outcome.Value!;
    }

    private static async Task<VersionedRequest> StoredAsync(World w, long requestId) => (await w.Fixture.Db.Requests().FindWithVersionsAsync(requestId, Token))!;

    private static ItemStage StageOf(VersionedRequest stored, int stageNo) => stored.Value.Items.Single().Stages.Single(s => s.StageNo == stageNo);

    private static string[] Snapshot(World w) => w.Fixture.Db.Query(
        "SELECT 'R', Id, Version, UpdatedAtUtc FROM Requests UNION ALL SELECT 'I', Id, Version, UpdatedAtUtc FROM RequestItems "
        + "UNION ALL SELECT 'S', RequestItemId, StageNo, Version || '/' || Status FROM ItemStages "
        + "UNION ALL SELECT 'Q', RequestItemId, StageNo, ApprovedByUserId || '/' || ApprovedAtUtc FROM QcApprovals "
        + "UNION ALL SELECT 'A', Id, 0, ActionCode FROM AuditLog WHERE ActionCode LIKE 'Stage%' ORDER BY 1, 2, 3, 4").ToArray();

    // ---- the complete approved sequence ----

    [Fact]
    public async Task The_complete_sequence_release_start_submit_and_qc_approval_walks_a_two_stage_item_to_completion()
    {
        using var w = await StartAsync();
        var request = await SeedAsync(w, 5m, 3m);
        Assert.Equal(ItemStageStatus.Planned, StageOf(await StoredAsync(w, request), 1).Status);
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, StageOf(await StoredAsync(w, request), 2).Status);

        var released = await MustSucceedAsync(w, w.Planning, StageAction.Release);
        Assert.Equal(ItemStageStatus.Waiting, released.NewStatus);
        var started = await MustSucceedAsync(w, w.Production, StageAction.Start);
        Assert.Equal(ItemStageStatus.InProgress, started.NewStatus);
        var submitted = await MustSucceedAsync(w, w.Production, StageAction.SubmitForQc);
        Assert.Equal(ItemStageStatus.WaitingQc, submitted.NewStatus);

        // The next stage stays locked until QC approves the preceding one.
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, StageOf(await StoredAsync(w, request), 2).Status);
        var approved = await MustSucceedAsync(w, w.Qc, StageAction.ApproveWholeQuantity);
        Assert.Equal(ItemStageStatus.QcApproved, approved.NewStatus);
        Assert.Equal(2, approved.NextStageNo);
        Assert.Equal(ItemStageStatus.Waiting, approved.NextStageNewStatus);

        var afterFirst = await StoredAsync(w, request);
        var approval = Assert.Single(afterFirst.Value.Items.Single().QcApprovals);
        Assert.Equal(1, approval.StageNo);
        Assert.Equal(10m, approval.ApprovedQuantity);   // whole planned quantity
        Assert.Equal(w.Qc.UserId, approval.ApprovedByUserId);
        Assert.Equal(ItemStageStatus.Waiting, StageOf(afterFirst, 2).Status);

        // The queue now shows stage 2 as the current stage; the second stage follows the same path.
        var entry = await EntryAsync(w);
        Assert.Equal(2, entry.CurrentStage!.StageNo);
        await MustSucceedAsync(w, w.Production, StageAction.Start);
        await MustSucceedAsync(w, w.Production, StageAction.SubmitForQc);
        var last = await MustSucceedAsync(w, w.Qc, StageAction.ApproveWholeQuantity);
        Assert.Null(last.NextStageNo);
        Assert.Null(last.NextStageNewStatus);

        var done = await StoredAsync(w, request);
        Assert.True(done.Value.Items.Single().IsRouteCompleted);
        Assert.Equal(2, done.Value.Items.Single().QcApprovals.Count);
        Assert.Equal(QueueRouteState.Completed, (await EntryAsync(w)).RouteState);
    }

    [Fact]
    public async Task The_results_are_visible_through_new_services_and_contexts()
    {
        using var w = await StartAsync();
        var request = await SeedAsync(w, 5m, 3m);
        await MustSucceedAsync(w, w.Planning, StageAction.Release);
        await MustSucceedAsync(w, w.Production, StageAction.Start);

        var restarted = WorkflowServices.Create(w.Fixture.Db.ContextFactory, w.Fixture.Services.Authorization, PersistenceTestData.Dates, w.Fixture.Clock, new FixedAuditContext());
        var view = await restarted.StageWork.LoadAsync(w.Production, Token);

        Assert.Equal(ItemStageStatus.InProgress, view.Value!.Entries.Single().CurrentStage!.Status);
        Assert.Equal(ItemStageStatus.InProgress, StageOf(await StoredAsync(w, request), 1).Status);
    }

    // ---- the QC gate ----

    [Fact]
    public async Task The_next_stage_cannot_be_started_or_approved_while_the_preceding_stage_is_not_approved()
    {
        using var w = await StartAsync();
        var request = await SeedAsync(w, 5m, 3m);
        await MustSucceedAsync(w, w.Planning, StageAction.Release);
        await MustSucceedAsync(w, w.Production, StageAction.Start);
        await MustSucceedAsync(w, w.Production, StageAction.SubmitForQc);
        var before = Snapshot(w);

        // A caller that names stage 2 with perfectly current versions still cannot pass the gate: stage 2 is WAITING_PREVIOUS_QC and stage 1 is not approved.
        var stored = await StoredAsync(w, request);
        var item = stored.Value.Items.Single();
        var stageVersions = stored.Versions.Stages.Where(p => p.Key.ItemId == item.Id).ToDictionary(p => p.Key.StageNo, p => p.Value);
        var forged = new StageTarget(request, item.Id, 2, stored.Versions.Request, stored.Versions.ItemVersion(item.Id), stageVersions);

        Assert.Equal(OperationStatus.InvalidState, (await w.Stages.PerformAsync(w.Production, StageAction.Start, forged, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Stages.PerformAsync(w.Production, StageAction.SubmitForQc, forged, Token)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await w.Stages.PerformAsync(w.Qc, StageAction.ApproveWholeQuantity, forged, Token)).Status);
        Assert.Equal(before, Snapshot(w));
        Assert.Equal(ItemStageStatus.WaitingPreviousQc, StageOf(await StoredAsync(w, request), 2).Status);
    }

    // ---- authorization ----

    [Fact]
    public async Task Each_action_is_allowed_only_for_its_department_and_a_refusal_changes_and_audits_nothing()
    {
        using var w = await StartAsync();
        await SeedAsync(w, 5m, 3m);
        var before = Snapshot(w);

        // Release needs Planning.
        foreach (var user in new[] { w.Production, w.Qc, w.Management, w.Central, w.Drawing, w.Admin })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await ActAsync(w, user, StageAction.Release)).Status);
        }

        Assert.Equal(before, Snapshot(w));
        await MustSucceedAsync(w, w.Planning, StageAction.Release);

        // Start and submit need Production.
        before = Snapshot(w);
        foreach (var user in new[] { w.Planning, w.Qc, w.Management, w.Central, w.Drawing, w.Admin })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await ActAsync(w, user, StageAction.Start)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await ActAsync(w, user, StageAction.SubmitForQc)).Status);
        }

        Assert.Equal(before, Snapshot(w));
        await MustSucceedAsync(w, w.Production, StageAction.Start);
        await MustSucceedAsync(w, w.Production, StageAction.SubmitForQc);

        // QC approval needs QC.
        before = Snapshot(w);
        foreach (var user in new[] { w.Planning, w.Production, w.Management, w.Central, w.Drawing, w.Admin })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await ActAsync(w, user, StageAction.ApproveWholeQuantity)).Status);
        }

        Assert.Equal(OperationStatus.NotAuthorized, (await ActAsync(w, null, StageAction.ApproveWholeQuantity)).Status);
        Assert.Equal(before, Snapshot(w));
    }

    [Fact]
    public async Task A_deactivated_user_loses_the_permission_at_once_and_a_forged_principal_is_judged_by_the_database()
    {
        using var w = await StartAsync();
        await SeedAsync(w, 5m);
        Assert.Equal(AccountStatus.Succeeded, (await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Planning.UserId, false, Token)).Status);
        var before = Snapshot(w);

        Assert.Equal(OperationStatus.NotAuthorized, (await ActAsync(w, w.Planning, StageAction.Release)).Status);

        // A principal that claims Planning but is the Drawing user in the database.
        var forged = new Principal(w.Drawing.UserId, w.Drawing.Username, w.Drawing.DisplayName, DepartmentCodes.Planning, RoleCodes.Staff, false);
        Assert.Equal(OperationStatus.NotAuthorized, (await ActAsync(w, forged, StageAction.Release)).Status);
        Assert.Equal(before, Snapshot(w));

        // Deactivated Production and QC users cannot continue either.
        var reactivated = await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Planning.UserId, true, Token);
        Assert.Equal(AccountStatus.Succeeded, reactivated.Status);
        await MustSucceedAsync(w, w.Planning, StageAction.Release);
        Assert.Equal(AccountStatus.Succeeded, (await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Production.UserId, false, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await ActAsync(w, w.Production, StageAction.Start)).Status);
    }

    [Fact]
    public async Task The_capabilities_and_the_available_actions_follow_the_department_and_the_stage_state()
    {
        using var w = await StartAsync();
        await SeedAsync(w, 5m, 3m);

        var planning = await w.Stages.GetCapabilitiesAsync(w.Planning, Token);
        var production = await w.Stages.GetCapabilitiesAsync(w.Production, Token);
        var qc = await w.Stages.GetCapabilitiesAsync(w.Qc, Token);
        var management = await w.Stages.GetCapabilitiesAsync(w.Management, Token);
        Assert.Equal(new StageWorkCapabilities(true, true, false, false), planning);
        Assert.Equal(new StageWorkCapabilities(true, false, true, false), production);
        Assert.Equal(new StageWorkCapabilities(true, false, false, true), qc);
        Assert.Equal(new StageWorkCapabilities(true, false, false, false), management);   // may view, may not act
        Assert.Equal(StageWorkCapabilities.None, await w.Stages.GetCapabilitiesAsync(w.Central, Token));
        Assert.Equal(StageWorkCapabilities.None, await w.Stages.GetCapabilitiesAsync(null, Token));

        var planned = await EntryAsync(w);
        Assert.Equal(new AvailableStageActions(true, false, false, false), StageWorkService.AvailableFor(planning, planned));
        Assert.Equal(AvailableStageActions.None, StageWorkService.AvailableFor(production, planned));
        await MustSucceedAsync(w, w.Planning, StageAction.Release);
        var waiting = await EntryAsync(w);
        Assert.Equal(new AvailableStageActions(false, true, false, false), StageWorkService.AvailableFor(production, waiting));
        Assert.Equal(AvailableStageActions.None, StageWorkService.AvailableFor(planning, waiting));
        await MustSucceedAsync(w, w.Production, StageAction.Start);
        Assert.Equal(new AvailableStageActions(false, false, true, false), StageWorkService.AvailableFor(production, await EntryAsync(w)));
        await MustSucceedAsync(w, w.Production, StageAction.SubmitForQc);
        Assert.Equal(new AvailableStageActions(false, false, false, true), StageWorkService.AvailableFor(qc, await EntryAsync(w)));
    }

    [Fact]
    public async Task Viewing_the_stage_work_needs_the_overview_permission_or_a_stage_permission()
    {
        using var w = await StartAsync();
        await SeedAsync(w, 5m);

        foreach (var user in new[] { w.Planning, w.Production, w.Qc, w.Management })
        {
            Assert.True((await w.Stages.LoadAsync(user, Token)).Succeeded);
        }

        foreach (var user in new[] { w.Central, w.Drawing, w.Admin })
        {
            var outcome = await w.Stages.LoadAsync(user, Token);
            Assert.Equal(OperationStatus.NotAuthorized, outcome.Status);
            Assert.Null(outcome.Value);
        }

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Stages.LoadAsync(null, Token)).Status);
    }

    // ---- invalid transitions and repeated actions ----

    [Fact]
    public async Task Invalid_transitions_are_refused_with_current_versions_and_change_nothing()
    {
        using var w = await StartAsync();
        await SeedAsync(w, 5m, 3m);
        var before = Snapshot(w);

        // PLANNED: only release is possible.
        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Production, StageAction.Start)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Production, StageAction.SubmitForQc)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Qc, StageAction.ApproveWholeQuantity)).Status);
        Assert.Equal(before, Snapshot(w));

        await MustSucceedAsync(w, w.Planning, StageAction.Release);
        before = Snapshot(w);
        // WAITING: not releasable again, not submittable, not approvable.
        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Planning, StageAction.Release)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Production, StageAction.SubmitForQc)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Qc, StageAction.ApproveWholeQuantity)).Status);
        Assert.Equal(before, Snapshot(w));

        await MustSucceedAsync(w, w.Production, StageAction.Start);
        before = Snapshot(w);
        // IN_PROGRESS: not startable again, not approvable before submission.
        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Production, StageAction.Start)).Status);
        Assert.Equal(OperationStatus.InvalidState, (await ActAsync(w, w.Qc, StageAction.ApproveWholeQuantity)).Status);
        Assert.Equal(before, Snapshot(w));
    }

    [Fact]
    public async Task A_repeated_action_with_the_old_view_is_a_conflict_and_never_runs_twice()
    {
        using var w = await StartAsync();
        var request = await SeedAsync(w, 5m);
        await MustSucceedAsync(w, w.Planning, StageAction.Release);
        await MustSucceedAsync(w, w.Production, StageAction.Start);
        await MustSucceedAsync(w, w.Production, StageAction.SubmitForQc);
        var target = StageTarget.From(await EntryAsync(w));

        var first = await w.Stages.PerformAsync(w.Qc, StageAction.ApproveWholeQuantity, target, Token);
        var second = await w.Stages.PerformAsync(w.Qc, StageAction.ApproveWholeQuantity, target, Token);   // a double click on the same loaded row

        Assert.True(first.Succeeded);
        Assert.Equal(OperationStatus.ConcurrencyConflict, second.Status);
        var stored = await StoredAsync(w, request);
        Assert.Single(stored.Value.Items.Single().QcApprovals);
        Assert.Equal(1, w.Fixture.Db.Query("SELECT COUNT(*) FROM AuditLog WHERE ActionCode = 'StageQcApproved'").Select(int.Parse).Single());
    }

    // ---- stale versions: request, item and stage ----

    [Fact]
    public async Task A_stale_stage_version_is_a_conflict_and_the_newer_state_survives()
    {
        using var w = await StartAsync();
        var request = await SeedAsync(w, 5m);
        await MustSucceedAsync(w, w.Planning, StageAction.Release);
        var stale = StageTarget.From(await EntryAsync(w));   // the view of a Production user who opened the screen while the stage was WAITING

        await MustSucceedAsync(w, w.Production, StageAction.Start);                                  // another Production user started it
        var before = Snapshot(w);
        var outcome = await w.Stages.PerformAsync(w.Production, StageAction.Start, stale, Token);   // and the first one clicks "start" on the old view

        Assert.Equal(OperationStatus.ConcurrencyConflict, outcome.Status);
        Assert.Equal(ItemStageStatus.InProgress, StageOf(await StoredAsync(w, request), 1).Status);
        Assert.Equal(before, Snapshot(w));
    }

    [Fact]
    public async Task A_stale_item_version_is_a_conflict_even_when_the_stage_itself_did_not_change()
    {
        using var w = await StartAsync();
        var request = await SeedAsync(w, 5m);
        var stale = StageTarget.From(await EntryAsync(w));

        // Planning changes the item's priority after the Planning user loaded the screen: the item row has a new version, the stage row has not changed.
        await w.Fixture.Db.Requests().UpdateRowsAsync(request, RowVersionExpectations.None, r => Assert.True(r.Items.Single().ChangePriority(Priority.VeryUrgent, w.Fixture.Clock.UtcNow)), Token);
        var before = Snapshot(w);

        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Stages.PerformAsync(w.Planning, StageAction.Release, stale, Token)).Status);
        Assert.Equal(before, Snapshot(w));
        Assert.Equal(ItemStageStatus.Planned, StageOf(await StoredAsync(w, request), 1).Status);

        // With a fresh view the same action works.
        Assert.True((await ActAsync(w, w.Planning, StageAction.Release)).Succeeded);
    }

    [Fact]
    public async Task A_stale_request_version_is_a_conflict()
    {
        using var w = await StartAsync();
        var request = await SeedAsync(w, 5m);
        var stale = StageTarget.From(await EntryAsync(w));

        // Another user adds an item: the request row has a new version.
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await w.Fixture.Db.Requests().UpdateRowsAsync(request, RowVersionExpectations.None, r => r.AddItem(0, "2.10", "Shaft B", 1m, w.Planning.UserId, w.Fixture.Clock.UtcNow), Token);
        var before = Snapshot(w);

        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Stages.PerformAsync(w.Planning, StageAction.Release, stale, Token)).Status);
        Assert.Equal(before, Snapshot(w));
    }

    [Fact]
    public async Task A_stale_previous_or_next_stage_version_rejects_the_whole_operation()
    {
        using var w = await StartAsync();
        var request = await SeedAsync(w, 5m, 3m);
        await MustSucceedAsync(w, w.Planning, StageAction.Release);
        await MustSucceedAsync(w, w.Production, StageAction.Start);
        await MustSucceedAsync(w, w.Production, StageAction.SubmitForQc);
        var entry = await EntryAsync(w);
        var good = StageTarget.From(entry);
        var before = Snapshot(w);

        // The approval relies on stage 1 (current) and changes stage 2 (the next one): a wrong version for stage 2 rejects it, and stage 1 stays WAITING_QC.
        var wrongNext = good with { StageVersions = new Dictionary<int, long>(good.StageVersions) { [2] = good.StageVersions[2] + 1 } };
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Stages.PerformAsync(w.Qc, StageAction.ApproveWholeQuantity, wrongNext, Token)).Status);
        Assert.Equal(before, Snapshot(w));
        Assert.Equal(ItemStageStatus.WaitingQc, StageOf(await StoredAsync(w, request), 1).Status);
        Assert.Empty((await StoredAsync(w, request)).Value.Items.Single().QcApprovals);

        // Finish stage 1 properly, then try to start stage 2 with a wrong version for the PRECEDING stage 1 (relied on by the start).
        Assert.True((await w.Stages.PerformAsync(w.Qc, StageAction.ApproveWholeQuantity, good, Token)).Succeeded);
        var current = StageTarget.From(await EntryAsync(w));
        before = Snapshot(w);
        var wrongPrevious = current with { StageVersions = new Dictionary<int, long>(current.StageVersions) { [1] = current.StageVersions[1] + 1 } };
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Stages.PerformAsync(w.Production, StageAction.Start, wrongPrevious, Token)).Status);
        Assert.Equal(before, Snapshot(w));
        Assert.Equal(ItemStageStatus.Waiting, StageOf(await StoredAsync(w, request), 2).Status);
    }

    [Fact]
    public async Task Missing_row_versions_are_refused_before_anything_is_changed()
    {
        using var w = await StartAsync();
        await SeedAsync(w, 5m, 3m);
        var target = StageTarget.From(await EntryAsync(w));
        var before = Snapshot(w);

        var noStages = target with { StageVersions = new Dictionary<int, long>() };
        var outcome = await w.Stages.PerformAsync(w.Planning, StageAction.Release, noStages, Token);

        Assert.Equal(OperationStatus.ValidationFailed, outcome.Status);
        Assert.Equal(ValidationIssue.VersionsMissing, outcome.Issue);
        Assert.Equal(OperationStatus.ValidationFailed, (await w.Stages.PerformAsync(w.Planning, (StageAction)99, target, Token)).Status);
        Assert.Equal(before, Snapshot(w));
    }

    [Fact]
    public async Task A_vanished_item_or_stage_is_reported_as_not_found()
    {
        using var w = await StartAsync();
        await SeedAsync(w, 5m);
        var target = StageTarget.From(await EntryAsync(w));

        var noItem = target with { ItemId = target.ItemId + 999 };
        var noRequest = target with { RequestId = target.RequestId + 999 };

        Assert.Equal(OperationStatus.NotFound, (await w.Stages.PerformAsync(w.Planning, StageAction.Release, noItem, Token)).Status);
        Assert.Equal(OperationStatus.NotFound, (await w.Stages.PerformAsync(w.Planning, StageAction.Release, noRequest, Token)).Status);
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
        await MustSucceedAsync(w, w.Planning, StageAction.Release);
        await MustSucceedAsync(w, w.Production, StageAction.Start);
        await MustSucceedAsync(w, w.Production, StageAction.SubmitForQc);
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

    // ---- audit ----

    [Fact]
    public async Task Every_action_writes_one_audit_entry_with_user_target_time_and_before_after_state_in_the_same_transaction()
    {
        using var w = await StartAsync();
        var request = await SeedAsync(w, 5m, 3m);
        var itemId = (await StoredAsync(w, request)).Value.Items.Single().Id;
        var start = w.Fixture.Clock.UtcNow;

        await MustSucceedAsync(w, w.Planning, StageAction.Release);
        await MustSucceedAsync(w, w.Production, StageAction.Start);
        await MustSucceedAsync(w, w.Production, StageAction.SubmitForQc);
        await MustSucceedAsync(w, w.Qc, StageAction.ApproveWholeQuantity);

        var rows = w.Fixture.Db.Query("SELECT ActionCode, UserId, TargetType, TargetId, MachineName, OsUserName, Summary FROM AuditLog WHERE ActionCode LIKE 'Stage%' ORDER BY Id");
        Assert.Equal(4, rows.Count);
        Assert.Equal(
            new[] { "StageReleased", "StageStarted", "StageSubmittedForQc", "StageQcApproved" },
            rows.Select(r => r.Split('|')[0]).ToArray());
        Assert.Equal(
            new[] { w.Planning.UserId, w.Production.UserId, w.Production.UserId, w.Qc.UserId }.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            rows.Select(r => r.Split('|')[1]).ToArray());
        Assert.All(rows, r =>
        {
            var cells = r.Split('|');
            Assert.Equal("ItemStage", cells[2]);
            Assert.Equal(itemId + "/1", cells[3]);
            Assert.Equal("TEST-PC", cells[4]);
            Assert.Equal("test.operator", cells[5]);
        });
        Assert.Contains("PLANNED -> WAITING", rows[0]);
        Assert.Contains("WAITING -> IN_PROGRESS", rows[1]);
        Assert.Contains("IN_PROGRESS -> WAITING_QC", rows[2]);
        Assert.Contains("WAITING_QC -> QC_APPROVED", rows[3]);
        Assert.Contains("stage 2: WAITING_PREVIOUS_QC -> WAITING", rows[3]);
        Assert.Contains("whole quantity 10", rows[3]);
        Assert.Contains("Request R-1, item 1.10, stage 1 (Stage 1)", rows[0]);

        // Timestamps are the clock's, in order, and not before the start of the sequence.
        var times = w.Fixture.Db.Query("SELECT OccurredAtUtc FROM AuditLog WHERE ActionCode LIKE 'Stage%' ORDER BY Id").Select(DateTime.Parse).ToArray();
        Assert.Equal(times.OrderBy(t => t).ToArray(), times);
        Assert.True(times[0] >= start.AddTicks(-TimeSpan.TicksPerSecond));
    }

    [Fact]
    public async Task The_last_approval_is_audited_as_the_end_of_the_route_and_no_audit_text_carries_a_secret()
    {
        using var w = await StartAsync();
        await SeedAsync(w, 5m);
        await MustSucceedAsync(w, w.Planning, StageAction.Release);
        await MustSucceedAsync(w, w.Production, StageAction.Start);
        await MustSucceedAsync(w, w.Production, StageAction.SubmitForQc);
        await MustSucceedAsync(w, w.Qc, StageAction.ApproveWholeQuantity);

        var text = string.Join("\n", w.Fixture.Db.Query("SELECT ActionCode || ' ' || TargetType || ' ' || COALESCE(TargetId, '') || ' ' || Summary FROM AuditLog WHERE ActionCode LIKE 'Stage%'"));
        Assert.Contains("no further stage", text);
        Assert.DoesNotContain(InitialPassword, w.Fixture.AllAuditText(), StringComparison.Ordinal);
        Assert.DoesNotContain(OwnPassword, w.Fixture.AllAuditText(), StringComparison.Ordinal);
        Assert.DoesNotContain(SecurityFixture.AdminPassword, w.Fixture.AllAuditText(), StringComparison.Ordinal);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refused_failed_and_conflicting_actions_write_no_stage_audit_entry()
    {
        using var w = await StartAsync();
        await SeedAsync(w, 5m);
        var stale = StageTarget.From(await EntryAsync(w));
        await MustSucceedAsync(w, w.Planning, StageAction.Release);
        var rowsBefore = w.Fixture.Db.Query("SELECT COUNT(*) FROM AuditLog WHERE ActionCode LIKE 'Stage%'").Single();

        await w.Stages.PerformAsync(w.Production, StageAction.Start, stale, Token);          // conflict
        await ActAsync(w, w.Production, StageAction.SubmitForQc);                             // invalid state
        await ActAsync(w, w.Planning, StageAction.Start);                                     // not authorized

        Assert.Equal(rowsBefore, w.Fixture.Db.Query("SELECT COUNT(*) FROM AuditLog WHERE ActionCode LIKE 'Stage%'").Single());
        Assert.Equal("1", rowsBefore);
    }
}
