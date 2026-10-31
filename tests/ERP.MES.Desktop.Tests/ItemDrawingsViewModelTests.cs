using System.Text;
using ERP.MES.Application.Requests;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The drawings screen against the real Application services (no window is opened): Drawing uploads, everyone opens, nothing loads on its own (Decision Log #125 Q14).
public class ItemDrawingsViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Pdf(string text) => Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional drawing " + text + "\n%%EOF");

    private static async Task<(long RequestId, long ItemId)> ItemAsync(WorkflowHarness h)
    {
        var customer = (await h.Workflow.Customers.CreateAsync(h.Central, "Fictional Customer", Token)).Value!;
        var created = (await h.Workflow.Requests.CreateAsync(h.Central, new CreateRequestCommand(customer.Value.Id, "DR-1", WorkType.Make, "1405/07/20", null, null), Token)).Value!;
        var withPdf = (await h.Workflow.Documents.AttachRequestPdfAsync(h.Central, created.Value.Id, created.Version, "a.pdf", Pdf("request"), Token)).Value!;
        var sent = (await h.Workflow.Requests.SubmitToManagementAsync(h.Central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        var approved = (await h.Workflow.Lifecycle.ApproveAsync(h.Manager, sent.Value.Id, sent.Version, Token)).Value!;
        var forwarded = (await h.Workflow.Lifecycle.ForwardToDrawingAsync(h.Planning, approved.Value.Id, approved.Version, Token)).Value!;
        Assert.True((await h.Workflow.Lifecycle.RecordDrawingResultAsync(h.Drawing, forwarded.Value.Id, forwarded.Version, DrawingOutcome.FullyAccepted, null, Token)).Succeeded);
        Assert.True((await h.Workflow.Contracts.DecideRequirementAsync(h.Planning, created.Value.Id, ContractRequirement.NotRequired, null, Token)).Succeeded);
        Assert.True((await h.Workflow.Materials.AddMaterialAsync(h.Warehouse, "ST37-R30", "میلگرد ST37", "ST37", "میلگرد", Token)).Succeeded);
        var piece = (await h.Workflow.QcMaster.LoadAsync(h.Qc, Token)).Value!.QuantityTypes.First(t => t.Value.Code == "PIECE").Value.Id;
        var material = (await h.Workflow.Materials.LoadAsync(h.Warehouse, Token)).Value!.Materials.First().Value.Id;
        var version = (await h.Workflow.Requests.GetAsync(h.Qc, created.Value.Id, Token)).Value!.Request.Version;
        var defined = await h.Workflow.ItemDefinition.DefineItemAsync(h.Planning, created.Value.Id, version, new DefineItemCommand("A", "قطعه", 10m, Priority.Normal, piece, material, 1m, null, null), Token);
        Assert.True(defined.Succeeded, defined.Status + " " + defined.Issue);
        return (created.Value.Id, defined.Value!.Value.Items.Single().Id);
    }

    private static ItemDrawingsViewModel For(WorkflowHarness h, ERP.MES.Application.Security.Principal user) => new(h.Workflow.Requests, h.Workflow.Drawings, h.Dates, () => user);

    [Fact]
    public async Task Drawing_uploads_and_replaces_through_the_screen_and_every_other_user_can_only_open()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var (requestId, itemId) = await ItemAsync(h);
        var drawing = For(h, h.Drawing);
        await drawing.LoadAsync(Token);
        Assert.True(drawing.CanManage);

        await drawing.SelectRequestAsync(drawing.Requests.Single(), Token);
        var row = Assert.Single(drawing.Items);
        Assert.Equal("بدون نقشه", row.DrawingText);
        await drawing.SelectItemAsync(row, Token);

        Assert.True(await drawing.UploadAsync("a.pdf", Pdf("one"), Token), drawing.Message);
        Assert.False(drawing.IsErrorMessage);
        Assert.Equal("نسخه 1: a.pdf", drawing.Items.Single().DrawingText);
        Assert.True(await drawing.UploadAsync("b.pdf", Pdf("two"), Token));
        Assert.Equal(["فعلی", "قدیمی"], drawing.Revisions.Select(r => r.StatusText).ToArray());

        Assert.False(await drawing.UploadAsync("c.png", Pdf("x"), Token));
        Assert.True(drawing.IsErrorMessage);

        foreach (var user in new[] { h.Planning, h.Qc, h.Production, h.Management })
        {
            var screen = For(h, user);
            await screen.LoadAsync(Token);
            Assert.False(screen.CanManage);
            await screen.SelectRequestAsync(screen.Requests.Single(), Token);
            Assert.Equal("نسخه 2: b.pdf", screen.Items.Single().DrawingText);
            await screen.SelectItemAsync(screen.Items.Single(), Token);
            Assert.False(await screen.UploadAsync("z.pdf", Pdf("z"), Token));
            Assert.True(screen.IsErrorMessage);
        }

        Assert.Equal(requestId, drawing.Requests.Single().RequestId);
        Assert.True(itemId > 0);
    }

    [Fact]
    public async Task Opening_writes_the_explicitly_requested_revision_to_a_temporary_file_and_says_so_when_there_is_none()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var (_, itemId) = await ItemAsync(h);
        var folder = Path.Combine(Path.GetTempPath(), "erp-mes-drawing-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var none = await DrawingOpening.PrepareAsync(h.Workflow.Drawings, h.Qc, itemId, null, folder, Token);
            Assert.Null(none.Path);
            Assert.Equal("برای این آیتم نقشه‌ای بارگذاری نشده است.", none.Message);

            Assert.True((await h.Workflow.Drawings.UploadAsync(h.Drawing, itemId, "a.pdf", Pdf("one"), Token)).Succeeded);
            Assert.True((await h.Workflow.Drawings.UploadAsync(h.Drawing, itemId, "b.pdf", Pdf("two"), Token)).Succeeded);

            var current = await DrawingOpening.PrepareAsync(h.Workflow.Drawings, h.Qc, itemId, null, folder, Token);
            Assert.Equal(Pdf("two"), await File.ReadAllBytesAsync(current.Path!, Token));
            var old = await DrawingOpening.PrepareAsync(h.Workflow.Drawings, h.Qc, itemId, 1, folder, Token);
            Assert.Equal(Pdf("one"), await File.ReadAllBytesAsync(old.Path!, Token));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
