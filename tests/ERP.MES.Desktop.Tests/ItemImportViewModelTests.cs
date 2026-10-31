using ERP.MES.Application.Imports;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Imports;

namespace ERP.MES.Desktop.Tests;

// The item-import screen against the real Application services and xlsx reader (no window is opened).
public class ItemImportViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static readonly XlsxWorkbookWriter Writer = new();

    private static ItemImportViewModel For(WorkflowHarness h, Principal user) => new(h.Workflow.ItemImport, h.Workflow.Requests, Writer, () => user);

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional\n%%EOF\n");

    /// <summary>A request that passed Management and Drawing; for MAKE also the contract decision (not required).</summary>
    private static async Task<long> ReadyAsync(WorkflowHarness h, WorkType type, string requestNo)
    {
        var customer = (await h.Workflow.Customers.CreateAsync(h.Central, "Fictional Customer " + requestNo, Token)).Value!;
        var created = (await h.Workflow.Requests.CreateAsync(h.Central, new CreateRequestCommand(customer.Value.Id, requestNo, type, "1405/07/20", null, null), Token)).Value!;
        var withPdf = (await h.Workflow.Documents.AttachRequestPdfAsync(h.Central, created.Value.Id, created.Version, "a.pdf", Pdf(), Token)).Value!;
        var sent = (await h.Workflow.Requests.SubmitToManagementAsync(h.Central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        var approved = (await h.Workflow.Lifecycle.ApproveAsync(h.Manager, sent.Value.Id, sent.Version, Token)).Value!;
        var forwarded = (await h.Workflow.Lifecycle.ForwardToDrawingAsync(h.Planning, approved.Value.Id, approved.Version, Token)).Value!;
        Assert.True((await h.Workflow.Lifecycle.RecordDrawingResultAsync(h.Drawing, forwarded.Value.Id, forwarded.Version, DrawingOutcome.FullyAccepted, null, Token)).Succeeded);
        if (type == WorkType.Make)
        {
            Assert.True((await h.Workflow.Contracts.DecideRequirementAsync(h.Planning, created.Value.Id, ContractRequirement.NotRequired, null, Token)).Succeeded);
        }

        return created.Value.Id;
    }

    private static byte[] File(params IReadOnlyList<string>[] rows) =>
        Writer.Write("S", [ItemImportTemplate.ItemNo, ItemImportTemplate.PartName, ItemImportTemplate.Quantity, ItemImportTemplate.QuantityType, ItemImportTemplate.Material, ItemImportTemplate.Weight], rows);

    [Fact]
    public async Task Planning_previews_chooses_for_a_conflict_and_commits_and_the_screen_summarises_the_result()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await ReadyAsync(h, WorkType.Make, "M-1");
        Assert.True((await h.Workflow.Materials.AddMaterialAsync(h.Warehouse, "ST37-R30", "میلگرد ST37", "ST37", "میلگرد", Token)).Succeeded);
        var vm = For(h, h.Planning);
        await vm.LoadAsync(Token);
        Assert.True(vm.CanImport);
        vm.SelectedRequest = vm.Requests.Single(r => r.RequestId == id);

        Assert.True(await vm.PreviewAsync("a.xlsx", File(["1.10", "حلقه", "10", "PIECE", "ST37-R30", "1.5"], ["1.20", "شفت", "4", "PIECE", "ST37-R30", "2"]), Token));
        Assert.True(vm.HasPreview);
        Assert.True(vm.CanCommit);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Contains("جدید: ۲".Replace("۲", "2"), vm.Summary, StringComparison.Ordinal);
        Assert.True(await vm.CommitAsync(Token));
        Assert.False(vm.HasPreview);
        Assert.Contains("2 آیتم جدید", vm.Message, StringComparison.Ordinal);

        // A changed row is a conflict: the screen offers Skip/Update and refuses to commit until the user chooses.
        await vm.LoadAsync(Token);
        vm.SelectedRequest = vm.Requests.Single(r => r.RequestId == id);
        Assert.True(await vm.PreviewAsync("b.xlsx", File(["1.10", "حلقه جدید", "10", "PIECE", "ST37-R30", "1.5"]), Token));
        var row = Assert.Single(vm.Rows);
        Assert.True(row.NeedsChoice);
        Assert.Null(row.Choice);
        Assert.Equal(2, row.Choices.Count);
        Assert.Contains("حلقه جدید", row.ChangesText, StringComparison.Ordinal);
        Assert.False(await vm.CommitAsync(Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.ImportDecisionMissing, ERP.MES.Domain.Calendar.JalaliDateError.None), vm.Message);

        row.Choice = ConflictChoice.Update;
        Assert.True(await vm.CommitAsync(Token));
        var item = (await h.Workflow.Requests.GetAsync(h.Planning, id, Token)).Value!.Request.Value.Items.Single(i => i.CustomerItemRef == "1.10");
        Assert.Equal("حلقه جدید", item.PartName);
    }

    [Fact]
    public async Task A_file_with_errors_shows_them_and_cannot_be_committed()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await ReadyAsync(h, WorkType.Make, "M-2");
        var vm = For(h, h.Planning);
        await vm.LoadAsync(Token);
        vm.SelectedRequest = vm.Requests.Single(r => r.RequestId == id);

        Assert.True(await vm.PreviewAsync("bad.xlsx", File(["1.10", "حلقه", "10", "PIECE", "NOPE", "1.5"]), Token));

        Assert.False(vm.CanCommit);
        Assert.Contains("خطا: ۱".Replace("۱", "1"), vm.Summary, StringComparison.Ordinal);
        Assert.NotEmpty(vm.Rows.Single().MessagesText);
        Assert.False(await vm.CommitAsync(Token));
        Assert.Empty((await h.Workflow.Requests.GetAsync(h.Planning, id, Token)).Value!.Request.Value.Items);
    }

    [Fact]
    public async Task Everyone_but_planning_sees_no_import_and_the_application_refuses_them_too()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await ReadyAsync(h, WorkType.Make, "M-3");
        foreach (var user in new[] { h.Drawing, h.Central, h.Manager, h.Warehouse, h.Qc, h.Production })
        {
            var vm = For(h, user);
            await vm.LoadAsync(Token);
            Assert.False(vm.CanImport);
            Assert.False(vm.CanCommit);
        }

        var planningVm = For(h, h.Drawing);
        await planningVm.LoadAsync(Token);
        planningVm.SelectedRequest = planningVm.Requests.FirstOrDefault(r => r.RequestId == id);
        Assert.False(await planningVm.PreviewAsync("a.xlsx", File(["1", "x", "1", "PIECE", "X", "1"]), Token));
        Assert.False(planningVm.HasPreview);
    }

    [Fact]
    public async Task The_template_the_screen_offers_is_a_readable_workbook_with_the_documented_headers()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = For(h, h.Planning);

        var read = new XlsxWorkbookReader().Read(vm.BuildTemplate());

        Assert.Equal(WorkbookError.None, read.Error);
        Assert.Equal(ItemImportTemplate.Headers, read.Rows[0].Cells.OrderBy(c => c.Key).Select(c => c.Value.Text).ToArray());
    }
}
