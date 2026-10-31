using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;

namespace ERP.MES.Desktop.Tests;

// The stage-title catalog screen against the real Application services (no window is opened): Planning maintains, others only read (Decision Log #124 B4).
public class StageTitlesViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static StageTitlesViewModel For(WorkflowHarness h, Principal user) => new(h.Workflow.PlanningMaster, () => user);

    [Fact]
    public async Task Planning_adds_retires_and_reactivates_titles_and_the_screen_reports_refusals()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var planning = For(h, h.Planning);
        await planning.LoadAsync(Token);
        Assert.True(planning.CanMaintain);

        Assert.True(await planning.AddAsync("برش لیزری", Token), planning.Message);
        Assert.False(planning.IsErrorMessage);
        Assert.Contains(planning.Titles, t => t.Title == "برش لیزری" && t.IsActive);

        Assert.False(await planning.AddAsync("برش لیزری", Token));
        Assert.True(planning.IsErrorMessage);

        planning.Selected = planning.Titles.Single(t => t.Title == "برش لیزری");
        Assert.True(await planning.SetSelectedActiveAsync(false, Token), planning.Message);
        Assert.Equal("بازنشسته", planning.Titles.Single(t => t.Title == "برش لیزری").StatusText);
        planning.Selected = planning.Titles.Single(t => t.Title == "برش لیزری");
        Assert.True(await planning.SetSelectedActiveAsync(true, Token));
        Assert.Equal("فعال", planning.Titles.Single(t => t.Title == "برش لیزری").StatusText);
    }

    [Fact]
    public async Task Other_departments_see_the_list_but_cannot_change_it()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        Assert.True(await For(h, h.Planning).AddAsync("لیست", Token));
        foreach (var user in new[] { h.Qc, h.Production, h.Management, h.Admin })
        {
            var screen = For(h, user);
            await screen.LoadAsync(Token);
            Assert.False(screen.CanMaintain);
            Assert.Contains(screen.Titles, t => t.Title == "لیست");
            Assert.False(await screen.AddAsync("ممنوع", Token));
            Assert.True(screen.IsErrorMessage);
        }
    }
}
