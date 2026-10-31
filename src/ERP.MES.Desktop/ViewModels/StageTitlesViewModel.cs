using System.Collections.ObjectModel;
using ERP.MES.Application.Planning;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Planning;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>One catalog entry as shown in the grid.</summary>
public sealed record StageTitleRow(long TitleId, long Version, string Title, bool IsActive)
{
    public string StatusText => IsActive ? "فعال" : "بازنشسته";
}

/// <summary>
/// The stage-title catalog screen (Decision Log #124 B4): Planning adds titles and retires or reactivates them; stages keep their own copy of the title, so retiring never changes an existing stage.
/// Every user who may read the Planning master data sees the list; <see cref="PlanningMasterService"/> checks the Planning permission again on each change.
/// </summary>
public sealed class StageTitlesViewModel(PlanningMasterService planning, Func<Principal> currentUser) : ObservableObject
{
    private readonly ObservableCollection<StageTitleRow> _titles = [];
    private StageTitleRow? _selected;
    private string _message = string.Empty;
    private bool _isError;
    private bool _canMaintain;
    private bool _isBusy;

    public ObservableCollection<StageTitleRow> Titles => _titles;

    public StageTitleRow? Selected
    {
        get => _selected;
        set => SetProperty(ref _selected, value);
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

    /// <summary>True when the signed-in user is Planning (buttons are enabled; the Application checks again).</summary>
    public bool CanMaintain
    {
        get => _canMaintain;
        private set => SetProperty(ref _canMaintain, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default) => await RunAsync(() => ReloadAsync(cancellationToken));

    public async Task<bool> AddAsync(string? title, CancellationToken cancellationToken = default)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            var outcome = await planning.AddStageTitleAsync(currentUser(), title, cancellationToken);
            if (!outcome.Succeeded)
            {
                Fail(Messages.For(outcome));
                return;
            }

            saved = true;
            await ReloadAsync(cancellationToken);
            Succeed("عنوان به فهرست اضافه شد.");
        });
        return saved;
    }

    public async Task<bool> SetSelectedActiveAsync(bool isActive, CancellationToken cancellationToken = default)
    {
        if (_selected is not { } row)
        {
            Fail(Messages.NothingSelected);
            return false;
        }

        var saved = false;
        await RunAsync(async () =>
        {
            var outcome = await planning.SetStageTitleActiveAsync(currentUser(), row.TitleId, row.Version, isActive, cancellationToken);
            if (!outcome.Succeeded)
            {
                Fail(Messages.For(outcome));
                await ReloadAsync(cancellationToken);
                return;
            }

            saved = true;
            await ReloadAsync(cancellationToken);
            Succeed(isActive ? "عنوان دوباره فعال شد." : "عنوان بازنشسته شد؛ مرحله‌های موجود تغییری نمی‌کنند.");
        });
        return saved;
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        var selectedId = _selected?.TitleId;
        CanMaintain = await planning.CanMaintainStageTitlesAsync(currentUser(), cancellationToken);
        var outcome = await planning.ListStageTitlesAsync(currentUser(), cancellationToken);
        _titles.Clear();
        if (!outcome.Succeeded)
        {
            Fail(Messages.For(outcome));
            return;
        }

        foreach (var stored in outcome.Value!)
        {
            _titles.Add(new StageTitleRow(stored.Value.Id, stored.Version, stored.Value.Title, stored.Value.IsActive));
        }

        Selected = selectedId is { } id ? _titles.FirstOrDefault(t => t.TitleId == id) : null;
    }

    private void Succeed(string text)
    {
        IsErrorMessage = false;
        Message = text;
    }

    private void Fail(string text)
    {
        IsErrorMessage = true;
        Message = text;
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
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
            Fail(Messages.Unexpected);
        }
        finally
        {
            _isBusy = false;
        }
    }
}
