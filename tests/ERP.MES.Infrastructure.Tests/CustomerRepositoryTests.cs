using ERP.MES.Application.Persistence;

namespace ERP.MES.Infrastructure.Tests;

// Customer persistence through the Application contract, on a real SQLite file. Every step uses a new repository (new contexts), as after a restart.
public class CustomerRepositoryTests
{
    private static readonly DateTime Now = PersistenceTestData.Now;
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_customer_is_saved_with_a_generated_id_and_version_one_and_reloaded_exactly()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();

        var added = await db.Customers().AddAsync(PersistenceTestData.NewCustomer("  Fictional Customer  "), Token);

        Assert.True(added.Value.Id > 0);
        Assert.Equal(1L, added.Version);

        var loaded = await db.Customers().FindAsync(added.Value.Id, Token);
        Assert.NotNull(loaded);
        Assert.Equal(1L, loaded.Version);
        Assert.Equal("  Fictional Customer  ", loaded.Value.Name);
        Assert.True(loaded.Value.IsActive);
        Assert.Equal(Now, loaded.Value.CreatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.Value.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, loaded.Value.UpdatedAtUtc.Kind);
    }

    [Fact]
    public async Task Customers_can_be_listed_and_filtered_by_active_state()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var first = await db.Customers().AddAsync(PersistenceTestData.NewCustomer("A"), Token);
        await db.Customers().AddAsync(PersistenceTestData.NewCustomer("B"), Token);
        await db.Customers().UpdateAsync(first.Value.Id, null, c => c.SetActive(false, Now.AddMinutes(1)), Token);

        var all = await db.Customers().ListAsync(activeOnly: false, Token);
        var active = await db.Customers().ListAsync(activeOnly: true, Token);

        Assert.Equal(new[] { "A", "B" }, all.Select(c => c.Value.Name).ToArray());
        Assert.Equal(new[] { "B" }, active.Select(c => c.Value.Name).ToArray());
    }

    [Fact]
    public async Task An_update_runs_the_domain_method_saves_and_raises_the_version()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var added = await db.Customers().AddAsync(PersistenceTestData.NewCustomer("Before"), Token);

        var updated = await db.Customers().UpdateAsync(added.Value.Id, added.Version, c => c.Rename("After", Now.AddMinutes(5)), Token);

        Assert.Equal(2L, updated.Version);
        var reloaded = await db.Customers().FindAsync(added.Value.Id, Token);
        Assert.Equal("After", reloaded!.Value.Name);
        Assert.Equal(Now.AddMinutes(5), reloaded.Value.UpdatedAtUtc);
        Assert.Equal(2L, reloaded.Version);
    }

    [Fact]
    public async Task A_domain_rule_violation_in_an_update_propagates_and_nothing_is_saved()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var added = await db.Customers().AddAsync(PersistenceTestData.NewCustomer("Keep me"), Token);

        // The persistence layer does not re-implement the Domain's rules and does not hide its exceptions.
        await Assert.ThrowsAsync<ArgumentException>(() => db.Customers().UpdateAsync(added.Value.Id, null, c => c.Rename("   ", Now.AddMinutes(1)), Token));
        await Assert.ThrowsAsync<ArgumentException>(() => db.Customers().UpdateAsync(added.Value.Id, null, c => c.SetActive(false, DateTime.SpecifyKind(Now, DateTimeKind.Local)), Token));

        var reloaded = await db.Customers().FindAsync(added.Value.Id, Token);
        Assert.Equal("Keep me", reloaded!.Value.Name);
        Assert.True(reloaded.Value.IsActive);
        Assert.Equal(1L, reloaded.Version);
    }

    [Fact]
    public async Task A_customer_with_an_id_cannot_be_added_again_and_a_missing_customer_is_reported()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var added = await db.Customers().AddAsync(PersistenceTestData.NewCustomer(), Token);

        await Assert.ThrowsAsync<ArgumentException>(() => db.Customers().AddAsync(added.Value, Token));
        Assert.Null(await db.Customers().FindAsync(9999, Token));
        var missing = await Assert.ThrowsAsync<RecordNotFoundException>(() => db.Customers().UpdateAsync(9999, null, c => c.SetActive(false, Now), Token));
        Assert.Equal("customer", missing.RecordKind);
        Assert.Equal(9999, missing.Id);
    }

    [Fact]
    public async Task An_update_based_on_a_stale_version_is_rejected_and_does_not_overwrite_the_first_update()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var added = await db.Customers().AddAsync(PersistenceTestData.NewCustomer("Original"), Token);

        // Two users loaded the same version.
        var userOne = await db.Customers().FindAsync(added.Value.Id, Token);
        var userTwo = await db.Customers().FindAsync(added.Value.Id, Token);
        Assert.Equal(userOne!.Version, userTwo!.Version);

        await db.Customers().UpdateAsync(added.Value.Id, userOne.Version, c => c.Rename("First update", Now.AddMinutes(1)), Token);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => db.Customers().UpdateAsync(added.Value.Id, userTwo.Version, c => c.Rename("Stale update", Now.AddMinutes(2)), Token));

        var stored = await db.Customers().FindAsync(added.Value.Id, Token);
        Assert.Equal("First update", stored!.Value.Name);
        Assert.Equal(2L, stored.Version);
    }

    [Fact]
    public async Task A_change_saved_by_someone_else_during_the_update_is_reported_and_not_overwritten()
    {
        using var db = new TempDatabase();
        await db.MigrateAsync();
        var added = await db.Customers().AddAsync(PersistenceTestData.NewCustomer("Original"), Token);

        // The change delegate runs between the load and the save; a raw statement stands for another user saving in that window.
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => db.Customers().UpdateAsync(
            added.Value.Id,
            null,
            c =>
            {
                db.Execute("UPDATE Customers SET Name = 'Changed by another user', Version = Version + 1");
                c.Rename("My update", Now.AddMinutes(1));
            },
            Token));

        var stored = await db.Customers().FindAsync(added.Value.Id, Token);
        Assert.Equal("Changed by another user", stored!.Value.Name);
        Assert.Equal(2L, stored.Version);
    }
}
