using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The supervisor and operator work board against the real Application services (no window is opened). The harness zone is UTC+3:30; the clock is set so that local time is 09:00 on a working Saturday.
public class WorkBoardViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional\n%%EOF\n");

    private static readonly DateOnly Saturday = new(2026, 10, 24);

    private sealed record Floor(long Unit, long Turning, long Machine, long Operator, long RequestId, long ItemId, long Template);

    private static WorkBoardViewModel For(WorkflowHarness h, Principal user) =>
        new(h.Workflow.UnitQueue, h.Workflow.WorkSessions, h.Workflow.StageWork, h.Dates, DigitStyle.Persian, () => h.Clock.UtcNow, () => user);

    private static async Task<long> ReadyRequestAsync(WorkflowHarness h, string requestNo, bool contractRequired)
    {
        var customer = (await h.Workflow.Customers.CreateAsync(h.Central, "Fictional Customer " + requestNo, Token)).Value!;
        var created = (await h.Workflow.Requests.CreateAsync(h.Central, new CreateRequestCommand(customer.Value.Id, requestNo, WorkType.Make, "1405/07/20", null, null), Token)).Value!;
        var withPdf = (await h.Workflow.Documents.AttachRequestPdfAsync(h.Central, created.Value.Id, created.Version, "a.pdf", Pdf(), Token)).Value!;
        var sent = (await h.Workflow.Requests.SubmitToManagementAsync(h.Central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        var approved = (await h.Workflow.Lifecycle.ApproveAsync(h.Manager, sent.Value.Id, sent.Version, Token)).Value!;
        var forwarded = (await h.Workflow.Lifecycle.ForwardToDrawingAsync(h.Planning, approved.Value.Id, approved.Version, Token)).Value!;
        Assert.True((await h.Workflow.Lifecycle.RecordDrawingResultAsync(h.Drawing, forwarded.Value.Id, forwarded.Version, DrawingOutcome.FullyAccepted, null, Token)).Succeeded);
        Assert.True((await h.Workflow.Contracts.DecideRequirementAsync(h.Planning, created.Value.Id, contractRequired ? ContractRequirement.Required : ContractRequirement.NotRequired, null, Token)).Succeeded);
        return created.Value.Id;
    }

    private static async Task<long> DefineAsync(WorkflowHarness h, long requestId, string itemNo, params long[] units)
    {
        var piece = (await h.Workflow.QcMaster.LoadAsync(h.Qc, Token)).Value!.QuantityTypes.First(t => t.Value.Code == "PIECE").Value.Id;
        var material = (await h.Workflow.Materials.LoadAsync(h.Warehouse, Token)).Value!.Materials.First().Value.Id;
        var version = (await h.Workflow.Requests.GetAsync(h.Qc, requestId, Token)).Value!.Request.Version;
        var defined = await h.Workflow.ItemDefinition.DefineItemAsync(h.Planning, requestId, version, new DefineItemCommand(itemNo, "قطعه " + itemNo, 10m, Priority.Normal, piece, material, 1m, null, null), Token);
        Assert.True(defined.Succeeded, defined.Status + " " + defined.Issue);
        var itemId = defined.Value!.Value.Items.Single(i => i.ItemNo == itemNo).Id;
        foreach (var unit in units)
        {
            var current = (await h.Workflow.Requests.GetAsync(h.Planning, requestId, Token)).Value!;
            Assert.True((await h.Workflow.ItemDefinition.DefineStageAsync(h.Planning, requestId, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand("مرحله", 2m, unit, null), Token)).Succeeded);
        }

        return itemId;
    }

    private static async Task<Floor> FloorAsync(WorkflowHarness h)
    {
        // 09:00 local on the Saturday (UTC+3:30).
        h.Clock.UtcNow = new DateTime(2026, 10, 24, 5, 30, 0, DateTimeKind.Utc);
        Assert.True((await h.Workflow.Materials.AddMaterialAsync(h.Warehouse, "ST37-R30", "میلگرد ST37", "ST37", "میلگرد", Token)).Succeeded);
        var unit = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Admin, "MILLING", "فرزکاری", Token)).Value!.Value.Id;
        var turning = (await h.Workflow.PlanningMaster.AddUnitAsync(h.Admin, "TURNING", "تراشکاری", Token)).Value!.Value.Id;
        var template = (await h.Workflow.Calendar.AddTemplateAsync(h.Planning, "D", "شیفت D", new TimeOnly(8, 0), new TimeOnly(17, 0), new TimeOnly(12, 0), new TimeOnly(13, 0), Token)).Value!.Value.Id;
        Assert.True((await h.Workflow.Calendar.AddUnitShiftAsync(h.Planning, unit, template, 3, 1, false, Token)).Succeeded);
        var machine = (await h.Workflow.PlanningMaster.AddMachineAsync(h.Admin, "FCC-06", "فرز", unit, Token)).Value!.Value.Id;
        Assert.True((await h.Workflow.UnitDesignations.AssignAsync(h.Admin, h.Production.UserId, unit, Token)).Succeeded);
        var row = (await h.Workflow.UnitDesignations.LoadAsync(h.ProductionManager, Token)).Value!.Rows.Single(r => r.UserId == h.Production.UserId);
        Assert.True((await h.Workflow.UnitDesignations.SetSupervisorAsync(h.ProductionManager, row.DesignationId, row.Version, true, Token)).Succeeded);
        var op = (await h.Workflow.Operators.RegisterAsync(h.Admin, "OP-1", "علی رضایی", h.Production.UserId, Token)).Value!.Value.Id;
        Assert.True((await h.Workflow.Operators.AssignAsync(h.Admin, op, machine, "1405/01/01", false, null, Token)).Succeeded);
        Assert.True((await h.Workflow.Operators.RecordQualificationAsync(h.Planning, op, machine, "1405/01/01", null, Token)).Succeeded);
        Assert.True((await h.Workflow.WorkerSchedule.ScheduleAsync(h.Planning, op, Saturday, unit, template, null, null, null, Token)).Succeeded);
        var requestId = await ReadyRequestAsync(h, "R-1", contractRequired: false);
        var itemId = await DefineAsync(h, requestId, "A", unit, turning);
        var current = (await h.Workflow.Requests.GetAsync(h.Planning, requestId, Token)).Value!;
        var target = new StageTarget(requestId, itemId, 1, current.Request.Version, current.Versions.ItemVersion(itemId), current.Versions.Stages.Where(s => s.Key.ItemId == itemId).ToDictionary(s => s.Key.StageNo, s => s.Value));
        Assert.True((await h.Workflow.StageWork.PerformAsync(h.Planning, StageAction.Release, target, Token)).Succeeded);
        return new Floor(unit, turning, machine, op, requestId, itemId, template);
    }

    [Fact]
    public async Task The_supervisor_sees_the_stage_the_choices_and_dispatches_starts_pauses_resumes_and_finishes_with_persian_texts()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var f = await FloorAsync(h);
        var board = For(h, h.Production);

        await board.LoadAsync(Token);

        Assert.True(board.IsSupervisor);
        Assert.Equal("MILLING", board.SelectedUnit!.Code);
        var stage = Assert.Single(board.Stages);
        Assert.Equal("قابل اجرا", stage.BlockText);
        Assert.Equal(["FCC-06"], board.Machines.Select(m => m.Code).ToArray());
        Assert.Equal(["OP-1"], board.Operators.Select(o => o.Code).ToArray());
        Assert.Equal(7, board.Reasons.Count);
        board.SelectStage(stage);
        Assert.True(board.CanDispatchSelected);

        await board.DispatchAsync(board.Machines[0], board.Operators[0], startNow: true, null);

        Assert.False(board.IsErrorMessage, board.Message);
        var session = Assert.Single(board.Sessions);
        Assert.Equal("در حال اجرا", session.StatusText);
        Assert.Equal(Messages.Ltr("FCC-06"), session.MachineText);
        Assert.Contains("فعال ۱", board.Stages[0].SessionsText, StringComparison.Ordinal);

        h.Clock.Advance(TimeSpan.FromMinutes(60));
        await board.PauseAsync(board.Sessions[0], board.Reasons.Single(r => r.Value.Code == PauseReason.MaterialUnavailable), "مواد نرسید");
        Assert.Equal("متوقف", board.Sessions[0].StatusText);
        Assert.Contains("نبود مواد", board.Sessions[0].PauseHistoryText, StringComparison.Ordinal);
        Assert.Equal("۱:۰۰", board.Sessions[0].ActiveText);

        h.Clock.Advance(TimeSpan.FromMinutes(30));
        await board.ResumeAsync(board.Sessions[0], null);
        h.Clock.Advance(TimeSpan.FromMinutes(30));
        await board.SubmitStageForQcAsync();
        Assert.True(board.IsErrorMessage);
        Assert.Equal(Messages.ForIssue(ValidationIssue.SubmitBlockedBySessions, JalaliDateError.None), board.Message);

        await board.FinishAsync(board.Sessions[0], null);

        Assert.Equal("پایان‌یافته", board.Sessions[0].StatusText);
        Assert.Equal("۱:۳۰", board.Sessions[0].ActiveText);
        Assert.Equal("۰:۳۰", board.Sessions[0].PausedText);
        Assert.Equal("منتظر تأیید برنامه‌ریزی", board.Sessions[0].ApprovalText);
        await board.SubmitStageForQcAsync();
        Assert.False(board.IsErrorMessage, board.Message);
        await board.ShowMetricsAsync(Token);
        Assert.Contains("ساعت ماشین", board.MetricsText, StringComparison.Ordinal);
        Assert.Contains("برنامه‌ریزی‌شده در برابر واقعی", board.MetricsText, StringComparison.Ordinal);
        Assert.Equal(f.ItemId, board.Stages[0].Entry.ItemId);
    }

    [Fact]
    public async Task A_later_stage_of_another_unit_is_visible_with_its_reason_but_not_dispatchable()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var f = await FloorAsync(h);
        Assert.True((await h.Workflow.UnitDesignations.AssignAsync(h.Admin, h.Production.UserId, f.Turning, Token)).Succeeded);
        var row = (await h.Workflow.UnitDesignations.LoadAsync(h.ProductionManager, Token)).Value!.Rows.Single(r => r.UserId == h.Production.UserId && r.UnitId == f.Turning);
        Assert.True((await h.Workflow.UnitDesignations.SetSupervisorAsync(h.ProductionManager, row.DesignationId, row.Version, true, Token)).Succeeded);
        var board = For(h, h.Production);
        await board.LoadAsync(Token);

        await board.SelectUnitAsync(f.Turning, Token);

        var stage = Assert.Single(board.Stages);
        Assert.Equal("منتظر تأیید کنترل کیفیت مرحلهٔ قبل", stage.BlockText);
        board.SelectStage(stage);
        Assert.False(board.CanDispatchSelected);
        Assert.Empty(board.Sessions);
    }

    [Fact]
    public async Task The_exact_gate_reason_is_shown_for_a_stage_held_by_the_items_contract()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var f = await FloorAsync(h);
        var requestId = await ReadyRequestAsync(h, "R-2", contractRequired: true);
        await DefineAsync(h, requestId, "B", f.Unit);
        var board = For(h, h.Production);

        await board.LoadAsync(Token);

        var held = board.Stages.Single(s => s.Entry.ItemNo == "B");
        Assert.Contains("شرط‌های آزادسازی تولید برقرار نیست", held.BlockText, StringComparison.Ordinal);
        Assert.Contains("قرارداد در حال تهیه است", held.BlockText, StringComparison.Ordinal);
        board.SelectStage(held);
        Assert.False(board.CanDispatchSelected);

        Assert.True((await h.Workflow.Contracts.DecideItemAsync(h.Planning, requestId, held.Entry.ItemId, ContractRequirement.NotRequired, "بدون قرارداد", Token)).Succeeded);
        await board.LoadAsync(Token);
        Assert.DoesNotContain("قرارداد در حال تهیه", board.Stages.Single(s => s.Entry.ItemNo == "B").BlockText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Planning_and_other_departments_cannot_operate_even_if_the_command_is_sent_and_the_conflict_text_is_shown_for_a_stale_session()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var f = await FloorAsync(h);
        var supervisor = For(h, h.Production);
        await supervisor.LoadAsync(Token);
        supervisor.SelectStage(supervisor.Stages[0]);
        await supervisor.DispatchAsync(supervisor.Machines[0], supervisor.Operators[0], true, null);
        var stale = supervisor.Sessions[0];

        var planning = For(h, h.Planning);
        await planning.LoadAsync(Token);
        Assert.False(planning.IsSupervisor);
        planning.SelectStage(planning.Stages[0]);
        Assert.False(planning.CanDispatchSelected);
        await planning.DispatchAsync(supervisor.Machines[0], supervisor.Operators[0], true, null);
        Assert.Equal(Messages.NotAuthorized, planning.Message);

        h.Clock.Advance(TimeSpan.FromMinutes(5));
        await supervisor.PauseAsync(stale, supervisor.Reasons[0], null);
        await supervisor.PauseAsync(stale, supervisor.Reasons[0], null);
        Assert.True(supervisor.IsErrorMessage);
        Assert.Equal(WorkBoardViewModel.ConflictText, supervisor.Message);
        Assert.Equal(f.Machine, supervisor.Machines[0].MachineId);
    }

    [Fact]
    public async Task The_operator_sees_only_their_own_sessions_in_my_sessions()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await FloorAsync(h);
        var supervisor = For(h, h.Production);
        await supervisor.LoadAsync(Token);
        supervisor.SelectStage(supervisor.Stages[0]);
        await supervisor.DispatchAsync(supervisor.Machines[0], supervisor.Operators[0], true, null);

        Assert.Single(supervisor.Mine);
        var other = For(h, h.Warehouse);
        await other.LoadAsync(Token);
        Assert.True(other.IsDenied);
        Assert.Empty(other.Mine);
    }
}
