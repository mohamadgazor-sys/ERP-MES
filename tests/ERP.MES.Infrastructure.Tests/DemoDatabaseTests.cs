using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Demo;
using ERP.MES.Infrastructure.Sqlite.Demo;

namespace ERP.MES.Infrastructure.Tests;

// The fictional demonstration database: creation, the safe-reset guard, repeatability and the shape of the sample data. Real SQLite files; a new
// repository (new contexts) on every read, as after a restart.
public class DemoDatabaseTests
{
    private static readonly string[] Tables = ["Customers", "Requests", "RequestItems", "ItemStages", "QcApprovals"];

    private static IJalaliDateService Dates => PersistenceTestData.Dates;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static List<string> Snapshot(TempDatabase db)
    {
        var rows = new List<string>();
        foreach (var table in Tables)
        {
            rows.Add("# " + table);
            rows.AddRange(db.Query("SELECT * FROM " + table + " ORDER BY 1, 2"));
        }

        return rows;
    }

    private static int Count(TempDatabase db, string table) => int.Parse(db.Query("SELECT COUNT(*) FROM " + table)[0], System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<List<Request>> LoadAllAsync(TempDatabase db)
    {
        var requests = new List<Request>();
        foreach (var summary in await db.Requests().ListAsync(null, Token))
        {
            var stored = await db.Requests().FindAsync(summary.RequestId, Token);
            Assert.NotNull(stored);
            requests.Add(stored.Value);
        }

        return requests;
    }

    private static string[] TemporaryFilesNextTo(TempDatabase db) =>
        Directory.GetFiles(Path.GetDirectoryName(db.Path)!, Path.GetFileName(db.Path) + ".*.tmp");

    // ---- creation and the marker ----

    [Fact]
    public async Task Initialising_a_missing_path_creates_a_marked_migrated_and_seeded_database()
    {
        using var db = new TempDatabase();
        Assert.Equal(DemoDatabaseState.Missing, DemoDatabase.Inspect(db.Path));

        var summary = await DemoDatabase.InitializeAsync(db.Path, Dates, Token);

        Assert.Equal(new DemoSeedSummary(3, 0, 6, 0), summary);
        Assert.Equal(DemoDatabaseState.Demo, DemoDatabase.Inspect(db.Path));
        Assert.Equal(new[] { DemoDatabase.ApplicationId.ToString(System.Globalization.CultureInfo.InvariantCulture) }, db.Query("PRAGMA application_id"));
        Assert.NotEmpty(db.Query("SELECT MigrationId FROM __EFMigrationsHistory"));
        Assert.Empty(TemporaryFilesNextTo(db));
    }

    [Fact]
    public async Task An_empty_file_is_treated_as_a_new_database()
    {
        using var db = new TempDatabase();
        await File.WriteAllBytesAsync(db.Path, [], Token);
        Assert.Equal(DemoDatabaseState.EmptyFile, DemoDatabase.Inspect(db.Path));

        await DemoDatabase.InitializeAsync(db.Path, Dates, Token);

        Assert.Equal(DemoDatabaseState.Demo, DemoDatabase.Inspect(db.Path));
        Assert.Equal(3, Count(db, "Customers"));
    }

    [Fact]
    public async Task Inspecting_never_creates_or_changes_a_file()
    {
        using var db = new TempDatabase();

        DemoDatabase.Inspect(db.Path);
        Assert.False(db.Exists);

        await DemoDatabase.InitializeAsync(db.Path, Dates, Token);
        var before = await File.ReadAllBytesAsync(db.Path, Token);
        DemoDatabase.Inspect(db.Path);
        Assert.Equal(before, await File.ReadAllBytesAsync(db.Path, Token));
    }

    // ---- idempotent seeding ----

    [Fact]
    public async Task Initialising_a_demo_database_again_adds_nothing_and_changes_nothing()
    {
        using var db = new TempDatabase();
        await DemoDatabase.InitializeAsync(db.Path, Dates, Token);
        var before = Snapshot(db);

        var second = await DemoDatabase.InitializeAsync(db.Path, Dates, Token);

        Assert.Equal(new DemoSeedSummary(0, 3, 0, 6), second);
        Assert.Equal(before, Snapshot(db));
    }

    [Fact]
    public async Task The_seeder_alone_is_repeatable_on_a_migrated_database()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var seeder = new DemoDataSeeder(db.Customers(), db.Requests(), Dates);

        var first = await seeder.SeedAsync(Token);
        var second = await new DemoDataSeeder(db.Customers(), db.Requests(), Dates).SeedAsync(Token);

        Assert.Equal(new DemoSeedSummary(3, 0, 6, 0), first);
        Assert.Equal(new DemoSeedSummary(0, 3, 0, 6), second);
        Assert.Equal(3, Count(db, "Customers"));
        Assert.Equal(6, Count(db, "Requests"));
    }

    // ---- the safe-reset guard ----

    [Fact]
    public async Task Reset_refuses_an_unmarked_database_and_changes_nothing()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        await db.Customers().AddAsync(PersistenceTestData.NewCustomer("Real customer"), Token);
        var rowsBefore = Snapshot(db);
        var bytesBefore = await File.ReadAllBytesAsync(db.Path, Token);

        var refusal = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(() => DemoDatabase.ResetAsync(db.Path, Dates, Token));

        Assert.Equal(DemoDatabaseState.Unrecognized, refusal.State);
        Assert.Contains("demonstration marker", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(DemoDatabaseState.Unrecognized, DemoDatabase.Inspect(db.Path));
        Assert.Equal(rowsBefore, Snapshot(db));
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(db.Path, Token));
        Assert.Empty(TemporaryFilesNextTo(db));
    }

    [Fact]
    public async Task Initialise_refuses_an_unmarked_database_and_does_not_mark_or_seed_it()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        await db.Customers().AddAsync(PersistenceTestData.NewCustomer("Real customer"), Token);
        var rowsBefore = Snapshot(db);

        var refusal = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(() => DemoDatabase.InitializeAsync(db.Path, Dates, Token));

        Assert.Equal(DemoDatabaseState.Unrecognized, refusal.State);
        Assert.Equal(DemoDatabaseState.Unrecognized, DemoDatabase.Inspect(db.Path));
        Assert.Equal(rowsBefore, Snapshot(db));
        Assert.Equal(new[] { "0" }, db.Query("PRAGMA application_id"));
    }

    [Fact]
    public async Task Reset_refuses_a_file_that_is_not_a_database_and_leaves_it_untouched()
    {
        using var db = new TempDatabase();
        var content = string.Concat(Enumerable.Repeat("This is somebody's notes file, not a database. ", 80));
        await File.WriteAllTextAsync(db.Path, content, Token);

        var reset = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(() => DemoDatabase.ResetAsync(db.Path, Dates, Token));
        var initialise = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(() => DemoDatabase.InitializeAsync(db.Path, Dates, Token));

        Assert.Equal(DemoDatabaseState.NotADatabase, reset.State);
        Assert.Equal(DemoDatabaseState.NotADatabase, initialise.State);
        Assert.Equal(content, await File.ReadAllTextAsync(db.Path, Token));
    }

    [Fact]
    public async Task Reset_refuses_a_folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "erpmes-folder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var refusal = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(() => DemoDatabase.ResetAsync(folder, Dates, Token));

            Assert.Equal(DemoDatabaseState.NotADatabase, refusal.State);
            Assert.True(Directory.Exists(folder));
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Fact]
    public async Task Reset_never_creates_a_database()
    {
        using var db = new TempDatabase();

        var refusal = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(() => DemoDatabase.ResetAsync(db.Path, Dates, Token));

        Assert.Equal(DemoDatabaseState.Missing, refusal.State);
        Assert.False(db.Exists);
        Assert.Empty(TemporaryFilesNextTo(db));
    }

    [Fact]
    public async Task A_blank_path_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => DemoDatabase.ResetAsync(" ", Dates, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => DemoDatabase.InitializeAsync("", Dates, Token));
    }

    // ---- reset and reseed ----

    [Fact]
    public async Task Reset_removes_changes_and_extra_data_and_restores_exactly_the_same_sample_data()
    {
        using var db = new TempDatabase();
        await DemoDatabase.InitializeAsync(db.Path, Dates, Token);
        var original = Snapshot(db);

        // Someone works with the demo: adds a customer, a request and changes a priority.
        var extra = await db.Customers().AddAsync(PersistenceTestData.NewCustomer("Extra customer"), Token);
        await db.Requests().AddAsync(PersistenceTestData.NewRequest(extra.Value, "EXTRA-1"), Token);
        var first = (await db.Requests().ListAsync(null, Token)).First(r => r.CustomerName != "Extra customer");
        await db.Requests().UpdateAsync(first.RequestId, null, r => r.Items[0].ChangePriority(Priority.Normal, PersistenceTestData.Now.AddDays(1)), Token);
        Assert.NotEqual(original, Snapshot(db));

        var summary = await DemoDatabase.ResetAsync(db.Path, Dates, Token);

        Assert.Equal(new DemoSeedSummary(3, 0, 6, 0), summary);
        Assert.Equal(original, Snapshot(db));
        Assert.Equal(DemoDatabaseState.Demo, DemoDatabase.Inspect(db.Path));
        Assert.Empty(TemporaryFilesNextTo(db));
    }

    [Fact]
    public async Task Reset_twice_in_a_row_gives_the_same_data_without_duplicates()
    {
        using var db = new TempDatabase();
        await DemoDatabase.InitializeAsync(db.Path, Dates, Token);

        await DemoDatabase.ResetAsync(db.Path, Dates, Token);
        var afterFirst = Snapshot(db);
        await DemoDatabase.ResetAsync(db.Path, Dates, Token);

        Assert.Equal(afterFirst, Snapshot(db));
        Assert.Equal(3, Count(db, "Customers"));
        Assert.Equal(6, Count(db, "Requests"));
        Assert.Equal(12, Count(db, "RequestItems"));
        Assert.Equal(27, Count(db, "ItemStages"));
        Assert.Equal(9, Count(db, "QcApprovals"));
    }

    // ---- the sample data ----

    [Fact]
    public async Task The_sample_data_has_the_expected_counts_and_mixes_make_and_repair_items()
    {
        using var db = new TempDatabase();
        await DemoDatabase.InitializeAsync(db.Path, Dates, Token);

        var customers = await db.Customers().ListAsync(false, Token);
        var requests = await LoadAllAsync(db);
        var items = requests.SelectMany(r => r.Items).ToList();

        Assert.Equal(3, customers.Count);
        Assert.Equal(3, customers.Select(c => c.Value.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(6, requests.Count);
        Assert.Equal(6, requests.Select(r => r.Key).Distinct().Count());
        Assert.All(requests, r => Assert.Equal(2, r.Items.Count));
        Assert.All(requests, r =>
        {
            Assert.Contains(r.Items, i => i.WorkType == WorkType.Make);
            Assert.Contains(r.Items, i => i.WorkType == WorkType.Repair);
        });
        Assert.Equal(12, items.Count);
        Assert.Equal(6, items.Count(i => i.WorkType == WorkType.Make));
        Assert.Equal(6, items.Count(i => i.WorkType == WorkType.Repair));
        Assert.All(items, i => Assert.InRange(i.Stages.Count, 2, 3));
        Assert.Equal(27, items.Sum(i => i.Stages.Count));
        Assert.Equal(9, items.Sum(i => i.QcApprovals.Count));
        Assert.Equal(12, items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public async Task The_sample_data_shows_the_approved_statuses_priorities_and_stage_states()
    {
        using var db = new TempDatabase();
        await DemoDatabase.InitializeAsync(db.Path, Dates, Token);
        var requests = await LoadAllAsync(db);
        var items = requests.SelectMany(r => r.Items).ToList();
        var stages = items.SelectMany(i => i.Stages).ToList();

        Assert.Equal(
            new[] { RequestStatus.Created, RequestStatus.SentToManagement, RequestStatus.MgmtApprovedAtPlanning, RequestStatus.Closed },
            requests.Select(r => r.Status).Distinct().OrderBy(s => (int)s).ToArray());
        Assert.Equal(
            new[] { Priority.VeryUrgent, Priority.Urgent, Priority.Normal },
            items.Select(i => i.Priority).Distinct().OrderBy(p => p.SortOrder()).ToArray());

        Assert.Equal(9, stages.Count(s => s.Status == ItemStageStatus.QcApproved));
        Assert.Equal(2, stages.Count(s => s.Status == ItemStageStatus.WaitingQc));
        Assert.Equal(1, stages.Count(s => s.Status == ItemStageStatus.InProgress));
        Assert.Equal(2, stages.Count(s => s.Status == ItemStageStatus.Waiting));
        Assert.Equal(4, stages.Count(s => s.Status == ItemStageStatus.Planned));
        Assert.Equal(9, stages.Count(s => s.Status == ItemStageStatus.WaitingPreviousQc));
        Assert.Equal(3, items.Count(i => i.IsRouteCompleted));
    }

    [Fact]
    public async Task The_stored_sample_data_respects_the_qc_gate()
    {
        using var db = new TempDatabase();
        await DemoDatabase.InitializeAsync(db.Path, Dates, Token);

        foreach (var item in (await LoadAllAsync(db)).SelectMany(r => r.Items))
        {
            var stages = item.Stages.OrderBy(s => s.StageNo).ToList();
            Assert.Equal(Enumerable.Range(1, stages.Count), stages.Select(s => s.StageNo));

            for (var i = 1; i < stages.Count; i++)
            {
                // A stage after the first is only released, running, waiting for QC or approved when the stage before it is QC approved.
                var proceeded = stages[i].Status is ItemStageStatus.Waiting or ItemStageStatus.InProgress or ItemStageStatus.WaitingQc or ItemStageStatus.QcApproved;
                if (proceeded)
                {
                    Assert.Equal(ItemStageStatus.QcApproved, stages[i - 1].Status);
                }
            }

            // Every approved stage has exactly one approval, and no approval exists for a stage that is not approved.
            var approvedStageNos = stages.Where(s => s.Status == ItemStageStatus.QcApproved).Select(s => s.StageNo).OrderBy(n => n).ToList();
            Assert.Equal(approvedStageNos, item.QcApprovals.Select(a => a.StageNo).OrderBy(n => n).ToList());
            Assert.All(item.QcApprovals, a => Assert.Equal(stages[a.StageNo - 1].PlannedQuantity, a.ApprovedQuantity));
        }
    }

    [Fact]
    public async Task The_sample_data_is_clearly_marked_as_demonstration_data_and_survives_reopening()
    {
        using var db = new TempDatabase();
        await DemoDatabase.InitializeAsync(db.Path, Dates, Token);

        // New repositories and contexts: the data comes from the file, not from memory.
        var customers = await db.Customers().ListAsync(false, Token);
        var requests = await LoadAllAsync(db);

        Assert.All(customers, c => Assert.EndsWith(DemoDataSeeder.DemoNameSuffix, c.Value.Name, StringComparison.Ordinal));
        Assert.All(customers, c => Assert.True(c.Value.IsActive));
        Assert.All(requests, r => Assert.StartsWith(DemoDataSeeder.RequestNoPrefix, r.RequestNo.Value, StringComparison.Ordinal));
        Assert.All(requests, r => Assert.StartsWith(DemoDataSeeder.TitlePrefix, r.Title, StringComparison.Ordinal));
        Assert.All(requests, r => Assert.Equal(DemoDataSeeder.ActorUserId, r.CreatedByUserId));
        Assert.All(requests.SelectMany(r => r.Items), i => Assert.Equal(DemoDataSeeder.ActorUserId, i.CreatedByUserId));
        Assert.All(customers, c => Assert.Equal(DateTimeKind.Utc, c.Value.CreatedAtUtc.Kind));
        Assert.All(requests, r => Assert.Equal(DateTimeKind.Utc, r.CreatedAtUtc.Kind));

        // Persian text is stored exactly, and the Jalali year is derived from the stored request date.
        Assert.Contains(requests, r => r.Items.Any(i => i.PartName == "شفت پمپ"));
        Assert.All(requests, r => Assert.Equal(Dates.GetJalaliYear(r.RequestDate), r.JalaliRequestYear));
        Assert.Equal(new[] { 1404, 1405 }, requests.Select(r => r.JalaliRequestYear).Distinct().OrderBy(y => y).ToArray());

        var closed = Assert.Single(requests, r => r.Status == RequestStatus.Closed);
        Assert.Equal("1404/12/20", Dates.FormatDate(closed.RequestDate));
        Assert.Equal(1404, closed.JalaliRequestYear);
        Assert.Equal(DemoDataSeeder.RequestNoPrefix + "1404-087", closed.RequestNo.Value);
    }
}
