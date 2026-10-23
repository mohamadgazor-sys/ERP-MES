using System.Collections.ObjectModel;
using System.Globalization;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>One stage of a unit queue (or its pending list) as shown: texts only; the entry carries the identifiers and row versions the commands use.</summary>
public sealed record UnitQueueRow(
    UnitQueueEntry Entry,
    string OrderText,
    string RequestNo,
    string ItemNo,
    string PartName,
    string StageText,
    string QuantityText,
    string PriorityText,
    string CategoryText,
    string StatusText,
    string ReasonText,
    string PositionText,
    string OverrideText,
    string PlannedEndText);

public sealed record DesignationDisplayRow(DesignationRow Row, string StatusText);

/// <summary>
/// State and actions of the operational production-unit queue screen (Decision Log #78, #99). It shows the queue the Application derives from persisted stages, positions, holds and
/// overrides, and relays commands (reorder, priority, override, stop, start, submit, release, designations). Authorization, unit scoping, gates, expected row versions and the audit are
/// the Application's; the buttons only mirror what it reported and are enforced again there. A stale view is reported as a conflict and the queue is reloaded.
/// </summary>
public sealed class UnitQueueViewModel(UnitQueueService queue, StageWorkService work, UnitDesignationService designations, DigitStyle digitStyle, Func<Principal> currentUser) : ObservableObject
{
    public const string Heading = "صف عملیاتی واحدهای تولید";

    public const string Note =
        "ترتیب هر صف: نخست وضعیت (در حال اجرا، متوقف/مشکل‌دار، در انتظار)؛ سپس جایگزینی مدیریت (تا زمانی که مدیریت آن را برندارد پابرجاست)؛ سپس اولویت برنامه‌ریزی (خیلی فوری، فوری، عادی)، جایگاه برنامه‌ریزی و در تساوی، زودتر آماده‌شده زودتر. "
        + "پایان برنامه‌ریزی‌شده بدون تقویم کاری (فاز ۶a) محاسبه نمی‌شود و نشان داده نمی‌شود. مرحله‌های آزادنشده یا منتظر کنترل کیفیت در بخش «در انتظار آزادسازی» با دلیل می‌آیند و قابل اجرا نیستند.";

    public const string DeniedText = "شما اجازهٔ مشاهدهٔ صف واحدهای تولید را ندارید.";
    public const string NoUnitText = "هیچ واحدی برای نمایش وجود ندارد. کاربر تولید فقط واحدهایی را می‌بیند که به او منصوب شده است.";
    public const string ConflictText = "این ردیف یا صف را کاربر دیگری تغییر داده است؛ کاری انجام نشد. صف دوباره بارگذاری شد؛ وضعیت فعلی را ببینید و در صورت نیاز دوباره تلاش کنید.";
    public const string NothingSelectedText = "ابتدا یک یا چند ردیف را انتخاب کنید.";

    private readonly ObservableCollection<UnitInfo> _units = [];
    private readonly ObservableCollection<UnitQueueRow> _rows = [];
    private readonly ObservableCollection<UnitQueueRow> _pending = [];
    private readonly ObservableCollection<DesignationDisplayRow> _designationRows = [];
    private readonly ObservableCollection<ProductionUserInfo> _productionUsers = [];
    private UnitQueueCapabilities _capabilities = UnitQueueCapabilities.None;
    private UnitInfo? _selectedUnit;
    private string _message = string.Empty;
    private string _summary = string.Empty;
    private bool _isError;
    private bool _isBusy;
    private bool _denied;

    public ObservableCollection<UnitInfo> Units => _units;

    public ObservableCollection<UnitQueueRow> Rows => _rows;

    public ObservableCollection<UnitQueueRow> Pending => _pending;

    public ObservableCollection<DesignationDisplayRow> DesignationRows => _designationRows;

    public ObservableCollection<ProductionUserInfo> ProductionUsers => _productionUsers;

    public IReadOnlyList<Choice<Priority>> Priorities { get; } = PriorityInfo.All.Select(p => new Choice<Priority>(p, p.PersianName())).ToList();

    public UnitQueueCapabilities Capabilities
    {
        get => _capabilities;
        private set
        {
            if (SetProperty(ref _capabilities, value))
            {
                OnPropertyChanged(nameof(CanOrder));
                OnPropertyChanged(nameof(CanOverride));
                OnPropertyChanged(nameof(CanHold));
                OnPropertyChanged(nameof(CanRun));
                OnPropertyChanged(nameof(CanManageDesignations));
                OnPropertyChanged(nameof(CanChangePriority));
            }
        }
    }

    public bool CanOrder => _capabilities.CanOrder;

    public bool CanOverride => _capabilities.CanOverride;

    public bool CanHold => _capabilities.CanStopOrResume;

    public bool CanRun => _capabilities.CanRun;

    public bool CanChangePriority => _capabilities.CanChangePriority;

    public bool CanManageDesignations => _capabilities.CanManageDesignations;

    public UnitInfo? SelectedUnit => _selectedUnit;

    public bool IsDenied
    {
        get => _denied;
        private set => SetProperty(ref _denied, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool IsErrorMessage
    {
        get => _isError;
        private set => SetProperty(ref _isError, value);
    }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    // ---- loading --------------------------------------------------------------------------------------------------------------------------------------------------

    public Task LoadAsync(CancellationToken cancellationToken = default) => RunAsync(async () =>
    {
        var overview = await queue.GetOverviewAsync(currentUser(), cancellationToken);
        _units.Clear();
        ClearQueue();
        if (!overview.Succeeded)
        {
            Capabilities = UnitQueueCapabilities.None;
            IsDenied = overview.Status == OperationStatus.NotAuthorized;
            Fail(IsDenied ? DeniedText : Messages.For(overview));
            return;
        }

        IsDenied = false;
        Capabilities = overview.Value!.Capabilities;
        foreach (var unit in overview.Value.Units)
        {
            _units.Add(unit);
        }

        var keep = _selectedUnit?.UnitId;
        _selectedUnit = _units.FirstOrDefault(u => u.UnitId == keep) ?? _units.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedUnit));
        if (_selectedUnit is null)
        {
            Summary = NoUnitText;
        }
        else
        {
            await LoadQueueAsync(_selectedUnit.UnitId, cancellationToken);
        }

        await LoadDesignationsAsync(cancellationToken);
    });

    public Task SelectUnitAsync(long unitId, CancellationToken cancellationToken = default) => RunAsync(async () =>
    {
        _selectedUnit = _units.FirstOrDefault(u => u.UnitId == unitId);
        OnPropertyChanged(nameof(SelectedUnit));
        if (_selectedUnit is not null)
        {
            await LoadQueueAsync(unitId, cancellationToken);
        }
        else
        {
            ClearQueue();
        }
    });

    private async Task LoadQueueAsync(long unitId, CancellationToken cancellationToken)
    {
        var outcome = await queue.LoadAsync(currentUser(), unitId, cancellationToken);
        ClearQueue();
        if (!outcome.Succeeded)
        {
            Fail(Messages.For(outcome));
            return;
        }

        var value = outcome.Value!;
        foreach (var entry in value.Queue)
        {
            _rows.Add(Format(entry));
        }

        foreach (var entry in value.Pending)
        {
            _pending.Add(Format(entry));
        }

        Summary = $"{Digits(value.Queue.Count)} مرحله در صف، {Digits(value.Pending.Count)} در انتظار آزادسازی، {Digits(value.CompletedStageCount)} تکمیل‌شده"
            + (value.OverridesNeedingReview > 0 ? $"؛ {Digits(value.OverridesNeedingReview)} جایگزینی مدیریت منتظر بازبینی است" : string.Empty);
    }

    private async Task LoadDesignationsAsync(CancellationToken cancellationToken)
    {
        _designationRows.Clear();
        _productionUsers.Clear();
        var outcome = await designations.LoadAsync(currentUser(), cancellationToken);
        if (!outcome.Succeeded)
        {
            return;
        }

        foreach (var row in outcome.Value!.Rows)
        {
            _designationRows.Add(new DesignationDisplayRow(row, row.IsActive ? "فعال" : "پس‌گرفته‌شده"));
        }

        foreach (var user in outcome.Value.ProductionUsers)
        {
            _productionUsers.Add(user);
        }
    }

    private void ClearQueue()
    {
        _rows.Clear();
        _pending.Clear();
        Summary = string.Empty;
    }

    // ---- Planning -------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Planning moves a queue row up (-1) or down (+1); the whole released order is sent with the row versions it was read with.</summary>
    public Task MoveAsync(UnitQueueRow row, int delta) => CommandAsync(
        "ترتیب برنامه‌ریزی ذخیره شد.",
        async token =>
        {
            var unit = _selectedUnit ?? throw new InvalidOperationException();
            var list = _rows.Select(r => r.Entry).ToList();
            var index = list.FindIndex(e => e.ItemId == row.Entry.ItemId && e.StageNo == row.Entry.StageNo);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= list.Count)
            {
                return Outcome<int>.Ok(0);
            }

            (list[index], list[target]) = (list[target], list[index]);
            return await queue.SetPlanningOrderAsync(currentUser(), unit.UnitId, list.Select(e => e.ToRowRef()).ToList(), token);
        });

    public Task ChangePriorityAsync(UnitQueueRow row, Priority priority) => CommandAsync(
        "اولویت تغییر کرد.",
        token => queue.ChangePriorityAsync(currentUser(), row.Entry.RequestId, row.Entry.ItemId, priority, row.Entry.ItemVersion, token));

    // ---- Management -----------------------------------------------------------------------------------------------------------------------------------------------

    public Task OverrideAsync(IReadOnlyList<UnitQueueRow> rows, string? reason) => CommandAsync(
        "جایگزینی مدیریت ثبت شد و تا پاک‌شدن توسط مدیریت پابرجاست.",
        token => rows.Count == 0 || _selectedUnit is null
            ? Task.FromResult(Outcome<int>.Invalid(ValidationIssue.QueueRowsInvalid))
            : queue.OverrideAsync(currentUser(), _selectedUnit.UnitId, reason, rows.Select(r => r.Entry.ToRowRef()).ToList(), token));

    public Task ClearOverrideAsync(IReadOnlyList<UnitQueueRow> rows, string? reason) => CommandAsync(
        "جایگزینی مدیریت پاک شد.",
        token => rows.Count == 0 || _selectedUnit is null
            ? Task.FromResult(Outcome<int>.Invalid(ValidationIssue.QueueRowsInvalid))
            : queue.ClearOverridesAsync(currentUser(), _selectedUnit.UnitId, reason, rows.Select(r => r.Entry.ToRowRef()).ToList(), token));

    public Task ConfirmReviewAsync(IReadOnlyList<UnitQueueRow> rows) => CommandAsync(
        "بازبینی تأیید شد؛ جایگزینی مدیریت همچنان پابرجاست.",
        token => rows.Count == 0 || _selectedUnit is null
            ? Task.FromResult(Outcome<int>.Invalid(ValidationIssue.QueueRowsInvalid))
            : queue.ConfirmOverrideReviewAsync(currentUser(), _selectedUnit.UnitId, rows.Select(r => r.Entry.ToRowRef()).ToList(), token));

    // ---- stage actions --------------------------------------------------------------------------------------------------------------------------------------------

    public Task StopAsync(UnitQueueRow row, string? reason) => CommandAsync("مرحله متوقف شد.", token => queue.StopStageAsync(currentUser(), row.Entry.ToRowRef(), reason, token));

    public Task ResumeAsync(UnitQueueRow row) => CommandAsync("مرحله ازسر گرفته شد.", token => queue.ResumeStageAsync(currentUser(), row.Entry.ToRowRef(), token));

    public Task StartAsync(UnitQueueRow row) => StageAsync(StageAction.Start, row, "مرحله شروع شد.");

    public Task SubmitForQcAsync(UnitQueueRow row) => StageAsync(StageAction.SubmitForQc, row, "مرحله برای کنترل کیفیت ارسال شد.");

    public Task ReleaseAsync(UnitQueueRow row) => StageAsync(StageAction.Release, row, "مرحلهٔ اول آزاد شد.");

    private Task StageAsync(StageAction action, UnitQueueRow row, string success) => CommandAsync(
        success,
        async token =>
        {
            var outcome = await work.PerformAsync(currentUser(), action, row.Entry.ToStageTarget(), token);
            return outcome.Succeeded ? Outcome<int>.Ok(1) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
        });

    // ---- designations ---------------------------------------------------------------------------------------------------------------------------------------------

    public Task AssignDesignationAsync(long userId, long unitId) => CommandAsync(
        "کاربر به واحد منصوب شد.",
        async token =>
        {
            var outcome = await designations.AssignAsync(currentUser(), userId, unitId, token);
            return outcome.Succeeded ? Outcome<int>.Ok(1) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
        });

    public Task RevokeDesignationAsync(DesignationDisplayRow row) => CommandAsync(
        "انتصاب پس گرفته شد.",
        async token =>
        {
            var outcome = await designations.RevokeAsync(currentUser(), row.Row.DesignationId, row.Row.Version, token);
            return outcome.Succeeded ? Outcome<int>.Ok(1) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
        });

    // ---- helpers --------------------------------------------------------------------------------------------------------------------------------------------------

    private async Task CommandAsync(string success, Func<CancellationToken, Task<Outcome<int>>> command)
    {
        if (IsBusy)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var outcome = await command(CancellationToken.None);
            if (outcome.Succeeded)
            {
                Succeed(success);
            }
            else
            {
                Fail(outcome.Status == OperationStatus.ConcurrencyConflict ? ConflictText : Messages.For(outcome));
            }

            // Always show the persisted truth afterwards, success or not.
            if (_selectedUnit is { } unit)
            {
                var keepMessage = Message;
                var keepError = IsErrorMessage;
                await LoadQueueAsync(unit.UnitId, CancellationToken.None);
                Message = keepMessage;
                IsErrorMessage = keepError;
            }

            await LoadDesignationsAsync(CancellationToken.None);
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            Fail(Messages.Unexpected);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Succeed(string text)
    {
        Message = text;
        IsErrorMessage = false;
    }

    private void Fail(string text)
    {
        Message = text;
        IsErrorMessage = true;
    }

    private UnitQueueRow Format(UnitQueueEntry e) => new(
        e,
        e.Order == 0 ? "—" : Digits(e.Order),
        e.RequestNo,
        e.ItemNo,
        e.PartName,
        $"مرحلهٔ {Digits(e.StageNo)} از {Digits(e.StageCount)}: {e.StageTitle}",
        Digits(e.PlannedQuantity.ToString("0.############################", CultureInfo.InvariantCulture)),
        e.Priority.PersianName(),
        Messages.QueueCategoryText(e.Category),
        ProductionQueueViewModel.StageStatusText(e.Status),
        e.Block == QueueBlock.StageStopped ? Messages.QueueBlockText(e.Block) + ": " + e.StopReason : Messages.QueueBlockText(e.Block),
        e.PlanningPosition is { } p ? Digits(p) : "—",
        e.OverrideRank is { } rank
            ? $"جایگزینی مدیریت (رتبهٔ {Digits(rank)}): {e.OverrideReason}" + (e.OverrideNeedsReview ? " — نیازمند بازبینی مدیریت: " + e.OverrideReviewNote : string.Empty)
            : string.Empty,
        e.PlannedEndUtc is null ? "—" : e.PlannedEndUtc.Value.ToString("u", CultureInfo.InvariantCulture));

    private string Digits(int value) => Digits(value.ToString(CultureInfo.InvariantCulture));

    private string Digits(string text) => digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : text;
}
