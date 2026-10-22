using System.Collections.ObjectModel;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Planning;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Materials;

namespace ERP.MES.Desktop.ViewModels;

public sealed record MaterialRow(long Id, string Code, string Name, string GradeText, string FormText, bool IsActive, string LatestPriceText, long Version)
{
    public string StatusText => IsActive ? "فعال" : "غیرفعال";
}

public sealed record PriceRow(string EffectiveFromText, string PriceText, string ReasonText);

/// <summary>
/// State and actions of the materials screen: the Warehouse-owned material list and the price-per-kg history in Rial (Decision Log #10). Warehouse maintains the list; Warehouse and
/// Management add prices; Planning reads prices; everyone reads the list. The screen mirrors the Application's capabilities and makes no authorization decision of its own.
/// </summary>
public sealed class MaterialsViewModel(MaterialService service, IJalaliDateService dates, DigitStyle digitStyle, Func<Principal> currentUser) : ObservableObject
{
    private readonly ObservableCollection<MaterialRow> _materials = [];
    private readonly ObservableCollection<PriceRow> _prices = [];
    private IReadOnlyList<MaterialPrice> _allPrices = [];
    private string _message = string.Empty;
    private bool _isBusy;
    private bool _isLoading;
    private MaterialCapabilities _capabilities = MaterialCapabilities.None;
    private MaterialRow? _selectedMaterial;

    public ObservableCollection<MaterialRow> Materials => _materials;

    public ObservableCollection<PriceRow> Prices => _prices;

    public MaterialRow? SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (SetProperty(ref _selectedMaterial, value))
            {
                RebuildPrices();
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

    public bool CanMaintain => _capabilities.CanMaintain;

    public bool CanChangePrices => _capabilities.CanChangePrices;

    public bool CanViewPrices => _capabilities.CanViewPrices;

    public async Task LoadAsync(CancellationToken cancellationToken = default) => await RunAsync(() => ReloadAsync(cancellationToken, keepMessage: false), showLoading: true);

    public async Task<bool> AddMaterialAsync(string? code, string? name, string? grade, string? form, CancellationToken cancellationToken = default) =>
        await ChangeAsync(async () => await service.AddMaterialAsync(currentUser(), code, name, grade, form, cancellationToken), "ماده ثبت شد.", cancellationToken);

    public async Task<bool> UpdateSelectedAsync(string? name, string? grade, string? form, CancellationToken cancellationToken = default) =>
        _selectedMaterial is { } m
            ? await ChangeAsync(async () => await service.UpdateMaterialAsync(currentUser(), m.Id, m.Version, name, grade, form, cancellationToken), "مشخصات ماده تغییر کرد.", cancellationToken)
            : Nothing();

    public async Task<bool> SetSelectedActiveAsync(bool isActive, CancellationToken cancellationToken = default) =>
        _selectedMaterial is { } m
            ? await ChangeAsync(async () => await service.SetMaterialActiveAsync(currentUser(), m.Id, m.Version, isActive, cancellationToken), isActive ? "ماده فعال شد." : "ماده غیرفعال شد.", cancellationToken)
            : Nothing();

    /// <summary>Adds a price (Rial per kg, typed without thousands separators) for the selected material, applying from the typed Jalali date.</summary>
    public async Task<bool> AddPriceForSelectedAsync(string? priceText, string? effectiveFromText, string? reason, CancellationToken cancellationToken = default)
    {
        if (_selectedMaterial is not { } material)
        {
            return Nothing();
        }

        if (!QuantityText.TryParse(priceText, out var price))
        {
            Message = Messages.ForIssue(ValidationIssue.PriceInvalid, JalaliDateError.None);
            return false;
        }

        return await ChangeAsync(async () => await service.AddPriceAsync(currentUser(), material.Id, price, effectiveFromText, reason, cancellationToken), "قیمت ثبت شد.", cancellationToken);
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
        OnPropertyChanged(nameof(CanMaintain));
        OnPropertyChanged(nameof(CanChangePrices));
        OnPropertyChanged(nameof(CanViewPrices));

        var outcome = await service.LoadAsync(currentUser(), cancellationToken);
        if (!outcome.Succeeded)
        {
            _materials.Clear();
            _prices.Clear();
            Message = Messages.For(outcome);
            return;
        }

        var data = outcome.Value!;
        _allPrices = data.Prices;
        var keep = _selectedMaterial?.Id;
        _materials.Clear();
        foreach (var m in data.Materials)
        {
            var latest = data.Prices.Where(p => p.MaterialId == m.Value.Id).MaxBy(p => p.EffectiveFrom);
            var latestText = !data.PricesVisible ? "—" : latest is null ? "ثبت نشده" : PriceText(latest.PricePerKgRial) + " (از " + dates.FormatDate(latest.EffectiveFrom) + ")";
            _materials.Add(new MaterialRow(m.Value.Id, m.Value.Code, m.Value.Name, m.Value.Grade ?? string.Empty, m.Value.Form ?? string.Empty, m.Value.IsActive, latestText, m.Version));
        }

        _selectedMaterial = _materials.FirstOrDefault(r => r.Id == keep);
        OnPropertyChanged(nameof(SelectedMaterial));
        RebuildPrices();
    }

    private void RebuildPrices()
    {
        _prices.Clear();
        if (_selectedMaterial is { } material)
        {
            foreach (var p in _allPrices.Where(p => p.MaterialId == material.Id).OrderByDescending(p => p.EffectiveFrom))
            {
                _prices.Add(new PriceRow(dates.FormatDate(p.EffectiveFrom), PriceText(p.PricePerKgRial), p.Reason ?? string.Empty));
            }
        }
    }

    // Iranian Rial per kilogram, exact; digits in the screen's digit shape. No rounding and never converted to Toman.
    private string PriceText(decimal rial)
    {
        var text = rial.ToString("#,##0.############################", System.Globalization.CultureInfo.InvariantCulture) + " ریال بر کیلوگرم";
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
