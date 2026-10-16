using System.Collections.ObjectModel;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>One line of the customer list. <see cref="Version"/> is the row version the record was loaded at; it goes back with every change so a change by someone else is detected.</summary>
public sealed record CustomerRow(long Id, string Name, bool IsActive, long Version, string UpdatedText)
{
    public string StatusText => IsActive ? "فعال" : "غیرفعال";
}

/// <summary>
/// State and actions of the customer screen. It talks only to the Application use cases (<see cref="CustomerService"/>, <see cref="RequestService"/> for the capabilities)
/// and shows what they answer: it makes no authorization decision. Every action catches failures and turns them into a Persian message, so the screen never crashes;
/// nothing blocks the UI thread because every call is awaited.
/// </summary>
public sealed class CustomersViewModel(
    CustomerService customers, RequestService requests, IJalaliDateService dates, Func<Principal> currentUser) : ObservableObject
{
    private readonly ObservableCollection<CustomerRow> _rows = [];
    private CustomerRow? _selected;
    private string _message = string.Empty;
    private bool _isBusy;
    private bool _isLoading;
    private bool _hasLoaded;
    private bool _showInactive;
    private Capabilities _capabilities = Capabilities.None;

    public ObservableCollection<CustomerRow> Customers => _rows;

    public CustomerRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
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

    /// <summary>True once a load has finished with no customer to show: the empty state of the list.</summary>
    public bool IsEmpty => _hasLoaded && _rows.Count == 0;

    public bool ShowInactive
    {
        get => _showInactive;
        set => SetProperty(ref _showInactive, value);
    }

    /// <summary>The Application's answer to "may this user maintain customers now?"; used only to enable the buttons.</summary>
    public bool CanMaintain => _capabilities.CanMaintainCustomers;

    public async Task LoadAsync(CancellationToken cancellationToken = default) =>
        await RunAsync(() => ReloadAsync(cancellationToken), showLoading: true);

    /// <summary>Adds a customer. Returns true when it was saved.</summary>
    public async Task<bool> CreateAsync(string? name, CancellationToken cancellationToken = default)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            var outcome = await customers.CreateAsync(currentUser(), name, cancellationToken);
            if (outcome.Succeeded)
            {
                saved = true;
                Message = "مشتری ثبت شد.";
                await ReloadAsync(cancellationToken, keepMessage: true, select: outcome.Value!.Value.Id);
            }
            else
            {
                await ShowFailureAsync(outcome, cancellationToken);
            }
        });
        return saved;
    }

    /// <summary>Renames the selected customer. Returns true when it was saved.</summary>
    public async Task<bool> RenameSelectedAsync(string? name, CancellationToken cancellationToken = default)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            if (_selected is null)
            {
                Message = Messages.NothingSelected;
                return;
            }

            var outcome = await customers.RenameAsync(currentUser(), _selected.Id, _selected.Version, name, cancellationToken);
            if (outcome.Succeeded)
            {
                saved = true;
                Message = "نام مشتری تغییر کرد.";
                await ReloadAsync(cancellationToken, keepMessage: true, select: _selected.Id);
            }
            else
            {
                await ShowFailureAsync(outcome, cancellationToken);
            }
        });
        return saved;
    }

    /// <summary>Deactivates or reactivates the selected customer (customers are never deleted).</summary>
    public async Task<bool> SetSelectedActiveAsync(bool isActive, CancellationToken cancellationToken = default)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            if (_selected is null)
            {
                Message = Messages.NothingSelected;
                return;
            }

            var outcome = await customers.SetActiveAsync(currentUser(), _selected.Id, _selected.Version, isActive, cancellationToken);
            if (outcome.Succeeded)
            {
                saved = true;
                Message = isActive ? "مشتری فعال شد." : "مشتری غیرفعال شد.";
                await ReloadAsync(cancellationToken, keepMessage: true, select: _selected.Id);
            }
            else
            {
                await ShowFailureAsync(outcome, cancellationToken);
            }
        });
        return saved;
    }

    /// <summary>Shows a refusal. After a conflict or a vanished record the list is reloaded, so the next attempt starts from the current data.</summary>
    private async Task ShowFailureAsync<T>(Outcome<T> outcome, CancellationToken cancellationToken)
    {
        var text = Messages.For(outcome);
        if (outcome.Status is OperationStatus.ConcurrencyConflict or OperationStatus.NotFound)
        {
            await ReloadAsync(cancellationToken, keepMessage: true);
        }

        Message = text;
    }

    private async Task ReloadAsync(CancellationToken cancellationToken, bool keepMessage = false, long? select = null)
    {
        if (!keepMessage)
        {
            Message = string.Empty;
        }

        _capabilities = await requests.GetCapabilitiesAsync(currentUser(), cancellationToken);
        OnPropertyChanged(nameof(CanMaintain));

        var outcome = await customers.ListAsync(currentUser(), activeOnly: !_showInactive, cancellationToken);
        if (!outcome.Succeeded)
        {
            _rows.Clear();
            _hasLoaded = false;
            OnPropertyChanged(nameof(IsEmpty));
            Message = Messages.For(outcome);
            return;
        }

        var keep = select ?? _selected?.Id;
        _rows.Clear();
        foreach (var stored in outcome.Value!)
        {
            _rows.Add(new CustomerRow(stored.Value.Id, stored.Value.Name, stored.Value.IsActive, stored.Version, dates.FormatDateTime(stored.Value.UpdatedAtUtc)));
        }

        Selected = _rows.FirstOrDefault(r => r.Id == keep);
        _hasLoaded = true;
        OnPropertyChanged(nameof(IsEmpty));
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
