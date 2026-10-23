using System.Collections.ObjectModel;
using ERP.MES.Application.Imports;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>One previewed worksheet row. For a conflicting row the user picks <see cref="Choice"/>; nothing is chosen for them.</summary>
public sealed class ImportRowViewModel(ImportRowPreview preview) : ObservableObject
{
    private ConflictChoice? _choice;

    public ImportRowPreview Preview { get; } = preview;

    public int SheetRowNo => Preview.SheetRowNo;

    public string ItemNo => Preview.ItemNo ?? string.Empty;

    public string StatusText => Messages.ImportRowStatusText(Preview.Status);

    public bool NeedsChoice => Preview.Status is ImportRowStatus.Conflict or ImportRowStatus.Locked;

    public bool CanUpdate => Preview.Status == ImportRowStatus.Conflict;

    public string ChangesText => string.Join("؛ ", Preview.Changes.Select(c => $"{Messages.ImportFieldText(c.Field)}: {c.Old ?? "—"} ← {c.New ?? "—"}"));

    public string MessagesText => string.Join(" | ", Preview.Messages.Select(Messages.ImportMessageText));

    public ConflictChoice? Choice
    {
        get => _choice;
        set => SetProperty(ref _choice, value);
    }

    /// <summary>The choices offered for this row: Skip always, Update only when the item may still be revised.</summary>
    public IReadOnlyList<Choice<ConflictChoice>> Choices { get; } = preview.Status == ImportRowStatus.Conflict
        ? [new(ConflictChoice.Skip, "رد کردن"), new(ConflictChoice.Update, "به‌روزرسانی")]
        : [new(ConflictChoice.Skip, "رد کردن")];
}

/// <summary>
/// State and actions of the item-import screen (Decision Log #16, #97). The user picks a request and an .xlsx file; the screen shows the preview (counts, row messages, conflicts with old and
/// new values) and, after the user chose Skip or Update for every conflict, commits. Reading, validation, conflict rules and the all-or-nothing commit are the Application\'s; this screen only
/// shows and relays. The same file is sent again with its hash at commit time.
/// </summary>
public sealed class ItemImportViewModel(ItemImportService service, RequestService requests, IWorkbookWriter templateWriter, Func<Principal> currentUser) : ObservableObject
{
    private readonly ObservableCollection<DefinitionRequestRow> _requests = [];
    private readonly ObservableCollection<ImportRowViewModel> _rows = [];
    private readonly ObservableCollection<string> _fileMessages = [];
    private DefinitionRequestRow? _selectedRequest;
    private ImportPreview? _preview;
    private string? _fileName;
    private byte[]? _content;
    private bool _canImport;
    private string _summary = string.Empty;
    private string _message = string.Empty;
    private bool _isBusy;

    public ObservableCollection<DefinitionRequestRow> Requests => _requests;

    public ObservableCollection<ImportRowViewModel> Rows => _rows;

    public ObservableCollection<string> FileMessages => _fileMessages;

    public DefinitionRequestRow? SelectedRequest
    {
        get => _selectedRequest;
        set
        {
            if (SetProperty(ref _selectedRequest, value))
            {
                ClearPreview();
            }
        }
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
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

    public bool CanImport => _canImport;

    public bool HasPreview => _preview is not null;

    public bool CanCommit => _canImport && _preview is { CanCommit: true } && _selectedRequest is not null;

    public async Task LoadAsync(CancellationToken cancellationToken = default) =>
        await RunAsync(async () =>
        {
            Message = string.Empty;
            _canImport = await service.CanImportAsync(currentUser(), cancellationToken);
            var list = await requests.ListAsync(currentUser(), null, cancellationToken);
            var keep = _selectedRequest?.RequestId;
            _requests.Clear();
            if (list.Succeeded)
            {
                foreach (var r in list.Value!)
                {
                    _requests.Add(new DefinitionRequestRow(r.RequestId, r.RequestNo.Value, r.CustomerName, Messages.WorkTypeText(r.WorkType), Messages.StatusText(r.Status), r.ItemCount));
                }
            }
            else
            {
                Message = Messages.For(list);
            }

            _selectedRequest = _requests.FirstOrDefault(r => r.RequestId == keep);
            OnPropertyChanged(nameof(SelectedRequest));
            Notify();
        });

    /// <summary>The canonical template as an .xlsx file (headers and two fictional example rows).</summary>
    public byte[] BuildTemplate() => templateWriter.Write("آیتم‌ها", ItemImportTemplate.Headers, ItemImportTemplate.ExampleRows);

    public async Task<bool> PreviewAsync(string? fileName, byte[]? content, CancellationToken cancellationToken = default)
    {
        var ok = false;
        await RunAsync(async () =>
        {
            if (_selectedRequest is not { } request)
            {
                Message = Messages.NothingSelected;
                return;
            }

            ClearPreview();
            var outcome = await service.PreviewAsync(currentUser(), request.RequestId, fileName, content, cancellationToken);
            if (!outcome.Succeeded)
            {
                Message = Messages.For(outcome);
                return;
            }

            _fileName = fileName;
            _content = content;
            _preview = outcome.Value;
            foreach (var m in _preview!.FileMessages)
            {
                _fileMessages.Add((m.IsError ? "خطا: " : "توجه: ") + Messages.ImportMessageText(m));
            }

            foreach (var row in _preview.Rows)
            {
                _rows.Add(new ImportRowViewModel(row));
            }

            Summary = $"ردیف‌های شناسایی‌شده: {_preview.Detected} — جدید: {_preview.NewCount} — بدون تغییر: {_preview.UnchangedCount} — تعارض: {_preview.ConflictCount} — خطا: {_preview.InvalidCount}";
            Message = _preview.CanCommit ? "پیش‌نمایش آماده است. برای هر تعارض یکی از گزینه‌ها را انتخاب کنید و سپس ثبت کنید." : "فایل خطا دارد و ثبت نمی‌شود؛ خطاها را اصلاح و دوباره پیش‌نمایش بگیرید.";
            ok = true;
            Notify();
        });
        return ok;
    }

    public async Task<bool> CommitAsync(CancellationToken cancellationToken = default)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            if (_selectedRequest is not { } request || _preview is not { } preview)
            {
                Message = Messages.NothingSelected;
                return;
            }

            var decisions = _rows.Where(r => r.NeedsChoice && r.Choice is not null).Select(r => new ImportDecision(r.SheetRowNo, r.Choice!.Value)).ToList();
            var outcome = await service.CommitAsync(currentUser(), request.RequestId, preview.RequestVersion, _fileName, _content, preview.FileHash, decisions, cancellationToken);
            if (!outcome.Succeeded)
            {
                Message = Messages.For(outcome);
                if (outcome.Status is OperationStatus.ConcurrencyConflict or OperationStatus.InvalidState)
                {
                    ClearPreview();
                }

                return;
            }

            var result = outcome.Value!;
            saved = true;
            ClearPreview();
            Message = result.ChangedAnything
                ? $"ورود انجام شد: {result.Created} آیتم جدید، {result.Updated} به‌روزرسانی، {result.Unchanged} بدون تغییر، {result.Skipped} ردشده."
                : $"تغییری لازم نبود: {result.Unchanged} ردیف بدون تغییر، {result.Skipped} ردشده. چیزی ذخیره نشد.";
            Notify();
        });
        return saved;
    }

    private void ClearPreview()
    {
        _preview = null;
        _fileName = null;
        _content = null;
        _rows.Clear();
        _fileMessages.Clear();
        Summary = string.Empty;
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(CanCommit));
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
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
            IsBusy = false;
        }
    }
}
