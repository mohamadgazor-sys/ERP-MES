using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Infrastructure.Tests;

// The schema comes only from the committed EF Core migration (never from EnsureCreated). These tests use real SQLite files.
public class MigrationTests
{
    [Fact]
    public async Task A_fresh_database_is_created_by_applying_the_committed_migrations()
    {
        using var db = new TempDatabase();
        Assert.False(db.Exists);

        await db.MigrateWithActorsAsync();

        Assert.True(db.Exists);
        var tables = db.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name");

        // EF Core keeps its own bookkeeping tables (the migration history, and in EF Core 10 a migrations lock); only the application tables are compared.
        Assert.Contains("__EFMigrationsHistory", tables);
        Assert.Equal(
            new[] { "AuditLog", "ContractDocumentLinks", "ContractHistory", "ContractVersions", "Contracts", "Customers", "Departments", "Documents", "DrawingClarifications", "ItemImportRows", "ItemImports", "ItemStages", "MachineHourlyRates", "Machines", "MaterialPrices", "Materials", "OperatorMachineAssignments", "OperatorMachineQualifications", "Operators", "ProductionUnits", "QcApprovals", "QuantityTypeMembers", "QuantityTypes", "QueueOverrideHistory", "QueueOverrides", "RawMaterialInquiries", "RawMaterialInquiryDispositions", "RawMaterialInquiryHistory", "RawMaterialInquiryLines", "RawMaterialInquiryResponses", "RawMaterialInquirySupplyRoutes", "RequestDocumentLinks", "RequestItems", "RequestWorkflowEntries", "Requests", "Roles", "SystemSetup", "UnitConversions", "UnitsOfMeasure", "UserCredentials", "UserUnitDesignations", "Users" },
            tables.Where(t => !t.StartsWith("__EF", StringComparison.Ordinal)).ToArray());

        await using var context = db.NewContext();
        var applied = (await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).ToArray();
        Assert.Equal(15, applied.Length);
        Assert.EndsWith("_InitialCreate", applied[0], StringComparison.Ordinal);
        Assert.EndsWith("_AddAuthentication", applied[1], StringComparison.Ordinal);
        Assert.EndsWith("_SingleWorkTypePerRequest", applied[2], StringComparison.Ordinal);
        Assert.EndsWith("_AddRequestPdfDocuments", applied[3], StringComparison.Ordinal);
        Assert.EndsWith("_AddRequestWorkflowHistory", applied[4], StringComparison.Ordinal);
        Assert.EndsWith("_AddQcMasterData", applied[5], StringComparison.Ordinal);
        Assert.EndsWith("_AddPlanningMachines", applied[6], StringComparison.Ordinal);
        Assert.EndsWith("_AddOperators", applied[7], StringComparison.Ordinal);
        Assert.EndsWith("_AddMaterials", applied[8], StringComparison.Ordinal);
        Assert.EndsWith("_AddContractGate", applied[9], StringComparison.Ordinal);
        Assert.EndsWith("_AddRawMaterialInquiries", applied[10], StringComparison.Ordinal);
        Assert.EndsWith("_AddInquiryDispositionsAndClarifications", applied[11], StringComparison.Ordinal);
        Assert.EndsWith("_AddItemSpecifications", applied[12], StringComparison.Ordinal);
        Assert.EndsWith("_AddItemImportLog", applied[13], StringComparison.Ordinal);
        Assert.EndsWith("_AddUnitQueue", applied[14], StringComparison.Ordinal);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_committed_migration_and_snapshot_match_the_current_model()
    {
        using var db = new TempDatabase();
        await using var context = db.NewContext();

        // Fails if the model changed without a new migration, or if the snapshot was edited away from the model.
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(15, context.Database.GetMigrations().Count());
    }

    [Fact]
    public async Task Opening_an_existing_migrated_database_keeps_its_schema_and_data()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();

        await using (var context = db.NewContext())
        {
            context.Customers.Add(PersistenceTestData.NewCustomer());
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var historyBefore = db.Query("SELECT MigrationId FROM __EFMigrationsHistory");

        // The normal startup path again, as after an application restart: nothing is recreated or re-applied.
        await db.MigrateWithActorsAsync();
        await db.MigrateWithActorsAsync();

        Assert.Equal(historyBefore, db.Query("SELECT MigrationId FROM __EFMigrationsHistory"));
        Assert.Equal(new[] { "Fictional Customer A" }, db.Query("SELECT Name FROM Customers").ToArray());

        await using var reopened = db.NewContext();
        Assert.Empty(await reopened.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Fictional Customer A", (await reopened.Customers.SingleAsync(TestContext.Current.CancellationToken)).Name);
    }

    [Fact]
    public async Task Business_keys_are_unique_indexes_with_the_specified_columns()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();

        Assert.Equal(new[] { "CustomerId,RequestNo,JalaliRequestYear" }, UniqueIndexColumns(db, "Requests"));
        Assert.Equal(new[] { "RequestId,ItemNo" }, UniqueIndexColumns(db, "RequestItems"));
        Assert.Equal(new[] { "RequestItemId,StageNo" }, UniqueIndexColumns(db, "ItemStages"));
    }

    [Fact]
    public async Task Every_foreign_key_restricts_deletion_because_transactional_rows_are_not_physically_deleted()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();

        var keys = new List<string>();
        foreach (var table in new[] { "Customers", "Requests", "RequestItems", "ItemStages", "QcApprovals" })
        {
            keys.AddRange(db.Query($"SELECT '{table}' || ':' || \"from\" || '->' || \"table\" || ':' || on_delete FROM pragma_foreign_key_list('{table}')"));
        }

        Assert.Equal(
            new[]
            {
                "Customers:CreatedByUserId->Users:RESTRICT",
                "ItemStages:MachineId->Machines:RESTRICT",
                "ItemStages:ProductionUnitId->ProductionUnits:RESTRICT",
                "ItemStages:RequestItemId->RequestItems:RESTRICT",
                "ItemStages:StoppedByUserId->Users:RESTRICT",
                "QcApprovals:ApprovedByUserId->Users:RESTRICT",
                "QcApprovals:RequestItemId->ItemStages:RESTRICT",
                "QcApprovals:RequestItemId->RequestItems:RESTRICT",
                "QcApprovals:StageNo->ItemStages:RESTRICT",
                "RequestItems:CreatedByUserId->Users:RESTRICT",
                "RequestItems:MaterialId->Materials:RESTRICT",
                "RequestItems:QuantityTypeId->QuantityTypes:RESTRICT",
                "RequestItems:RequestId->Requests:RESTRICT",
                "Requests:CreatedByUserId->Users:RESTRICT",
                "Requests:CustomerId->Customers:RESTRICT",
            },
            keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Foreign_keys_are_enforced_on_every_connection()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();

        Assert.Equal(new[] { "1" }, db.Query("PRAGMA foreign_keys").ToArray());
        Assert.Throws<SqliteException>(() => db.Execute(
            "INSERT INTO Requests (CustomerId, RequestNo, RequestDate, JalaliRequestYear, Status, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc) " +
            "VALUES (999, 'X', '2026-10-12', 1405, 1, 1, '2026-10-15 09:30:15', '2026-10-15 09:30:15')"));
    }

    [Fact]
    public async Task A_customer_with_requests_cannot_be_deleted()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();
        await using (var context = db.NewContext())
        {
            var customer = PersistenceTestData.NewCustomer();
            context.Customers.Add(customer);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            context.Requests.Add(PersistenceTestData.NewRequest(customer));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Throws<SqliteException>(() => db.Execute("DELETE FROM Customers"));
        Assert.Single(db.Query("SELECT Id FROM Customers"));
    }

    [Fact]
    public async Task The_request_business_key_is_enforced_and_request_numbers_are_case_sensitive()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();

        await using (var context = db.NewContext())
        {
            var customer = PersistenceTestData.NewCustomer();
            var other = PersistenceTestData.NewCustomer("Fictional Customer B");
            context.Customers.AddRange(customer, other);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            // Same number: a different case, another customer and another Jalali year are all different business keys.
            context.Requests.Add(PersistenceTestData.NewRequest(customer, "wsj-898"));
            context.Requests.Add(PersistenceTestData.NewRequest(customer, "WSJ-898"));
            context.Requests.Add(PersistenceTestData.NewRequest(other, "wsj-898"));
            context.Requests.Add(PersistenceTestData.NewRequest(customer, "wsj-898", new DateOnly(2027, 3, 21)));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal("4", db.Query("SELECT COUNT(*) FROM Requests").Single());

        await using var duplicateContext = db.NewContext();
        var first = await duplicateContext.Customers.OrderBy(c => c.Id).FirstAsync(TestContext.Current.CancellationToken);
        duplicateContext.Requests.Add(PersistenceTestData.NewRequest(first, "wsj-898"));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Item_numbers_and_stage_numbers_are_unique_at_the_database_level_and_item_numbers_are_case_sensitive()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();

        await using (var context = db.NewContext())
        {
            var customer = PersistenceTestData.NewCustomer();
            context.Customers.Add(customer);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var request = PersistenceTestData.NewRequest(customer);
            request.AddItem(0, "A-01", "Ring", 1m, 1, PersistenceTestData.Now);
            request.AddItem(0, "a-01", "Ring", 1m, 1, PersistenceTestData.Now);
            request.AddItem(0, "1.1", "Ring", 1m, 1, PersistenceTestData.Now);
            request.AddItem(0, "1.10", "Ring", 1m, 1, PersistenceTestData.Now).DefineInitialStage("Turning", 1m);
            context.Requests.Add(request);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal("4", db.Query("SELECT COUNT(*) FROM RequestItems").Single());

        // The domain prevents duplicates; the database must refuse them too if anything bypasses the domain.
        Assert.Throws<SqliteException>(() => db.Execute(
            "INSERT INTO RequestItems (RequestId, ItemNo, PartName, RequestedQuantity, Priority, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc) " +
            "SELECT RequestId, ItemNo, PartName, RequestedQuantity, Priority, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc FROM RequestItems WHERE ItemNo = 'A-01'"));
        Assert.Throws<SqliteException>(() => db.Execute(
            "INSERT INTO ItemStages (RequestItemId, StageNo, StageTitle, PlannedMinutesPerPiece, PlannedQuantity, Status) " +
            "SELECT RequestItemId, StageNo, StageTitle, PlannedMinutesPerPiece, PlannedQuantity, Status FROM ItemStages"));
    }

    [Fact]
    public async Task A_qc_approval_must_refer_to_an_existing_stage_of_the_same_item()
    {
        using var db = new TempDatabase();
        await db.MigrateWithActorsAsync();
        await PersistenceTestData.SeedSampleRequestAsync(db);

        var makeItemId = db.Query("SELECT Id FROM RequestItems WHERE ItemNo = '1.10'").Single();
        var repairItemId = db.Query("SELECT Id FROM RequestItems WHERE ItemNo = '2.10'").Single();
        string Insert(string itemId, int stageNo) =>
            "INSERT INTO QcApprovals (RequestItemId, StageNo, ApprovedQuantity, ApprovedByUserId, ApprovedAtUtc) " +
            $"VALUES ({itemId}, {stageNo}, '1', 9, '2026-10-15 09:30:15')";

        // The item exists but has no stage 99, and stage 3 exists only on the other item: both are rejected by SQLite.
        var nonexistent = Assert.Throws<SqliteException>(() => db.Execute(Insert(makeItemId, 99)));
        Assert.Equal(19, nonexistent.SqliteErrorCode);
        Assert.Throws<SqliteException>(() => db.Execute(Insert(repairItemId, 3)));
        Assert.Single(db.Query("SELECT Id FROM QcApprovals"));

        // The append-only history still works: another approval row for an existing stage is accepted.
        Assert.Equal(1, db.Execute(Insert(makeItemId, 1)));
        Assert.Equal(2, db.Query("SELECT Id FROM QcApprovals").Count);
    }

    private static string[] UniqueIndexColumns(TempDatabase db, string table)
    {
        var result = new List<string>();
        foreach (var index in db.Query($"SELECT name FROM pragma_index_list('{table}') WHERE \"unique\" = 1 AND origin IN ('c', 'u')"))
        {
            var columns = db.Query($"SELECT name FROM pragma_index_info('{index}') ORDER BY seqno");
            result.Add(string.Join(",", columns));
        }

        return result.ToArray();
    }
}
