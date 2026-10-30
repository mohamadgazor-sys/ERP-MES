using System.Collections.ObjectModel;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Planning;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Planning;

namespace ERP.MES.Desktop.ViewModels;

public sealed record OperatorRow(long Id, string Code, string FullName, bool IsActive, bool HasAccount, long Version)
{
    public string StatusText => IsActive ? "فعال" : "غیرفعال";

    public string AccountText => HasAccount ? "دارد" : "ندارد";
}

public sealed record AssignmentRow(long Id, string OperatorText, string MachineText, string FromText, string ToText, string PrimaryText, string QualifiedText, bool IsOpen, long Version);

public sealed record QualificationRow(long Id, string OperatorText, string MachineText, string FromText, string ToText, string NoteText, bool IsOpen, long Version);

/// <summary>
/// State and actions of the operators screen: operators, their master assignments to machines and their qualifications (Decision Log #82 R2, #89). Planning changes; Planning, Management
/// and Production read. An assignment is not a qualification: the list shows whether a current qualification exists and an assignment without one is made with a warning. The screen makes no
/// authorization decision; every command is checked again by the Application.
/// </summary>
public sealed class OperatorsViewModel(OperatorService service, PlanningMasterService planning, IJalaliDateService dates, Func<Principal> currentUser) : ObservableObject
{
    private readonly ObservableCollection<OperatorRow> _operators = [];
    private readonly ObservableCollection<AssignmentRow> _assignments = [];
    private readonly ObservableCollection<QualificationRow> _qualifications = [];
    private IReadOnlyList<Choice<long>> _operatorChoices = [];
    private IReadOnlyList<Choice<long>> _machineChoices = [];
    private string _message = string.Empty;
    private bool _isBusy;
    private bool _isLoading;
    private bool _canMaintain;
    private bool _canMaintainQualifications;
    private OperatorRow? _selectedOperator;
    private AssignmentRow? _selectedAssignment;
    private QualificationRow? _selectedQualification;

    public ObservableCollection<OperatorRow> Operators => _operators;

    public ObservableCollection<AssignmentRow> Assignments => _assignments;

    public ObservableCollection<QualificationRow> Qualifications => _qualifications;

    public IReadOnlyList<Choice<long>> OperatorChoices => _operatorChoices;

    public IReadOnlyList<Choice<long>> MachineChoices => _machineChoices;

    public OperatorRow? SelectedOperator
    {
        get => _selectedOperator;
        set => SetProperty(ref _selectedOperator, value);
    }

    public AssignmentRow? SelectedAssignment
    {
        get => _selectedAssignment;
        set => SetProperty(ref _selectedAssignment, value);
    }

    public QualificationRow? SelectedQualification
    {
        get => _selectedQualification;
        set => SetProperty(ref _selectedQualification, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>True when the signed-in user is Program Admin (the Application's answer from the database); the others only read. Operators and eligibility assignments are Admin's (Decision Log #122).</summary>
    public bool CanMaintain
    {
        get => _canMaintain;
        private set => SetProperty(ref _canMaintain, value);
    }

    /// <summary>True when the signed-in user may record qualifications (Planning); separate from <see cref="CanMaintain"/>.</summary>
    public bool CanMaintainQualifications
    {
        get => _canMaintainQualifications;
        private set => SetProperty(ref _canMaintainQualifications, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default) => await RunAsync(() => ReloadAsync(cancellationToken, keepMessage: false), showLoading: true);

    public async Task<bool> RegisterAsync(string? code, string? fullName, CancellationToken cancellationToken = default) =>
        await ChangeAsync(async () => await service.RegisterAsync(currentUser(), code, fullName, null, cancellationToken), "اپراتور ثبت شد.", cancellationToken);

    public async Task<bool> RenameSelectedAsync(string? fullName, CancellationToken cancellationToken = default) =>
        _selectedOperator is { } o
            ? await ChangeAsync(async () => await service.RenameAsync(currentUser(), o.Id, o.Version, fullName, cancellationToken), "نام اپراتور تغییر کرد.", cancellationToken)
            : Nothing();

    public async Task<bool> SetSelectedActiveAsync(bool isActive, CancellationToken cancellationToken = default) =>
        _selectedOperator is { } o
            ? await ChangeAsync(async () => await service.SetActiveAsync(currentUser(), o.Id, o.Version, isActive, cancellationToken), isActive ? "اپراتور فعال شد." : "اپراتور غیرفعال شد.", cancellationToken)
            : Nothing();

    public async Task<bool> AssignAsync(long operatorId, long machineId, string? fromText, bool isPrimary, string? reason, CancellationToken cancellationToken = default) =>
        await ChangeAsync(async () => await service.AssignAsync(currentUser(), operatorId, machineId, fromText, isPrimary, reason, cancellationToken), "تخصیص ثبت شد.", cancellationToken);

    public async Task<bool> CloseSelectedAssignmentAsync(string? lastDayText, CancellationToken cancellationToken = default) =>
        _selectedAssignment is { } a
            ? await ChangeAsync(async () => await service.CloseAssignmentAsync(currentUser(), a.Id, a.Version, lastDayText, cancellationToken), "تخصیص بسته شد.", cancellationToken)
            : Nothing();

    public async Task<bool> RecordQualificationAsync(long operatorId, long machineId, string? fromText, string? note, CancellationToken cancellationToken = default) =>
        await ChangeAsync(async () => await service.RecordQualificationAsync(currentUser(), operatorId, machineId, fromText, note, cancellationToken), "صلاحیت ثبت شد.", cancellationToken);

    public async Task<bool> EndSelectedQualificationAsync(string? lastDayText, CancellationToken cancellationToken = default) =>
        _selectedQualification is { } q
            ? await ChangeAsync(async () => await service.EndQualificationAsync(currentUser(), q.Id, q.Version, lastDayText, cancellationToken), "صلاحیت پایان یافت.", cancellationToken)
            : Nothing();

    private bool Nothing()
    {
        Message = Messages.NothingSelected;
        return false;
    }

    private async Task<bool> ChangeAsync<T>(Func<Task<Outcome<T>>> call, string success, CancellationToken cancellationToken)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            var outcome = await call();
            if (!outcome.Succeeded)
            {
                var text = Messages.For(outcome);
                if (outcome.Status is OperationStatus.ConcurrencyConflict or OperationStatus.NotFound)
                {
                    await ReloadAsync(cancellationToken, keepMessage: true);
                }

                Message = text;
                return;
            }

            saved = true;
            await ReloadAsync(cancellationToken, keepMessage: true);
            Message = outcome.WarningList.Contains(OperatorService.NoQualificationWarning)
                ? success + " هشدار: این اپراتور در تاریخ شروع صلاحیت ثبت‌شده‌ای برای این ماشین ندارد (تخصیص به معنای صلاحیت نیست)."
                : success;
        });
        return saved;
    }

    private async Task ReloadAsync(CancellationToken cancellationToken, bool keepMessage)
    {
        if (!keepMessage)
        {
            Message = string.Empty;
        }

        CanMaintain = await service.CanMaintainAsync(currentUser(), cancellationToken);
        CanMaintainQualifications = await service.CanMaintainQualificationsAsync(currentUser(), cancellationToken);
        var outcome = await service.LoadAsync(currentUser(), cancellationToken);
        if (!outcome.Succeeded)
        {
            _operators.Clear();
            _assignments.Clear();
            _qualifications.Clear();
            Message = Messages.For(outcome);
            return;
        }

        var machines = await planning.LoadAsync(currentUser(), cancellationToken);
        var machineById = machines.Succeeded ? machines.Value!.Machines.ToDictionary(m => m.Value.Id, m => m.Value) : [];
        var data = outcome.Value!;
        var byId = data.Operators.ToDictionary(o => o.Value.Id, o => o.Value);
        string OperatorText(long id) => byId.TryGetValue(id, out var o) ? o.Code + " — " + o.FullName : "?";
        string MachineText(long id) => machineById.TryGetValue(id, out var m) ? m.Code : "?";
        string ToText(DateOnly? d) => d is { } v ? dates.FormatDate(v) : "باز";

        var keepOperator = _selectedOperator?.Id;
        var keepAssignment = _selectedAssignment?.Id;
        var keepQualification = _selectedQualification?.Id;
        _operators.Clear();
        foreach (var o in data.Operators)
        {
            _operators.Add(new OperatorRow(o.Value.Id, o.Value.Code, o.Value.FullName, o.Value.IsActive, o.Value.UserId is not null, o.Version));
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var qualifications = data.Qualifications.Select(q => q.Value).ToList();
        _assignments.Clear();
        foreach (var a in data.Assignments)
        {
            var qualified = OperatorMachineRules.IsQualified(qualifications, a.Value.OperatorId, a.Value.MachineId, a.Value.IsOpen ? today : a.Value.EffectiveFrom);
            _assignments.Add(new AssignmentRow(
                a.Value.Id, OperatorText(a.Value.OperatorId), MachineText(a.Value.MachineId), dates.FormatDate(a.Value.EffectiveFrom), ToText(a.Value.EffectiveTo),
                a.Value.IsPrimary ? "اصلی" : "—", qualified ? "دارد" : "ندارد", a.Value.IsOpen, a.Version));
        }

        _qualifications.Clear();
        foreach (var q in data.Qualifications)
        {
            _qualifications.Add(new QualificationRow(
                q.Value.Id, OperatorText(q.Value.OperatorId), MachineText(q.Value.MachineId), dates.FormatDate(q.Value.QualifiedFrom), ToText(q.Value.QualifiedTo), q.Value.Note ?? string.Empty, q.Value.IsOpen, q.Version));
        }

        _operatorChoices = data.Operators.Where(o => o.Value.IsActive).Select(o => new Choice<long>(o.Value.Id, o.Value.Code + " — " + o.Value.FullName)).ToList();
        _machineChoices = machines.Succeeded ? machines.Value!.Machines.Where(m => m.Value.IsActive).Select(m => new Choice<long>(m.Value.Id, m.Value.Code + " — " + m.Value.Name)).ToList() : [];
        _selectedOperator = _operators.FirstOrDefault(r => r.Id == keepOperator);
        OnPropertyChanged(nameof(SelectedOperator));
        _selectedAssignment = _assignments.FirstOrDefault(r => r.Id == keepAssignment);
        OnPropertyChanged(nameof(SelectedAssignment));
        _selectedQualification = _qualifications.FirstOrDefault(r => r.Id == keepQualification);
        OnPropertyChanged(nameof(SelectedQualification));
        OnPropertyChanged(nameof(OperatorChoices));
        OnPropertyChanged(nameof(MachineChoices));
    }

    private async Task RunAsync(Func<Task> action, bool showLoading = false)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        IsLoading = showLoading;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // The screen was closed; nothing to report.
        }
        catch (Exception)
        {
            Message = Messages.Unexpected;
        }
        finally
        {
            IsLoading = false;
            IsBusy = false;
        }
    }
}
