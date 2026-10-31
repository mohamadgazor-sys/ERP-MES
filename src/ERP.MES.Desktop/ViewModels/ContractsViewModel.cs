using System.Collections.ObjectModel;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Contracts;

namespace ERP.MES.Desktop.ViewModels;

public sealed record ContractListRow(long RequestId, string RequestNo, string CustomerName, string StatusText, string ContractNoText, string DateText);

public sealed record ContractVersionRow(int VersionNo, string ContractNo, string DateText, string StateText, string PdfText, string DecisionText, string DecisionNoteText);

public sealed record ItemContractRow(long ItemId, string ItemNo, string PartName, string EffectiveText, string ReasonText, bool IsInProduction) : IHasItemId;

public sealed record ContractHistoryRow(string DateText, string ActionText, string StatusChangeText, string DepartmentText, string NoteText, string ActorText = "");

/// <summary>
/// State and actions of the contract screen (Decision Log #85): the MAKE requests that Drawing returned or that already have a contract. Planning decides whether a contract is required,
/// prepares and submits it with its PDF; Management approves or rejects (a reason is needed to reject); Warehouse only reads. The screen mirrors what the Application allows and every
/// command is checked again there. A rejection returns the contract to Planning for a new version (a PROVISIONAL default routing). Approval is shown in this list to Planning and Warehouse;
/// there is no separate notification mechanism yet.
/// </summary>
public sealed class ContractsViewModel(ContractService service, RequestGateService gates, IJalaliDateService dates, Func<Principal> currentUser, UserDirectoryService? users = null) : ObservableObject
{
    private readonly ObservableCollection<ContractListRow> _rows = [];
    private readonly ObservableCollection<ContractVersionRow> _versions = [];
    private readonly ObservableCollection<ContractHistoryRow> _history = [];
    private readonly ObservableCollection<ItemContractRow> _itemRows = [];
    private ItemContractRow? _selectedItem;
    private ContractCapabilities _capabilities = ContractCapabilities.None;
    private ContractDetail? _detail;
    private ProductionGateStatus? _readiness;
    private ContractListRow? _selected;
    private string _message = string.Empty;
    private bool _isBusy;
    private bool _isLoading;

    public ObservableCollection<ContractListRow> Rows => _rows;

    public ObservableCollection<ContractVersionRow> Versions => _versions;

    public ObservableCollection<ContractHistoryRow> History => _history;

    /// <summary>The request's items with their contract decision (Decision Log #111): "required" follows the request's contract, "not required" releases the item from it.</summary>
    public ObservableCollection<ItemContractRow> ItemRows => _itemRows;

    public ItemContractRow? SelectedItemRow
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                OnPropertyChanged(nameof(CanDecideItem));
            }
        }
    }

    /// <summary>Planning may decide an item once the request itself has a contract decision and the item's stages are not yet released.</summary>
    public bool CanDecideItem => _capabilities.CanPrepare && Eligible && _detail?.Contract is not null && _selectedItem is { IsInProduction: false };

    public ContractListRow? SelectedRow
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

    public bool HasDetail => _detail is not null;

    public string DetailHeading => _detail is null ? string.Empty : $"درخواست {Messages.Ltr(_detail.RequestNo)} — {_detail.CustomerName}";

    public string StatusLine => _detail is null ? string.Empty : "وضعیت قرارداد: " + (_detail.Contract is { } c ? Messages.ContractStatusText(c.Value.Status) : "تصمیم‌گیری نشده");

    public string GateLine => _readiness is null
        ? string.Empty
        : (_readiness.Satisfied
            ? "دروازه‌های مدیریت، نقشه‌کشی، قرارداد، استعلام مواد و پرسش‌ها برقرار است"
            : "مانع: " + (_readiness.DefinitionBlock != ERP.MES.Domain.Contracts.DefinitionBlock.None
                ? Messages.DefinitionBlockText(_readiness.DefinitionBlock)
                : string.Join("؛ ", _readiness.Blockers.Select(Messages.GateBlockerText))))
        + " — توجه: بررسی تعریف اقلام هنوز پیاده‌سازی نشده و این به معنای آزادسازی تولید نیست.";

    public bool CanView => _capabilities.CanView;

    private ContractStatus? Status => _detail?.Contract?.Value.Status;

    private bool Eligible => _detail?.Eligibility.Check() == ContractIneligibility.None;

    public bool CanDecide => _capabilities.CanPrepare && Eligible && _detail?.Contract is null;

    public bool CanChangeRequirement =>
        _capabilities.CanPrepare && Eligible && _detail?.Contract is { } c
        && (c.Value.Status == ContractStatus.NotRequired || (c.Value.Status == ContractStatus.Draft && c.Value.Versions.All(v => v.State != ContractVersionState.Submitted)));

    public bool CanEditDraft => _capabilities.CanPrepare && Eligible && Status == ContractStatus.Draft;

    public bool CanStartRevision => _capabilities.CanPrepare && Eligible && Status == ContractStatus.RejectedAtPlanning;

    public bool CanDecideContract => _capabilities.CanDecide && Eligible && Status == ContractStatus.PendingManagement;

    public bool CanViewPdf => _detail?.Contract?.Value.CurrentVersion?.CurrentPdf is not null || _detail?.Contract?.Value.Versions.Any(v => v.CurrentPdf is not null) == true;

    public async Task LoadAsync(CancellationToken cancellationToken = default) =>
        await RunAsync(async () =>
        {
            Message = string.Empty;
            await ReloadAsync(_selected?.RequestId, cancellationToken);
        }, showLoading: true);

    public async Task SelectAsync(long requestId, CancellationToken cancellationToken = default) =>
        await RunAsync(async () =>
        {
            _selected = _rows.FirstOrDefault(r => r.RequestId == requestId);
            OnPropertyChanged(nameof(SelectedRow));
            await LoadDetailAsync(requestId, cancellationToken);
        });

    public Task<bool> DecideAsync(bool required, string? note, CancellationToken cancellationToken = default) =>
        ChangeAsync((id, _) => service.DecideRequirementAsync(currentUser(), id, required ? ContractRequirement.Required : ContractRequirement.NotRequired, note, cancellationToken), "تصمیم ثبت شد.");

    public Task<bool> ChangeRequirementAsync(bool required, string? reason, CancellationToken cancellationToken = default) =>
        ChangeAsync((id, v) => service.ChangeRequirementAsync(currentUser(), id, v, required ? ContractRequirement.Required : ContractRequirement.NotRequired, reason, cancellationToken), "تصمیم تغییر کرد.");

    /// <summary>Planning decides the selected item's contract requirement with a reason (Decision Log #111).</summary>
    public async Task<bool> DecideItemAsync(bool required, string? reason, CancellationToken cancellationToken = default)
    {
        if (_detail is null || _selectedItem is null)
        {
            Message = Messages.NothingSelected;
            return false;
        }

        var saved = false;
        var requestId = _detail.RequestId;
        var itemId = _selectedItem.ItemId;
        await RunAsync(async () =>
        {
            var outcome = await service.DecideItemAsync(currentUser(), requestId, itemId, required ? ContractRequirement.Required : ContractRequirement.NotRequired, reason, cancellationToken);
            if (!outcome.Succeeded)
            {
                var text = Messages.For(outcome);
                if (outcome.Status != OperationStatus.NotAuthorized)
                {
                    await ReloadAsync(requestId, CancellationToken.None);
                }

                Message = text;
                return;
            }

            saved = true;
            await ReloadAsync(requestId, CancellationToken.None);
            Message = "تصمیم قرارداد آیتم ثبت شد.";
        });
        return saved;
    }

    public Task<bool> SaveDraftAsync(string? contractNo, string? dateText, string? title, string? note, CancellationToken cancellationToken = default) =>
        ChangeAsync((id, v) => service.SaveDraftAsync(currentUser(), id, v, contractNo, dateText, title, note, cancellationToken), "پیش‌نویس ذخیره شد.");

    public Task<bool> AttachPdfAsync(string? fileName, byte[]? content, CancellationToken cancellationToken = default) =>
        ChangeAsync((id, v) => service.AttachPdfAsync(currentUser(), id, v, fileName, content, cancellationToken), "فایل PDF پیوست شد.");

    public Task<bool> SubmitAsync(CancellationToken cancellationToken = default) =>
        ChangeAsync((id, v) => service.SubmitAsync(currentUser(), id, v, cancellationToken), "قرارداد برای مدیریت ارسال شد.");

    public Task<bool> StartRevisionAsync(CancellationToken cancellationToken = default) =>
        ChangeAsync((id, v) => service.StartRevisionAsync(currentUser(), id, v, cancellationToken), "نسخهٔ جدید برای اصلاح ایجاد شد.");

    public Task<bool> ApproveAsync(string? note, CancellationToken cancellationToken = default) =>
        ChangeAsync((id, v) => service.ApproveAsync(currentUser(), id, v, note, cancellationToken), "قرارداد تأیید شد.");

    public Task<bool> RejectAsync(string? reason, CancellationToken cancellationToken = default) =>
        ChangeAsync((id, v) => service.RejectAsync(currentUser(), id, v, reason, cancellationToken), "قرارداد رد شد و برای اصلاح به برنامه‌ریزی بازگشت.");

    public void ReportPdfTooLarge() => Message = Messages.ForIssue(ValidationIssue.PdfTooLarge, JalaliDateError.None);

    /// <summary>The PDF of the newest version that has one (or of <paramref name="versionNo"/>), for viewing.</summary>
    public async Task<ContractPdfFile?> LoadPdfAsync(int? versionNo = null, CancellationToken cancellationToken = default)
    {
        ContractPdfFile? file = null;
        await RunAsync(async () =>
        {
            if (_detail?.Contract?.Value is not { } contract)
            {
                Message = Messages.NothingSelected;
                return;
            }

            var number = versionNo ?? contract.Versions.LastOrDefault(v => v.CurrentPdf is not null)?.VersionNo;
            if (number is null)
            {
                Message = Messages.NothingSelected;
                return;
            }

            var outcome = await service.GetPdfAsync(currentUser(), _detail.RequestId, number.Value, cancellationToken);
            if (outcome.Succeeded)
            {
                file = outcome.Value;
            }
            else
            {
                Message = Messages.For(outcome);
            }
        });
        return file;
    }

    private async Task<bool> ChangeAsync(Func<long, long, Task<Outcome<Stored<RequestContract>>>> call, string success)
    {
        if (_detail is null)
        {
            Message = Messages.NothingSelected;
            return false;
        }

        var saved = false;
        var requestId = _detail.RequestId;
        var version = _detail.Contract?.Version ?? 0;
        await RunAsync(async () =>
        {
            var outcome = await call(requestId, version);
            if (!outcome.Succeeded)
            {
                var text = Messages.For(outcome);

                // Anything but a permission refusal may mean the data changed under the user: show the current data so they see what happened.
                if (outcome.Status != OperationStatus.NotAuthorized)
                {
                    await ReloadAsync(requestId, CancellationToken.None);
                }

                Message = text;
                return;
            }

            saved = true;
            await ReloadAsync(requestId, CancellationToken.None);
            Message = success;
        });
        return saved;
    }

    private async Task ReloadAsync(long? keepRequestId, CancellationToken cancellationToken)
    {
        _capabilities = await service.GetCapabilitiesAsync(currentUser(), cancellationToken);
        var list = await service.ListAsync(currentUser(), cancellationToken);
        _rows.Clear();
        if (!list.Succeeded)
        {
            ClearDetail();
            Message = Messages.For(list);
            NotifyAll();
            return;
        }

        foreach (var item in list.Value!)
        {
            _rows.Add(new ContractListRow(
                item.RequestId, item.RequestNo, item.CustomerName, item.ContractStatus is { } s ? Messages.ContractStatusText(s) : "تصمیم‌گیری نشده",
                item.ContractNo ?? string.Empty, item.ContractDate is { } d ? dates.FormatDate(d) : string.Empty));
        }

        _selected = _rows.FirstOrDefault(r => r.RequestId == keepRequestId);
        OnPropertyChanged(nameof(SelectedRow));
        if (keepRequestId is { } id && _selected is not null)
        {
            await LoadDetailAsync(id, cancellationToken);
        }
        else
        {
            ClearDetail();
            NotifyAll();
        }
    }

    private async Task LoadDetailAsync(long requestId, CancellationToken cancellationToken)
    {
        var detail = await service.GetAsync(currentUser(), requestId, cancellationToken);
        var readiness = await gates.GetAsync(currentUser(), requestId, cancellationToken);
        if (!detail.Succeeded)
        {
            ClearDetail();
            Message = Messages.For(detail);
            NotifyAll();
            return;
        }

        _detail = detail.Value;
        _readiness = readiness.Value;
        _versions.Clear();
        _history.Clear();
        var decisions = await service.ListItemDecisionsAsync(currentUser(), requestId, cancellationToken);
        var latest = (decisions.Value ?? []).GroupBy(d => d.ItemId).ToDictionary(g => g.Key, g => g.Last());
        var keep = _selectedItem?.ItemId;
        _itemRows.Clear();
        foreach (var item in _detail!.Items)
        {
            latest.TryGetValue(item.Id, out var decision);
            var requirement = decision?.Requirement ?? _detail.Contract?.Value.Requirement;
            _itemRows.Add(new ItemContractRow(
                item.Id, item.ItemNo, item.PartName,
                requirement is null ? "تصمیم درخواست ثبت نشده" : requirement == ContractRequirement.Required ? "قرارداد لازم است" : "قرارداد لازم نیست",
                decision is null ? (_detail.Contract is null ? string.Empty : "مطابق تصمیم درخواست") : decision.Reason, item.HasReleasedStage));
        }

        _selectedItem = _itemRows.FirstOrDefault(r => r.ItemId == keep);
        OnPropertyChanged(nameof(SelectedItemRow));
        if (_detail!.Contract?.Value is { } contract)
        {
            foreach (var v in contract.Versions)
            {
                _versions.Add(new ContractVersionRow(
                    v.VersionNo, v.ContractNo ?? string.Empty, v.ContractDate is { } d ? dates.FormatDate(d) : string.Empty, Messages.ContractVersionStateText(v.State),
                    Messages.Ltr(v.CurrentPdf?.FileName) is { Length: > 0 } pdfName ? pdfName : "—", v.Decision is { } decision ? (decision == ContractDecision.Approved ? "تأیید" : "رد") + (v.DecidedAtUtc is { } at ? " (" + dates.FormatDateTime(at) + ")" : string.Empty) : string.Empty,
                    v.DecisionNote ?? string.Empty));
            }

            var actors = await ActorLookup.DescribeAsync(users, currentUser(), contract.History.Select(h => h.ActionByUserId));
            foreach (var h in contract.History.Reverse())
            {
                _history.Add(new ContractHistoryRow(
                    dates.FormatDateTime(h.ActionAtUtc), Messages.ContractActionText(h.Action),
                    (h.FromStatus is { } from ? Messages.ContractStatusText(from) + " ← " : string.Empty) + Messages.ContractStatusText(h.ToStatus) + (h.VersionNo is { } n ? $" (نسخه {n})" : string.Empty),
                    Messages.DepartmentText(h.ActionByDepartmentCode), h.Note ?? string.Empty, ActorLookup.Text(actors, h.ActionByUserId)));
            }
        }

        NotifyAll();
    }

    private void ClearDetail()
    {
        _detail = null;
        _readiness = null;
        _versions.Clear();
        _history.Clear();
        _itemRows.Clear();
        _selectedItem = null;
    }

    private void NotifyAll()
    {
        foreach (var name in new[]
                 {
                     nameof(HasDetail), nameof(DetailHeading), nameof(StatusLine), nameof(GateLine), nameof(CanView), nameof(CanDecide), nameof(CanChangeRequirement), nameof(CanEditDraft),
                     nameof(CanStartRevision), nameof(CanDecideContract), nameof(CanViewPdf), nameof(CanDecideItem),
                 })
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
}
