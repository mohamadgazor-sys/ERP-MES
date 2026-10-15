using ERP.MES.Domain.Requests;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Tests;

// Application-managed optimistic concurrency (Decision Log #23): a version column on customer, request, request item and item stage.
// Two independent contexts on one real SQLite file stand for two users or two windows.
public class ConcurrencyTests
{
    private static readonly DateTime Now = PersistenceTestData.Now;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_stale_customer_update_is_rejected_and_does_not_overwrite_the_first_update()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        await using (var seed = db.NewContext())
        {
            seed.Customers.Add(PersistenceTestData.NewCustomer("Original name"));
            await seed.SaveChangesAsync(Token);
        }

        await using var first = db.NewContext();
        await using var second = db.NewContext();
        var firstCopy = await first.Customers.SingleAsync(Token);
        var secondCopy = await second.Customers.SingleAsync(Token);

        // Both contexts loaded the same version.
        Assert.Equal(1L, first.GetRowVersion(firstCopy));
        Assert.Equal(1L, second.GetRowVersion(secondCopy));

        firstCopy.Rename("First update", Now.AddMinutes(1));
        await first.SaveChangesAsync(Token);
        Assert.Equal(2L, first.GetRowVersion(firstCopy));

        secondCopy.Rename("Stale update", Now.AddMinutes(2));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));

        // The stale update did not overwrite the first one.
        await using var check = db.NewContext();
        var stored = await check.Customers.SingleAsync(Token);
        Assert.Equal("First update", stored.Name);
        Assert.Equal(Now.AddMinutes(1), stored.UpdatedAtUtc);
        Assert.Equal(2L, check.GetRowVersion(stored));
    }

    [Fact]
    public async Task A_stale_request_item_update_is_rejected_and_does_not_overwrite_the_first_update()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var requestId = await PersistenceTestData.SeedMixedRequestAsync(db);

        await using var first = db.NewContext();
        await using var second = db.NewContext();
        var firstItem = await first.RequestItems.SingleAsync(i => i.ItemNo == "1.10", Token);
        var secondItem = await second.RequestItems.SingleAsync(i => i.ItemNo == "1.10", Token);
        Assert.Equal(first.GetRowVersion(firstItem), second.GetRowVersion(secondItem));
        Assert.Equal(Priority.Urgent, firstItem.Priority);

        Assert.True(firstItem.ChangePriority(Priority.VeryUrgent, Now.AddMinutes(1)));
        await first.SaveChangesAsync(Token);

        Assert.True(secondItem.ChangePriority(Priority.Normal, Now.AddMinutes(2)));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));

        await using var check = db.NewContext();
        var stored = await check.RequestItems.SingleAsync(i => i.ItemNo == "1.10", Token);
        Assert.Equal(Priority.VeryUrgent, stored.Priority);
        Assert.Equal(Now.AddMinutes(1), stored.UpdatedAtUtc);
        Assert.True(requestId > 0);
    }

    [Fact]
    public async Task A_stale_item_stage_update_is_rejected_and_does_not_overwrite_the_first_update()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var requestId = await PersistenceTestData.SeedMixedRequestAsync(db);

        await using var first = db.NewContext();
        await using var second = db.NewContext();
        var firstItem = await first.RequestItems.Include(i => i.Stages).SingleAsync(i => i.ItemNo == "1.10", Token);
        var secondItem = await second.RequestItems.Include(i => i.Stages).SingleAsync(i => i.ItemNo == "1.10", Token);

        // Stage 2 is executable after QC approved stage 1. Both contexts start it from the same stored state.
        firstItem.StartStage(2);
        secondItem.StartStage(2);
        await first.SaveChangesAsync(Token);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));

        await using var check = db.NewContext();
        var stage = (await check.RequestItems.Include(i => i.Stages).SingleAsync(i => i.ItemNo == "1.10", Token)).Stages.Single(s => s.StageNo == 2);
        Assert.Equal(ItemStageStatus.InProgress, stage.Status);
        Assert.Equal(2L, check.GetRowVersion(stage));
        Assert.True(requestId > 0);
    }

    [Fact]
    public async Task A_stale_request_update_is_rejected_at_the_persistence_level()
    {
        // Request status transitions are not approved, so the Domain offers no public way to change a stored request. This test
        // changes the stored status directly through EF Core only to prove that the request row carries a working version.
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var requestId = await PersistenceTestData.SeedMixedRequestAsync(db);

        await using var first = db.NewContext();
        await using var second = db.NewContext();
        var firstRequest = await first.Requests.SingleAsync(r => r.Id == requestId, Token);
        var secondRequest = await second.Requests.SingleAsync(r => r.Id == requestId, Token);

        first.Entry(firstRequest).Property(r => r.Status).CurrentValue = RequestStatus.SentToManagement;
        await first.SaveChangesAsync(Token);

        second.Entry(secondRequest).Property(r => r.Status).CurrentValue = RequestStatus.Cancelled;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Token));

        await using var check = db.NewContext();
        var stored = await check.Requests.SingleAsync(r => r.Id == requestId, Token);
        Assert.Equal(RequestStatus.SentToManagement, stored.Status);
        Assert.Equal(2L, check.GetRowVersion(stored));
    }

    [Fact]
    public async Task New_rows_start_at_version_one_and_each_successful_update_adds_one()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var requestId = await PersistenceTestData.SeedMixedRequestAsync(db);

        Assert.All(new[] { "Customers", "Requests", "RequestItems", "ItemStages" }, table =>
            Assert.All(db.Query($"SELECT Version FROM {table}"), version => Assert.Equal("1", version)));

        await using (var context = db.NewContext())
        {
            var customers = await context.Customers.ToListAsync(Token);
            var other = PersistenceTestData.NewCustomer("Another customer");
            context.Customers.Add(other);
            await context.SaveChangesAsync(Token);

            customers[0].Rename("Renamed", Now.AddMinutes(1));
            await context.SaveChangesAsync(Token);

            // Saving again without a change does not touch the version, and the other customer is unaffected.
            await context.SaveChangesAsync(Token);
            Assert.Equal(2L, context.GetRowVersion(customers[0]));
            Assert.Equal(1L, context.GetRowVersion(other));
        }

        Assert.Equal(new[] { "1", "2" }, db.Query("SELECT Version FROM Customers ORDER BY Id").ToArray());
        Assert.True(requestId > 0);
    }

    [Fact]
    public async Task Qc_approvals_are_append_only_and_have_no_version_column()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();

        foreach (var table in new[] { "Customers", "Requests", "RequestItems", "ItemStages" })
        {
            Assert.Contains("Version", db.Query($"SELECT name FROM pragma_table_info('{table}')"));
        }

        Assert.DoesNotContain("Version", db.Query("SELECT name FROM pragma_table_info('QcApprovals')"));
    }

    [Fact]
    public void The_version_lives_in_the_persistence_layer_not_in_the_domain()
    {
        var domainTypes = new[] { typeof(Customer), typeof(Request), typeof(RequestItem), typeof(ItemStage) };

        Assert.All(domainTypes, type => Assert.Null(type.GetProperty("Version")));
    }
}
