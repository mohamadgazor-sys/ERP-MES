using ERP.MES.Application.Persistence;
using ERP.MES.Application.Imports;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Infrastructure.Tests;

// The stage-title catalog (Decision Log #124 B4): Planning maintains it, stage definition and import accept only active titles, and a stage keeps its own copy of the text.
public class StageTitleCatalogTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Planning_adds_titles_exactly_once_and_only_valid_text_and_everyone_else_is_refused()
    {
        using var w = await KitWorld.StartAsync();
        var master = w.Services.PlanningMaster;

        var added = await master.AddStageTitleAsync(w.Planning, "پولیش‌کاری", Token);
        Assert.True(added.Succeeded, added.Status + " " + added.Issue);
        Assert.Equal(ValidationIssue.StageTitleDuplicate, (await master.AddStageTitleAsync(w.Planning, "پولیش‌کاری", Token)).Issue);
        Assert.True((await master.AddStageTitleAsync(w.Planning, "t ", Token)).Issue == ValidationIssue.StageTitleRequired);
        Assert.Equal(ValidationIssue.StageTitleRequired, (await master.AddStageTitleAsync(w.Planning, " ", Token)).Issue);
        Assert.Equal(ValidationIssue.StageTitleRequired, (await master.AddStageTitleAsync(w.Planning, new string('x', 201), Token)).Issue);
        Assert.True((await master.AddStageTitleAsync(w.Planning, new string('x', 200), Token)).Succeeded);

        // Titles are compared exactly: a different letter case is a different title.
        Assert.True((await master.AddStageTitleAsync(w.Planning, "TURNING", Token)).Succeeded);

        foreach (var user in new[] { w.Qc, w.Production, w.Manager, w.Admin, w.Central, w.Drawing })
        {
            Assert.Equal(OperationStatus.NotAuthorized, (await master.AddStageTitleAsync(user, "ممنوع", Token)).Status);
            Assert.False(await master.CanMaintainStageTitlesAsync(user, Token));
        }

        Assert.True(await master.CanMaintainStageTitlesAsync(w.Planning, Token));
        Assert.Contains("StageTitleAdded", w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode = 'StageTitleAdded'"));
        var listed = await master.ListStageTitlesAsync(w.Qc, Token);
        Assert.Contains(listed.Value!, t => t.Value.Title == "پولیش‌کاری");
    }

    [Fact]
    public async Task A_stage_needs_an_active_catalog_title_and_keeps_its_text_after_the_title_is_retired()
    {
        using var w = await KitWorld.StartAsync();
        var (requestId, _) = await w.ReadyRequestAsync("CT-1", contractRequired: false);
        var unit = await w.AddUnitAsync("MILLING");
        var material = await w.MaterialAsync();
        var piece = await w.PieceTypeAsync();
        var version = await w.RequestVersionAsync(requestId);
        var defined = await w.Services.ItemDefinition.DefineItemAsync(w.Planning, requestId, version, new DefineItemCommand("A", "Part", 10m, Priority.Normal, piece, material, 1m, 100m, 20m), Token);
        Assert.True(defined.Succeeded, defined.Status + " " + defined.Issue);
        var itemId = defined.Value!.Value.Items.Single().Id;

        async Task<Outcome<Stored<Request>>> Define(string title)
        {
            var current = (await w.Services.Requests.GetAsync(w.Planning, requestId, Token)).Value!;
            return await w.Services.ItemDefinition.DefineStageAsync(w.Planning, requestId, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand(title, 5m, unit, null), Token);
        }

        Assert.Equal(ValidationIssue.StageTitleNotInCatalog, (await Define("عنوان ناشناخته")).Issue);
        Assert.Equal(ValidationIssue.StageTitleNotInCatalog, (await Define("turning")).Issue);

        var catalog = await w.Services.PlanningMaster.ListStageTitlesAsync(w.Planning, Token);
        var turning = catalog.Value!.Single(t => t.Value.Title == "Turning");
        var first = await Define("Turning");
        Assert.True(first.Succeeded, first.Status + " " + first.Issue);

        // Retired: refused for new stages; the existing stage keeps its text; reactivated: accepted again.
        var retired = await w.Services.PlanningMaster.SetStageTitleActiveAsync(w.Planning, turning.Value.Id, turning.Version, false, Token);
        Assert.True(retired.Succeeded);
        Assert.Equal(ValidationIssue.StageTitleNotInCatalog, (await Define("Turning")).Issue);
        Assert.Equal("Turning", (await w.Services.Requests.GetAsync(w.Planning, requestId, Token)).Value!.Request.Value.Items.Single().Stages.Single().StageTitle);

        var again = await w.Services.PlanningMaster.SetStageTitleActiveAsync(w.Planning, turning.Value.Id, retired.Value!.Version, true, Token);
        Assert.True(again.Succeeded);
        Assert.True((await Define("Turning")).Succeeded);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.PlanningMaster.SetStageTitleActiveAsync(w.Qc, turning.Value.Id, again.Value!.Version, false, Token)).Status);
        Assert.Equal(2, w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode = 'StageTitleActivityChanged'").Count);
    }
}
