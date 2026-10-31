using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Demo;
using ERP.MES.Infrastructure.Security;
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

    // A fresh credential file outside the repository for every call, and the documented fast work factor (the floor) so the suite stays quick.
    private static Task<DemoDatabaseResult> Init(TempDatabase db) =>
        DemoDatabase.InitializeAsync(db.Path, Dates, db.NextCredentialPath(), SecurityFixture.FastPolicy, Token);

    private static Task<DemoDatabaseResult> Reset(TempDatabase db) =>
        DemoDatabase.ResetAsync(db.Path, Dates, db.NextCredentialPath(), SecurityFixture.FastPolicy, Token);

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

        var summary = await Init(db);

        Assert.Equal(new DemoSeedSummary(3, 0, 3, 0), summary.Seed);
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

        await Init(db);

        Assert.Equal(DemoDatabaseState.Demo, DemoDatabase.Inspect(db.Path));
        Assert.Equal(3, Count(db, "Customers"));
    }

    [Fact]
    public async Task Inspecting_never_creates_or_changes_a_file()
    {
        using var db = new TempDatabase();

        DemoDatabase.Inspect(db.Path);
        Assert.False(db.Exists);

        await Init(db);
        var before = await File.ReadAllBytesAsync(db.Path, Token);
        DemoDatabase.Inspect(db.Path);
        Assert.Equal(before, await File.ReadAllBytesAsync(db.Path, Token));
    }

    // ---- idempotent seeding ----

    [Fact]
    public async Task Initialising_a_demo_database_again_adds_nothing_and_changes_nothing()
    {
        using var db = new TempDatabase();
        await Init(db);
        var before = Snapshot(db);

        var second = await Init(db);

        Assert.Equal(new DemoSeedSummary(0, 3, 0, 3), second.Seed);
        Assert.Equal(0, second.AccountsCreated);
        Assert.Null(second.CredentialFile);
        Assert.Equal(before, Snapshot(db));
    }

    [Fact]
    public async Task The_seeder_alone_is_repeatable_on_a_migrated_database()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var services = SecurityServices.Create(db.ContextFactory, SecurityFixture.FastPolicy);
        var provisioner = new DemoAccountProvisioner(services.Bootstrap, services.Accounts, db.ContextFactory);
        await provisioner.ProvisionAsync(Token);
        var actors = await provisioner.ResolveActorsAsync(Token);

        var first = await new DemoDataSeeder(db.Customers(), db.Requests(), Dates, actors).SeedAsync(Token);
        var second = await new DemoDataSeeder(db.Customers(), db.Requests(), Dates, actors).SeedAsync(Token);

        Assert.Equal(new DemoSeedSummary(3, 0, 3, 0), first);
        Assert.Equal(new DemoSeedSummary(0, 3, 0, 3), second);
        Assert.Equal(3, Count(db, "Customers"));
        Assert.Equal(3, Count(db, "Requests"));
    }

    // ---- the safe-reset guard ----

    [Fact]
    public async Task Reset_refuses_an_unmarked_database_and_changes_nothing()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();
        await db.Customers().AddAsync(PersistenceTestData.NewCustomer("Real customer"), Token);
        var rowsBefore = Snapshot(db);
        var bytesBefore = await File.ReadAllBytesAsync(db.Path, Token);

        var refusal = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(() => Reset(db));

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
        await db.MigrateWithActorsAsync();
        await db.Customers().AddAsync(PersistenceTestData.NewCustomer("Real customer"), Token);
        var rowsBefore = Snapshot(db);

        var refusal = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(() => Init(db));

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

        var reset = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(() => Reset(db));
        var initialise = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(() => Init(db));

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
            var refusal = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(() => DemoDatabase.ResetAsync(folder, Dates, null, null, Token));

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

        var refusal = await Assert.ThrowsAsync<DemoDatabaseRefusedException>(() => Reset(db));

        Assert.Equal(DemoDatabaseState.Missing, refusal.State);
        Assert.False(db.Exists);
        Assert.Empty(TemporaryFilesNextTo(db));
    }

    [Fact]
    public async Task A_blank_path_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => DemoDatabase.ResetAsync(" ", Dates, null, null, Token));
        await Assert.ThrowsAsync<ArgumentException>(() => DemoDatabase.InitializeAsync("", Dates, null, null, Token));
    }

    // ---- reset and reseed ----

    [Fact]
    public async Task Reset_removes_changes_and_extra_data_and_restores_exactly_the_same_sample_data()
    {
        using var db = new TempDatabase();
        await Init(db);
        var original = Snapshot(db);

        // Someone works with the demo: adds a customer, a request and changes a priority.
        var extra = await db.Customers().AddAsync(PersistenceTestData.NewCustomer("Extra customer"), Token);
        await db.Requests().AddAsync(PersistenceTestData.NewRequest(extra.Value, "EXTRA-1"), Token);
        var first = (await db.Requests().ListAsync(null, Token)).First(r => r.CustomerName != "Extra customer");
        await db.Requests().UpdateAsync(first.RequestId, null, r => r.Items[0].ChangePriority(Priority.Normal, PersistenceTestData.Now.AddDays(1)), Token);
        Assert.NotEqual(original, Snapshot(db));

        var summary = await Reset(db);

        Assert.Equal(new DemoSeedSummary(3, 0, 3, 0), summary.Seed);
        Assert.Equal(original, Snapshot(db));
        Assert.Equal(DemoDatabaseState.Demo, DemoDatabase.Inspect(db.Path));
        Assert.Empty(TemporaryFilesNextTo(db));
    }

    [Fact]
    public async Task Reset_twice_in_a_row_gives_the_same_data_without_duplicates()
    {
        using var db = new TempDatabase();
        await Init(db);

        await Reset(db);
        var afterFirst = Snapshot(db);
        await Reset(db);

        Assert.Equal(afterFirst, Snapshot(db));
        Assert.Equal(3, Count(db, "Customers"));
        Assert.Equal(3, Count(db, "Requests"));
        Assert.Equal(9, Count(db, "RequestItems"));
        Assert.Equal(0, Count(db, "ItemStages"));
        Assert.Equal(0, Count(db, "QcApprovals"));
    }

    // ---- the sample data ----

    [Fact]
    public async Task The_sample_data_has_the_expected_counts_and_one_work_type_per_request()
    {
        using var db = new TempDatabase();
        await Init(db);

        var customers = await db.Customers().ListAsync(false, Token);
        var requests = await LoadAllAsync(db);
        var items = requests.SelectMany(r => r.Items).ToList();

        Assert.Equal(3, customers.Count);
        Assert.Equal(3, customers.Select(c => c.Value.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(3, requests.Count);
        Assert.Equal(3, requests.Select(r => r.Key).Distinct().Count());
        Assert.Equal(new[] { 2, 2, 5 }, requests.Select(r => r.Items.Count).Order().ToArray());
        // Every request has exactly one work type that its items inherit (Decision Log #84); the sample has two MAKE requests (one with a nested assembly) and one REPAIR request.
        Assert.Equal(2, requests.Count(r => r.WorkType == WorkType.Make));
        Assert.Equal(1, requests.Count(r => r.WorkType == WorkType.Repair));
        Assert.Equal(9, items.Count);

        // The requests waiting for Management carry a fictional PDF; the freshly created one has none, which shows the rule "attach the PDF before submitting" (Decision Log #87).
        Assert.Equal(2, requests.Count(r => r.HasRequestPdf));
        Assert.Equal(new[] { RequestStatus.Created }, requests.Where(r => !r.HasRequestPdf).Select(r => r.Status).ToArray());
        Assert.All(requests.Where(r => r.HasRequestPdf), r => Assert.EndsWith(".pdf", r.CurrentRequestPdf!.FileName, StringComparison.Ordinal));
        Assert.Equal(9, items.Select(i => i.Id).Distinct().Count());
    }

    [Fact]
    public async Task The_sample_requests_are_valid_under_the_current_workflow_with_no_unit_less_stage_progress()
    {
        using var db = new TempDatabase();
        await Init(db);
        var requests = await LoadAllAsync(db);
        var items = requests.SelectMany(r => r.Items).ToList();

        // Only the two states that precede Management's decision are seeded; stages, queue positions and QC approvals are created through the production-unit workflow (docs/DEMO_WALKTHROUGH.md).
        Assert.Equal(new[] { RequestStatus.Created, RequestStatus.SentToManagement }, requests.Select(r => r.Status).Distinct().OrderBy(s => (int)s).ToArray());
        Assert.Equal(new[] { Priority.VeryUrgent, Priority.Urgent, Priority.Normal }, items.Select(i => i.Priority).Distinct().OrderBy(p => p.SortOrder()).ToArray());
        Assert.All(items, i => Assert.Empty(i.Stages));
        Assert.All(items, i => Assert.Empty(i.QcApprovals));
        Assert.Empty(db.Query("SELECT Id FROM WorkSessions"));
        Assert.Empty(db.Query("SELECT Id FROM QueueOverrides"));
    }

    [Fact]
    public async Task The_sample_data_is_clearly_marked_as_demonstration_data_and_survives_reopening()
    {
        using var db = new TempDatabase();
        await Init(db);

        // New repositories and contexts: the data comes from the file, not from memory.
        var customers = await db.Customers().ListAsync(false, Token);
        var requests = await LoadAllAsync(db);

        Assert.All(customers, c => Assert.EndsWith(DemoDataSeeder.DemoNameSuffix, c.Value.Name, StringComparison.Ordinal));
        Assert.All(customers, c => Assert.True(c.Value.IsActive));
        Assert.All(requests, r => Assert.StartsWith(DemoDataSeeder.RequestNoPrefix, r.RequestNo.Value, StringComparison.Ordinal));
        Assert.All(requests, r => Assert.StartsWith(DemoDataSeeder.TitlePrefix, r.Title, StringComparison.Ordinal));
        var centralId = long.Parse(db.Query("SELECT Id FROM Users WHERE Username = 'demo.secretary'").Single(), System.Globalization.CultureInfo.InvariantCulture);
        Assert.All(customers, c => Assert.Equal(centralId, c.Value.CreatedByUserId));
        Assert.All(requests, r => Assert.Equal(centralId, r.CreatedByUserId));
        Assert.All(requests.SelectMany(r => r.Items), i => Assert.Equal(centralId, i.CreatedByUserId));
        Assert.All(customers, c => Assert.Equal(DateTimeKind.Utc, c.Value.CreatedAtUtc.Kind));
        Assert.All(requests, r => Assert.Equal(DateTimeKind.Utc, r.CreatedAtUtc.Kind));

        // Persian text is stored exactly, and the Jalali year is derived from the stored request date.
        Assert.Contains(requests, r => r.Items.Any(i => i.PartName == "چرخ‌دنده مارپیچ"));
        Assert.All(requests, r => Assert.Equal(Dates.GetJalaliYear(r.RequestDate), r.JalaliRequestYear));
        Assert.Equal(new[] { 1405 }, requests.Select(r => r.JalaliRequestYear).Distinct().OrderBy(y => y).ToArray());
        Assert.Equal(new[] { "1405/07/15", "1405/07/18", "1405/07/20" }, requests.OrderBy(r => r.RequestDate).Select(r => Dates.FormatDate(r.RequestDate)).ToArray());
    }

    [Fact]
    public async Task The_sample_data_shows_a_nested_assembly_valid_repair_items_and_fictional_drawings()
    {
        using var db = new TempDatabase();
        await Init(db);
        var requests = await LoadAllAsync(db);

        // The assembly request: system numbers derive from the request number (Decision Log #121), nested to the third level, children numbered per parent.
        var assembly = requests.Single(r => r.RequestNo.Value.EndsWith("1405-016", StringComparison.Ordinal));
        Assert.Equal(
            new[] { "DEMO-1405-016-001", "DEMO-1405-016-001.001", "DEMO-1405-016-001.002", "DEMO-1405-016-001.002.001", "DEMO-1405-016-001.002.002" },
            assembly.Items.Select(i => i.ItemNo).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "A.00", "A.20" }, assembly.Items.Where(i => i.IsAssembly).Select(i => i.CustomerItemRef!).Order(StringComparer.Ordinal).ToArray());
        var shaft = assembly.Items.Single(i => i.CustomerItemRef == "A.21");
        Assert.Equal("DEMO-1405-016-001.002.001", shaft.ItemNo);
        Assert.Equal("DEMO-1405-016-001.002", assembly.Items.Single(i => i.Id == shaft.ParentItemId).ItemNo);

        // The sample REPAIR items are complete: a delivery date on or after the request date and a warehouse code, so they can be released (Decision Log #124 B5).
        var repair = requests.Single(r => r.WorkType == WorkType.Repair);
        Assert.All(repair.Items, i =>
        {
            Assert.False(i.IsRepairIncomplete(WorkType.Repair));
            Assert.True(i.DeliveryDate >= repair.RequestDate);
            Assert.StartsWith("WH-DEMO-", i.WarehouseItemCode, StringComparison.Ordinal);
        });

        // Four fictional drawings uploaded by the Drawing account, one current revision each.
        Assert.Equal(4, Count(db, "ItemDrawings"));
        Assert.Equal(4, Count(db, "ItemDrawings WHERE SupersededAtUtc IS NULL"));
        Assert.Equal(4, Count(db, "Documents WHERE Type = 3"));
        Assert.Equal(new[] { "demo.drawing" }, db.Query("SELECT DISTINCT u.Username FROM ItemDrawings d JOIN Users u ON u.Id = d.UploadedByUserId"));
        Assert.All(db.Query("SELECT FileName FROM ItemDrawings"), name => Assert.StartsWith("DEMO-1405-", name, StringComparison.Ordinal));
    }
}
