using System.Text;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Infrastructure.Tests;

// The technical drawing of an item (Decision Log #125 Q14): Drawing uploads and replaces a PDF, revisions are kept, everybody may open it, nothing is loaded until asked, a missing drawing only warns.
public class ItemDrawingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Pdf(string text) => Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional drawing " + text + "\n%%EOF");

    private static async Task<long> ItemAsync(KitWorld w, string requestNo)
    {
        var (requestId, _) = await w.ReadyRequestAsync(requestNo, contractRequired: false);
        var material = await w.MaterialAsync();
        var piece = await w.PieceTypeAsync();
        var version = await w.RequestVersionAsync(requestId);
        var defined = await w.Services.ItemDefinition.DefineItemAsync(w.Planning, requestId, version, new DefineItemCommand("A", "Part", 10m, Priority.Normal, piece, material, 1m, 100m, 20m), Token);
        Assert.True(defined.Succeeded, defined.Status + " " + defined.Issue);
        return defined.Value!.Value.Items.Single().Id;
    }

    [Fact]
    public async Task Drawing_uploads_and_replaces_keeps_revisions_and_audits_the_actor()
    {
        using var w = await KitWorld.StartAsync();
        var itemId = await ItemAsync(w, "DW-1");
        var drawings = w.Services.Drawings;

        Assert.Empty((await drawings.ListCurrentAsync(w.Planning, [itemId], Token)).Value!);
        Assert.Equal(OperationStatus.NotFound, (await drawings.OpenAsync(w.Planning, itemId, null, Token)).Status);

        var first = await drawings.UploadAsync(w.Drawing, itemId, "part-a.pdf", Pdf("one"), Token);
        Assert.True(first.Succeeded, first.Status + " " + first.Issue);
        Assert.Equal(1, first.Value!.Revision);
        w.Tick();
        var second = await drawings.UploadAsync(w.Drawing, itemId, "part-a-b.pdf", Pdf("two"), Token);
        Assert.Equal(2, second.Value!.Revision);

        var current = Assert.Single((await drawings.ListCurrentAsync(w.Qc, [itemId], Token)).Value!);
        Assert.Equal(2, current.Revision);
        Assert.Equal("part-a-b.pdf", current.FileName);
        var revisions = (await drawings.ListRevisionsAsync(w.Production, itemId, Token)).Value!;
        Assert.Equal([2, 1], revisions.Select(r => r.Revision).ToArray());
        Assert.Equal([true, false], revisions.Select(r => r.IsCurrent).ToArray());

        // Opening is explicit: the current revision by default, an older one by number; the bytes are the ones uploaded.
        Assert.Equal(Pdf("two"), (await drawings.OpenAsync(w.Qc, itemId, null, Token)).Value!.Content);
        Assert.Equal(Pdf("one"), (await drawings.OpenAsync(w.Qc, itemId, 1, Token)).Value!.Content);
        Assert.Equal(OperationStatus.NotFound, (await drawings.OpenAsync(w.Qc, itemId, 9, Token)).Status);

        Assert.Equal(["ItemDrawingUploaded", "ItemDrawingReplaced"], w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode LIKE 'ItemDrawing%' ORDER BY Id"));
        Assert.Equal(2, w.Fixture.Db.Query("SELECT Id FROM Documents WHERE Type = 3").Count);
        Assert.Equal(1, w.Fixture.Db.Query("SELECT Id FROM ItemDrawings WHERE SupersededAtUtc IS NULL").Count);
    }

    [Fact]
    public async Task Only_drawing_uploads_and_only_a_pdf_of_at_most_twenty_mebibytes_is_accepted()
    {
        using var w = await KitWorld.StartAsync();
        var itemId = await ItemAsync(w, "DW-2");
        var drawings = w.Services.Drawings;

        foreach (var user in new[] { w.Planning, w.Qc, w.Production, w.Manager, w.Admin, w.Central })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await drawings.UploadAsync(user, itemId, "x.pdf", Pdf("x"), Token)).Status);
            Assert.False(await drawings.CanManageAsync(user, Token));
        }

        Assert.True(await drawings.CanManageAsync(w.Drawing, Token));
        Assert.Equal(ValidationIssue.PdfNotPdf, (await drawings.UploadAsync(w.Drawing, itemId, "x.pdf", Encoding.ASCII.GetBytes("not a pdf"), Token)).Issue);
        Assert.Equal(ValidationIssue.PdfEmpty, (await drawings.UploadAsync(w.Drawing, itemId, "x.pdf", [], Token)).Issue);
        Assert.Equal(ValidationIssue.PdfFileNameInvalid, (await drawings.UploadAsync(w.Drawing, itemId, "x.png", Pdf("x"), Token)).Issue);
        Assert.Equal(ValidationIssue.DrawingItemNotFound, (await drawings.UploadAsync(w.Drawing, 987654, "x.pdf", Pdf("x"), Token)).Issue);

        var limit = (int)DocumentPolicy.DefaultMaxRequestPdfBytes;
        var exactly = Pdf("x").Concat(new byte[limit - Pdf("x").Length]).ToArray();
        Assert.Equal(limit, exactly.Length);
        Assert.True((await drawings.UploadAsync(w.Drawing, itemId, "big.pdf", exactly, Token)).Succeeded);
        Assert.Equal(ValidationIssue.PdfTooLarge, (await drawings.UploadAsync(w.Drawing, itemId, "big.pdf", [.. exactly, 0], Token)).Issue);
        Assert.Equal(1, w.Fixture.Db.Query("SELECT Id FROM ItemDrawings").Count);
    }

    [Fact]
    public async Task A_missing_drawing_warns_when_the_first_stage_is_released_but_does_not_block_it()
    {
        using var w = await KitWorld.StartAsync();
        var (requestId, _) = await w.ReadyRequestAsync("DW-3", contractRequired: false);
        var unit = await w.AddUnitAsync("MILLING");
        await w.AddMachineAsync("FCC-06", unit);
        await w.MaterialAsync();
        var itemId = await w.DefineItemAsync(requestId, "B", [unit]);
        var withDrawing = await w.DefineItemAsync(requestId, "C", [unit]);
        Assert.True((await w.Services.Drawings.UploadAsync(w.Drawing, withDrawing, "c.pdf", Pdf("c"), Token)).Succeeded);

        var released = await w.Services.StageWork.PerformAsync(w.Planning, StageAction.Release, await w.TargetAsync(requestId, itemId, 1), Token);
        Assert.True(released.Succeeded, released.Status + " " + released.Issue);
        Assert.Equal([StageWorkService.NoDrawingWarning], released.WarningList);

        var releasedWith = await w.Services.StageWork.PerformAsync(w.Planning, StageAction.Release, await w.TargetAsync(requestId, withDrawing, 1), Token);
        Assert.True(releasedWith.Succeeded, releasedWith.Status + " " + releasedWith.Issue);
        Assert.Empty(releasedWith.WarningList);
    }
}
