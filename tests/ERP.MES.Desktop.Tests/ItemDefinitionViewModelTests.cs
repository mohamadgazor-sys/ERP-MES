using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The item-definition screen against the real Application services (no window is opened).
public class ItemDefinitionViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional\n%%EOF\n");

    private static ItemDefinitionViewModel For(WorkflowHarness h, Principal user) =>
        new(h.Workflow.ItemDefinition, h.Workflow.Requests, h.Workflow.Gates, h.Workflow.QcMaster, h.Workflow.Materials, h.Workflow.PlanningMaster, ERP.MES.Domain.Calendar.DigitStyle.Persian, () => user, h.Workflow.StageWork);

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

    [Fact]
    public async Task The_stage_title_choices_are_the_active_catalog_titles_and_a_retired_title_is_not_offered_but_stays_in_old_stage_snapshots()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = For(h, h.Planning);
        await vm.LoadAsync(Token);
        Assert.Contains("فرزکاری", vm.StageTitleChoices);

        var titles = (await h.Workflow.PlanningMaster.ListStageTitlesAsync(h.Planning, Token)).Value!;
        var milling = titles.Single(t => t.Value.Title == "فرزکاری");
        Assert.True((await h.Workflow.PlanningMaster.SetStageTitleActiveAsync(h.Planning, milling.Value.Id, milling.Version, false, Token)).Succeeded);
        await vm.LoadAsync(Token);

        Assert.DoesNotContain("فرزکاری", vm.StageTitleChoices);
        Assert.Contains("تراشکاری", vm.StageTitleChoices);
    }

    [Fact]
    public async Task The_priority_choices_are_the_three_exact_labels()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);

        Assert.Equal(new[] { "خیلی فوری", "فوری", "عادی" }, For(h, h.Planning).Priorities.Select(p => p.Text).ToArray());
    }

    [Fact]
    public async Task Planning_defines_a_make_item_and_its_stages_with_persian_digits_and_the_screen_shows_the_gate()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await ReadyAsync(h, WorkType.Make, "M-1");
        Assert.True((await h.Workflow.Materials.AddMaterialAsync(h.Warehouse, "ST37-R30", "میلگرد ST37", "ST37", "میلگرد", Token)).Succeeded);
        var unit = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Admin, "MILLING", "فرزکاری", Token)).Value!;
        var machine = (await h.Workflow.PlanningMaster.AddMachineAsync(h.Admin, "FCC-06", "فرز CNC", unit.Value.Id, Token)).Value!;
        var vm = For(h, h.Planning);

        await vm.LoadAsync(Token);
        await vm.SelectRequestAsync(id, Token);
        Assert.True(vm.HasRequest);
        Assert.True(vm.CanDefineItem);
        Assert.Contains("دروازه‌ها برقرار است", vm.GateText, StringComparison.Ordinal);

        var piece = vm.QuantityTypeChoices.First(t => t.Text.StartsWith("PIECE", StringComparison.Ordinal)).Value;
        var material = vm.MaterialChoices.Single().Value;
        Assert.False(await vm.DefineItemAsync("1.10", "حلقه", "۱۰", Priority.Normal, piece, material, "abc", null, null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.WeightInvalid, ERP.MES.Domain.Calendar.JalaliDateError.None), vm.Message);
        Assert.False(await vm.DefineItemAsync("1.10", "حلقه", "۱۰", Priority.Normal, piece, material, null, null, null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.ItemSpecificationIncomplete, ERP.MES.Domain.Calendar.JalaliDateError.None), vm.Message);

        Assert.True(await vm.DefineItemAsync("1.10", "حلقه", "۱۰", Priority.Urgent, piece, material, "۱٫۲۵", "۶۰۰۰", "۳۰", Token));
        var row = vm.Items.Single();
        Assert.Equal("۱۰", row.QuantityText);
        Assert.Contains("وزن: ۱.۲۵ kg", row.SpecificationText, StringComparison.Ordinal);
        Assert.Contains("طول: ۶۰۰۰ mm", row.SpecificationText, StringComparison.Ordinal);
        Assert.Equal("فوری", row.PriorityText);

        vm.SelectedItem = vm.Items.Single();
        Assert.True(vm.CanDefineStage);
        Assert.False(await vm.DefineStageAsync("فرزکاری", "۵٫۵", 987654, null, Token));
        Assert.True(await vm.DefineStageAsync("فرزکاری", "۵٫۵", unit.Value.Id, machine.Value.Id, Token));
        var stage = vm.Stages.Single();
        Assert.Equal("۱", stage.StageNo.ToString().Replace("1", "۱"));
        Assert.Equal("MILLING", stage.UnitText);
        Assert.Equal("FCC-06", stage.MachineText);
        Assert.Equal("۵.۵", stage.MinutesText);
        Assert.Equal(1, vm.Items.Single().StageCount);
    }

    [Fact]
    public async Task Releasing_all_items_names_the_blocking_item_first_then_releases_everything_once_and_refuses_a_second_release()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await ReadyAsync(h, WorkType.Make, "M-9");
        Assert.True((await h.Workflow.Materials.AddMaterialAsync(h.Warehouse, "ST37-R30", "میلگرد ST37", "ST37", "میلگرد", Token)).Succeeded);
        var unit = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Admin, "MILLING", "فرزکاری", Token)).Value!;
        var vm = For(h, h.Planning);
        await vm.LoadAsync(Token);
        await vm.SelectRequestAsync(id, Token);
        var piece = vm.QuantityTypeChoices.First(t => t.Text.StartsWith("PIECE", StringComparison.Ordinal)).Value;
        var material = vm.MaterialChoices.Single().Value;
        Assert.True(await vm.DefineItemAsync("A.1", "الف", "۱۰", Priority.Normal, piece, material, "۱", "۱۰۰", "۲۰", Token));
        Assert.True(await vm.DefineItemAsync("B.1", "ب", "۱۰", Priority.Normal, piece, material, "۱", "۱۰۰", "۲۰", Token));
        var itemA = vm.Items[0];
        var itemB = vm.Items[1];
        vm.SelectedItem = itemA;
        Assert.True(await vm.DefineStageAsync("فرزکاری", "۵", unit.Value.Id, null, Token));

        // Item B has no stage: nothing is released and the message names it.
        Assert.True(vm.CanRelease);
        Assert.False(await vm.ReleaseAllAsync(Token));
        Assert.Contains(Messages.Ltr(itemB.ItemNo), vm.Message, StringComparison.Ordinal);
        Assert.Contains(Messages.ForIssue(ValidationIssue.ItemHasNoStages, ERP.MES.Domain.Calendar.JalaliDateError.None), vm.Message, StringComparison.Ordinal);

        vm.SelectedItem = vm.Items.Single(i => i.ItemId == itemB.ItemId);
        Assert.True(await vm.DefineStageAsync("تراشکاری", "۵", unit.Value.Id, null, Token));
        Assert.True(await vm.ReleaseAllAsync(Token));
        Assert.Contains(Messages.Ltr(itemA.ItemNo), vm.Message, StringComparison.Ordinal);
        Assert.Contains(Messages.Ltr(itemB.ItemNo), vm.Message, StringComparison.Ordinal);

        Assert.False(await vm.ReleaseAllAsync(Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.NothingToRelease, ERP.MES.Domain.Calendar.JalaliDateError.None), vm.Message);

        // Production cannot release; the service refuses.
        var production = For(h, h.Production);
        await production.LoadAsync(Token);
        Assert.Equal(OperationStatus.NotAuthorized, (await h.Workflow.StageWork.ReleaseAllAsync(h.Production, id, Token)).Status);
    }

    [Fact]
    public void The_release_question_is_the_exact_owner_text()
    {
        Assert.Equal("آیا مطمئن هستید که می‌خواهید تمام آیتم‌ها را به همراه مراحل آن‌ها به صف کار واحدهای تولیدی ارسال کنید؟ پس از انجام این عملیات امکان بازگشت وجود ندارد.", ItemDefinitionViewModel.ReleaseConfirmationText);
    }

    [Fact]
    public async Task While_a_gate_holds_the_screen_explains_it_and_offers_no_definition_and_the_application_refuses_too()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var customer = (await h.Workflow.Customers.CreateAsync(h.Central, "Fictional Customer", Token)).Value!;
        var created = (await h.Workflow.Requests.CreateAsync(h.Central, new CreateRequestCommand(customer.Value.Id, "G-1", WorkType.Make, "1405/07/20", null, null), Token)).Value!;
        var vm = For(h, h.Planning);

        await vm.LoadAsync(Token);
        await vm.SelectRequestAsync(created.Value.Id, Token);

        Assert.False(vm.CanDefineItem);
        Assert.Contains("تعریف آیتم ممکن نیست", vm.GateText, StringComparison.Ordinal);
        Assert.False(await vm.DefineItemAsync("1", "x", "1", Priority.Normal, null, null, null, null, null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.ItemGateNotSatisfied, ERP.MES.Domain.Calendar.JalaliDateError.None), vm.Message);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Drawing_and_the_other_departments_cannot_define_items()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await ReadyAsync(h, WorkType.Repair, "R-1");

        foreach (var user in new[] { h.Drawing, h.Central, h.Management, h.Warehouse, h.Qc, h.Production })
        {
            var vm = For(h, user);
            await vm.LoadAsync(Token);
            if (vm.Requests.Count > 0)
            {
                await vm.SelectRequestAsync(id, Token);
            }

            Assert.False(vm.CanDefineItem);
            Assert.False(await vm.DefineItemAsync("1", "x", "1", Priority.Normal, null, null, null, null, null, Token));
        }

        Assert.Empty((await h.Workflow.Requests.GetAsync(h.Central, id, Token)).Value!.Request.Value.Items);
    }
}
