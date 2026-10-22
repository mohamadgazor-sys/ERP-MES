using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

// The request PDF (Decision Log #87, #54, #35): Central attaches it before submitting; the binary is stored once and linked; a replaced PDF stays as history; the size limit
// is exact; everything is audited in the same transaction. Real SQLite file and real authorization.
public class RequestPdfTests
{
    private const string InitialPassword = "Staff-Pass-Phrase-2";
    private const string OwnPassword = "My-Own-Pass-Phrase-3";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private sealed class World : IDisposable
    {
        public required SecurityFixture Fixture { get; init; }

        public required Principal Central { get; init; }

        public required Principal Planning { get; init; }

        public required Principal Qc { get; init; }

        public required WorkflowServices Services { get; set; }

        public void Dispose() => Fixture.Dispose();
    }

    private static async Task<Principal> UserAsync(SecurityFixture fixture, Principal admin, string name, string department)
    {
        await fixture.CreateUserAsync(admin, name, InitialPassword, department, RoleCodes.Staff, Token);
        var first = await fixture.SignInAsync(name, InitialPassword, Token);
        await fixture.Services.Accounts.ChangeOwnPasswordAsync(first, InitialPassword, OwnPassword, Token);
        return await fixture.SignInAsync(name, OwnPassword, Token);
    }

    private static async Task<World> StartAsync(DocumentPolicy? policy = null)
    {
        var fixture = await SecurityFixture.CreateAsync();
        var admin = await fixture.BootstrapAdminAsync(cancellationToken: Token);
        return new World
        {
            Fixture = fixture,
            Central = await UserAsync(fixture, admin, "central.one", DepartmentCodes.Central),
            Planning = await UserAsync(fixture, admin, "planning.one", DepartmentCodes.Planning),
            Qc = await UserAsync(fixture, admin, "qc.one", DepartmentCodes.Qc),
            Services = WorkflowServices.Create(fixture.Db.ContextFactory, fixture.Services.Authorization, PersistenceTestData.Dates, fixture.Clock, documentPolicy: policy),
        };
    }

    private static async Task<Stored<Request>> NewRequestAsync(World w, string requestNo = "P-1")
    {
        var customer = (await w.Services.Customers.CreateAsync(w.Central, "Fictional Customer A", Token)).Value!;
        var created = await w.Services.Requests.CreateAsync(w.Central, new CreateRequestCommand(customer.Value.Id, requestNo, WorkType.Make, "1405/07/20", null, null), Token);
        Assert.True(created.Succeeded, created.Status + " " + created.Issue);
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        return created.Value!;
    }

    /// <summary>A fictional PDF of exactly <paramref name="size"/> bytes (signature first, then padding), or a small default one.</summary>
    private static byte[] Pdf(int size = 64)
    {
        var bytes = new byte[size];
        Array.Fill(bytes, (byte)' ');
        var head = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n");
        head.AsSpan(0, Math.Min(head.Length, size)).CopyTo(bytes);
        return bytes;
    }

    private static long Count(World w, string table) => long.Parse(w.Fixture.Db.Query("SELECT COUNT(*) FROM " + table).Single(), System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task Central_attaches_a_pdf_it_is_stored_once_linked_audited_and_survives_a_restart()
    {
        using var w = await StartAsync();
        var request = await NewRequestAsync(w);
        var content = Pdf(300);

        var attached = await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version, @"C:\Users\someone\Documents\request 1.PDF", content, Token);

        Assert.True(attached.Succeeded, attached.Status + " " + attached.Issue);
        Assert.Equal(1, Count(w, "Documents"));
        Assert.Equal(1, Count(w, "RequestDocumentLinks"));
        Assert.Equal("request 1.PDF", w.Fixture.Db.Query("SELECT FileName FROM Documents").Single());
        Assert.Equal("300", w.Fixture.Db.Query("SELECT ContentLength FROM Documents").Single());
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)), w.Fixture.Db.Query("SELECT ContentHash FROM Documents").Single());

        var restarted = WorkflowServices.Create(w.Fixture.Db.ContextFactory, w.Fixture.Services.Authorization, PersistenceTestData.Dates, w.Fixture.Clock);
        var loaded = (await restarted.Requests.GetAsync(w.Qc, request.Value.Id, Token)).Value!.Request.Value;
        Assert.True(loaded.HasRequestPdf);
        Assert.Equal("request 1.PDF", loaded.CurrentRequestPdf!.FileName);
        Assert.Equal(300, loaded.CurrentRequestPdf.ContentLength);

        var file = await restarted.Documents.GetRequestPdfAsync(w.Qc, request.Value.Id, Token);
        Assert.True(file.Succeeded);
        Assert.Equal(content, file.Value!.Content);

        var audit = w.Fixture.Db.Query("SELECT ActionCode, TargetType, TargetId FROM AuditLog WHERE ActionCode = 'RequestPdfAttached'");
        Assert.Equal(new[] { "RequestPdfAttached|Request|" + request.Value.Id }, audit.ToArray());
        Assert.DoesNotContain("%PDF", w.Fixture.Db.Query("SELECT Summary FROM AuditLog WHERE ActionCode = 'RequestPdfAttached'").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_cannot_be_submitted_until_its_pdf_is_attached()
    {
        using var w = await StartAsync();
        var request = await NewRequestAsync(w);

        var refused = await w.Services.Requests.SubmitToManagementAsync(w.Central, request.Value.Id, request.Version, Token);

        Assert.Equal(OperationStatus.ValidationFailed, refused.Status);
        Assert.Equal(ValidationIssue.RequestPdfRequired, refused.Issue);
        Assert.Equal(RequestStatus.Created, (await w.Services.Requests.GetAsync(w.Central, request.Value.Id, Token)).Value!.Request.Value.Status);

        var attached = (await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version, "a.pdf", Pdf(), Token)).Value!;
        var submitted = await w.Services.Requests.SubmitToManagementAsync(w.Central, request.Value.Id, attached.Version, Token);
        Assert.True(submitted.Succeeded);
        Assert.Equal(RequestStatus.SentToManagement, submitted.Value!.Value.Status);
    }

    [Fact]
    public async Task Replacing_the_pdf_keeps_the_old_document_and_link_as_history_and_the_newest_is_current()
    {
        using var w = await StartAsync();
        var request = await NewRequestAsync(w);
        var first = (await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version, "first.pdf", Pdf(100), Token)).Value!;
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        var second = await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, first.Version, "second.pdf", Pdf(200), Token);

        Assert.True(second.Succeeded);
        Assert.Equal(2, Count(w, "Documents"));
        Assert.Equal(2, Count(w, "RequestDocumentLinks"));
        Assert.Equal(new[] { "first.pdf|1", "second.pdf|0" }, w.Fixture.Db.Query("SELECT FileName, SupersededAtUtc IS NOT NULL FROM RequestDocumentLinks ORDER BY Id").ToArray());
        var file = (await w.Services.Documents.GetRequestPdfAsync(w.Qc, request.Value.Id, Token)).Value!;
        Assert.Equal("second.pdf", file.FileName);
        Assert.Equal(200, file.ContentLength);
        Assert.Equal(2, w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode = 'RequestPdfAttached'").Count);
    }

    [Theory]
    [InlineData(0, ValidationIssue.PdfEmpty)]
    [InlineData(1, ValidationIssue.PdfNotPdf)]
    public async Task An_empty_or_non_pdf_file_is_refused_and_nothing_is_stored(int kind, ValidationIssue expected)
    {
        using var w = await StartAsync();
        var request = await NewRequestAsync(w);
        var content = kind == 0 ? [] : System.Text.Encoding.ASCII.GetBytes("This is a plain text file, not a PDF at all.");

        var outcome = await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version, "x.pdf", content, Token);

        Assert.Equal(OperationStatus.ValidationFailed, outcome.Status);
        Assert.Equal(expected, outcome.Issue);
        Assert.Equal(0, Count(w, "Documents"));
        Assert.Equal(0, Count(w, "RequestDocumentLinks"));
        Assert.Equal(0, Count(w, "AuditLog WHERE ActionCode = 'RequestPdfAttached'"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("request.txt")]
    [InlineData(".pdf")]
    public async Task A_missing_or_non_pdf_file_name_is_refused(string? name)
    {
        using var w = await StartAsync();
        var request = await NewRequestAsync(w);

        var outcome = await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version, name, Pdf(), Token);

        Assert.Equal(ValidationIssue.PdfFileNameInvalid, outcome.Issue);
        Assert.Equal(0, Count(w, "Documents"));
    }

    [Fact]
    public async Task The_size_limit_is_exactly_20_mib_one_byte_more_is_refused()
    {
        Assert.Equal(20_971_520L, DocumentPolicy.Default.MaxRequestPdfBytes);
        using var w = await StartAsync();
        var request = await NewRequestAsync(w);

        var tooLarge = await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version, "big.pdf", Pdf(20_971_521), Token);
        Assert.Equal(ValidationIssue.PdfTooLarge, tooLarge.Issue);
        Assert.Equal(0, Count(w, "Documents"));

        var exact = await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version, "exact.pdf", Pdf(20_971_520), Token);
        Assert.True(exact.Succeeded, exact.Status + " " + exact.Issue);
        Assert.Equal("20971520", w.Fixture.Db.Query("SELECT ContentLength FROM Documents").Single());
    }

    [Fact]
    public async Task The_size_limit_is_a_configuration_value()
    {
        using var w = await StartAsync(new DocumentPolicy(100));
        var request = await NewRequestAsync(w);

        Assert.Equal(ValidationIssue.PdfTooLarge, (await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version, "a.pdf", Pdf(101), Token)).Issue);
        Assert.True((await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version, "a.pdf", Pdf(100), Token)).Succeeded);
    }

    [Fact]
    public async Task Only_central_attaches_but_every_active_user_may_view()
    {
        using var w = await StartAsync();
        var request = await NewRequestAsync(w);

        foreach (var user in new Principal?[] { w.Planning, w.Qc, null })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.Documents.AttachRequestPdfAsync(user, request.Value.Id, request.Version, "a.pdf", Pdf(), Token)).Status);
        }

        Assert.Equal(0, Count(w, "Documents"));
        Assert.Equal(OperationStatus.NotFound, (await w.Services.Documents.GetRequestPdfAsync(w.Qc, request.Value.Id, Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.Documents.GetRequestPdfAsync(null, request.Value.Id, Token)).Status);
        Assert.True((await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version, "a.pdf", Pdf(), Token)).Succeeded);
        Assert.True((await w.Services.Documents.GetRequestPdfAsync(w.Planning, request.Value.Id, Token)).Succeeded);
    }

    [Fact]
    public async Task A_stale_version_is_a_conflict_an_unknown_request_is_not_found_and_a_submitted_request_takes_no_new_pdf()
    {
        using var w = await StartAsync();
        var request = await NewRequestAsync(w);
        var attached = (await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version, "a.pdf", Pdf(), Token)).Value!;
        w.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(OperationStatus.ConcurrencyConflict, (await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version + 50, "b.pdf", Pdf(), Token)).Status);
        Assert.Equal(OperationStatus.NotFound, (await w.Services.Documents.AttachRequestPdfAsync(w.Central, 4242, 1, "b.pdf", Pdf(), Token)).Status);

        var submitted = (await w.Services.Requests.SubmitToManagementAsync(w.Central, request.Value.Id, attached.Version, Token)).Value!;
        Assert.Equal(OperationStatus.InvalidState, (await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, submitted.Version, "c.pdf", Pdf(), Token)).Status);
        Assert.Equal(1, Count(w, "Documents"));
        Assert.Equal("a.pdf", (await w.Services.Documents.GetRequestPdfAsync(w.Qc, request.Value.Id, Token)).Value!.FileName);
    }

    [Fact]
    public async Task A_failed_attachment_leaves_no_partial_data()
    {
        using var w = await StartAsync();
        var request = await NewRequestAsync(w);

        // A conflicting version is refused before anything is written: no document, no link, no audit entry.
        var outcome = await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version + 7, "a.pdf", Pdf(), Token);

        Assert.Equal(OperationStatus.ConcurrencyConflict, outcome.Status);
        Assert.Equal(0, Count(w, "Documents"));
        Assert.Equal(0, Count(w, "RequestDocumentLinks"));
        Assert.Equal(0, Count(w, "AuditLog WHERE ActionCode = 'RequestPdfAttached'"));
    }

    [Fact]
    public async Task A_document_is_referenced_by_foreign_keys_and_cannot_be_orphaned()
    {
        using var w = await StartAsync();
        var request = await NewRequestAsync(w);
        Assert.True((await w.Services.Documents.AttachRequestPdfAsync(w.Central, request.Value.Id, request.Version, "a.pdf", Pdf(), Token)).Succeeded);

        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => w.Fixture.Db.Execute("DELETE FROM Documents"));
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() => w.Fixture.Db.Execute("DELETE FROM Requests"));
        Assert.Empty(w.Fixture.Db.Query("PRAGMA foreign_key_check"));
    }
}
