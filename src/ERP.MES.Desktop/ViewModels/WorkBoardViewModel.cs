using System.Collections.ObjectModel;
using System.Globalization;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>A stage of the unit (queue or pending list) with why it cannot start and what its sessions are doing.</summary>
public sealed record WorkStageRow(UnitQueueEntry Entry, string ItemText, string StageText, string StatusText, string BlockText, string SessionsText, bool IsReleased, int UnfinishedSessions);

/// <summary>A work session as shown: texts only; the row carries the identifiers and the row version the commands use.</summary>
public sealed record SessionDisplayRow(
    SessionRow Row, string IdText, string StageText, string StatusText, string MachineText, string OperatorText, string StartedText, string ActiveText, string PausedText, string FlagsText,
    string ApprovalText, string PauseHistoryText);

/// <summary>
/// The supervisor and operator work board (owner direction of 2026-10-27, Decision Log #115): the unit's stages with the exact reason a stage cannot start, the sessions under each stage, dispatch
/// to a machine of the unit and an authorized operator, start, pause (reason), resume, finish, handover, common outage, the operator's own sessions, and Planning's correction and time approval.
/// It reuses the unit queue (no second queue). Every button only relays a command; the Application decides authorization, unit scope, gates, resources and row versions, and the screen reloads the
/// persisted truth afterwards. Visibility of a stage is never permission to start it.
/// </summary>
public sealed class WorkBoardViewModel(
    UnitQueueService queue, WorkSessionService sessions, StageWorkService work, IJalaliDateService dates, DigitStyle digitStyle, Func<DateTime> utcNow, Func<Principal> currentUser) : ObservableObject
{
    public const string Heading = "نوبت‌های کاری واحد تولید";

    public const string Note =
        "هر مرحله با دلیل دقیق «چرا هنوز قابل شروع نیست» نشان داده می‌شود (شرط‌های آزادسازی، تصمیم قرارداد آیتم، استعلام و پرسش‌های نقشه‌کشی، تأیید کنترل کیفیت مرحلهٔ قبل). دیدن یک مرحله اجازهٔ شروع آن نیست. "
        + "سرپرست واحد ماشینِ همین واحد و اپراتور مجاز را انتخاب و نوبت کاری را شروع می‌کند؛ هر نوبت می‌تواند چند بازهٔ فعال و توقف (با دلیل) داشته باشد. پایان یک نوبت، مرحله را کامل نمی‌کند؛ ارسال مرحله به کنترل کیفیت فقط وقتی ممکن است که هیچ نوبت تخصیص‌یافته، فعال یا متوقفی نمانده باشد. "
        + "زمان‌ها از لحظه‌های ثبت‌شده محاسبه می‌شوند و در منطقهٔ زمانی تنظیم‌شده نمایش داده می‌شوند.";

    public const string DeniedText = "شما اجازهٔ مشاهدهٔ نوبت‌های کاری را ندارید.";
    public const string ConflictText = "این نوبت یا مرحله را کاربر دیگری هم‌زمان تغییر داده است؛ کاری انجام نشد. اطلاعات دوباره بارگذاری شد؛ وضعیت فعلی را ببینید و در صورت نیاز دوباره تلاش کنید.";

    private readonly ObservableCollection<UnitInfo> _units = [];
    private readonly ObservableCollection<WorkStageRow> _stages = [];
    private readonly ObservableCollection<SessionDisplayRow> _sessions = [];
    private readonly ObservableCollection<SessionDisplayRow> _mine = [];
    private readonly ObservableCollection<MachineChoice> _machines = [];
    private readonly ObservableCollection<OperatorChoice> _operators = [];
    private readonly ObservableCollection<Stored<PauseReason>> _reasons = [];
    private readonly ObservableCollection<OutageEvent> _outages = [];
    private IReadOnlyList<SessionRow> _unitRows = [];
    private WorkSessionCapabilities _capabilities = WorkSessionCapabilities.None;
    private UnitInfo? _selectedUnit;
    private WorkStageRow? _selectedStage;
    private string _message = string.Empty;
    private string _metrics = string.Empty;
    private string _summary = string.Empty;
    private bool _isError;
    private bool _isBusy;
    private bool _denied;

    public ObservableCollection<UnitInfo> Units => _units;

    public ObservableCollection<WorkStageRow> Stages => _stages;

    public ObservableCollection<SessionDisplayRow> Sessions => _sessions;

    public ObservableCollection<SessionDisplayRow> Mine => _mine;

    public ObservableCollection<MachineChoice> Machines => _machines;

    public ObservableCollection<OperatorChoice> Operators => _operators;

    public ObservableCollection<Stored<PauseReason>> Reasons => _reasons;

    public ObservableCollection<OutageEvent> Outages => _outages;

    public UnitInfo? SelectedUnit => _selectedUnit;

    public WorkStageRow? SelectedStage => _selectedStage;

    public WorkSessionCapabilities Capabilities => _capabilities;

    public bool IsSupervisor => _capabilities.IsSupervisorOfUnit;

    public bool IsOperator => _capabilities.IsOperator;

    public bool CanCorrect => _capabilities.CanCorrect;

    public bool CanApprove => _capabilities.CanApprove;

    public bool CanDispatchSelected => IsSupervisor && _selectedStage is { IsReleased: true, Entry.IsActionable: true };

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

    /// <summary>The five distinct metrics of the selected stage as text.</summary>
    public string MetricsText
    {
        get => _metrics;
        private set => SetProperty(ref _metrics, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    // ---- loading ------------------------------------------------------------------------------------------------------------------------------------------------

    public Task LoadAsync(CancellationToken cancellationToken = default) => RunAsync(async () =>
    {
        var overview = await queue.GetOverviewAsync(currentUser(), cancellationToken);
        _units.Clear();
        Clear();
        if (!overview.Succeeded)
        {
            IsDenied = overview.Status == OperationStatus.NotAuthorized;
            Fail(IsDenied ? DeniedText : Messages.For(overview));
            return;
        }

        IsDenied = false;
        foreach (var unit in overview.Value!.Units)
        {
            _units.Add(unit);
        }

        var keep = _selectedUnit?.UnitId;
        _selectedUnit = _units.FirstOrDefault(u => u.UnitId == keep) ?? _units.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedUnit));
        await LoadMineAsync(cancellationToken);
        if (_selectedUnit is not null)
        {
            await LoadUnitAsync(_selectedUnit.UnitId, cancellationToken);
        }
    });

    public Task SelectUnitAsync(long unitId, CancellationToken cancellationToken = default) => RunAsync(async () =>
    {
        _selectedUnit = _units.FirstOrDefault(u => u.UnitId == unitId);
        _selectedStage = null;
        OnPropertyChanged(nameof(SelectedUnit));
        OnPropertyChanged(nameof(SelectedStage));
        if (_selectedUnit is not null)
        {
            await LoadUnitAsync(unitId, cancellationToken);
        }
        else
        {
            Clear();
        }
    });

    public void SelectStage(WorkStageRow? row)
    {
        _selectedStage = row;
        OnPropertyChanged(nameof(SelectedStage));
        OnPropertyChanged(nameof(CanDispatchSelected));
        FillSessions();
    }

    /// <summary>Loads the metrics of the selected stage into <see cref="MetricsText"/>.</summary>
    public Task ShowMetricsAsync(CancellationToken cancellationToken = default) => RunAsync(async () =>
    {
        if (_selectedStage is not { } stage)
        {
            MetricsText = string.Empty;
            return;
        }

        var outcome = await sessions.LoadStageMetricsAsync(currentUser(), stage.Entry.ItemId, stage.Entry.StageNo, false, cancellationToken);
        MetricsText = outcome.Succeeded ? FormatMetrics(outcome.Value!) : Messages.For(outcome);
    });

    private async Task LoadUnitAsync(long unitId, CancellationToken cancellationToken)
    {
        var board = await queue.LoadAsync(currentUser(), unitId, cancellationToken);
        var work = await sessions.LoadUnitAsync(currentUser(), unitId, 7, cancellationToken);
        var choices = await sessions.LoadChoicesAsync(currentUser(), unitId, cancellationToken);
        Clear();
        if (!board.Succeeded)
        {
            Fail(Messages.For(board));
            return;
        }

        if (!work.Succeeded)
        {
            Fail(work.Status == OperationStatus.NotAuthorized ? DeniedText : Messages.For(work));
            return;
        }

        _capabilities = work.Value!.Capabilities;
        _unitRows = work.Value.Sessions;
        foreach (var reason in work.Value.Reasons.Where(r => r.Value.IsActive))
        {
            _reasons.Add(reason);
        }

        foreach (var outage in work.Value.OpenOutages)
        {
            _outages.Add(outage);
        }

        if (choices.Succeeded)
        {
            foreach (var machine in choices.Value!.Machines)
            {
                _machines.Add(machine);
            }

            foreach (var worker in choices.Value.Operators)
            {
                _operators.Add(worker);
            }
        }

        foreach (var entry in board.Value!.Queue.Concat(board.Value.Pending))
        {
            _stages.Add(FormatStage(entry));
        }

        Summary = $"{Digits(_stages.Count)} مرحله، {Digits(_unitRows.Count(r => r.Session.Value.IsUnfinished))} نوبت باز، {Digits(_outages.Count)} قطعی باز";
        _selectedStage = _stages.FirstOrDefault(s => _selectedStage is { } old && s.Entry.ItemId == old.Entry.ItemId && s.Entry.StageNo == old.Entry.StageNo);
        OnPropertyChanged(nameof(SelectedStage));
        NotifyCapabilities();
        FillSessions();
    }

    private async Task LoadMineAsync(CancellationToken cancellationToken)
    {
        _mine.Clear();
        var mine = await sessions.LoadMineAsync(currentUser(), 7, cancellationToken);
        if (mine.Succeeded)
        {
            foreach (var row in mine.Value!)
            {
                _mine.Add(FormatSession(row));
            }
        }
    }

    private void FillSessions()
    {
        _sessions.Clear();
        if (_selectedStage is not { } stage)
        {
            return;
        }

        foreach (var row in _unitRows.Where(r => r.Session.Value.RequestItemId == stage.Entry.ItemId && r.Session.Value.StageNo == stage.Entry.StageNo))
        {
            _sessions.Add(FormatSession(row));
        }
    }

    private void Clear()
    {
        _stages.Clear();
        _sessions.Clear();
        _machines.Clear();
        _operators.Clear();
        _reasons.Clear();
        _outages.Clear();
        _unitRows = [];
        _capabilities = WorkSessionCapabilities.None;
        MetricsText = string.Empty;
        Summary = string.Empty;
        NotifyCapabilities();
    }

    private void NotifyCapabilities()
    {
        foreach (var name in new[] { nameof(Capabilities), nameof(IsSupervisor), nameof(IsOperator), nameof(CanCorrect), nameof(CanApprove), nameof(CanDispatchSelected) })
        {
            OnPropertyChanged(name);
        }
    }

    // ---- commands -----------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Whether the signed-in user may pause, resume or finish the session (supervisor of the unit, or the session's own operator); the Application checks again.</summary>
    public bool CanActOn(SessionDisplayRow? row) =>
        row is not null && row.Row.Session.Value.IsUnfinished && (IsSupervisor || IsOperator && _mine.Any(m => m.Row.Session.Value.Id == row.Row.Session.Value.Id));

    public Task DispatchAsync(MachineChoice? machine, OperatorChoice? worker, bool startNow, string? outsideShiftReason) => CommandAsync(
        startNow ? "نوبت کاری ثبت و شروع شد." : "نوبت کاری برای ماشین و اپراتور ثبت شد.",
        async token =>
        {
            if (_selectedStage is not { } stage || machine is null || worker is null)
            {
                return Outcome<int>.Invalid(ValidationIssue.QueueRowsInvalid);
            }

            var outcome = await sessions.DispatchAsync(currentUser(), new DispatchCommand(stage.Entry.ItemId, stage.Entry.StageNo, machine.MachineId, worker.OperatorId, stage.Entry.StageVersion, outsideShiftReason, startNow), token);
            return Map(outcome, outcome.Value is { Value.QualificationWarning: true } ? Warning : null);
        });

    public Task StartAsync(SessionDisplayRow row, string? outsideShiftReason) => CommandAsync("نوبت کاری شروع شد.", async token => Map(await sessions.StartAsync(currentUser(), row.Row.Session.Value.Id, row.Row.Session.Version, outsideShiftReason, token)));

    public Task PauseAsync(SessionDisplayRow row, Stored<PauseReason>? reason, string? note) => CommandAsync(
        "نوبت کاری متوقف شد (مرحله متوقف نشده است).",
        async token => reason is null
            ? Outcome<int>.Invalid(ValidationIssue.SessionPauseReasonInvalid)
            : Map(await sessions.PauseAsync(currentUser(), row.Row.Session.Value.Id, row.Row.Session.Version, reason.Value.Id, note, token)));

    public Task ResumeAsync(SessionDisplayRow row, string? outsideShiftReason) => CommandAsync("نوبت کاری ازسر گرفته شد.", async token => Map(await sessions.ResumeAsync(currentUser(), row.Row.Session.Value.Id, row.Row.Session.Version, outsideShiftReason, token)));

    public Task FinishAsync(SessionDisplayRow row, string? note) => CommandAsync("نوبت کاری پایان یافت؛ مرحله هنوز کامل نشده است.", async token => Map(await sessions.FinishAsync(currentUser(), row.Row.Session.Value.Id, row.Row.Session.Version, note, token)));

    public Task CancelAsync(SessionDisplayRow row, string? reason) => CommandAsync("نوبت کاری لغو شد.", async token => Map(await sessions.CancelAsync(currentUser(), row.Row.Session.Value.Id, row.Row.Session.Version, reason, token)));

    public Task HandOverAsync(SessionDisplayRow row, OperatorChoice? worker, string? outsideShiftReason) => CommandAsync(
        "نوبت به اپراتور جدید تحویل شد؛ تاریخچهٔ نوبت قبلی حفظ شد.",
        async token =>
        {
            if (worker is null)
            {
                return Outcome<int>.Invalid(ValidationIssue.SessionOperatorUnavailable);
            }

            var outcome = await sessions.HandOverAsync(currentUser(), row.Row.Session.Value.Id, row.Row.Session.Version, worker.OperatorId, outsideShiftReason, token);
            return outcome.Succeeded ? Outcome<int>.Ok(outcome.Value!.Count) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
        });

    public Task RecordOutageAsync(Stored<PauseReason>? reason, string? note, IReadOnlyCollection<MachineChoice> machines) => CommandAsync(
        "قطعی ثبت شد و نوبت‌های فعال متوقف شدند.",
        async token =>
        {
            if (_selectedUnit is not { } unit || reason is null)
            {
                return Outcome<int>.Invalid(ValidationIssue.SessionPauseReasonInvalid);
            }

            var outcome = await sessions.RecordOutageAsync(currentUser(), unit.UnitId, machines.Select(m => m.MachineId).ToList(), reason.Value.Id, note, token);
            return outcome.Succeeded ? Outcome<int>.Ok(outcome.Value!.Paused.Count) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
        });

    public Task EndOutageAsync(OutageEvent outage) => CommandAsync(
        "قطعی پایان یافت و نوبت‌ها یکی‌یکی ازسر گرفته شدند.",
        async token =>
        {
            var outcome = await sessions.EndOutageAsync(currentUser(), outage.Id, token);
            if (!outcome.Succeeded)
            {
                return new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
            }

            return outcome.Value!.StillPaused.Count == 0
                ? Outcome<int>.Ok(outcome.Value.Resumed.Count)
                : Outcome<int>.Ok(outcome.Value.Resumed.Count, [$"{Digits(outcome.Value.StillPaused.Count)} نوبت متوقف ماند: " + string.Join("؛ ", outcome.Value.StillPaused.Select(p => $"{Digits((int)p.SessionId)} — {Messages.ForIssue(p.Issue, JalaliDateError.None)}"))]);
        });

    public Task SubmitStageForQcAsync() => CommandAsync(
        "مرحله برای کنترل کیفیت ارسال شد.",
        async token =>
        {
            if (_selectedStage is not { } stage)
            {
                return Outcome<int>.Invalid(ValidationIssue.QueueRowsInvalid);
            }

            var outcome = await work.PerformAsync(currentUser(), StageAction.SubmitForQc, stage.Entry.ToStageTarget(), token);
            return outcome.Succeeded ? Outcome<int>.Ok(1) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);
        });

    /// <summary>Planning corrects one recorded instant. <paramref name="instantText"/> is a Jalali date and a 24-hour time in the configured zone; a reason is mandatory.</summary>
    public Task CorrectAsync(SessionDisplayRow row, SessionBoundary boundary, int pauseIndex, string? instantText, string? reason) => CommandAsync(
        "زمان اصلاح شد؛ مقدار قبلی، دلیل و کاربر در تاریخچه ثبت شد و تأیید زمان برداشته شد.",
        async token =>
        {
            if (!dates.TryParseDateTime(instantText, out var utc, out var error))
            {
                return Outcome<int>.Invalid(ValidationIssue.DateInvalid, error);
            }

            return Map(await sessions.CorrectAsync(currentUser(), row.Row.Session.Value.Id, row.Row.Session.Version, boundary, pauseIndex, utc, reason, token));
        });

    public Task ApproveTimeAsync(SessionDisplayRow row) => CommandAsync("زمان واقعی نوبت تأیید شد.", async token => Map(await sessions.ApproveTimeAsync(currentUser(), row.Row.Session.Value.Id, row.Row.Session.Version, token)));

    // ---- helpers --------------------------------------------------------------------------------------------------------------------------------------------------

    private const string Warning = "هشدار: اپراتور صلاحیت جاری برای این ماشین ثبت‌شده ندارد؛ هشدار روی نوبت و در سابقه می‌ماند.";

    private static Outcome<int> Map<T>(Outcome<T> outcome, string? warning = null) =>
        outcome.Succeeded ? Outcome<int>.Ok(1, warning is null ? null : [warning]) : new Outcome<int>(outcome.Status, 0, outcome.Issue, outcome.DateError);

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
                Succeed(success + (outcome.WarningList.Count > 0 ? " " + string.Join(" ", outcome.WarningList) : string.Empty));
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
                await LoadMineAsync(CancellationToken.None);
                await LoadUnitAsync(unit.UnitId, CancellationToken.None);
                Message = keepMessage;
                IsErrorMessage = keepError;
            }
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

    private WorkStageRow FormatStage(UnitQueueEntry e)
    {
        var rows = _unitRows.Where(r => r.Session.Value.RequestItemId == e.ItemId && r.Session.Value.StageNo == e.StageNo).Select(r => r.Session.Value).ToList();
        var active = rows.Count(s => s.Status == WorkSessionStatus.Active);
        var paused = rows.Count(s => s.Status == WorkSessionStatus.Paused);
        var assigned = rows.Count(s => s.Status == WorkSessionStatus.Assigned);
        var finished = rows.Count(s => s.Status == WorkSessionStatus.Finished);
        var sessionsText = rows.Count == 0
            ? "—"
            : $"فعال {Digits(active)}، متوقف {Digits(paused)}، تخصیص‌یافته {Digits(assigned)}، پایان‌یافته {Digits(finished)}";
        return new WorkStageRow(
            e, $"{e.RequestNo} / {e.ItemNo} — {e.PartName}", $"مرحلهٔ {Digits(e.StageNo)} از {Digits(e.StageCount)}: {e.StageTitle}", ProductionQueueViewModel.StageStatusText(e.Status),
            BlockText(e), sessionsText, e.Status is ItemStageStatus.Waiting or ItemStageStatus.InProgress, active + paused + assigned);
    }

    /// <summary>The exact reason a stage cannot start: the item's gate state when it does not hold, otherwise the queue reason.</summary>
    public static string BlockText(UnitQueueEntry e)
    {
        if (e.Block == QueueBlock.GateNotSatisfied && e.Gate is { } gate)
        {
            var parts = new List<string>();
            if (gate.DefinitionBlock != ERP.MES.Domain.Contracts.DefinitionBlock.None)
            {
                parts.Add(Messages.DefinitionBlockText(gate.DefinitionBlock));
            }

            parts.AddRange(gate.Blockers.Where(b => b != GateBlocker.DefinitionGate).Select(Messages.GateBlockerText));
            return Messages.QueueBlockText(e.Block) + ": " + string.Join("؛ ", parts);
        }

        return e.Block == QueueBlock.StageStopped ? Messages.QueueBlockText(e.Block) + ": " + e.StopReason : Messages.QueueBlockText(e.Block);
    }

    private SessionDisplayRow FormatSession(SessionRow row)
    {
        var s = row.Session.Value;
        var now = utcNow();
        var flags = new List<string>();
        if (s.QualificationWarning)
        {
            flags.Add("بدون صلاحیت جاری ماشین");
        }

        if (s.OutsideShiftReason is not null)
        {
            flags.Add("خارج از شیفت: " + s.OutsideShiftReason);
        }

        if (s.PredecessorSessionId is { } predecessor)
        {
            flags.Add("جانشین نوبت " + Digits((int)predecessor));
        }

        var history = string.Join("؛ ", s.Pauses.Select((p, i) =>
            $"{Digits(i + 1)}) {_reasonNames(p.ReasonId)} از {dates.FormatDateTime(p.PausedAtUtc)}" + (p.ResumedAtUtc is { } resumed ? " تا " + dates.FormatDateTime(resumed) : " (باز)") + (p.Note is null ? string.Empty : " — " + p.Note)));
        return new SessionDisplayRow(
            row, Digits((int)s.Id), $"{row.RequestNo}/{row.ItemNo} مرحلهٔ {Digits(s.StageNo)}", StatusText(s.Status), row.MachineCode, $"{row.OperatorCode} — {row.OperatorName}",
            s.StartedAtUtc is { } start ? dates.FormatDateTime(start) : "—", Duration(SessionTime.Active(s, now)), Duration(SessionTime.Paused(s, now)), string.Join("؛ ", flags),
            s.IsTimeApproved ? "تأیید‌شده" : s.Status == WorkSessionStatus.Finished ? "منتظر تأیید برنامه‌ریزی" : string.Empty, history);
    }

    private string _reasonNames(long id) => _reasons.FirstOrDefault(r => r.Value.Id == id)?.Value.Name ?? ("#" + Digits((int)id));

    public static string StatusText(WorkSessionStatus status) => status switch
    {
        WorkSessionStatus.Assigned => "تخصیص‌یافته (شروع‌نشده)",
        WorkSessionStatus.Active => "در حال اجرا",
        WorkSessionStatus.Paused => "متوقف",
        WorkSessionStatus.Finished => "پایان‌یافته",
        _ => "لغوشده",
    };

    private string FormatMetrics(StageMetrics m)
    {
        var lines = new List<string>
        {
            "ساعت ماشین (اجتماع بازه‌ها؛ تحویل ماشین یک‌بار شمرده می‌شود): " + (m.MachineHours.Count == 0 ? "—" : string.Join("؛ ", m.MachineHours.Select(r => $"ماشین {Digits((int)r.MachineId)}: {Duration(r.Runtime)}"))),
            "ساعت کار اپراتورها: " + (m.LaborHours.Count == 0 ? "—" : string.Join("؛ ", m.LaborHours.Select(r => $"اپراتور {Digits((int)r.OperatorId)}: {Duration(r.Time)}"))),
            "زمان تقویمی مرحله: " + (m.Elapsed.Count == 0 ? "—" : string.Join("؛ ", m.Elapsed.Select(r => Duration(r.Elapsed) + (r.IsOpen ? " (هنوز باز)" : string.Empty)))),
            "زمان توقف به تفکیک دلیل: " + (m.PausedByReason.Count == 0 ? "—" : string.Join("؛ ", m.PausedByReason.Select(r => $"{_reasonNames(r.ReasonId)}: {Duration(r.Time)} ({Digits(r.Pauses)} بار)"))),
            "برنامه‌ریزی‌شده در برابر واقعی: " + (m.PlannedVsActual.Count == 0 ? "—" : string.Join("؛ ", m.PlannedVsActual.Select(r =>
                $"برنامه {Digits(((int)r.PlannedMinutes).ToString(CultureInfo.InvariantCulture))} دقیقه | ماشین {Duration(r.MachineRuntime)} | تقویمی {Duration(r.Elapsed)} | کار اپراتورها {Duration(r.Labor)}"))),
        };
        return string.Join(Environment.NewLine, lines);
    }

    private string Duration(TimeSpan span)
    {
        var total = (int)Math.Max(0, Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero));
        return $"{Digits(total / 60)}:{Digits((total % 60).ToString("00", CultureInfo.InvariantCulture))}";
    }

    private string Digits(int value) => Digits(value.ToString(CultureInfo.InvariantCulture));

    private string Digits(string text) => digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : text;
}
