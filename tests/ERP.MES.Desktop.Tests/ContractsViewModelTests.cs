using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The contract screen against the real Application services (no window is opened).
public class ContractsViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Pdf(string text = "fictional") => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% " + text + "\n%%EOF\n");

    private static ContractsViewModel For(WorkflowHarness h, Principal user) => new(h.Workflow.Contracts, h.Dates, () => user);

    private static async Task<long> ReturnedMakeRequestAsync(WorkflowHarness h, string requestNo = "C-1")
    {
        var customer = (await h.Workflow.Customers.CreateAsync(h.Central, "Fictional Customer", Token)).Value!;
        var created = (await h.Workflow.Requests.CreateAsync(h.Central, new CreateRequestCommand(customer.Value.Id, requestNo, WorkType.Make, "1405/07/20", null, null), Token)).Value!;
        var withPdf = (await h.Workflow.Documents.AttachRequestPdfAsync(h.Central, created.Value.Id, created.Version, "a.pdf", Pdf(), Token)).Value!;
        var sent = (await h.Workflow.Requests.SubmitToManagementAsync(h.Central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        var approved = (await h.Workflow.Lifecycle.ApproveAsync(h.Manager, sent.Value.Id, sent.Version, Token)).Value!;
        var forwarded = (await h.Workflow.Lifecycle.ForwardToDrawingAsync(h.Planning, approved.Value.Id, approved.Version, Token)).Value!;
        Assert.True((await h.Workflow.Lifecycle.RecordDrawingResultAsync(h.Drawing, forwarded.Value.Id, forwarded.Version, DrawingOutcome.FullyAccepted, null, Token)).Succeeded);
        return created.Value.Id;
    }

    [Fact]
    public async Task Planning_sees_the_returned_make_request_decides_and_prepares_the_contract_and_the_gate_text_follows()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await ReturnedMakeRequestAsync(h);
        var planning = For(h, h.Planning);

        await planning.LoadAsync(Token);
        Assert.Equal("تصمیم‌گیری نشده", planning.Rows.Single().StatusText);
        await planning.SelectAsync(id, Token);
        Assert.True(planning.HasDetail);
        Assert.True(planning.CanDecide);
        Assert.Contains("برنامه‌ریزی هنوز دربارهٔ لزوم قرارداد تصمیم نگرفته", planning.GateLine, StringComparison.Ordinal);
        Assert.Contains("هنوز پیاده‌سازی نشده", planning.GateLine, StringComparison.Ordinal);

        Assert.True(await planning.DecideAsync(true, null, Token));
        Assert.False(planning.CanDecide);
        Assert.True(planning.CanEditDraft);
        Assert.Contains("قرارداد در حال تهیه است", planning.GateLine, StringComparison.Ordinal);

        Assert.False(await planning.SubmitAsync(Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.ContractNoRequired, ERP.MES.Domain.Calendar.JalaliDateError.None), planning.Message);
        Assert.True(await planning.SaveDraftAsync("CN-1", "۱۴۰۵/۰۷/۲۵", "قرارداد", null, Token));
        Assert.True(await planning.AttachPdfAsync("contract.pdf", Pdf("c"), Token));
        Assert.True(await planning.SubmitAsync(Token));

        Assert.Equal("در انتظار مدیریت", planning.Rows.Single().StatusText);
        Assert.Equal("۱۴۰۵/۰۷/۲۵", planning.Versions.Single().DateText);
        Assert.Equal("contract.pdf", planning.Versions.Single().PdfText);
        Assert.False(planning.CanEditDraft);
        Assert.False(planning.CanDecideContract);
        Assert.Equal("ارسال به مدیریت", planning.History[0].ActionText);
    }

    [Fact]
    public async Task Management_rejects_with_a_reason_planning_revises_and_management_approves_and_warehouse_only_reads()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await ReturnedMakeRequestAsync(h);
        var planning = For(h, h.Planning);
        await planning.LoadAsync(Token);
        await planning.SelectAsync(id, Token);
        await planning.DecideAsync(true, null, Token);
        await planning.SaveDraftAsync("CN-1", "1405/07/25", null, null, Token);
        await planning.AttachPdfAsync("contract.pdf", Pdf("c"), Token);
        await planning.SubmitAsync(Token);

        var management = For(h, h.Management);
        await management.LoadAsync(Token);
        await management.SelectAsync(id, Token);
        Assert.True(management.CanDecideContract);
        Assert.False(management.CanEditDraft);
        Assert.False(await management.RejectAsync(" ", Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.ReasonRequired, ERP.MES.Domain.Calendar.JalaliDateError.None), management.Message);
        Assert.True(await management.RejectAsync("مبلغ نادرست", Token));
        Assert.Contains("ردشده", management.Rows.Single().StatusText, StringComparison.Ordinal);
        Assert.Equal("مبلغ نادرست", management.Versions.Single().DecisionNoteText);

        await planning.SelectAsync(id, Token);
        Assert.True(planning.CanStartRevision);
        Assert.True(await planning.StartRevisionAsync(Token));
        Assert.True(await planning.SaveDraftAsync("CN-1-R1", "1405/08/01", null, null, Token));
        Assert.True(await planning.SubmitAsync(Token));
        Assert.Equal(2, planning.Versions.Count);

        await management.SelectAsync(id, Token);
        Assert.True(await management.ApproveAsync(null, Token));
        Assert.Equal("تأیید شده", management.Rows.Single().StatusText);
        Assert.Contains("برقرار است", management.GateLine, StringComparison.Ordinal);

        var warehouse = For(h, h.Warehouse);
        await warehouse.LoadAsync(Token);
        await warehouse.SelectAsync(id, Token);
        Assert.Equal("تأیید شده", warehouse.Rows.Single().StatusText);
        Assert.True(warehouse.CanView);
        Assert.False(warehouse.CanDecide);
        Assert.False(warehouse.CanEditDraft);
        Assert.False(warehouse.CanDecideContract);
        Assert.False(await warehouse.ApproveAsync(null, Token));
        Assert.Equal(Messages.NotAuthorized, warehouse.Message);
        Assert.NotNull(await warehouse.LoadPdfAsync(null, Token));
    }

    [Fact]
    public async Task Other_departments_see_nothing_and_get_the_not_authorized_message()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await ReturnedMakeRequestAsync(h);

        foreach (var user in new[] { h.Central, h.Drawing, h.Qc, h.Production })
        {
            var vm = For(h, user);
            await vm.LoadAsync(Token);
            Assert.Empty(vm.Rows);
            Assert.False(vm.CanView);
            Assert.Equal(Messages.NotAuthorized, vm.Message);
        }
    }

    [Fact]
    public async Task A_stale_screen_reloads_after_a_conflict_and_shows_the_current_state()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await ReturnedMakeRequestAsync(h);
        var first = For(h, h.Planning);
        var second = For(h, h.Planning);
        foreach (var vm in new[] { first, second })
        {
            await vm.LoadAsync(Token);
            await vm.SelectAsync(id, Token);
        }

        Assert.True(await first.DecideAsync(false, null, Token));

        // The second screen still believes there is no decision; the Application refuses and the screen reloads.
        Assert.False(await second.DecideAsync(true, null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.ContractAlreadyDecided, ERP.MES.Domain.Calendar.JalaliDateError.None), second.Message);
        Assert.Equal("قرارداد لازم نیست", second.Rows.Single().StatusText);
    }
}
