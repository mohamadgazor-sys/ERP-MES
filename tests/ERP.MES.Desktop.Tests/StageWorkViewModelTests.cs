using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The demonstration stage-work screen: buttons that mirror the Application's answer, Persian status feedback, reload after every action, and the Application enforcing everything
// again. Real SQLite file, real services; nothing opens a window.
public class StageWorkViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>One request with one MAKE item "1.10" (quantity 10) and a fixed route of the given minutes per piece; the first stage is PLANNED.</summary>
    private static async Task<long> SeedAsync(WorkflowHarness h, params decimal[] minutesPerPiece)
    {
        var customer = (await h.Workflow.Customers.CreateAsync(h.Central, "مشتری فرضی", Token)).Value!.Value.Id;
        var request = (await h.Workflow.Requests.CreateAsync(h.Central, new CreateRequestCommand(customer, "wsj-898", WorkType.Make, "1405/07/20", null, null), Token)).Value!.Value.Id;
        // Fixture seeding through the repository (the gated definition workflow is tested in ItemDefinitionViewModelTests).
        await h.RequestRepository.UpdateRowsAsync(request, RowVersionExpectations.None, r => r.AddItem(0, "1.10", "قطعه ۱", 10m, h.Planning.UserId, h.Clock.UtcNow, Priority.Urgent), Token);
        await h.RequestRepository.UpdateRowsAsync(
            request,
            RowVersionExpectations.None,
            r =>
            {
                for (var i = 0; i < minutesPerPiece.Length; i++)
                {
                    r.Items.Single().DefineInitialStage("مرحله " + (i + 1), minutesPerPiece[i]);
                }
            },
            Token);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        return request;
    }

    private static async Task<StageWorkViewModel> OpenAsync(WorkflowHarness h, Principal user)
    {
        var vm = h.StagesFor(user);
        await vm.LoadAsync(Token);
        vm.SelectedRow = vm.Rows.FirstOrDefault();
        return vm;
    }

    private static async Task<ItemStageStatus> StatusAsync(WorkflowHarness h, long request, int stageNo) =>
        (await h.RequestRepository.FindWithVersionsAsync(request, Token))!.Value.Items.Single().Stages.Single(s => s.StageNo == stageNo).Status;

    [Fact]
    public async Task The_complete_sequence_runs_through_the_screens_with_persian_feedback_and_a_reload_after_each_action()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var request = await SeedAsync(h, 5m, 3m);

        var planning = await OpenAsync(h, h.Planning);
        Assert.True(planning.ShowRows);
        Assert.True(planning.CanRelease);
        Assert.False(planning.CanStart);
        Assert.True(await planning.ReleaseAsync(Token));
        Assert.Equal(StageWorkViewModel.ReleasedText, planning.Message);
        Assert.False(planning.IsErrorMessage);
        Assert.Equal("آمادهٔ اجرا", planning.Rows.Single().Display.StageStatusText);   // reloaded
        Assert.False(planning.CanRelease);
        Assert.Equal(ItemStageStatus.Waiting, await StatusAsync(h, request, 1));

        var production = await OpenAsync(h, h.Production);
        Assert.True(production.CanStart);
        Assert.False(production.CanSubmitForQc);
        Assert.True(await production.StartAsync(Token));
        Assert.Equal(StageWorkViewModel.StartedText, production.Message);
        Assert.True(production.CanSubmitForQc);
        Assert.False(production.CanStart);
        Assert.True(await production.SubmitForQcAsync(Token));
        Assert.Equal(StageWorkViewModel.SubmittedText, production.Message);
        Assert.Equal("در انتظار کنترل کیفیت", production.Rows.Single().Display.StageStatusText);
        Assert.False(production.CanSubmitForQc);

        var qc = await OpenAsync(h, h.Qc);
        Assert.True(qc.CanApprove);
        Assert.False(qc.CanStart);
        Assert.True(await qc.ApproveAsync(Token));
        Assert.Equal("تأیید کنترل کیفیت ثبت شد؛ مرحلهٔ ۲ اکنون آمادهٔ اجرا است.", qc.Message);
        Assert.False(qc.IsErrorMessage);
        Assert.Equal("مرحلهٔ ۲ از ۲: مرحله 2", qc.Rows.Single().Display.CurrentStageText);
        Assert.Equal("۱ از ۲ مرحله تأیید شده", qc.Rows.Single().Display.QcStatusText);
        Assert.False(qc.CanApprove);
        Assert.Equal(ItemStageStatus.QcApproved, await StatusAsync(h, request, 1));
        Assert.Equal(ItemStageStatus.Waiting, await StatusAsync(h, request, 2));

        // The second stage; the last approval completes the route.
        await production.LoadAsync(Token);
        production.SelectedRow = production.Rows.Single();
        Assert.True(await production.StartAsync(Token));
        Assert.True(await production.SubmitForQcAsync(Token));
        await qc.LoadAsync(Token);
        qc.SelectedRow = qc.Rows.Single();
        Assert.True(await qc.ApproveAsync(Token));
        Assert.Equal(StageWorkViewModel.ApprovedAllText, qc.Message);
        Assert.Equal("همهٔ مراحل تکمیل شد", qc.Rows.Single().Display.CurrentStageText);
        Assert.False(qc.CanApprove);
    }

    [Fact]
    public async Task The_buttons_follow_the_department_and_nothing_is_enabled_without_a_selection()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await SeedAsync(h, 5m);

        var management = h.StagesFor(h.Management);
        await management.LoadAsync(Token);
        Assert.True(management.ShowRows);
        Assert.False(management.CanRelease || management.CanStart || management.CanSubmitForQc || management.CanApprove);   // nothing selected

        management.SelectedRow = management.Rows.Single();
        Assert.False(management.CanRelease || management.CanStart || management.CanSubmitForQc || management.CanApprove);   // Management may view, not act

        var production = await OpenAsync(h, h.Production);
        Assert.False(production.CanRelease || production.CanStart || production.CanSubmitForQc || production.CanApprove);   // the stage is PLANNED
        production.SelectedRow = null;
        Assert.False(await production.StartAsync(Token));
        Assert.Equal(StageWorkViewModel.NothingSelectedText, production.Message);
        Assert.True(production.IsErrorMessage);
    }

    [Fact]
    public async Task An_action_the_user_may_not_perform_is_refused_by_the_application_even_when_the_view_model_is_asked_to_try()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var request = await SeedAsync(h, 5m);
        var planning = await OpenAsync(h, h.Planning);

        // Planning is not Production and not QC; the screen does not offer these, but a direct call is refused by the use case.
        Assert.False(await planning.StartAsync(Token));
        Assert.Equal(Messages.NotAuthorized, planning.Message);
        Assert.True(planning.IsErrorMessage);
        Assert.False(await planning.ApproveAsync(Token));
        Assert.Equal(ItemStageStatus.Planned, await StatusAsync(h, request, 1));
    }

    [Fact]
    public async Task A_user_deactivated_after_opening_the_screen_is_refused_and_sees_the_denied_state_after_the_reload()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var request = await SeedAsync(h, 5m);
        var planning = await OpenAsync(h, h.Planning);
        Assert.True(planning.CanRelease);

        Assert.Equal(AccountStatus.Succeeded, (await h.Security.Accounts.SetActiveAsync(h.Admin, h.Planning.UserId, false, Token)).Status);
        var released = await planning.ReleaseAsync(Token);

        Assert.False(released);
        Assert.True(planning.IsDenied);
        Assert.Empty(planning.Rows);
        Assert.Equal(Messages.NotAuthorized, planning.Message);   // the refusal is what the user is told; the list behind it is already the denied state
        Assert.True(planning.IsErrorMessage);
        Assert.Equal(ItemStageStatus.Planned, await StatusAsync(h, request, 1));
    }

    [Fact]
    public async Task Two_users_acting_on_the_same_stage_the_second_gets_a_conflict_and_the_current_state_is_shown()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var request = await SeedAsync(h, 5m);
        var planning = await OpenAsync(h, h.Planning);
        Assert.True(await planning.ReleaseAsync(Token));

        var first = await OpenAsync(h, h.Production);
        var second = await OpenAsync(h, h.Production);   // opened while the stage was WAITING
        Assert.True(second.CanStart);

        Assert.True(await first.StartAsync(Token));
        var started = await second.StartAsync(Token);   // acts on the old view

        Assert.False(started);
        Assert.Equal(StageWorkViewModel.ConflictText, second.Message);
        Assert.True(second.IsErrorMessage);
        Assert.Equal("در حال اجرا", second.Rows.Single().Display.StageStatusText);   // reloaded: the truth is shown
        Assert.True(second.CanSubmitForQc);
        Assert.Equal(ItemStageStatus.InProgress, await StatusAsync(h, request, 1));
    }

    [Fact]
    public async Task A_repeated_click_on_a_stale_row_does_not_run_the_action_twice()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var request = await SeedAsync(h, 5m, 3m);
        var planning = await OpenAsync(h, h.Planning);
        await planning.ReleaseAsync(Token);
        var production = await OpenAsync(h, h.Production);
        await production.StartAsync(Token);
        await production.SubmitForQcAsync(Token);
        var qc = await OpenAsync(h, h.Qc);
        var staleRow = qc.SelectedRow;

        Assert.True(await qc.ApproveAsync(Token));
        qc.SelectedRow = staleRow;   // the old row again (as a second click on a list that has not refreshed yet would use)
        Assert.False(await qc.ApproveAsync(Token));

        Assert.Equal(StageWorkViewModel.ConflictText, qc.Message);
        Assert.Single((await h.RequestRepository.FindWithVersionsAsync(request, Token))!.Value.Items.Single().QcApprovals);
    }

    [Fact]
    public async Task Denied_empty_and_error_states_are_explicit_and_in_persian()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);

        var empty = h.StagesFor(h.Production);
        await empty.LoadAsync(Token);
        Assert.True(empty.IsEmpty);
        Assert.False(empty.ShowRows);

        var denied = h.StagesFor(h.Central);
        await denied.LoadAsync(Token);
        Assert.True(denied.IsDenied);
        Assert.Equal(StageWorkViewModel.DeniedText, denied.Message);
        Assert.False(denied.CanRelease || denied.CanStart || denied.CanSubmitForQc || denied.CanApprove);

        var failing = h.StagesFor(h.Production, new StageWorkService(
            new ProductionQueueViewModelTests.ScriptedRepository { Failure = new PersistenceException("database error") },
            h.Security.Authorization,
            h.Clock,
            new TestAuditContext(),
            h.Workflow.Gates,
            h.Workflow.UnitQueueStore));
        await failing.LoadAsync(Token);
        Assert.True(failing.HasError);
        Assert.Equal(Messages.Failed, failing.Message);
        Assert.Empty(failing.Rows);

        var broken = h.StagesFor(h.Production, new StageWorkService(
            new ProductionQueueViewModelTests.ScriptedRepository { Failure = new InvalidOperationException("bug") },
            h.Security.Authorization,
            h.Clock,
            new TestAuditContext(),
            h.Workflow.Gates,
            h.Workflow.UnitQueueStore));
        await broken.LoadAsync(Token);
        Assert.True(broken.HasError);
        Assert.Equal(Messages.Unexpected, broken.Message);
    }

    [Fact]
    public void The_screen_states_what_it_is_not()
    {
        Assert.Contains("رد کنترل کیفیت", StageWorkViewModel.Note);
        Assert.Contains("ویرایش مسیر", StageWorkViewModel.Note);
        Assert.Contains("صف عملیاتی رسمی", StageWorkViewModel.Note);
        Assert.Contains("وضعیت درخواست، قرارداد و تأیید مدیریت", StageWorkViewModel.Note);
        Assert.Contains("بررسی نمی‌شوند", StageWorkViewModel.Note);
        Assert.Contains("نسخهٔ نمایشی", StageWorkViewModel.Heading);
    }

    private sealed class TestAuditContext : IAuditContext
    {
        public string MachineName => "TEST-PC";

        public string OsUserName => "test.operator";

        public string? DatabaseLogin => null;
    }
}
