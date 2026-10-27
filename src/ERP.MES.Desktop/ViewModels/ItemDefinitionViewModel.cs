using System.Collections.ObjectModel;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Planning;
using ERP.MES.Application.Quality;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.ViewModels;

public sealed record DefinitionRequestRow(long RequestId, string RequestNo, string CustomerName, string WorkTypeText, string StatusText, int ItemCount);

public sealed record DefinedItemRow(long ItemId, long Version, string ItemNo, string PartName, string QuantityText, string SpecificationText, string PriorityText, int StageCount);

public sealed record DefinedStageRow(int StageNo, string Title, string MinutesText, string UnitText, string MachineText, string StatusText);

/// <summary>
/// State and actions of the item-definition screen (Decision Log #86, #95): Planning defines items and their stages after the gates hold. The screen shows the gate state and what the
/// Application refuses; it enforces nothing itself - the gate, the specification rules and the permission are checked again by <see cref="ItemDefinitionService"/>.
/// </summary>
public sealed class ItemDefinitionViewModel(
    ItemDefinitionService service,
    RequestService requests,
    RequestGateService gates,
    QcMasterService qc,
    MaterialService materials,
    PlanningMasterService planning,
    DigitStyle digitStyle,
    Func<Principal> currentUser) : ObservableObject
{
    private readonly ObservableCollection<DefinitionRequestRow> _requests = [];
    private readonly ObservableCollection<DefinedItemRow> _items = [];
    private readonly ObservableCollection<DefinedStageRow> _stages = [];
    private ItemDefinitionCapabilities _capabilities = ItemDefinitionCapabilities.None;
    private RequestDetail? _detail;
    private ProductionGateStatus? _gate;
    private DefinitionRequestRow? _selectedRequest;
    private DefinedItemRow? _selectedItem;
    private IReadOnlyList<Choice<long>> _quantityTypes = [];
    private IReadOnlyList<Choice<long>> _materialChoices = [];
    private IReadOnlyList<Choice<long>> _unitChoices = [];
    private IReadOnlyList<MachineChoice> _machineChoices = [];
    private string _message = string.Empty;
    private bool _isBusy;
    private bool _isLoading;

    public ObservableCollection<DefinitionRequestRow> Requests => _requests;

    public ObservableCollection<DefinedItemRow> Items => _items;

    public ObservableCollection<DefinedStageRow> Stages => _stages;

    public IReadOnlyList<Choice<Priority>> Priorities { get; } = PriorityInfo.All.Select(p => new Choice<Priority>(p, p.PersianName())).ToList();

    public IReadOnlyList<Choice<long>> QuantityTypeChoices => _quantityTypes;

    public IReadOnlyList<Choice<long>> MaterialChoices => _materialChoices;

    public IReadOnlyList<Choice<long>> UnitChoices => _unitChoices;

    /// <summary>The machines of the given unit, for the stage form.</summary>
    public IReadOnlyList<Choice<long>> MachinesOf(long unitId) => _machineChoices.Where(m => m.UnitId == unitId).Select(m => new Choice<long>(m.Id, m.Text)).ToList();

    public DefinitionRequestRow? SelectedRequest
    {
        get => _selectedRequest;
        set => SetProperty(ref _selectedRequest, value);
    }

    public DefinedItemRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                RebuildStages();
                OnPropertyChanged(nameof(CanDefineStage));
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

    public bool HasRequest => _detail is not null;

    public string RequestHeading => _detail is null ? string.Empty : $"درخواست {_detail.Request.Value.RequestNo.Value} — {_detail.CustomerName} — {Messages.WorkTypeText(_detail.Request.Value.WorkType)}";

    public string GateText => _gate is null
        ? string.Empty
        : _gate.DefinitionAllowed
            ? "دروازه‌ها برقرار است؛ تعریف آیتم ممکن است."
            : "تعریف آیتم ممکن نیست. موانع: " + (_gate.DefinitionBlock != ERP.MES.Domain.Contracts.DefinitionBlock.None ? Messages.DefinitionBlockText(_gate.DefinitionBlock) + "؛ " : string.Empty)
                + string.Join("؛ ", _gate.Blockers.Where(b => b != GateBlocker.DefinitionGate).Select(Messages.GateBlockerText));

    public bool CanDefineItem => _detail is not null && _gate is { DefinitionAllowed: true }
        && (_detail.Request.Value.WorkType == WorkType.Make ? _capabilities.CanDefineMake : _capabilities.CanDefineRepair);

    public bool CanDefineStage => CanDefineItem && _selectedItem is not null;

    public async Task LoadAsync(CancellationToken cancellationToken = default) =>
        await RunAsync(async () =>
        {
            Message = string.Empty;
            await ReloadAsync(_selectedRequest?.RequestId, cancellationToken);
        }, showLoading: true);

    public async Task SelectRequestAsync(long requestId, CancellationToken cancellationToken = default) =>
        await RunAsync(async () => await ReloadAsync(requestId, cancellationToken));

    public async Task<bool> DefineItemAsync(
        string? itemNo, string? partName, string? quantityText, Priority priority, long? quantityTypeId, long? materialId, string? weightText, string? lengthText, string? diameterText,
        CancellationToken cancellationToken = default)
    {
        if (_detail is null)
        {
            Message = Messages.NothingSelected;
            return false;
        }

        if (!QuantityText.TryParse(quantityText, out var quantity))
        {
            Message = Messages.ForIssue(ValidationIssue.QuantityInvalid, JalaliDateError.None);
            return false;
        }

        if (!TryOptional(weightText, out var weight))
        {
            Message = Messages.ForIssue(ValidationIssue.WeightInvalid, JalaliDateError.None);
            return false;
        }

        if (!TryOptional(lengthText, out var length) || !TryOptional(diameterText, out var diameter))
        {
            Message = Messages.ForIssue(ValidationIssue.DimensionInvalid, JalaliDateError.None);
            return false;
        }

        var requestId = _detail.Request.Value.Id;
        var version = _detail.Request.Version;
        return await ChangeAsync(
            () => service.DefineItemAsync(currentUser(), requestId, version, new DefineItemCommand(itemNo, partName, quantity, priority, quantityTypeId, materialId, weight, length, diameter), cancellationToken),
            "آیتم تعریف شد.");
    }

    public async Task<bool> DefineStageAsync(string? title, string? minutesText, long unitId, long? machineId, CancellationToken cancellationToken = default)
    {
        if (_detail is null || _selectedItem is null)
        {
            Message = Messages.NothingSelected;
            return false;
        }

        if (!QuantityText.TryParse(minutesText, out var minutes))
        {
            Message = Messages.ForIssue(ValidationIssue.StageMinutesInvalid, JalaliDateError.None);
            return false;
        }

        var requestId = _detail.Request.Value.Id;
        var version = _detail.Request.Version;
        var item = _selectedItem;
        return await ChangeAsync(
            () => service.DefineStageAsync(currentUser(), requestId, version, item.ItemId, item.Version, new DefineStageCommand(title, minutes, unitId, machineId), cancellationToken),
            "مرحله تعریف شد.");
    }

    private static bool TryOptional(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!QuantityText.TryParse(text, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private async Task<bool> ChangeAsync(Func<Task<Outcome<Stored<Request>>>> call, string success)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            var requestId = _detail?.Request.Value.Id;
            var itemId = _selectedItem?.ItemId;
            var outcome = await call();
            if (!outcome.Succeeded)
            {
                var text = Messages.For(outcome);
                if (outcome.Status != OperationStatus.NotAuthorized)
                {
                    await ReloadAsync(requestId, CancellationToken.None, itemId);
                }

                Message = text;
                return;
            }

            saved = true;
            await ReloadAsync(requestId, CancellationToken.None, itemId);
            Message = success;
        });
        return saved;
    }

    private async Task ReloadAsync(long? keepRequestId, CancellationToken cancellationToken, long? keepItemId = null)
    {
        _capabilities = await service.GetCapabilitiesAsync(currentUser(), cancellationToken);
        var list = await requests.ListAsync(currentUser(), null, cancellationToken);
        _requests.Clear();
        _items.Clear();
        _stages.Clear();
        _detail = null;
        _gate = null;
        if (!list.Succeeded)
        {
            Message = Messages.For(list);
            NotifyAll();
            return;
        }

        foreach (var r in list.Value!)
        {
            _requests.Add(new DefinitionRequestRow(r.RequestId, r.RequestNo.Value, r.CustomerName, Messages.WorkTypeText(r.WorkType), Messages.StatusText(r.Status), r.ItemCount));
        }

        var types = await qc.LoadAsync(currentUser(), cancellationToken);
        _quantityTypes = types.Succeeded
            ? types.Value!.QuantityTypes.Where(t => t.Value.IsActive).Select(t => new Choice<long>(t.Value.Id, $"{t.Value.Code} — {t.Value.Name} ({Digits(t.Value.BaseUnitsPerType.ToString(System.Globalization.CultureInfo.InvariantCulture))} واحد پایه)")).ToList()
            : [];
        var mats = await materials.LoadAsync(currentUser(), cancellationToken);
        _materialChoices = mats.Succeeded ? mats.Value!.Materials.Where(m => m.Value.IsActive).Select(m => new Choice<long>(m.Value.Id, m.Value.Code + " — " + m.Value.Name)).ToList() : [];
        var master = await planning.LoadAsync(currentUser(), cancellationToken);
        _unitChoices = master.Succeeded ? master.Value!.Units.Where(u => u.Value.IsActive).Select(u => new Choice<long>(u.Value.Id, u.Value.Code + " — " + u.Value.Name)).ToList() : [];
        _machineChoices = master.Succeeded
            ? master.Value!.Machines.Where(m => m.Value.IsActive).Select(m => new MachineChoice(m.Value.Id, m.Value.ProductionUnitId, m.Value.Code + " — " + m.Value.Name)).ToList()
            : [];
        OnPropertyChanged(nameof(QuantityTypeChoices));
        OnPropertyChanged(nameof(MaterialChoices));
        OnPropertyChanged(nameof(UnitChoices));

        _selectedRequest = _requests.FirstOrDefault(r => r.RequestId == keepRequestId);
        OnPropertyChanged(nameof(SelectedRequest));
        if (_selectedRequest is not null)
        {
            var detail = await requests.GetAsync(currentUser(), _selectedRequest.RequestId, cancellationToken);
            if (detail.Succeeded)
            {
                _detail = detail.Value;
                var gate = await gates.GetAsync(currentUser(), _selectedRequest.RequestId, cancellationToken);
                _gate = gate.Succeeded ? gate.Value : null;
                var request = _detail!.Request.Value;
                foreach (var i in request.Items)
                {
                    _items.Add(new DefinedItemRow(
                        i.Id, _detail.Versions.ItemVersion(i.Id), i.ItemNo, i.PartName, Digits(i.RequestedQuantity.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture)),
                        SpecText(i), i.Priority.PersianName(), i.Stages.Count));
                }
            }
        }

        _selectedItem = _items.FirstOrDefault(i => i.ItemId == keepItemId);
        OnPropertyChanged(nameof(SelectedItem));
        RebuildStages();
        NotifyAll();
    }

    private string SpecText(RequestItem item)
    {
        var parts = new List<string>();
        if (item.QuantityTypeId is { } typeId)
        {
            parts.Add("نوع تعداد: " + (_quantityTypes.FirstOrDefault(t => t.Value == typeId)?.Text.Split(' ')[0] ?? "؟"));
        }

        if (item.MaterialId is { } materialId)
        {
            parts.Add("ماده: " + (_materialChoices.FirstOrDefault(m => m.Value == materialId)?.Text.Split(' ')[0] ?? "؟"));
        }

        if (item.WeightKgPerBaseUnit is { } weight)
        {
            parts.Add("وزن: " + Digits(weight.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)) + " kg");
        }

        if (item.LengthMm is { } length)
        {
            parts.Add("طول: " + Digits(length.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)) + " mm");
        }

        if (item.DiameterMm is { } diameter)
        {
            parts.Add("قطر: " + Digits(diameter.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)) + " mm");
        }

        return parts.Count == 0 ? "—" : string.Join("؛ ", parts);
    }

    private void RebuildStages()
    {
        _stages.Clear();
        if (_detail is null || _selectedItem is null)
        {
            return;
        }

        var item = _detail.Request.Value.Items.FirstOrDefault(i => i.Id == _selectedItem.ItemId);
        if (item is null)
        {
            return;
        }

        var units = _unitChoices.ToDictionary(u => u.Value, u => u.Text.Split(' ')[0]);
        var machines = _machineChoices.ToDictionary(m => m.Id, m => m.Text.Split(' ')[0]);
        foreach (var s in item.Stages)
        {
            _stages.Add(new DefinedStageRow(
                s.StageNo, s.StageTitle, Digits(s.PlannedMinutesPerPiece.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)),
                s.ProductionUnitId is { } u ? units.GetValueOrDefault(u, "؟") : "—", s.MachineId is { } m ? machines.GetValueOrDefault(m, "؟") : "—", ProductionQueueViewModel.StageStatusText(s.Status)));
        }
    }

    private string Digits(string text) =>
        digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : text;

    private void NotifyAll()
    {
        foreach (var name in new[] { nameof(HasRequest), nameof(RequestHeading), nameof(GateText), nameof(CanDefineItem), nameof(CanDefineStage) })
        {
            OnPropertyChanged(name);
        }
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

    private sealed record MachineChoice(long Id, long UnitId, string Text);
}
