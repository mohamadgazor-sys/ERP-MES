using System.Collections.ObjectModel;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Planning;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Planning;

namespace ERP.MES.Desktop.ViewModels;

public sealed record ProductionUnitRow(long Id, string Code, string Name, bool IsActive, long Version)
{
    public string StatusText => IsActive ? "فعال" : "غیرفعال";
}

public sealed record MachineRow(long Id, string Code, string Name, string UnitText, bool IsActive, string LatestRateText, long Version)
{
    public string StatusText => IsActive ? "فعال" : "غیرفعال";
}

public sealed record RateRow(string MachineCode, string EffectiveFromText, string RateText, string ReasonText);

/// <summary>
/// State and actions of the Planning resource screen: production units, machines with stable codes and the hourly-rate history (Decision Log #82 R2, #89). Planning registers units and
/// machines; Planning and Management add rates; Warehouse reads rates; the screen makes no authorization decision, it only mirrors the Application's capabilities, and every command is
/// checked again. Failures become Persian messages.
/// </summary>
public sealed class PlanningMasterViewModel(PlanningMasterService service, IJalaliDateService dates, DigitStyle digitStyle, Func<Principal> currentUser) : ObservableObject
{
    private readonly ObservableCollection<ProductionUnitRow> _units = [];
    private readonly ObservableCollection<MachineRow> _machines = [];
    private readonly ObservableCollection<RateRow> _rates = [];
    private IReadOnlyList<Stored<ProductionUnit>> _storedUnits = [];
    private string _message = string.Empty;
    private bool _isBusy;
    private bool _isLoading;
    private PlanningMasterCapabilities _capabilities = PlanningMasterCapabilities.None;
    private ProductionUnitRow? _selectedUnit;
    private MachineRow? _selectedMachine;

    public ObservableCollection<ProductionUnitRow> Units => _units;

    public ObservableCollection<MachineRow> Machines => _machines;

    /// <summary>The rate history of the selected machine, newest first; empty when the user may not read rates.</summary>
    public ObservableCollection<RateRow> Rates => _rates;

    /// <summary>Active units a new machine can be registered in.</summary>
    public IReadOnlyList<Choice<long>> UnitChoices => _storedUnits.Where(u => u.Value.IsActive).Select(u => new Choice<long>(u.Value.Id, u.Value.Code + " — " + u.Value.Name)).ToList();

    public ProductionUnitRow? SelectedUnit
    {
        get => _selectedUnit;
        set => SetProperty(ref _selectedUnit, value);
    }

    public MachineRow? SelectedMachine
    {
        get => _selectedMachine;
        set
        {
            if (SetProperty(ref _selectedMachine, value))
            {
                RebuildRates();
            }
        }
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

    public bool CanMaintainResources => _capabilities.CanMaintainResources;

    public bool CanChangeRates => _capabilities.CanChangeRates;

    public bool CanViewRates => _capabilities.CanViewRates;

    private IReadOnlyList<MachineHourlyRate> _allRates = [];
    private IReadOnlyDictionary<long, string> _machineCodes = new Dictionary<long, string>();

    public async Task LoadAsync(CancellationToken cancellationToken = default) => await RunAsync(() => ReloadAsync(cancellationToken, keepMessage: false), showLoading: true);

    public async Task<bool> AddUnitAsync(string? code, string? name, CancellationToken cancellationToken = default) =>
        await ChangeAsync(async () => await service.AddUnitAsync(currentUser(), code, name, cancellationToken), "واحد تولیدی ثبت شد.", cancellationToken);

    public async Task<bool> RenameSelectedUnitAsync(string? name, CancellationToken cancellationToken = default) =>
        _selectedUnit is { } u
            ? await ChangeAsync(async () => await service.RenameUnitAsync(currentUser(), u.Id, u.Version, name, cancellationToken), "نام واحد تغییر کرد.", cancellationToken)
            : Nothing();

    public async Task<bool> SetSelectedUnitActiveAsync(bool isActive, CancellationToken cancellationToken = default) =>
        _selectedUnit is { } u
            ? await ChangeAsync(async () => await service.SetUnitActiveAsync(currentUser(), u.Id, u.Version, isActive, cancellationToken), isActive ? "واحد فعال شد." : "واحد غیرفعال شد.", cancellationToken)
            : Nothing();

    public async Task<bool> AddMachineAsync(string? code, string? name, long productionUnitId, CancellationToken cancellationToken = default) =>
        await ChangeAsync(async () => await service.AddMachineAsync(currentUser(), code, name, productionUnitId, cancellationToken), "ماشین ثبت شد.", cancellationToken);

    public async Task<bool> RenameSelectedMachineAsync(string? name, CancellationToken cancellationToken = default) =>
        _selectedMachine is { } m
            ? await ChangeAsync(async () => await service.RenameMachineAsync(currentUser(), m.Id, m.Version, name, cancellationToken), "نام ماشین تغییر کرد.", cancellationToken)
            : Nothing();

    public async Task<bool> SetSelectedMachineActiveAsync(bool isActive, CancellationToken cancellationToken = default) =>
        _selectedMachine is { } m
            ? await ChangeAsync(async () => await service.SetMachineActiveAsync(currentUser(), m.Id, m.Version, isActive, cancellationToken), isActive ? "ماشین فعال شد." : "ماشین غیرفعال شد.", cancellationToken)
            : Nothing();

    /// <summary>Adds a rate (Rial per hour, typed without thousands separators) for the selected machine, applying from the typed Jalali date.</summary>
    public async Task<bool> AddRateForSelectedMachineAsync(string? rateText, string? effectiveFromText, string? reason, CancellationToken cancellationToken = default)
    {
        if (_selectedMachine is not { } machine)
        {
            return Nothing();
        }

        if (!QuantityText.TryParse(rateText, out var rate))
        {
            Message = Messages.ForIssue(ValidationIssue.RateInvalid, JalaliDateError.None);
            return false;
        }

        var saved = false;
        await RunAsync(async () =>
        {
            var outcome = await service.AddRateAsync(currentUser(), machine.Id, rate, effectiveFromText, reason, cancellationToken);
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
            Message = "نرخ ثبت شد.";
        });
        return saved;
    }

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
            Message = success;
        });
        return saved;
    }

    private async Task ReloadAsync(CancellationToken cancellationToken, bool keepMessage)
    {
        if (!keepMessage)
        {
            Message = string.Empty;
        }

        _capabilities = await service.GetCapabilitiesAsync(currentUser(), cancellationToken);
        OnPropertyChanged(nameof(CanMaintainResources));
        OnPropertyChanged(nameof(CanChangeRates));
        OnPropertyChanged(nameof(CanViewRates));

        var outcome = await service.LoadAsync(currentUser(), cancellationToken);
        if (!outcome.Succeeded)
        {
            _units.Clear();
            _machines.Clear();
            _rates.Clear();
            Message = Messages.For(outcome);
            return;
        }

        var data = outcome.Value!;
        _storedUnits = data.Units;
        _allRates = data.Rates;
        var unitCodes = data.Units.ToDictionary(u => u.Value.Id, u => u.Value.Code);
        _machineCodes = data.Machines.ToDictionary(m => m.Value.Id, m => m.Value.Code);

        var keepUnit = _selectedUnit?.Id;
        var keepMachine = _selectedMachine?.Id;
        _units.Clear();
        foreach (var u in data.Units)
        {
            _units.Add(new ProductionUnitRow(u.Value.Id, u.Value.Code, u.Value.Name, u.Value.IsActive, u.Version));
        }

        _machines.Clear();
        foreach (var m in data.Machines)
        {
            var latest = data.Rates.Where(r => r.MachineId == m.Value.Id).MaxBy(r => r.EffectiveFrom);
            var latestText = !data.RatesVisible ? "—" : latest is null ? "ثبت نشده" : RateText(latest.RatePerHourRial) + " (از " + dates.FormatDate(latest.EffectiveFrom) + ")";
            _machines.Add(new MachineRow(m.Value.Id, m.Value.Code, m.Value.Name, unitCodes.GetValueOrDefault(m.Value.ProductionUnitId, "?"), m.Value.IsActive, latestText, m.Version));
        }

        _selectedUnit = _units.FirstOrDefault(r => r.Id == keepUnit);
        OnPropertyChanged(nameof(SelectedUnit));
        _selectedMachine = _machines.FirstOrDefault(r => r.Id == keepMachine);
        OnPropertyChanged(nameof(SelectedMachine));
        RebuildRates();
        OnPropertyChanged(nameof(UnitChoices));
    }

    private void RebuildRates()
    {
        _rates.Clear();
        if (_selectedMachine is { } machine)
        {
            foreach (var r in _allRates.Where(r => r.MachineId == machine.Id).OrderByDescending(r => r.EffectiveFrom))
            {
                _rates.Add(new RateRow(_machineCodes.GetValueOrDefault(r.MachineId, "?"), dates.FormatDate(r.EffectiveFrom), RateText(r.RatePerHourRial), r.Reason ?? string.Empty));
            }
        }
    }

    // Iranian Rial per hour, exact; digits in the screen's digit shape. No rounding and never converted to Toman.
    private string RateText(decimal rial)
    {
        var text = rial.ToString("#,##0.############################", System.Globalization.CultureInfo.InvariantCulture) + " ریال در ساعت";
        return digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : text;
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
