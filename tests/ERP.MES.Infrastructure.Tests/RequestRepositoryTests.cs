using ERP.MES.Application.Persistence;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Infrastructure.Tests;

// Request-aggregate persistence through the Application contract, on a real SQLite file. Every step uses a new repository (new contexts),
// as after a restart. Domain rules are exercised through the Domain's own methods; these tests do not restate them.
public class RequestRepositoryTests
{
    private static readonly DateTime Now = PersistenceTestData.Now;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(long CustomerId, Stored<Request> Request)> SeedAsync(TempDatabase db)
    {
        var customer = await db.Customers().AddAsync(PersistenceTestData.NewCustomer(), Token);
        var request = PersistenceTestData.NewMixedRequest(customer.Value);
        var stored = await db.Requests().AddAsync(request, Token);
        return (customer.Value.Id, stored);
    }

    [Fact]
    public async Task A_mixed_request_with_items_stages_and_approvals_is_saved_and_reloaded_in_a_new_session()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();

        var (customerId, added) = await SeedAsync(db);

        Assert.True(added.Value.Id > 0);
        Assert.Equal(1L, added.Version);
        Assert.All(added.Value.Items, item => Assert.True(item.Id > 0));

        var loaded = await db.Requests().FindAsync(added.Value.Id, Token);
        Assert.NotNull(loaded);
        var request = loaded.Value;
        Assert.Equal(new RequestKey(customerId, "P-565", 1405), request.Key);
        Assert.Equal(RequestStatus.Created, request.Status);
        Assert.Equal("درخواست فرضی", request.Title);

        var items = request.Items.OrderBy(i => i.ItemNo, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { WorkType.Make, WorkType.Repair }, items.Select(i => i.WorkType).ToArray());
        Assert.Equal(new[] { 10.123456789m, 4m }, items.Select(i => i.RequestedQuantity).ToArray());
        Assert.Equal(new[] { Priority.Urgent, Priority.Normal }, items.Select(i => i.Priority).ToArray());
        Assert.Equal(
            new[] { ItemStageStatus.QcApproved, ItemStageStatus.Waiting, ItemStageStatus.WaitingPreviousQc },
            items[0].Stages.Select(s => s.Status).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, items[0].Stages.Select(s => s.StageNo).ToArray());
        var approval = Assert.Single(items[0].QcApprovals);
        Assert.Equal((1, 10.123456789m, 9L), (approval.StageNo, approval.ApprovedQuantity, approval.ApprovedByUserId));
        Assert.Equal(DateTimeKind.Utc, approval.ApprovedAtUtc.Kind);
    }

    [Fact]
    public async Task A_request_for_a_missing_customer_is_rejected_and_nothing_is_saved()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var unsavedCustomer = PersistenceTestData.NewCustomer();
        var request = PersistenceTestData.NewRequest(unsavedCustomer);

        var missing = await Assert.ThrowsAsync<RecordNotFoundException>(() => db.Requests().AddAsync(request, Token));

        Assert.Equal("customer", missing.RecordKind);
        Assert.Empty(db.Query("SELECT Id FROM Requests"));
    }

    [Fact]
    public async Task The_business_key_is_unique_per_customer_request_number_and_jalali_year_and_case_sensitive()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var customer = (await db.Customers().AddAsync(PersistenceTestData.NewCustomer("A"), Token)).Value;
        var other = (await db.Customers().AddAsync(PersistenceTestData.NewCustomer("B"), Token)).Value;

        await db.Requests().AddAsync(PersistenceTestData.NewRequest(customer, "wsj-898"), Token);

        var duplicate = await Assert.ThrowsAsync<DuplicateRequestException>(
            () => db.Requests().AddAsync(PersistenceTestData.NewRequest(customer, "wsj-898"), Token));
        Assert.Equal(new RequestKey(customer.Id, "wsj-898", 1405), duplicate.Key);

        // Different case, other customer and other Jalali year are different keys.
        await db.Requests().AddAsync(PersistenceTestData.NewRequest(customer, "WSJ-898"), Token);
        await db.Requests().AddAsync(PersistenceTestData.NewRequest(other, "wsj-898"), Token);
        await db.Requests().AddAsync(PersistenceTestData.NewRequest(customer, "wsj-898", new DateOnly(2027, 3, 21)), Token);

        Assert.Equal("4", db.Query("SELECT COUNT(*) FROM Requests").Single());
        Assert.True(await db.Requests().ExistsAsync(new RequestKey(customer.Id, "wsj-898", 1405), Token));
        Assert.True(await db.Requests().ExistsAsync(new RequestKey(customer.Id, "WSJ-898", 1405), Token));
        Assert.False(await db.Requests().ExistsAsync(new RequestKey(customer.Id, "wsj-899", 1405), Token));
        Assert.False(await db.Requests().ExistsAsync(new RequestKey(other.Id, "WSJ-898", 1405), Token));
        Assert.False(await db.Requests().ExistsAsync(new RequestKey(customer.Id, " wsj-898", 1405), Token));
    }

    [Fact]
    public async Task Case_only_differences_are_reported_as_near_duplicates_without_blocking()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var customer = (await db.Customers().AddAsync(PersistenceTestData.NewCustomer("A"), Token)).Value;
        var other = (await db.Customers().AddAsync(PersistenceTestData.NewCustomer("B"), Token)).Value;
        await db.Requests().AddAsync(PersistenceTestData.NewRequest(customer, "wsj-898"), Token);
        await db.Requests().AddAsync(PersistenceTestData.NewRequest(customer, "other-1"), Token);
        await db.Requests().AddAsync(PersistenceTestData.NewRequest(other, "Wsj-898"), Token);

        var near = await db.Requests().FindCaseInsensitiveNearDuplicatesAsync(customer.Id, 1405, RequestNo.Create("WSJ-898"), Token);
        var exact = await db.Requests().FindCaseInsensitiveNearDuplicatesAsync(customer.Id, 1405, RequestNo.Create("wsj-898"), Token);
        var otherYear = await db.Requests().FindCaseInsensitiveNearDuplicatesAsync(customer.Id, 1406, RequestNo.Create("WSJ-898"), Token);

        Assert.Equal(new[] { "wsj-898" }, near.Select(n => n.Value).ToArray());
        Assert.Empty(exact);
        Assert.Empty(otherYear);

        // Not blocking: the near duplicate itself can be saved.
        await db.Requests().AddAsync(PersistenceTestData.NewRequest(customer, "WSJ-898"), Token);
    }

    [Fact]
    public async Task The_request_list_shows_summaries_newest_first_and_can_be_filtered_by_customer()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var customer = (await db.Customers().AddAsync(PersistenceTestData.NewCustomer("Customer A"), Token)).Value;
        var other = (await db.Customers().AddAsync(PersistenceTestData.NewCustomer("Customer B"), Token)).Value;
        var older = await db.Requests().AddAsync(PersistenceTestData.NewRequest(customer, "1", new DateOnly(2026, 4, 1), "older"), Token);
        var newer = await db.Requests().AddAsync(PersistenceTestData.NewMixedRequest(customer), Token);
        await db.Requests().AddAsync(PersistenceTestData.NewRequest(other, "2", new DateOnly(2026, 5, 1)), Token);

        var all = await db.Requests().ListAsync(null, Token);
        var mine = await db.Requests().ListAsync(customer.Id, Token);

        Assert.Equal(3, all.Count);
        Assert.Equal(new[] { newer.Value.Id, older.Value.Id }, mine.Select(r => r.RequestId).ToArray());
        Assert.Equal(new[] { 2, 0 }, mine.Select(r => r.ItemCount).ToArray());
        Assert.Equal("Customer A", mine[0].CustomerName);
        Assert.Equal("P-565", mine[0].RequestNo.Value);
        Assert.Equal(new DateOnly(2026, 10, 12), mine[0].RequestDate);
        Assert.Equal(RequestStatus.Created, mine[0].Status);
        Assert.Equal(1405, mine[0].JalaliRequestYear);
        Assert.Equal("older", mine[1].Title);
    }

    [Fact]
    public async Task Items_stages_and_qc_approvals_are_added_and_changed_through_the_domain_and_persist()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var (_, added) = await SeedAsync(db);
        var requestId = added.Value.Id;

        // Session 2: a new item, its route, and the repair item's whole route up to QC approval.
        var second = await db.Requests().UpdateAsync(requestId, added.Version, request =>
        {
            var extra = request.AddItem(0, "3.10", "Bushing C", WorkType.Make, 2m, 1, Now.AddHours(4));
            extra.DefineInitialStage("Turning", 2m);

            var repair = request.Items.Single(i => i.ItemNo == "2.10");
            repair.ReleaseFirstStage();
            repair.StartStage(1);
            repair.SubmitStageForQc(1);
            repair.ApproveStageWholeQuantity(1, 9, Now.AddHours(4));
        }, Token);

        // Adding an item touches the request (its update time), so the request row's version went up by one: adding items is protected by the request version.
        Assert.Equal(added.Version + 1, second.Version);

        // Session 3: everything is there, and the QC gate still works on reloaded data.
        var third = await db.Requests().UpdateAsync(requestId, null, request =>
        {
            Assert.Equal(new[] { "1.10", "2.10", "3.10" }, request.Items.Select(i => i.ItemNo).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            var make = request.Items.Single(i => i.ItemNo == "1.10");
            make.StartStage(2);
            make.SubmitStageForQc(2);
            make.ApproveStageWholeQuantity(2, 9, Now.AddHours(5));
            Assert.True(make.ChangePriority(Priority.VeryUrgent, Now.AddHours(5)));
        }, Token);

        var final = (await db.Requests().FindAsync(requestId, Token))!.Value;
        var makeItem = final.Items.Single(i => i.ItemNo == "1.10");
        Assert.Equal(Priority.VeryUrgent, makeItem.Priority);
        Assert.Equal(new[] { ItemStageStatus.QcApproved, ItemStageStatus.QcApproved, ItemStageStatus.Waiting }, makeItem.Stages.Select(s => s.Status).ToArray());
        Assert.Equal(new[] { 1, 2 }, makeItem.QcApprovals.Select(a => a.StageNo).ToArray());
        var repairItem = final.Items.Single(i => i.ItemNo == "2.10");
        Assert.True(repairItem.IsRouteCompleted);
        Assert.Equal(1, Assert.Single(repairItem.QcApprovals).StageNo);
        Assert.Equal(ItemStageStatus.Planned, final.Items.Single(i => i.ItemNo == "3.10").Stages.Single().Status);
        // Item and stage changes alone do not touch the request row.
        Assert.Equal(second.Version, third.Version);
        Assert.Equal("3", db.Query("SELECT COUNT(*) FROM RequestItems").Single());
        Assert.Equal("3", db.Query("SELECT COUNT(*) FROM QcApprovals").Single());
    }

    [Fact]
    public async Task Domain_rule_violations_in_an_update_propagate_unchanged_and_nothing_is_saved()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var (_, added) = await SeedAsync(db);
        var requestId = added.Value.Id;

        // Duplicate item number, a stage started out of order, a stage approved before it was submitted, an invalid quantity.
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Requests().UpdateAsync(requestId, null, r => r.AddItem(0, "1.10", "Dup", WorkType.Make, 1m, 1, Now), Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Requests().UpdateAsync(requestId, null, r => r.Items.Single(i => i.ItemNo == "1.10").StartStage(3), Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Requests().UpdateAsync(requestId, null, r => r.Items.Single(i => i.ItemNo == "1.10").ApproveStageWholeQuantity(2, 9, Now), Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => db.Requests().UpdateAsync(requestId, null, r => r.AddItem(0, "9.9", "Zero", WorkType.Make, 0m, 1, Now), Token));

        // A change that fails after some Domain changes succeeded saves none of them.
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Requests().UpdateAsync(requestId, null, r =>
        {
            r.AddItem(0, "5.5", "Would be saved", WorkType.Make, 1m, 1, Now);
            r.AddItem(0, "5.5", "Duplicate of the previous one", WorkType.Make, 1m, 1, Now);
        }, Token));

        var stored = (await db.Requests().FindAsync(requestId, Token))!.Value;
        Assert.Equal(2, stored.Items.Count);
        Assert.Equal(new[] { ItemStageStatus.QcApproved, ItemStageStatus.Waiting, ItemStageStatus.WaitingPreviousQc }, stored.Items.Single(i => i.ItemNo == "1.10").Stages.Select(s => s.Status).ToArray());
    }

    [Fact]
    public async Task An_update_based_on_a_stale_request_version_is_rejected()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var (_, added) = await SeedAsync(db);

        // Another user changed the request row (there is no public Domain method that does, so the version is raised directly).
        db.Execute("UPDATE Requests SET Version = Version + 1");

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => db.Requests().UpdateAsync(
            added.Value.Id, added.Version, r => r.AddItem(0, "7.7", "Late item", WorkType.Make, 1m, 1, Now), Token));
        Assert.Equal("2", db.Query("SELECT COUNT(*) FROM RequestItems").Single());
    }

    [Fact]
    public async Task A_stage_changed_by_someone_else_during_the_update_is_reported_and_not_overwritten()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var (_, added) = await SeedAsync(db);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => db.Requests().UpdateAsync(added.Value.Id, null, request =>
        {
            // Another user saves the same stage between this operation's load and its save.
            db.Execute("UPDATE ItemStages SET StageTitle = 'Changed by another user', Version = Version + 1 WHERE StageNo = 2 AND RequestItemId = (SELECT Id FROM RequestItems WHERE ItemNo = '1.10')");
            request.Items.Single(i => i.ItemNo == "1.10").StartStage(2);
        }, Token));

        var stage = (await db.Requests().FindAsync(added.Value.Id, Token))!.Value.Items.Single(i => i.ItemNo == "1.10").Stages.Single(s => s.StageNo == 2);
        Assert.Equal("Changed by another user", stage.StageTitle);
        Assert.Equal(ItemStageStatus.Waiting, stage.Status);
    }

    [Fact]
    public async Task Missing_requests_are_reported_and_a_stored_request_cannot_be_added_again()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var (_, added) = await SeedAsync(db);

        Assert.Null(await db.Requests().FindAsync(9999, Token));
        var missing = await Assert.ThrowsAsync<RecordNotFoundException>(() => db.Requests().UpdateAsync(9999, null, _ => { }, Token));
        Assert.Equal("request", missing.RecordKind);
        await Assert.ThrowsAsync<ArgumentException>(() => db.Requests().AddAsync(added.Value, Token));
    }

    [Fact]
    public async Task Request_numbers_and_item_numbers_are_stored_and_returned_exactly()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var customer = (await db.Customers().AddAsync(PersistenceTestData.NewCustomer(), Token)).Value;

        // Persian digits, mixed case and inner spaces are kept as given.
        var numbers = new[] { "۱۲", "wsj-898", "WSJ-898", "A 1" };
        foreach (var number in numbers)
        {
            var request = PersistenceTestData.NewRequest(customer, number);
            request.AddItem(0, number, "Part", WorkType.Make, 1m, 1, Now);
            await db.Requests().AddAsync(request, Token);
        }

        var list = await db.Requests().ListAsync(customer.Id, Token);
        Assert.Equal(
            numbers.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            list.Select(r => r.RequestNo.Value).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        foreach (var summary in list)
        {
            var request = (await db.Requests().FindAsync(summary.RequestId, Token))!.Value;
            Assert.Equal(request.RequestNo.Value, Assert.Single(request.Items).ItemNo);
        }
    }
}
