using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The read-only production queue screen: states, Persian texts and digits, and the fact that it offers nothing that changes data. Real SQLite file, real services.
public class ProductionQueueViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<long> RequestWithItemsAsync(WorkflowHarness h, string requestNo, params (string ItemNo, WorkType Type, Priority Priority, decimal Quantity)[] items)
    {
        var customer = (await h.Workflow.Customers.CreateAsync(h.Central, "مشتری فرضی", Token)).Value!.Value.Id;
        var request = (await h.Workflow.Requests.CreateAsync(h.Central, new CreateRequestCommand(customer, requestNo, "1405/07/20", null, null), Token)).Value!.Value.Id;
        foreach (var (itemNo, type, priority, quantity) in items)
        {
            h.Clock.Advance(TimeSpan.FromMinutes(1));
            var version = (await h.Workflow.Requests.GetAsync(h.Planning, request, Token)).Value!.Request.Version;
            var added = await h.Workflow.Requests.AddItemAsync(h.Planning, request, version, new AddItemCommand(itemNo, "قطعه " + itemNo, type, quantity, priority), Token);
            Assert.True(added.Succeeded, added.Status + " " + added.Issue);
        }

        return request;
    }

    private static Task ChangeItemAsync(WorkflowHarness h, long requestId, string itemNo, Action<RequestItem> change) =>
        h.RequestRepository.UpdateRowsAsync(requestId, RowVersionExpectations.None, r => change(r.Items.Single(i => i.ItemNo == itemNo)), Token);

    [Fact]
    public async Task Management_sees_the_queue_in_persian_with_persian_digits_and_labelled_estimates()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var request = await RequestWithItemsAsync(h, "wsj-898", ("1.10", WorkType.Make, Priority.Urgent, 10m), ("2.10", WorkType.Repair, Priority.Normal, 4m));
        await ChangeItemAsync(h, request, "1.10", i =>
        {
            i.DefineInitialStage("تراشکاری", 5m);
            i.DefineInitialStage("سنگ‌زنی", 3m);
            i.ReleaseFirstStage();
            i.StartStage(1);
            i.SubmitStageForQc(1);
        });
        var vm = h.QueueFor(h.Management);

        await vm.LoadAsync(Token);

        Assert.False(vm.IsLoading);
        Assert.False(vm.IsDenied);
        Assert.False(vm.HasError);
        Assert.False(vm.IsEmpty);
        Assert.True(vm.ShowRows);
        Assert.Equal(string.Empty, vm.Message);
        Assert.Equal(2, vm.Rows.Count);

        var make = vm.Rows[0];
        Assert.Equal("۱", make.Position);
        Assert.Equal("wsj-898", make.RequestNo);
        Assert.Equal("مشتری فرضی", make.CustomerName);
        Assert.Equal("ایجادشده", make.RequestStatusText);
        Assert.Equal("1.10", make.ItemNo);
        Assert.Equal("ساخت", make.WorkTypeText);
        Assert.Equal("فوری", make.PriorityText);
        Assert.Equal("۱۰", make.QuantityText);
        Assert.Equal("مرحلهٔ ۱ از ۲: تراشکاری", make.CurrentStageText);
        Assert.Equal("در انتظار کنترل کیفیت", make.StageStatusText);
        Assert.Equal("در انتظار تأیید کنترل کیفیت؛ ۰ از ۲ مرحله تأیید شده", make.QcStatusText);
        Assert.Equal("۵۰", make.StageEstimateText);
        Assert.Equal("۸۰", make.TotalEstimateText);

        var repair = vm.Rows[1];
        Assert.Equal("۲", repair.Position);
        Assert.Equal("تعمیر", repair.WorkTypeText);
        Assert.Equal("عادی", repair.PriorityText);
        Assert.Equal("مسیر تعریف نشده", repair.CurrentStageText);
        Assert.Equal("—", repair.StageEstimateText);
        Assert.Equal("—", repair.TotalEstimateText);
        Assert.Equal("—", repair.QcStatusText);
        Assert.False(string.IsNullOrEmpty(vm.LoadedAtText));
    }

    [Fact]
    public async Task A_completed_route_is_shown_as_completed_with_no_current_stage_estimate()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var request = await RequestWithItemsAsync(h, "R-1", ("1.10", WorkType.Make, Priority.Normal, 2m));
        await ChangeItemAsync(h, request, "1.10", i =>
        {
            i.DefineInitialStage("Only", 2m);
            i.ReleaseFirstStage();
            i.StartStage(1);
            i.SubmitStageForQc(1);
            i.ApproveStageWholeQuantity(1, h.Qc.UserId, h.Clock.UtcNow);
        });
        var vm = h.QueueFor(h.Management);

        await vm.LoadAsync(Token);

        var row = vm.Rows.Single();
        Assert.Equal("همهٔ مراحل تکمیل شد", row.CurrentStageText);
        Assert.Equal("همهٔ مراحل توسط کنترل کیفیت تأیید شده است", row.QcStatusText);
        Assert.Equal("—", row.StageEstimateText);
        Assert.Equal("۴", row.TotalEstimateText);
    }

    [Theory]
    [InlineData(ItemStageStatus.Planned, "برنامه‌ریزی‌شده (هنوز آزاد نشده)")]
    [InlineData(ItemStageStatus.WaitingPreviousQc, "در انتظار تأیید کنترل کیفیت مرحلهٔ قبل")]
    [InlineData(ItemStageStatus.Waiting, "آمادهٔ اجرا")]
    [InlineData(ItemStageStatus.InProgress, "در حال اجرا")]
    [InlineData(ItemStageStatus.WaitingQc, "در انتظار کنترل کیفیت")]
    [InlineData(ItemStageStatus.QcApproved, "تأییدشده توسط کنترل کیفیت")]
    public void Every_stage_status_has_its_own_persian_text(ItemStageStatus status, string expected) =>
        Assert.Equal(expected, ProductionQueueViewModel.StageStatusText(status));

    [Fact]
    public async Task The_rows_follow_the_documented_order_and_are_numbered_from_one()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await RequestWithItemsAsync(h, "R-1", ("a", WorkType.Make, Priority.Normal, 1m), ("b", WorkType.Make, Priority.VeryUrgent, 1m), ("c", WorkType.Make, Priority.Normal, 1m));
        var vm = h.QueueFor(h.Management);

        await vm.LoadAsync(Token);

        Assert.Equal(new[] { "b", "a", "c" }, vm.Rows.Select(r => r.ItemNo).ToArray());
        Assert.Equal(new[] { "۱", "۲", "۳" }, vm.Rows.Select(r => r.Position).ToArray());
        Assert.Equal("خیلی فوری", vm.Rows[0].PriorityText);
    }

    [Fact]
    public async Task Departments_other_than_management_and_planning_get_a_persian_permission_denied_state_and_no_rows()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await RequestWithItemsAsync(h, "R-1", ("1.10", WorkType.Make, Priority.Normal, 1m));

        foreach (var user in new[] { h.Central, h.Drawing, h.Qc, h.Admin })
        {
            var vm = h.QueueFor(user);
            await vm.LoadAsync(Token);

            Assert.True(vm.IsDenied);
            Assert.False(vm.HasError);
            Assert.False(vm.ShowRows);
            Assert.False(vm.IsEmpty);
            Assert.Empty(vm.Rows);
            Assert.Equal(ProductionQueueViewModel.DeniedText, vm.Message);
            Assert.Equal(string.Empty, vm.LoadedAtText);
        }
    }

    [Fact]
    public async Task Planning_sees_the_same_overview_as_management()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await RequestWithItemsAsync(h, "R-1", ("1.10", WorkType.Make, Priority.Urgent, 3m));
        var management = h.QueueFor(h.Management);
        var planning = h.QueueFor(h.Planning);

        await management.LoadAsync(Token);
        await planning.LoadAsync(Token);

        Assert.False(planning.IsDenied);
        Assert.True(planning.ShowRows);
        Assert.Equal(management.Rows, planning.Rows);
    }

    [Fact]
    public async Task An_empty_queue_shows_the_empty_state_and_not_an_error()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = h.QueueFor(h.Management);

        await vm.LoadAsync(Token);

        Assert.True(vm.IsEmpty);
        Assert.False(vm.ShowRows);
        Assert.False(vm.IsDenied);
        Assert.False(vm.HasError);
        Assert.Equal(string.Empty, vm.Message);
    }

    [Fact]
    public async Task A_user_who_loses_access_gets_the_denied_state_on_the_next_load_and_the_old_rows_disappear()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await RequestWithItemsAsync(h, "R-1", ("1.10", WorkType.Make, Priority.Normal, 1m));
        var vm = h.QueueFor(h.Management);
        await vm.LoadAsync(Token);
        Assert.Single(vm.Rows);

        Assert.Equal(AccountStatus.Succeeded, (await h.Security.Accounts.SetActiveAsync(h.Admin, h.Management.UserId, false, Token)).Status);
        await vm.LoadAsync(Token);

        Assert.True(vm.IsDenied);
        Assert.Empty(vm.Rows);
    }

    // ---- failure states, with a repository that fails or waits ----

    private sealed class ScriptedRepository : IRequestRepository
    {
        public Exception? Failure { get; set; }

        public TaskCompletionSource<bool>? Gate { get; init; }

        public Task<Stored<Request>> AddAsync(Request request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Stored<Request>?> FindAsync(long requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> ExistsAsync(RequestKey key, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<RequestNo>> FindCaseInsensitiveNearDuplicatesAsync(long customerId, int jalaliRequestYear, RequestNo requestNo, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<IReadOnlyList<RequestSummary>> ListAsync(long? customerId, CancellationToken cancellationToken = default)
        {
            if (Gate is not null)
            {
                await Gate.Task;
            }

            return Failure is null ? [] : throw Failure;
        }

        public Task<Stored<Request>> UpdateAsync(long requestId, long? expectedVersion, Action<Request> change, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<VersionedRequest?> FindWithVersionsAsync(long requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<VersionedRequest> UpdateRowsAsync(long requestId, RowVersionExpectations expected, Action<Request> change, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    [Theory]
    [InlineData("persistence", "Failed")]
    [InlineData("conflict", "Conflict")]
    [InlineData("notfound", "NotFound")]
    [InlineData("unexpected", "Unexpected")]
    public async Task Failures_become_a_persian_error_state_with_no_rows_and_no_exception(string kind, string which)
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        Exception failure = kind switch
        {
            "persistence" => new PersistenceException("database error"),
            "conflict" => new ConcurrencyConflictException("conflict"),
            "notfound" => new RecordNotFoundException("request", 1),
            _ => new InvalidOperationException("bug"),
        };
        var expected = which switch
        {
            "Failed" => Messages.Failed,
            "Conflict" => ProductionQueueViewModel.ConflictText,
            "NotFound" => ProductionQueueViewModel.NotFoundText,
            _ => Messages.Unexpected,
        };
        var vm = h.QueueFor(h.Management, new ProductionQueueService(new ScriptedRepository { Failure = failure }, h.Security.Authorization));

        await vm.LoadAsync(Token);

        Assert.True(vm.HasError);
        Assert.False(vm.IsDenied);
        Assert.False(vm.ShowRows);
        Assert.False(vm.IsEmpty);
        Assert.Empty(vm.Rows);
        Assert.Equal(expected, vm.Message);
        Assert.False(vm.IsLoading);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task A_failed_load_can_be_retried_and_the_error_state_clears()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var repository = new ScriptedRepository { Failure = new PersistenceException("x") };
        var vm = h.QueueFor(h.Management, new ProductionQueueService(repository, h.Security.Authorization));
        await vm.LoadAsync(Token);
        Assert.True(vm.HasError);

        repository.Failure = null;   // the fault is gone; "بارگذاری دوباره" loads again
        await vm.LoadAsync(Token);

        Assert.False(vm.HasError);
        Assert.Equal(string.Empty, vm.Message);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public async Task The_loading_state_is_shown_while_the_data_is_being_read_and_a_second_load_is_ignored()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = h.QueueFor(h.Management, new ProductionQueueService(new ScriptedRepository { Gate = gate }, h.Security.Authorization));

        var first = vm.LoadAsync(Token);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!vm.IsLoading && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, Token);
        }

        Assert.True(vm.IsLoading);
        Assert.True(vm.IsBusy);
        await vm.LoadAsync(Token);   // ignored while busy
        Assert.True(vm.IsLoading);

        gate.SetResult(true);
        await first;

        Assert.False(vm.IsLoading);
        Assert.False(vm.IsBusy);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public void The_screen_states_that_estimates_are_not_dates_that_the_order_is_provisional_and_that_the_page_is_an_overview()
    {
        Assert.Contains("تاریخ تکمیل نیست", ProductionQueueViewModel.EstimateNote);
        Assert.Contains("موقت است", ProductionQueueViewModel.OrderNote);
        Assert.Contains("ترتیب عملیاتی رسمی", ProductionQueueViewModel.OrderNote);
        Assert.Contains("هنوز پیاده‌سازی نشده", ProductionQueueViewModel.OrderNote);
        Assert.Contains("نمای کلی", ProductionQueueViewModel.OverviewNote);
        Assert.Contains("صف عملیاتی رسمی", ProductionQueueViewModel.OverviewNote);
        Assert.Contains("زنده نیست", ProductionQueueViewModel.OverviewNote);
        Assert.Contains("فقط‌خواندنی", ProductionQueueViewModel.Heading);
    }

    [Fact]
    public void The_view_model_offers_no_action_that_changes_data()
    {
        var methods = typeof(ProductionQueueViewModel).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToArray();

        Assert.Equal(new[] { "LoadAsync" }, methods);
    }
}
