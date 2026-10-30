using ERP.MES.Application.Persistence;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Infrastructure.Tests;

// M8 (Decision Log #77): callers pass the version of the specific item or stage row they loaded. Real SQLite files and the committed migrations; every call uses
// its own context, so two repository calls stand for two users.
public class RowVersionTests
{
    private static readonly DateTime Later = PersistenceTestData.Now.AddHours(3);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(TempDatabase Db, long RequestId)> SeedAsync()
    {
        var db = new TempDatabase();
        await db.MigrateWithActorsAsync();
        return (db, await PersistenceTestData.SeedSampleRequestAsync(db));
    }

    private static RequestItem Make(Request request) => request.Items.Single(i => i.CustomerItemRef == "1.10");

    private static RequestItem Repair(Request request) => request.Items.Single(i => i.CustomerItemRef == "2.10");

    private static ItemStage Stage(RequestItem item, int stageNo) => item.Stages.Single(s => s.StageNo == stageNo);

    [Fact]
    public async Task Loading_a_request_returns_a_version_for_the_request_every_item_and_every_stage()
    {
        var (db, requestId) = await SeedAsync();
        using (db)
        {
            var loaded = await db.Requests().FindWithVersionsAsync(requestId, Token);

            Assert.NotNull(loaded);
            var make = Make(loaded.Value);
            var repair = Repair(loaded.Value);
            Assert.Equal(2, loaded.Versions.Items.Count);
            Assert.Equal(4, loaded.Versions.Stages.Count);
            Assert.All(loaded.Versions.Items.Values, v => Assert.True(v >= 1));
            Assert.All(loaded.Versions.Stages.Values, v => Assert.True(v >= 1));
            Assert.True(loaded.Versions.Request >= 1);
            Assert.True(loaded.Versions.ItemVersion(make.Id) >= 1);
            Assert.True(loaded.Versions.StageVersion(repair.Id, 1) >= 1);
            Assert.Null(await db.Requests().FindWithVersionsAsync(requestId + 1000, Token));
        }
    }

    [Fact]
    public async Task An_update_with_the_current_item_and_stage_versions_succeeds_and_advances_only_the_rows_it_changed()
    {
        var (db, requestId) = await SeedAsync();
        using (db)
        {
            var repository = db.Requests();
            var before = (await repository.FindWithVersionsAsync(requestId, Token))!;
            var make = Make(before.Value);
            var repair = Repair(before.Value);

            var after = await repository.UpdateRowsAsync(
                requestId,
                RowVersionExpectations.ForItem(make.Id, before.Versions.ItemVersion(make.Id)).WithStage(make.Id, 2, before.Versions.StageVersion(make.Id, 2)),
                r => Make(r).StartStage(2),
                Token);

            Assert.Equal(ItemStageStatus.InProgress, Stage(Make(after.Value), 2).Status);
            Assert.True(after.Versions.StageVersion(make.Id, 2) > before.Versions.StageVersion(make.Id, 2));
            Assert.Equal(before.Versions.StageVersion(make.Id, 3), after.Versions.StageVersion(make.Id, 3));
            Assert.Equal(before.Versions.StageVersion(repair.Id, 1), after.Versions.StageVersion(repair.Id, 1));
            Assert.Equal(before.Versions.ItemVersion(repair.Id), after.Versions.ItemVersion(repair.Id));

            var stored = (await repository.FindWithVersionsAsync(requestId, Token))!;
            Assert.Equal(after.Versions.StageVersion(make.Id, 2), stored.Versions.StageVersion(make.Id, 2));
            Assert.Equal(ItemStageStatus.InProgress, Stage(Make(stored.Value), 2).Status);
        }
    }

    [Fact]
    public async Task A_stale_item_version_is_rejected_and_the_newer_update_survives()
    {
        var (db, requestId) = await SeedAsync();
        using (db)
        {
            var repository = db.Requests();
            var loaded = (await repository.FindWithVersionsAsync(requestId, Token))!;
            var make = Make(loaded.Value);
            var staleItemVersion = loaded.Versions.ItemVersion(make.Id);

            // Another user changes the priority first.
            await repository.UpdateRowsAsync(
                requestId,
                RowVersionExpectations.ForItem(make.Id, staleItemVersion),
                r => Assert.True(Make(r).ChangePriority(Priority.VeryUrgent, Later)),
                Token);

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repository.UpdateRowsAsync(
                requestId,
                RowVersionExpectations.ForItem(make.Id, staleItemVersion),
                r => Assert.True(Make(r).ChangePriority(Priority.Normal, Later.AddMinutes(1))),
                Token));

            var stored = (await repository.FindWithVersionsAsync(requestId, Token))!;
            Assert.Equal(Priority.VeryUrgent, Make(stored.Value).Priority);
            Assert.Equal(Later, Make(stored.Value).UpdatedAtUtc);
        }
    }

    [Fact]
    public async Task A_stale_stage_version_is_rejected_and_the_newer_update_survives()
    {
        var (db, requestId) = await SeedAsync();
        using (db)
        {
            var repository = db.Requests();
            var loaded = (await repository.FindWithVersionsAsync(requestId, Token))!;
            var make = Make(loaded.Value);
            var staleStageVersion = loaded.Versions.StageVersion(make.Id, 2);

            await repository.UpdateRowsAsync(requestId, RowVersionExpectations.ForStage(make.Id, 2, staleStageVersion), r => Make(r).StartStage(2), Token);

            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repository.UpdateRowsAsync(
                requestId,
                RowVersionExpectations.ForStage(make.Id, 2, staleStageVersion),
                r => Make(r).StartStage(2),
                Token));

            var stored = (await repository.FindWithVersionsAsync(requestId, Token))!;
            Assert.Equal(ItemStageStatus.InProgress, Stage(Make(stored.Value), 2).Status);
        }
    }

    [Fact]
    public async Task A_stale_version_on_one_named_row_rejects_the_whole_operation_and_changes_nothing()
    {
        var (db, requestId) = await SeedAsync();
        using (db)
        {
            var repository = db.Requests();
            var loaded = (await repository.FindWithVersionsAsync(requestId, Token))!;
            var make = Make(loaded.Value);
            // Stage 2 is taken to WAITING_QC; the user who loaded the request earlier will try to approve it.
            await repository.UpdateRowsAsync(requestId, RowVersionExpectations.None, r => { Make(r).StartStage(2); Make(r).SubmitStageForQc(2); }, Token);
            var current = (await repository.FindWithVersionsAsync(requestId, Token))!;

            // Stage 2 is current but stage 3 (released together with the approval) is named with an old version.
            var expected = RowVersionExpectations
                .ForStage(make.Id, 2, current.Versions.StageVersion(make.Id, 2))
                .WithStage(make.Id, 3, current.Versions.StageVersion(make.Id, 3) - 1);

            var changeRan = false;
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repository.UpdateRowsAsync(
                requestId,
                expected,
                r => { changeRan = true; Make(r).ApproveStageWholeQuantity(2, 9, Later); },
                Token));

            Assert.False(changeRan);
            var stored = (await repository.FindWithVersionsAsync(requestId, Token))!;
            Assert.Equal(ItemStageStatus.WaitingQc, Stage(Make(stored.Value), 2).Status);
            Assert.Equal(ItemStageStatus.WaitingPreviousQc, Stage(Make(stored.Value), 3).Status);
            Assert.Single(Make(stored.Value).QcApprovals);
            Assert.Equal(current.Versions.StageVersion(make.Id, 2), stored.Versions.StageVersion(make.Id, 2));
            Assert.Equal(current.Versions.ItemVersion(make.Id), stored.Versions.ItemVersion(make.Id));
        }
    }

    [Fact]
    public async Task A_competing_write_between_loading_and_saving_gives_a_conflict_and_leaves_no_partial_change()
    {
        var (db, requestId) = await SeedAsync();
        using (db)
        {
            var repository = db.Requests();
            await repository.UpdateRowsAsync(requestId, RowVersionExpectations.None, r => { Make(r).StartStage(2); Make(r).SubmitStageForQc(2); }, Token);
            var loaded = (await repository.FindWithVersionsAsync(requestId, Token))!;
            var make = Make(loaded.Value);

            // The approval touches stage 2, stage 3, the append-only QC approvals and the item's UpdatedAtUtc. A second user changes stage 3 after our load.
            var competing = db.Requests();
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repository.UpdateRowsAsync(
                requestId,
                RowVersionExpectations.FromLoaded(loaded.Versions),
                r =>
                {
                    competing.UpdateRowsAsync(requestId, RowVersionExpectations.None, c => Make(c).ApproveStageWholeQuantity(2, 9, Later), Token).GetAwaiter().GetResult();
                    Make(r).ApproveStageWholeQuantity(2, 9, Later.AddMinutes(5));
                },
                Token));

            var stored = (await repository.FindWithVersionsAsync(requestId, Token))!;
            var item = Make(stored.Value);
            // Only the competing user's approval exists: ours added no second approval, no changed timestamp, no extra stage change.
            Assert.Equal(2, item.QcApprovals.Count);
            Assert.Equal(Later, item.QcApprovals.Last().ApprovedAtUtc);
            Assert.Equal(Later, item.UpdatedAtUtc);
            Assert.Equal(ItemStageStatus.QcApproved, Stage(item, 2).Status);
            Assert.Equal(ItemStageStatus.Waiting, Stage(item, 3).Status);
            Assert.True(make.Id > 0);
        }
    }

    [Fact]
    public async Task A_request_version_alone_does_not_detect_an_item_or_stage_change_which_is_the_documented_per_row_guarantee()
    {
        var (db, requestId) = await SeedAsync();
        using (db)
        {
            var repository = db.Requests();
            var loaded = (await repository.FindWithVersionsAsync(requestId, Token))!;
            var make = Make(loaded.Value);
            await repository.UpdateRowsAsync(
                requestId, RowVersionExpectations.None, r => Assert.True(Make(r).ChangePriority(Priority.VeryUrgent, Later)), Token);

            // The request row did not change, so its version still matches: the request-level check cannot protect item rows.
            await repository.UpdateRowsAsync(requestId, RowVersionExpectations.ForRequest(loaded.Versions.Request), r => Make(r).StartStage(2), Token);

            // Naming the item row does detect the change.
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => repository.UpdateRowsAsync(
                requestId, RowVersionExpectations.ForItem(make.Id, loaded.Versions.ItemVersion(make.Id)), _ => { }, Token));
        }
    }

    [Fact]
    public async Task Naming_a_missing_item_or_stage_is_reported_as_a_missing_record()
    {
        var (db, requestId) = await SeedAsync();
        using (db)
        {
            var repository = db.Requests();
            var loaded = (await repository.FindWithVersionsAsync(requestId, Token))!;
            var make = Make(loaded.Value);

            await Assert.ThrowsAsync<RecordNotFoundException>(() => repository.UpdateRowsAsync(requestId, RowVersionExpectations.ForItem(make.Id + 9999, 1), _ => { }, Token));
            await Assert.ThrowsAsync<RecordNotFoundException>(() => repository.UpdateRowsAsync(requestId, RowVersionExpectations.ForStage(make.Id, 9, 1), _ => { }, Token));
            await Assert.ThrowsAsync<RecordNotFoundException>(() => repository.UpdateRowsAsync(requestId + 1000, RowVersionExpectations.None, _ => { }, Token));
        }
    }

    [Fact]
    public async Task Domain_rules_still_apply_with_current_versions_and_a_rejected_change_persists_nothing()
    {
        var (db, requestId) = await SeedAsync();
        using (db)
        {
            var repository = db.Requests();
            var loaded = (await repository.FindWithVersionsAsync(requestId, Token))!;
            var make = Make(loaded.Value);

            // Stage 3 cannot start while stage 2 is not QC approved, and stage 2 cannot be QC approved before it was submitted.
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateRowsAsync(
                requestId, RowVersionExpectations.FromLoaded(loaded.Versions), r => Make(r).StartStage(3), Token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateRowsAsync(
                requestId, RowVersionExpectations.FromLoaded(loaded.Versions), r => Make(r).ApproveStageWholeQuantity(2, 9, Later), Token));

            var stored = (await repository.FindWithVersionsAsync(requestId, Token))!;
            Assert.Equal(ItemStageStatus.Waiting, Stage(Make(stored.Value), 2).Status);
            Assert.Single(Make(stored.Value).QcApprovals);
            Assert.Equal(loaded.Versions.ItemVersion(make.Id), stored.Versions.ItemVersion(make.Id));
            Assert.Equal(loaded.Versions.StageVersion(make.Id, 2), stored.Versions.StageVersion(make.Id, 2));
        }
    }
}
