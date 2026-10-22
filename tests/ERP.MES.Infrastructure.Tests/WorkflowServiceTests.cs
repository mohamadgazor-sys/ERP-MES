using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Persistence;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// The customer and request use cases (Decision Log #34, #75) on a real SQLite file with the real authorization: who may do what is decided by the Application from the
// database at the time of each call, never by the principal the caller holds. Persistence is proven by reading through NEW services and contexts.
public class WorkflowServiceTests
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

        public CustomerService Customers => Services.Customers;

        public RequestService Requests => Services.Requests;

        /// <summary>New services over the same file, as after restarting the application.</summary>
        public WorkflowServices Restarted() => Create(Fixture);

        public void Dispose() => Fixture.Dispose();
    }

    private static WorkflowServices Create(SecurityFixture fixture) =>
        WorkflowServices.Create(fixture.Db.ContextFactory, fixture.Services.Authorization, PersistenceTestData.Dates, fixture.Clock);

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
            Services = Create(fixture),
        };
    }

    private static async Task<Stored<Customer>> CustomerAsync(World w, string name = "Fictional Customer A")
    {
        var outcome = await w.Customers.CreateAsync(w.Central, name, Token);
        Assert.True(outcome.Succeeded, outcome.Status.ToString());
        return outcome.Value!;
    }

    private static async Task<Stored<Request>> RequestAsync(World w, long customerId, string requestNo = "P-565", string date = "1405/07/20", WorkType workType = WorkType.Make)
    {
        var outcome = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(customerId, requestNo, workType, date, "Fictional request", null), Token);
        Assert.True(outcome.Succeeded, outcome.Status + " " + outcome.Issue);
        return outcome.Value!;
    }

    private static AddItemCommand Make(string itemNo = "1.10", Priority priority = Priority.Normal) => new(itemNo, "Ring A", 10m, priority);

    private static AddItemCommand Repair(string itemNo = "2.10", Priority priority = Priority.Normal) => new(itemNo, "Shaft B", 4m, priority);

    /// <summary>A small FICTIONAL but valid-looking PDF: the signature line and a trailer. <paramref name="size"/> pads it to an exact number of bytes when given.</summary>
    private static byte[] PdfBytes(int? size = null)
    {
        var head = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional test document\n");
        var tail = System.Text.Encoding.ASCII.GetBytes("\n%%EOF\n");
        if (size is null)
        {
            return [.. head, .. tail];
        }

        var bytes = new byte[size.Value];
        Array.Fill(bytes, (byte)' ');
        head.AsSpan(0, Math.Min(head.Length, bytes.Length)).CopyTo(bytes);
        return bytes;
    }

    /// <summary>Attaches a fictional request PDF as Central (Decision Log #87) and returns the request at its new version.</summary>
    private static async Task<Stored<Request>> WithPdfAsync(World w, Stored<Request> request, string fileName = "request.pdf")
    {
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var attached = await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version, fileName, PdfBytes(), Token);
        Assert.True(attached.Succeeded, attached.Status + " " + attached.Issue);
        return attached.Value!;
    }

    private static long Count(World w, string table) => long.Parse(w.Fixture.Db.Query("SELECT COUNT(*) FROM " + table).Single(), System.Globalization.CultureInfo.InvariantCulture);

    // ---- customers: who may maintain, validation, persistence ----

    [Fact]
    public async Task Central_and_Planning_create_customers_and_the_name_is_stored_exactly_as_typed()
    {
        using var w = await StartAsync();

        var byCentral = await w.Customers.CreateAsync(w.Central, " Acme Ltd ", Token);
        var byPlanning = await w.Customers.CreateAsync(w.Planning, "wsj Co", Token);

        Assert.True(byCentral.Succeeded);
        Assert.True(byPlanning.Succeeded);
        Assert.Equal(" Acme Ltd ", byCentral.Value!.Value.Name);
        Assert.True(byCentral.Value.Value.IsActive);
        Assert.Equal(w.Central.UserId, byCentral.Value.Value.CreatedByUserId);
        Assert.Equal(w.Planning.UserId, byPlanning.Value!.Value.CreatedByUserId);

        // Visible after a restart: new services, new contexts.
        var listed = await w.Restarted().Customers.ListAsync(w.Central, activeOnly: false, Token);
        Assert.Equal(new[] { " Acme Ltd ", "wsj Co" }, listed.Value!.Select(c => c.Value.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Nobody_else_may_create_rename_or_deactivate_a_customer()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);

        foreach (var user in new[] { w.Drawing, w.Management, w.Qc, w.Admin })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Customers.CreateAsync(user, "Other", Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Customers.RenameAsync(user, customer.Value.Id, customer.Version, "Renamed", Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Customers.SetActiveAsync(user, customer.Value.Id, customer.Version, false, Token)).Status);
        }

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Customers.CreateAsync(null, "Anonymous", Token)).Status);
        Assert.Equal(1, Count(w, "Customers"));
        var unchanged = (await w.Customers.ListAsync(w.Management, false, Token)).Value!.Single();
        Assert.Equal("Fictional Customer A", unchanged.Value.Name);
        Assert.True(unchanged.Value.IsActive);
    }

    [Fact]
    public async Task Every_active_user_may_view_and_an_inactive_one_may_not()
    {
        using var w = await StartAsync();
        await CustomerAsync(w);

        foreach (var user in new[] { w.Central, w.Planning, w.Drawing, w.Management, w.Qc, w.Admin })
        {
            Assert.Single((await w.Customers.ListAsync(user, false, Token)).Value!);
        }

        Assert.Equal(AccountStatus.Succeeded, (await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Qc.UserId, false, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Customers.ListAsync(w.Qc, false, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Customers.ListAsync(null, false, Token)).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public async Task A_customer_needs_a_name_and_nothing_is_saved_without_one(string? name)
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);

        var created = await w.Customers.CreateAsync(w.Central, name, Token);
        var renamed = await w.Customers.RenameAsync(w.Central, customer.Value.Id, customer.Version, name, Token);

        Assert.Equal(OperationStatus.ValidationFailed, created.Status);
        Assert.Equal(ValidationIssue.CustomerNameRequired, created.Issue);
        Assert.Equal(ValidationIssue.CustomerNameRequired, renamed.Issue);
        Assert.Equal(1, Count(w, "Customers"));
        Assert.Equal("Fictional Customer A", (await w.Restarted().Customers.ListAsync(w.Central, false, Token)).Value!.Single().Value.Name);
    }

    [Fact]
    public async Task Renaming_persists_and_a_stale_version_is_a_conflict_that_changes_nothing()
    {
        using var w = await StartAsync();
        var loaded = await CustomerAsync(w);

        var first = await w.Customers.RenameAsync(w.Planning, loaded.Value.Id, loaded.Version, "Renamed by Planning", Token);
        var stale = await w.Customers.RenameAsync(w.Central, loaded.Value.Id, loaded.Version, "Renamed by Central", Token);

        Assert.True(first.Succeeded);
        Assert.NotEqual(loaded.Version, first.Value!.Version);
        Assert.Equal(OperationStatus.ConcurrencyConflict, stale.Status);
        var after = (await w.Restarted().Customers.ListAsync(w.Central, false, Token)).Value!.Single();
        Assert.Equal("Renamed by Planning", after.Value.Name);

        // With the version that was reloaded, the second change goes through.
        Assert.True((await w.Customers.RenameAsync(w.Central, after.Value.Id, after.Version, "Renamed by Central", Token)).Succeeded);
    }

    [Fact]
    public async Task Customers_are_deactivated_and_reactivated_never_deleted()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        await RequestAsync(w, customer.Value.Id);

        var off = await w.Customers.SetActiveAsync(w.Central, customer.Value.Id, customer.Version, false, Token);

        Assert.True(off.Succeeded);
        Assert.False(off.Value!.Value.IsActive);
        Assert.Empty((await w.Customers.ListAsync(w.Central, activeOnly: true, Token)).Value!);
        Assert.Single((await w.Customers.ListAsync(w.Central, activeOnly: false, Token)).Value!);
        Assert.Equal(1, Count(w, "Customers"));
        Assert.Equal(1, Count(w, "Requests"));

        var on = await w.Customers.SetActiveAsync(w.Central, customer.Value.Id, off.Value.Version, true, Token);
        Assert.True(on.Value!.Value.IsActive);
        Assert.Single((await w.Restarted().Customers.ListAsync(w.Central, activeOnly: true, Token)).Value!);
    }

    [Fact]
    public async Task An_unknown_customer_is_not_found()
    {
        using var w = await StartAsync();

        Assert.Equal(OperationStatus.NotFound, (await w.Customers.RenameAsync(w.Central, 4242, 1, "Name", Token)).Status);
        Assert.Equal(OperationStatus.NotFound, (await w.Customers.SetActiveAsync(w.Central, 4242, 1, false, Token)).Status);
    }

    [Fact]
    public async Task A_stale_principal_cannot_maintain_customers_after_the_department_changed_or_the_user_was_deactivated()
    {
        using var w = await StartAsync();
        await CustomerAsync(w);
        Assert.True((await w.Customers.CreateAsync(w.Planning, "Before", Token)).Succeeded);

        // Planning moves to QC in the database; the object in the window still says PLANNING.
        w.Fixture.Db.Execute("UPDATE Users SET DepartmentId = (SELECT Id FROM Departments WHERE Code = '" + DepartmentCodes.Qc + "') WHERE Username = 'planning.one'");
        Assert.Equal(DepartmentCodes.Planning, w.Planning.DepartmentCode);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Customers.CreateAsync(w.Planning, "After", Token)).Status);

        Assert.Equal(AccountStatus.Succeeded, (await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Central.UserId, false, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Customers.CreateAsync(w.Central, "Late", Token)).Status);
        Assert.Equal(2, Count(w, "Customers"));
    }

    // ---- requests: creation ----

    [Fact]
    public async Task Central_creates_a_request_with_the_number_as_typed_and_the_jalali_year_of_its_date()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);

        var outcome = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "wsj-898", WorkType.Make, "۱۴۰۵/۰۷/۲۰", "Title", "Description"), Token);

        Assert.True(outcome.Succeeded);
        var request = outcome.Value!.Value;
        Assert.Equal(RequestStatus.Created, request.Status);
        Assert.Equal("wsj-898", request.RequestNo.Value);
        Assert.Equal(1405, request.JalaliRequestYear);
        Assert.Equal(new DateOnly(2026, 10, 12), request.RequestDate);
        Assert.Equal(w.Central.UserId, request.CreatedByUserId);

        var detail = (await w.Restarted().Requests.GetAsync(w.Management, request.Id, Token)).Value!;
        Assert.Equal("wsj-898", detail.Request.Value.RequestNo.Value);
        Assert.Equal("Fictional Customer A", detail.CustomerName);
        Assert.Equal("Title", detail.Request.Value.Title);
        Assert.Equal("Description", detail.Request.Value.Description);
    }

    [Fact]
    public async Task The_jalali_year_follows_the_nowruz_boundary_of_the_request_date()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);

        var lastDayOfYear = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "N-1", WorkType.Make, "1404/12/29", null, null), Token);
        var firstDayOfYear = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "N-2", WorkType.Make, "1405/01/01", null, null), Token);
        var leapDay = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "N-3", WorkType.Make, "1403/12/30", null, null), Token);

        Assert.Equal(1404, lastDayOfYear.Value!.Value.JalaliRequestYear);
        Assert.Equal(1405, firstDayOfYear.Value!.Value.JalaliRequestYear);
        Assert.Equal(1403, leapDay.Value!.Value.JalaliRequestYear);
        Assert.Equal(new DateOnly(2025, 3, 20), leapDay.Value.Value.RequestDate);
    }

    [Fact]
    public async Task Only_central_may_create_a_request()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);

        foreach (var user in new[] { w.Planning, w.Drawing, w.Management, w.Qc, w.Admin })
        {
            var outcome = await w.Requests.CreateAsync(user, new CreateRequestCommand(customer.Value.Id, "X-1", WorkType.Make, "1405/07/20", null, null), Token);
            Assert.Equal(OperationStatus.NotAuthorized, outcome.Status);
        }

        Assert.Equal(0, Count(w, "Requests"));
    }

    [Theory]
    [InlineData(null, "1405/07/20", ValidationIssue.RequestNoRequired)]
    [InlineData("", "1405/07/20", ValidationIssue.RequestNoRequired)]
    [InlineData(" P-1", "1405/07/20", ValidationIssue.RequestNoWhitespace)]
    [InlineData("P-1 ", "1405/07/20", ValidationIssue.RequestNoWhitespace)]
    [InlineData("P-1", null, ValidationIssue.RequestDateRequired)]
    [InlineData("P-1", "   ", ValidationIssue.RequestDateRequired)]
    [InlineData("P-1", "1405-07-20", ValidationIssue.RequestDateInvalid)]
    [InlineData("P-1", "1405/13/01", ValidationIssue.RequestDateInvalid)]
    [InlineData("P-1", "1404/12/30", ValidationIssue.RequestDateInvalid)]
    [InlineData("P-1", "1405/07", ValidationIssue.RequestDateInvalid)]
    public async Task An_invalid_request_is_refused_with_the_reason_and_nothing_is_saved(string? requestNo, string? date, ValidationIssue expected)
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);

        var outcome = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, requestNo, WorkType.Make, date, null, null), Token);

        Assert.Equal(OperationStatus.ValidationFailed, outcome.Status);
        Assert.Equal(expected, outcome.Issue);
        Assert.Equal(0, Count(w, "Requests"));
    }

    [Fact]
    public async Task A_date_error_reports_which_part_is_wrong()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);

        var day = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "P-1", WorkType.Make, "1404/12/30", null, null), Token);
        var month = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "P-1", WorkType.Make, "1405/13/01", null, null), Token);
        var character = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "P-1", WorkType.Make, "1405-07-20", null, null), Token);

        Assert.Equal(JalaliDateError.DayOutOfRange, day.DateError);
        Assert.Equal(JalaliDateError.MonthOutOfRange, month.DateError);
        Assert.Equal(JalaliDateError.InvalidCharacter, character.DateError);
    }

    [Fact]
    public async Task A_request_needs_an_existing_customer()
    {
        using var w = await StartAsync();

        Assert.Equal(ValidationIssue.CustomerRequired, (await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(0, "P-1", WorkType.Make, "1405/07/20", null, null), Token)).Issue);
        var missing = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(4242, "P-1", WorkType.Make, "1405/07/20", null, null), Token);
        Assert.Equal(OperationStatus.ValidationFailed, missing.Status);
        Assert.Equal(ValidationIssue.CustomerNotFound, missing.Issue);
        Assert.Equal(0, Count(w, "Requests"));
    }

    // ---- requests only for ACTIVE customers (Decision Log #76) ----

    private static string CustomerRows(World w) => string.Join("\n", w.Fixture.Db.Query("SELECT Id, Name, IsActive, Version, UpdatedAtUtc FROM Customers ORDER BY Id"));

    private static CreateRequestCommand NewRequestFor(long customerId, string requestNo = "P-1") => new(customerId, requestNo, WorkType.Make, "1405/07/20", "Title", null);

    [Fact]
    public async Task A_request_is_created_for_an_active_customer()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);

        var outcome = await w.Requests.CreateAsync(w.Central, NewRequestFor(customer.Value.Id), Token);

        Assert.True(outcome.Succeeded);
        Assert.Equal(customer.Value.Id, outcome.Value!.Value.CustomerId);
        Assert.Equal(1, Count(w, "Requests"));
    }

    [Fact]
    public async Task A_request_for_an_inactive_customer_is_refused_by_the_application_service_alone_and_nothing_changes()
    {
        using var w = await StartAsync();
        var active = await CustomerAsync(w, "Active customer");
        var inactive = await CustomerAsync(w, "Inactive customer");
        Assert.True((await w.Customers.SetActiveAsync(w.Central, inactive.Value.Id, inactive.Version, false, Token)).Succeeded);
        var before = CustomerRows(w);

        // Called directly, as a test or another client would: no screen, no dropdown, no cached list is involved.
        var outcome = await w.Requests.CreateAsync(w.Central, NewRequestFor(inactive.Value.Id), Token);

        Assert.Equal(OperationStatus.ValidationFailed, outcome.Status);
        Assert.Equal(ValidationIssue.CustomerInactive, outcome.Issue);
        Assert.Null(outcome.Value);
        Assert.Equal(0, Count(w, "Requests"));
        Assert.Equal(0, Count(w, "RequestItems"));

        // The customer was not reactivated or changed, and the request was not given to another customer.
        Assert.Equal(before, CustomerRows(w));
        var listed = (await w.Customers.ListAsync(w.Central, activeOnly: false, Token)).Value!;
        Assert.False(listed.Single(c => c.Value.Id == inactive.Value.Id).Value.IsActive);
        Assert.True(listed.Single(c => c.Value.Id == active.Value.Id).Value.IsActive);
        Assert.Empty((await w.Requests.ListAsync(w.Central, active.Value.Id, Token)).Value!);
    }

    [Fact]
    public async Task A_customer_deactivated_after_being_listed_but_before_the_request_is_created_is_refused()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);

        // The screen's dropdown was filled from this list while the customer was active...
        var shown = (await w.Customers.ListAsync(w.Central, activeOnly: true, Token)).Value!;
        Assert.Contains(shown, c => c.Value.Id == customer.Value.Id);

        // ...then someone else deactivates the customer...
        Assert.True((await w.Customers.SetActiveAsync(w.Planning, customer.Value.Id, customer.Version, false, Token)).Succeeded);

        // ...and the stale choice is used. The current persisted status decides.
        var outcome = await w.Requests.CreateAsync(w.Central, NewRequestFor(shown.Single().Value.Id), Token);

        Assert.Equal(ValidationIssue.CustomerInactive, outcome.Issue);
        Assert.Equal(0, Count(w, "Requests"));
        Assert.Empty((await w.Restarted().Requests.ListAsync(w.Central, null, Token)).Value!);
    }

    [Fact]
    public async Task A_nonexistent_customer_is_refused_with_its_own_code_and_no_other_customer_is_used()
    {
        using var w = await StartAsync();
        await CustomerAsync(w, "The only customer");
        var before = CustomerRows(w);

        foreach (var id in new long[] { 4242, long.MaxValue })
        {
            var outcome = await w.Requests.CreateAsync(w.Central, NewRequestFor(id), Token);
            Assert.Equal(OperationStatus.ValidationFailed, outcome.Status);
            Assert.Equal(ValidationIssue.CustomerNotFound, outcome.Issue);
        }

        Assert.Equal(0, Count(w, "Requests"));
        Assert.Equal(before, CustomerRows(w));
        Assert.Equal(ValidationIssue.CustomerRequired, (await w.Requests.CreateAsync(w.Central, NewRequestFor(0), Token)).Issue);
        Assert.Equal(ValidationIssue.CustomerRequired, (await w.Requests.CreateAsync(w.Central, NewRequestFor(-3), Token)).Issue);
    }

    [Fact]
    public async Task A_refused_request_leaves_the_database_exactly_as_it_was()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        await RequestAsync(w, customer.Value.Id, "OLD-1");
        Assert.True((await w.Customers.SetActiveAsync(w.Central, customer.Value.Id, customer.Version, false, Token)).Succeeded);
        var requests = w.Fixture.Db.Query("SELECT Id, CustomerId, RequestNo, Status, Version FROM Requests ORDER BY Id");
        var customers = CustomerRows(w);
        var audit = w.Fixture.AuditActionsInOrder();

        var outcome = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "NEW-1", WorkType.Make, "1405/07/21", "Title", "Description"), Token);

        Assert.False(outcome.Succeeded);
        Assert.Equal(requests, w.Fixture.Db.Query("SELECT Id, CustomerId, RequestNo, Status, Version FROM Requests ORDER BY Id"));
        Assert.Equal(customers, CustomerRows(w));
        Assert.Equal(audit, w.Fixture.AuditActionsInOrder());
        Assert.Equal(0, Count(w, "RequestItems"));
        Assert.Equal(0, Count(w, "ItemStages"));
    }

    [Fact]
    public async Task Reactivating_the_customer_allows_a_request_again_because_only_the_current_status_counts()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        var off = await w.Customers.SetActiveAsync(w.Central, customer.Value.Id, customer.Version, false, Token);
        Assert.Equal(ValidationIssue.CustomerInactive, (await w.Requests.CreateAsync(w.Central, NewRequestFor(customer.Value.Id), Token)).Issue);

        Assert.True((await w.Customers.SetActiveAsync(w.Central, customer.Value.Id, off.Value!.Version, true, Token)).Succeeded);

        Assert.True((await w.Requests.CreateAsync(w.Central, NewRequestFor(customer.Value.Id), Token)).Succeeded);
        Assert.Equal(1, Count(w, "Requests"));
    }

    [Fact]
    public async Task Requests_that_already_exist_keep_working_after_their_customer_is_deactivated()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        var created = await RequestAsync(w, customer.Value.Id);
        Assert.True((await w.Customers.SetActiveAsync(w.Central, customer.Value.Id, customer.Version, false, Token)).Succeeded);
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var detail = await w.Requests.GetAsync(w.Qc, created.Value.Id, Token);
        var withPdf = await WithPdfAsync(w, created);
        var item = await w.Requests.AddItemAsync(w.Drawing, created.Value.Id, withPdf.Version, Make("1.10"), Token);
        var submitted = await w.Requests.SubmitToManagementAsync(w.Central, created.Value.Id, item.Value!.Version, Token);

        Assert.True(detail.Succeeded);
        Assert.Equal("Fictional Customer A", detail.Value!.CustomerName);
        Assert.True(item.Succeeded);
        Assert.True(submitted.Succeeded);
        Assert.Single((await w.Requests.ListAsync(w.Central, customer.Value.Id, Token)).Value!);
    }

    [Fact]
    public async Task The_permission_is_checked_before_the_customer_so_a_user_without_it_learns_nothing_about_the_customer()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        Assert.True((await w.Customers.SetActiveAsync(w.Central, customer.Value.Id, customer.Version, false, Token)).Succeeded);

        foreach (var user in new[] { w.Planning, w.Drawing, w.Qc })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Requests.CreateAsync(user, NewRequestFor(customer.Value.Id), Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Requests.CreateAsync(user, NewRequestFor(4242), Token)).Status);
        }

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Requests.CreateAsync(null, NewRequestFor(customer.Value.Id), Token)).Status);
    }

    [Fact]
    public async Task The_business_key_is_customer_plus_number_plus_jalali_year_compared_exactly()
    {
        using var w = await StartAsync();
        var a = await CustomerAsync(w, "Customer A");
        var b = await CustomerAsync(w, "Customer B");
        await RequestAsync(w, a.Value.Id, "P-565", "1405/07/20");

        var duplicate = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(a.Value.Id, "P-565", WorkType.Make, "1405/01/02", null, null), Token);
        var otherYear = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(a.Value.Id, "P-565", WorkType.Make, "1404/12/29", null, null), Token);
        var otherCustomer = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(b.Value.Id, "P-565", WorkType.Make, "1405/07/20", null, null), Token);

        Assert.Equal(OperationStatus.DuplicateRequest, duplicate.Status);
        Assert.True(otherYear.Succeeded);
        Assert.True(otherCustomer.Succeeded);
        Assert.Equal(3, Count(w, "Requests"));
    }

    [Fact]
    public async Task A_number_that_differs_only_by_letter_case_is_accepted_with_a_non_blocking_warning()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        await RequestAsync(w, customer.Value.Id, "wsj-898");

        var outcome = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "WSJ-898", WorkType.Make, "1405/07/20", null, null), Token);

        Assert.True(outcome.Succeeded);
        Assert.Equal(new[] { "wsj-898" }, outcome.WarningList);
        Assert.Equal(2, Count(w, "Requests"));
        Assert.Empty((await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "other-1", WorkType.Make, "1405/07/20", null, null), Token)).WarningList);
    }

    [Fact]
    public async Task Blank_optional_texts_are_stored_as_nothing()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);

        var outcome = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "P-1", WorkType.Make, "1405/07/20", "  ", ""), Token);

        Assert.Null(outcome.Value!.Value.Title);
        Assert.Null(outcome.Value.Value.Description);
    }

    // ---- requests: viewing ----

    [Fact]
    public async Task The_list_shows_newest_first_filters_by_customer_and_survives_a_restart()
    {
        using var w = await StartAsync();
        var a = await CustomerAsync(w, "Customer A");
        var b = await CustomerAsync(w, "Customer B");
        await RequestAsync(w, a.Value.Id, "P-1", "1405/07/01");
        await RequestAsync(w, b.Value.Id, "P-2", "1405/07/20");
        await RequestAsync(w, a.Value.Id, "P-3", "1405/07/10");

        var all = (await w.Restarted().Requests.ListAsync(w.Qc, null, Token)).Value!;
        var onlyA = (await w.Restarted().Requests.ListAsync(w.Drawing, a.Value.Id, Token)).Value!;

        Assert.Equal(new[] { "P-2", "P-3", "P-1" }, all.Select(r => r.RequestNo.Value).ToArray());
        Assert.Equal(new[] { "P-3", "P-1" }, onlyA.Select(r => r.RequestNo.Value).ToArray());
        Assert.All(all, r => Assert.Equal(1405, r.JalaliRequestYear));
    }

    [Fact]
    public async Task Viewing_a_missing_request_is_not_found_and_viewing_needs_an_active_user()
    {
        using var w = await StartAsync();

        Assert.Equal(OperationStatus.NotFound, (await w.Requests.GetAsync(w.Central, 4242, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Requests.GetAsync(null, 1, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Requests.ListAsync(null, null, Token)).Status);
    }

    // ---- requests: submit ----

    [Fact]
    public async Task Central_submits_a_created_request_to_management_even_without_items_and_only_once()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        var created = await WithPdfAsync(w, await RequestAsync(w, customer.Value.Id));

        var submitted = await w.Requests.SubmitToManagementAsync(w.Central, created.Value.Id, created.Version, Token);

        Assert.True(submitted.Succeeded);
        Assert.Equal(RequestStatus.SentToManagement, submitted.Value!.Value.Status);
        Assert.Equal(RequestStatus.SentToManagement, (await w.Restarted().Requests.GetAsync(w.Central, created.Value.Id, Token)).Value!.Request.Value.Status);

        var again = await w.Requests.SubmitToManagementAsync(w.Central, created.Value.Id, submitted.Value.Version, Token);
        Assert.Equal(OperationStatus.InvalidState, again.Status);
        Assert.Equal(RequestStatus.SentToManagement, (await w.Requests.GetAsync(w.Central, created.Value.Id, Token)).Value!.Request.Value.Status);
    }

    [Fact]
    public async Task Only_central_submits_and_a_stale_version_is_a_conflict()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        var created = await WithPdfAsync(w, await RequestAsync(w, customer.Value.Id));

        foreach (var user in new[] { w.Planning, w.Drawing, w.Management, w.Qc, w.Admin })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Requests.SubmitToManagementAsync(user, created.Value.Id, created.Version, Token)).Status);
        }

        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Requests.SubmitToManagementAsync(w.Central, created.Value.Id, created.Version + 100, Token)).Status);
        Assert.Equal(OperationStatus.NotFound, (await w.Requests.SubmitToManagementAsync(w.Central, 4242, 1, Token)).Status);
        Assert.Equal(RequestStatus.Created, (await w.Requests.GetAsync(w.Central, created.Value.Id, Token)).Value!.Request.Value.Status);
    }

    // ---- requests: one work type per request (Decision Log #84), items, permissions ----

    [Fact]
    public async Task A_make_request_is_built_by_drawing_and_planning_in_turn_and_each_item_records_who_added_it()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        var created = await RequestAsync(w, customer.Value.Id);

        var first = await w.Requests.AddItemAsync(w.Drawing, created.Value.Id, created.Version, Make("1.10"), Token);
        var second = await w.Requests.AddItemAsync(w.Planning, created.Value.Id, first.Value!.Version, Make("2.10", Priority.Urgent), Token);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        var request = (await w.Restarted().Requests.GetAsync(w.Qc, created.Value.Id, Token)).Value!.Request.Value;
        Assert.Equal(WorkType.Make, request.WorkType);
        Assert.Equal(new[] { w.Drawing.UserId, w.Planning.UserId }, request.Items.Select(i => i.CreatedByUserId).ToArray());
        Assert.Equal(new[] { Priority.Normal, Priority.Urgent }, request.Items.Select(i => i.Priority).ToArray());
        Assert.Equal(new[] { 10m, 10m }, request.Items.Select(i => i.RequestedQuantity).ToArray());
        var summary = (await w.Requests.ListAsync(w.Central, null, Token)).Value!.Single();
        Assert.Equal(2, summary.ItemCount);
        Assert.Equal(WorkType.Make, summary.WorkType);
    }

    [Fact]
    public async Task The_work_type_is_chosen_at_creation_stored_on_the_request_and_must_be_valid()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);

        var make = await RequestAsync(w, customer.Value.Id, "T-1", workType: WorkType.Make);
        var repair = await RequestAsync(w, customer.Value.Id, "T-2", workType: WorkType.Repair);
        var invalid = await w.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, "T-3", (WorkType)0, "1405/07/20", null, null), Token);

        Assert.Equal(WorkType.Make, make.Value.WorkType);
        Assert.Equal(WorkType.Repair, repair.Value.WorkType);
        Assert.Equal(OperationStatus.ValidationFailed, invalid.Status);
        Assert.Equal(ValidationIssue.WorkTypeInvalid, invalid.Issue);
        Assert.Equal(2, Count(w, "Requests"));
        var types = (await w.Restarted().Requests.ListAsync(w.Central, null, Token)).Value!.ToDictionary(r => r.RequestNo.Value, r => r.WorkType);
        Assert.Equal(WorkType.Make, types["T-1"]);
        Assert.Equal(WorkType.Repair, types["T-2"]);
    }

    [Fact]
    public async Task Item_permissions_follow_the_provisional_map_for_the_requests_own_work_type_and_nobody_gets_more()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        var make = await RequestAsync(w, customer.Value.Id, "I-MAKE", workType: WorkType.Make);
        var repair = await RequestAsync(w, customer.Value.Id, "I-REPAIR", workType: WorkType.Repair);

        // Drawing defines items of MAKE requests only (provisional map #34, until the item-definition phase moves item definition to Planning, #86).
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Requests.AddItemAsync(w.Drawing, repair.Value.Id, repair.Version, Repair("D-R"), Token)).Status);
        // Central creates and submits requests; it does not define items. Management, QC and Admin have no item permission.
        foreach (var user in new[] { w.Central, w.Management, w.Qc, w.Admin })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Requests.AddItemAsync(user, make.Value.Id, make.Version, Make("X-M"), Token)).Status);
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Requests.AddItemAsync(user, repair.Value.Id, repair.Version, Repair("X-R"), Token)).Status);
        }

        Assert.Equal(OperationStatus.NotAuthorized, (await w.Requests.AddItemAsync(null, make.Value.Id, make.Version, Make("N"), Token)).Status);
        Assert.Empty((await w.Requests.GetAsync(w.Central, make.Value.Id, Token)).Value!.Request.Value.Items);
        Assert.Empty((await w.Requests.GetAsync(w.Central, repair.Value.Id, Token)).Value!.Request.Value.Items);

        // Drawing may define MAKE items; Planning may define items of either type.
        Assert.True((await w.Requests.AddItemAsync(w.Drawing, make.Value.Id, make.Version, Make("D-M"), Token)).Succeeded);
        Assert.True((await w.Requests.AddItemAsync(w.Planning, make.Value.Id, (await w.Requests.GetAsync(w.Central, make.Value.Id, Token)).Value!.Request.Version, Make("P-M"), Token)).Succeeded);
        Assert.True((await w.Requests.AddItemAsync(w.Planning, repair.Value.Id, repair.Version, Repair("P-R"), Token)).Succeeded);
    }

    [Fact]
    public async Task A_priority_other_than_normal_needs_the_planning_priority_permission()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        var created = await RequestAsync(w, customer.Value.Id);

        var drawingUrgent = await w.Requests.AddItemAsync(w.Drawing, created.Value.Id, created.Version, Make("U-1", Priority.Urgent), Token);
        var drawingNormal = await w.Requests.AddItemAsync(w.Drawing, created.Value.Id, created.Version, Make("U-2"), Token);
        var planningVery = await w.Requests.AddItemAsync(w.Planning, created.Value.Id, drawingNormal.Value!.Version, Repair("U-3", Priority.VeryUrgent), Token);

        Assert.Equal(OperationStatus.NotAuthorized, drawingUrgent.Status);
        Assert.True(drawingNormal.Succeeded);
        Assert.True(planningVery.Succeeded);
        var items = (await w.Requests.GetAsync(w.Central, created.Value.Id, Token)).Value!.Request.Value.Items;
        Assert.Equal(new[] { "U-2", "U-3" }, items.Select(i => i.ItemNo).ToArray());
        Assert.Equal(new[] { "عادی", "خیلی فوری" }, items.Select(i => i.Priority.PersianName()).ToArray());
        Assert.Equal(OperationStatus.ValidationFailed, (await w.Requests.AddItemAsync(w.Planning, created.Value.Id, planningVery.Value!.Version, Make("U-4", (Priority)99), Token)).Status);
    }

    [Theory]
    [InlineData(null, "Ring", 1, ValidationIssue.ItemNoRequired)]
    [InlineData("  ", "Ring", 1, ValidationIssue.ItemNoRequired)]
    [InlineData("1.10", null, 1, ValidationIssue.PartNameRequired)]
    [InlineData("1.10", " ", 1, ValidationIssue.PartNameRequired)]
    [InlineData("1.10", "Ring", 0, ValidationIssue.QuantityInvalid)]
    [InlineData("1.10", "Ring", -2, ValidationIssue.QuantityInvalid)]
    public async Task An_invalid_item_is_refused_with_the_reason_and_nothing_is_saved(string? itemNo, string? partName, int quantity, ValidationIssue expected)
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        var created = await RequestAsync(w, customer.Value.Id);

        var outcome = await w.Requests.AddItemAsync(w.Drawing, created.Value.Id, created.Version, new AddItemCommand(itemNo, partName, quantity), Token);

        Assert.Equal(OperationStatus.ValidationFailed, outcome.Status);
        Assert.Equal(expected, outcome.Issue);
        Assert.Equal(0, Count(w, "RequestItems"));
    }

    [Fact]
    public async Task Item_numbers_are_unique_per_request_compared_exactly_and_exact_decimal_quantities_are_kept()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        var first = await RequestAsync(w, customer.Value.Id, "P-1");
        var second = await RequestAsync(w, customer.Value.Id, "P-2");

        var one = await w.Requests.AddItemAsync(w.Drawing, first.Value.Id, first.Version, new AddItemCommand("A-1", "Ring", 2.0001m), Token);
        var duplicate = await w.Requests.AddItemAsync(w.Drawing, first.Value.Id, one.Value!.Version, Make("A-1"), Token);
        var otherCase = await w.Requests.AddItemAsync(w.Drawing, first.Value.Id, one.Value.Version, Make("a-1"), Token);
        var otherRequest = await w.Requests.AddItemAsync(w.Drawing, second.Value.Id, second.Version, Make("A-1"), Token);

        Assert.Equal(ValidationIssue.ItemNoDuplicate, duplicate.Issue);
        Assert.True(otherCase.Succeeded);
        Assert.True(otherRequest.Succeeded);
        var stored = (await w.Restarted().Requests.GetAsync(w.Central, first.Value.Id, Token)).Value!.Request.Value.Items;
        Assert.Equal(new[] { "A-1", "a-1" }, stored.Select(i => i.ItemNo).ToArray());
        Assert.Equal(2.0001m, stored[0].RequestedQuantity);
    }

    [Fact]
    public async Task A_cancelled_or_closed_request_accepts_no_new_items()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        var created = await RequestAsync(w, customer.Value.Id);
        w.Fixture.Db.Execute("UPDATE Requests SET Status = 7 WHERE Id = " + created.Value.Id);

        var outcome = await w.Requests.AddItemAsync(w.Drawing, created.Value.Id, created.Version, Make(), Token);

        Assert.Equal(ValidationIssue.RequestClosedForItems, outcome.Issue);
        Assert.Equal(0, Count(w, "RequestItems"));
        Assert.Equal(OperationStatus.NotFound, (await w.Requests.AddItemAsync(w.Drawing, 4242, 1, Make(), Token)).Status);
    }

    [Fact]
    public async Task Two_people_editing_the_same_request_cannot_overwrite_each_other()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        var created = await WithPdfAsync(w, await RequestAsync(w, customer.Value.Id));

        // Drawing and Planning both opened the request at the same version. (The clock moves on between operations, as it does in real use: the version of the
        // request row changes when the row changes, and a new item changes the request's "updated" time.)
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var drawing = await w.Requests.AddItemAsync(w.Drawing, created.Value.Id, created.Version, Make("D-1"), Token);
        var planning = await w.Requests.AddItemAsync(w.Planning, created.Value.Id, created.Version, Repair("P-1"), Token);

        Assert.True(drawing.Succeeded);
        Assert.Equal(OperationStatus.ConcurrencyConflict, planning.Status);
        var current = (await w.Requests.GetAsync(w.Planning, created.Value.Id, Token)).Value!.Request;
        Assert.Equal(new[] { "D-1" }, current.Value.Items.Select(i => i.ItemNo).ToArray());

        // After reloading, Planning's change goes through, and Central's submit with the old version is refused too.
        Assert.True((await w.Requests.AddItemAsync(w.Planning, created.Value.Id, current.Version, Repair("P-1"), Token)).Succeeded);
        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Requests.SubmitToManagementAsync(w.Central, created.Value.Id, created.Version, Token)).Status);
    }

    [Fact]
    public async Task A_stale_principal_cannot_add_items_after_the_department_or_activity_changed()
    {
        using var w = await StartAsync();
        var customer = await CustomerAsync(w);
        var created = await RequestAsync(w, customer.Value.Id);
        var ok = await w.Requests.AddItemAsync(w.Drawing, created.Value.Id, created.Version, Make("D-1"), Token);
        Assert.True(ok.Succeeded);

        w.Fixture.Db.Execute("UPDATE Users SET DepartmentId = (SELECT Id FROM Departments WHERE Code = '" + DepartmentCodes.Qc + "') WHERE Username = 'drawing.one'");
        Assert.Equal(DepartmentCodes.Drawing, w.Drawing.DepartmentCode);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Requests.AddItemAsync(w.Drawing, created.Value.Id, ok.Value!.Version, Make("D-2"), Token)).Status);

        await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Planning.UserId, false, Token);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Requests.AddItemAsync(w.Planning, created.Value.Id, ok.Value.Version, Repair("P-1"), Token)).Status);
        Assert.Single((await w.Requests.GetAsync(w.Central, created.Value.Id, Token)).Value!.Request.Value.Items);
    }

    // ---- capabilities (what the screens may enable) ----

    [Fact]
    public async Task The_capabilities_answer_for_each_department_comes_from_the_approved_map()
    {
        using var w = await StartAsync();

        var central = await w.Requests.GetCapabilitiesAsync(w.Central, Token);
        var planning = await w.Requests.GetCapabilitiesAsync(w.Planning, Token);
        var drawing = await w.Requests.GetCapabilitiesAsync(w.Drawing, Token);
        var management = await w.Requests.GetCapabilitiesAsync(w.Management, Token);
        var qc = await w.Requests.GetCapabilitiesAsync(w.Qc, Token);

        Assert.Equal(new Capabilities(true, true, true, true, true, false, false, false), central);
        Assert.Equal(new Capabilities(true, true, false, false, false, true, true, true), planning);
        Assert.Equal(new Capabilities(true, false, false, false, false, true, false, false), drawing);
        Assert.Equal(new Capabilities(true, false, false, false, false, false, false, true), management);
        Assert.Equal(new Capabilities(true, false, false, false, false, false, false, false), qc);
        Assert.Equal(Capabilities.None, await w.Requests.GetCapabilitiesAsync(null, Token));

        await w.Fixture.Services.Accounts.SetActiveAsync(w.Admin, w.Central.UserId, false, Token);
        Assert.Equal(Capabilities.None, await w.Requests.GetCapabilitiesAsync(w.Central, Token));
    }

    // ---- database failures ----

    private sealed class FailingCustomers : ICustomerRepository
    {
        public Task<Stored<Customer>> AddAsync(Customer customer, CancellationToken cancellationToken = default) => throw new PersistenceException("The data could not be saved.");

        public Task<Stored<Customer>?> FindAsync(long customerId, CancellationToken cancellationToken = default) => throw new PersistenceException("The data could not be read.");

        public Task<IReadOnlyList<Stored<Customer>>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default) => throw new PersistenceException("The data could not be read.");

        public Task<Stored<Customer>> UpdateAsync(long customerId, long? expectedVersion, Action<Customer> change, CancellationToken cancellationToken = default) =>
            throw new PersistenceException("The data could not be saved.");
    }

    [Fact]
    public async Task A_database_failure_is_reported_as_failed_and_not_thrown()
    {
        using var w = await StartAsync();
        var customers = new CustomerService(new FailingCustomers(), w.Fixture.Services.Authorization, w.Fixture.Clock);

        Assert.Equal(OperationStatus.Failed, (await customers.ListAsync(w.Central, false, Token)).Status);
        Assert.Equal(OperationStatus.Failed, (await customers.CreateAsync(w.Central, "Name", Token)).Status);
        Assert.Equal(OperationStatus.Failed, (await customers.RenameAsync(w.Central, 1, 1, "Name", Token)).Status);
        Assert.Equal(OperationStatus.Failed, (await customers.SetActiveAsync(w.Central, 1, 1, false, Token)).Status);

        // And the permission is still checked first: a user without it learns nothing about the failure.
        Assert.Equal(OperationStatus.NotAuthorized, (await customers.CreateAsync(w.Qc, "Name", Token)).Status);
    }

    // ---- quantity text ----

    [Theory]
    [InlineData("5", "5")]
    [InlineData("2.5", "2.5")]
    [InlineData("۲٫۵", "2.5")]
    [InlineData("٣", "3")]
    [InlineData(" 7 ", "7")]
    [InlineData("0.0001", "0.0001")]
    [InlineData("007", "7")]
    public void A_quantity_is_read_exactly_in_latin_persian_or_arabic_indic_digits(string text, string expected)
    {
        Assert.True(QuantityText.TryParse(text, out var value));
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1.2.3")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1e3")]
    [InlineData("1,000")]
    [InlineData("1 000")]
    [InlineData("١٫٢٫٣")]
    public void Anything_that_is_not_a_plain_decimal_number_is_refused(string? text)
    {
        Assert.False(QuantityText.TryParse(text, out _));
    }
}
