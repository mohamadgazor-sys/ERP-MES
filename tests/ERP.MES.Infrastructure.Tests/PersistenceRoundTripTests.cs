using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Tests;

// Representative data is saved with one context and read back with a fresh context and connection on a real database file,
// as after an application restart. The tests check that the mapping stores the Domain types faithfully and that the Domain
// keeps working on reloaded objects; they do not restate the Domain's own rules.
public class PersistenceRoundTripTests
{
    private static readonly DateTime Now = PersistenceTestData.Now;

    private static Task<long> SaveMixedRequestAsync(TempDatabase db) => PersistenceTestData.SeedSampleRequestAsync(db);

    private static Task<Request> LoadAsync(ErpMesDbContext context, long requestId) =>
        context.Requests
            .Include(r => r.Items).ThenInclude(i => i.Stages)
            .Include(r => r.Items).ThenInclude(i => i.QcApprovals)
            .SingleAsync(r => r.Id == requestId, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Keys_are_generated_and_assigned_to_the_domain_objects()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();

        var requestId = await SaveMixedRequestAsync(db);

        Assert.True(requestId > 0);
        await using var context = db.NewContext();
        var request = await LoadAsync(context, requestId);
        Assert.All(request.Items, item => Assert.True(item.Id > 0));
        Assert.Equal(2, request.Items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public async Task A_customer_is_saved_and_loaded_with_all_values_and_utc_timestamps()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();
        await using (var context = db.NewContext())
        {
            var customer = PersistenceTestData.NewCustomer("  Fictional Customer with spaces  ");
            customer.SetActive(false, Now.AddMinutes(5));
            context.Customers.Add(customer);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reopened = db.NewContext();
        var loaded = await reopened.Customers.SingleAsync(TestContext.Current.CancellationToken);

        Assert.True(loaded.Id > 0);
        Assert.Equal("  Fictional Customer with spaces  ", loaded.Name);
        Assert.False(loaded.IsActive);
        Assert.Equal(1, loaded.CreatedByUserId);
        Assert.Equal(Now, loaded.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAtUtc.Kind);
        Assert.Equal(Now.AddMinutes(5), loaded.UpdatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.UpdatedAtUtc.Kind);
    }

    [Fact]
    public async Task A_mixed_request_with_items_stages_and_a_qc_approval_survives_a_restart()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();
        var requestId = await SaveMixedRequestAsync(db);

        await using var context = db.NewContext();
        var request = await LoadAsync(context, requestId);

        Assert.Equal(new RequestKey(request.CustomerId, "P-565", 1405), request.Key);
        Assert.Equal("P-565", request.RequestNo.Value);
        Assert.Equal(new DateOnly(2026, 10, 12), request.RequestDate);
        Assert.Equal(1405, request.JalaliRequestYear);
        Assert.Equal(RequestStatus.Created, request.Status);
        Assert.Equal("درخواست فرضی", request.Title);
        Assert.Null(request.Description);
        Assert.Equal(Now, request.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, request.CreatedAtUtc.Kind);

        var items = request.Items.OrderBy(i => i.ItemNo, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "1.10", "2.10" }, items.Select(i => i.CustomerItemRef).ToArray());
        Assert.Equal(new[] { "P-565-001", "P-565-002" }, items.Select(i => i.ItemNo).ToArray());
        Assert.Equal(WorkType.Make, request.WorkType);
        Assert.Equal(new[] { "Ring A", "Shaft B" }, items.Select(i => i.PartName).ToArray());
        Assert.Equal(new[] { Priority.Urgent, Priority.Normal }, items.Select(i => i.Priority).ToArray());

        // Exact decimals, no rounding.
        Assert.Equal(new[] { 10.123456789m, 4m }, items.Select(i => i.RequestedQuantity).ToArray());

        var make = items[0];
        var stages = make.Stages.OrderBy(s => s.StageNo).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, stages.Select(s => s.StageNo).ToArray());
        Assert.Equal(new[] { "Turning", "Grinding", "Polishing" }, stages.Select(s => s.StageTitle).ToArray());
        Assert.Equal(
            new[] { ItemStageStatus.QcApproved, ItemStageStatus.Waiting, ItemStageStatus.WaitingPreviousQc },
            stages.Select(s => s.Status).ToArray());
        Assert.Equal(new[] { 5m, 3m, 1.5m }, stages.Select(s => s.PlannedMinutesPerPiece).ToArray());
        Assert.All(stages, s => Assert.Equal(10.123456789m, s.PlannedQuantity));
        Assert.Equal(5m * 10.123456789m, stages[0].PlannedTotalMinutes);

        var approval = Assert.Single(make.QcApprovals);
        Assert.Equal(1, approval.StageNo);
        Assert.Equal(10.123456789m, approval.ApprovedQuantity);
        Assert.Equal(9, approval.ApprovedByUserId);
        Assert.Equal(Now.AddHours(2), approval.ApprovedAtUtc);
        Assert.Equal(DateTimeKind.Utc, approval.ApprovedAtUtc.Kind);

        var repair = items[1];
        var repairStage = Assert.Single(repair.Stages);
        Assert.Equal(ItemStageStatus.Planned, repairStage.Status);
        Assert.Empty(repair.QcApprovals);
        Assert.False(make.IsRouteCompleted);
    }

    [Fact]
    public async Task The_domain_keeps_working_on_reloaded_objects_and_each_change_persists()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();
        var requestId = await SaveMixedRequestAsync(db);

        // Session 2: continue the route of the MAKE item through stage 2 and change a priority.
        await using (var context = db.NewContext())
        {
            var request = await LoadAsync(context, requestId);
            var make = request.Items.Single(i => i.CustomerItemRef == "1.10");
            make.StartStage(2);
            make.SubmitStageForQc(2);
            make.ApproveStageWholeQuantity(2, 9, Now.AddHours(3));
            Assert.True(make.ChangePriority(Priority.VeryUrgent, Now.AddHours(3)));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Session 3: everything is still there, and the QC gate still holds on the reloaded data.
        await using (var context = db.NewContext())
        {
            var request = await LoadAsync(context, requestId);
            var make = request.Items.Single(i => i.CustomerItemRef == "1.10");

            Assert.Equal(Priority.VeryUrgent, make.Priority);
            Assert.Equal(Now.AddHours(3), make.UpdatedAtUtc);
            Assert.Equal(
                new[] { ItemStageStatus.QcApproved, ItemStageStatus.QcApproved, ItemStageStatus.Waiting },
                make.Stages.OrderBy(s => s.StageNo).Select(s => s.Status).ToArray());
            Assert.Equal(new[] { 1, 2 }, make.QcApprovals.Select(a => a.StageNo).OrderBy(n => n).ToArray());

            // Stage 3 is now executable; the repair item's stage was never released, so it cannot start.
            make.StartStage(3);
            Assert.Throws<InvalidOperationException>(() => request.Items.Single(i => i.CustomerItemRef == "2.10").StartStage(1));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var context = db.NewContext())
        {
            var make = (await LoadAsync(context, requestId)).Items.Single(i => i.CustomerItemRef == "1.10");
            Assert.Equal(ItemStageStatus.InProgress, make.Stages.Single(s => s.StageNo == 3).Status);
        }
    }

    [Fact]
    public async Task Request_numbers_and_item_numbers_come_back_exactly_as_stored()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();

        // Persian digits, mixed case and inner spaces are kept as given; nothing is normalised.
        var numbers = new[] { "۱۲", "wsj-898", "WSJ-898", "A 1" };
        await using (var context = db.NewContext())
        {
            var customer = PersistenceTestData.NewCustomer();
            context.Customers.Add(customer);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            foreach (var number in numbers)
            {
                var request = PersistenceTestData.NewRequest(customer, number);
                request.AddItem(0, number, "Part", 1m, 1, Now);
                context.Requests.Add(request);
            }

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reopened = db.NewContext();
        var loaded = await reopened.Requests.Include(r => r.Items).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            numbers.OrderBy(n => n, StringComparer.Ordinal).ToArray(),
            loaded.Select(r => r.RequestNo.Value).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.All(loaded, r => Assert.Equal(r.RequestNo.Value + "-001", Assert.Single(r.Items).ItemNo));
    }

    [Fact]
    public async Task A_second_context_on_the_same_file_sees_committed_data_only()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();

        await using var writer = db.NewContext();
        writer.Customers.Add(PersistenceTestData.NewCustomer());

        await using (var reader = db.NewContext())
        {
            Assert.Empty(await reader.Customers.ToListAsync(TestContext.Current.CancellationToken));
        }

        await writer.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var after = db.NewContext();
        Assert.Single(await after.Customers.ToListAsync(TestContext.Current.CancellationToken));
    }
}
