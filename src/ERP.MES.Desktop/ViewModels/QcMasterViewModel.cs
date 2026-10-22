using System.Collections.ObjectModel;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Quality;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Quality;

namespace ERP.MES.Desktop.ViewModels;

public sealed record UnitRow(long Id, string Code, string Name, string KindText, bool IsActive, long Version)
{
    public string StatusText => IsActive ? "فعال" : "غیرفعال";
}

public sealed record ConversionRow(long Id, string FromText, string ToText, string FactorText, string ReciprocalText, bool IsActive, long Version)
{
    public string StatusText => IsActive ? "فعال" : "غیرفعال";
}

public sealed record QuantityTypeRow(long Id, string Code, string Name, string BaseUnitText, int BaseUnitsPerType, string MappingText, bool IsActive, long Version)
{
    public string StatusText => IsActive ? "فعال" : "غیرفعال";
}

/// <summary>
/// State and actions of the QC master-data screen: units of measure, conversions and quantity types (Decision Log #9, #90). QC changes them; everyone reads them. The screen makes no
/// authorization decision: <see cref="CanMaintain"/> only enables buttons (the Application's answer) and every command is checked again. Failures become Persian messages.
/// </summary>
public sealed class QcMasterViewModel(QcMasterService service, Func<Principal> currentUser) : ObservableObject
{
    private readonly ObservableCollection<UnitRow> _units = [];
    private readonly ObservableCollection<ConversionRow> _conversions = [];
    private readonly ObservableCollection<QuantityTypeRow> _types = [];
    private IReadOnlyList<Stored<UnitOfMeasure>> _storedUnits = [];
    private string _message = string.Empty;
    private bool _isBusy;
    private bool _isLoading;
    private bool _canMaintain;
    private UnitRow? _selectedUnit;
    private ConversionRow? _selectedConversion;
    private QuantityTypeRow? _selectedType;

    public ObservableCollection<UnitRow> Units => _units;

    public ObservableCollection<ConversionRow> Conversions => _conversions;

    public ObservableCollection<QuantityTypeRow> QuantityTypes => _types;

    /// <summary>The kinds offered when adding a unit.</summary>
    public IReadOnlyList<Choice<UnitKind>> Kinds { get; } =
    [
        new(UnitKind.Count, Messages.UnitKindText(UnitKind.Count)),
        new(UnitKind.Mass, Messages.UnitKindText(UnitKind.Mass)),
        new(UnitKind.Length, Messages.UnitKindText(UnitKind.Length)),
        new(UnitKind.Other, Messages.UnitKindText(UnitKind.Other)),
    ];

    /// <summary>The units a conversion or a quantity type can name (all, active first).</summary>
    public IReadOnlyList<Choice<long>> UnitChoices => _storedUnits.OrderByDescending(u => u.Value.IsActive).ThenBy(u => u.Value.Id).Select(u => new Choice<long>(u.Value.Id, u.Value.Code + " — " + u.Value.Name)).ToList();

    /// <summary>The active counting units: the only valid base units of a quantity type.</summary>
    public IReadOnlyList<Choice<long>> BaseUnitChoices => _storedUnits.Where(u => u.Value.IsActive && u.Value.Kind == UnitKind.Count).Select(u => new Choice<long>(u.Value.Id, u.Value.Code + " — " + u.Value.Name)).ToList();

    public UnitRow? SelectedUnit
    {
        get => _selectedUnit;
        set => SetProperty(ref _selectedUnit, value);
    }

    public ConversionRow? SelectedConversion
    {
        get => _selectedConversion;
        set => SetProperty(ref _selectedConversion, value);
    }

    public QuantityTypeRow? SelectedType
    {
        get => _selectedType;
        set => SetProperty(ref _selectedType, value);
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

    /// <summary>True when the signed-in user is QC (the Application's answer, from the database); other users only read.</summary>
    public bool CanMaintain
    {
        get => _canMaintain;
        private set => SetProperty(ref _canMaintain, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default) => await RunAsync(() => ReloadAsync(cancellationToken, keepMessage: false), showLoading: true);

    public async Task<bool> AddUnitAsync(string? code, string? name, UnitKind kind, CancellationToken cancellationToken = default) =>
        await ChangeAsync(async () => await service.AddUnitAsync(currentUser(), code, name, kind, cancellationToken), "واحد ثبت شد.", cancellationToken);

    public async Task<bool> RenameSelectedUnitAsync(string? name, CancellationToken cancellationToken = default) =>
        _selectedUnit is { } u
            ? await ChangeAsync(async () => await service.RenameUnitAsync(currentUser(), u.Id, u.Version, name, cancellationToken), "نام واحد تغییر کرد.", cancellationToken)
            : Nothing();

    public async Task<bool> SetSelectedUnitActiveAsync(bool isActive, CancellationToken cancellationToken = default) =>
        _selectedUnit is { } u
            ? await ChangeAsync(async () => await service.SetUnitActiveAsync(currentUser(), u.Id, u.Version, isActive, cancellationToken), isActive ? "واحد فعال شد." : "واحد غیرفعال شد.", cancellationToken)
            : Nothing();

    /// <summary>Adds "one <paramref name="fromUnitId"/> equals the typed factor of <paramref name="toUnitId"/>". The factor is typed text: Latin, Persian or Arabic-Indic digits, one decimal separator.</summary>
    public async Task<bool> AddConversionAsync(long fromUnitId, long toUnitId, string? factorText, CancellationToken cancellationToken = default)
    {
        if (!QuantityText.TryParse(factorText, out var factor))
        {
            Message = Messages.ForIssue(ValidationIssue.FactorInvalid, ERP.MES.Domain.Calendar.JalaliDateError.None);
            return false;
        }

        return await ChangeAsync(async () => await service.AddConversionAsync(currentUser(), fromUnitId, toUnitId, factor, cancellationToken), "تبدیل ثبت شد.", cancellationToken);
    }

    public async Task<bool> SetSelectedConversionActiveAsync(bool isActive, CancellationToken cancellationToken = default) =>
        _selectedConversion is { } c
            ? await ChangeAsync(async () => await service.SetConversionActiveAsync(currentUser(), c.Id, c.Version, isActive, cancellationToken), isActive ? "تبدیل فعال شد." : "تبدیل غیرفعال شد.", cancellationToken)
            : Nothing();

    /// <summary>
    /// Adds a quantity type. Without members it is piece-like and counts <paramref name="baseUnitsText"/> base units (piece 1, pair 2). With members it is a set: one member per line,
    /// "description: count" (a set has no standard composition, Decision Log #90) and the base units are their sum.
    /// </summary>
    public async Task<bool> AddQuantityTypeAsync(string? code, string? name, long baseUnitId, string? baseUnitsText, string? membersText, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SetMemberInput>? members = null;
        var baseUnits = 0;
        if (!string.IsNullOrWhiteSpace(membersText))
        {
            if (!TryParseMembers(membersText, out var parsed))
            {
                Message = Messages.ForIssue(ValidationIssue.SetMembersInvalid, ERP.MES.Domain.Calendar.JalaliDateError.None);
                return false;
            }

            members = parsed;
        }
        else if (!TryParseWholeNumber(baseUnitsText, out baseUnits))
        {
            Message = Messages.ForIssue(ValidationIssue.BaseUnitsInvalid, ERP.MES.Domain.Calendar.JalaliDateError.None);
            return false;
        }

        var command = new AddQuantityTypeCommand(code, name, baseUnitId, baseUnits, members);
        return await ChangeAsync(async () => await service.AddQuantityTypeAsync(currentUser(), command, cancellationToken), "نوع مقدار ثبت شد.", cancellationToken);
    }

    private static bool TryParseWholeNumber(string? text, out int value)
    {
        value = 0;
        if (!QuantityText.TryParse(text, out var number) || number != decimal.Truncate(number) || number < 1 || number > 100000)
        {
            return false;
        }

        value = (int)number;
        return true;
    }

    // One member per line: "description: count" (':' or '=' or the Persian colon). A malformed line makes the whole input invalid; nothing is guessed.
    private static bool TryParseMembers(string text, out IReadOnlyList<SetMemberInput> members)
    {
        var list = new List<SetMemberInput>();
        members = list;
        foreach (var line in text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cut = line.LastIndexOfAny([':', '=', '\u06D4', '\uFF1A']);
            if (cut <= 0 || !TryParseWholeNumber(line[(cut + 1)..], out var count))
            {
                return false;
            }

            var description = line[..cut].Trim();
            if (description.Length == 0)
            {
                return false;
            }

            list.Add(new SetMemberInput(description, count));
        }

        return list.Count >= 2;
    }

    public async Task<bool> RenameSelectedTypeAsync(string? name, CancellationToken cancellationToken = default) =>
        _selectedType is { } q
            ? await ChangeAsync(async () => await service.RenameQuantityTypeAsync(currentUser(), q.Id, q.Version, name, cancellationToken), "نام نوع مقدار تغییر کرد.", cancellationToken)
            : Nothing();

    public async Task<bool> SetSelectedTypeActiveAsync(bool isActive, CancellationToken cancellationToken = default) =>
        _selectedType is { } q
            ? await ChangeAsync(async () => await service.SetQuantityTypeActiveAsync(currentUser(), q.Id, q.Version, isActive, cancellationToken), isActive ? "نوع مقدار فعال شد." : "نوع مقدار غیرفعال شد.", cancellationToken)
            : Nothing();

    private bool Nothing()
    {
        Message = Messages.NothingSelected;
        return false;
    }

    // One path for every change: the Application decides; a conflict or a vanished row reloads what is true now.
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

        CanMaintain = await service.CanMaintainAsync(currentUser(), cancellationToken);
        var outcome = await service.LoadAsync(currentUser(), cancellationToken);
        if (!outcome.Succeeded)
        {
            _units.Clear();
            _conversions.Clear();
            _types.Clear();
            Message = Messages.For(outcome);
            return;
        }

        var data = outcome.Value!;
        _storedUnits = data.Units;
        var byId = data.Units.ToDictionary(u => u.Value.Id, u => u.Value);
        string UnitText(long id) => byId.TryGetValue(id, out var u) ? u.Code : "?";

        var keepUnit = _selectedUnit?.Id;
        var keepConversion = _selectedConversion?.Id;
        var keepType = _selectedType?.Id;
        _units.Clear();
        foreach (var u in data.Units)
        {
            _units.Add(new UnitRow(u.Value.Id, u.Value.Code, u.Value.Name, Messages.UnitKindText(u.Value.Kind), u.Value.IsActive, u.Version));
        }

        _conversions.Clear();
        foreach (var c in data.Conversions)
        {
            var factor = c.Value.Factor.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
            var reciprocal = (1m / c.Value.Factor).ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
            _conversions.Add(new ConversionRow(
                c.Value.Id, "۱ " + UnitText(c.Value.FromUnitId), factor + " " + UnitText(c.Value.ToUnitId), factor, "۱ " + UnitText(c.Value.ToUnitId) + " = " + reciprocal + " " + UnitText(c.Value.FromUnitId), c.Value.IsActive, c.Version));
        }

        _types.Clear();
        foreach (var q in data.QuantityTypes)
        {
            var mapping = q.Value.IsSet
                ? string.Join(" + ", q.Value.Members.Select(m => m.Description + " (" + m.BaseUnits + ")"))
                : q.Value.BaseUnitsPerType + " " + UnitText(q.Value.BaseUnitId);
            _types.Add(new QuantityTypeRow(q.Value.Id, q.Value.Code, q.Value.Name, UnitText(q.Value.BaseUnitId), q.Value.BaseUnitsPerType, mapping, q.Value.IsActive, q.Version));
        }

        SelectedUnit = _units.FirstOrDefault(r => r.Id == keepUnit);
        SelectedConversion = _conversions.FirstOrDefault(r => r.Id == keepConversion);
        SelectedType = _types.FirstOrDefault(r => r.Id == keepType);
        OnPropertyChanged(nameof(UnitChoices));
        OnPropertyChanged(nameof(BaseUnitChoices));
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
