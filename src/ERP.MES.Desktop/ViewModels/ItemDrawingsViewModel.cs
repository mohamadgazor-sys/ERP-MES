using System.IO;
using System.Collections.ObjectModel;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>One request in the drawings screen's list.</summary>
public sealed record DrawingRequestRow(long RequestId, string RequestNo, string CustomerName)
{
    public string Text => RequestNo + " — " + CustomerName;
}

/// <summary>One item in the drawings screen's list, with its current drawing state.</summary>
public sealed record DrawingItemRow(long ItemId, string ItemNo, string PartName, string DrawingText);

/// <summary>One revision of the selected item's drawing.</summary>
public sealed record DrawingRevisionRow(int Revision, string FileName, string SizeText, string UploadedText, string StatusText);

/// <summary>
/// Opens an item's drawing on request (Decision Log #125 Q14): the PDF is read from the database only when somebody asks, written to a temporary file and handed to the viewer by the caller. It
/// never runs on its own, so a list never loads a drawing.
/// </summary>
public static class DrawingOpening
{
    /// <summary>Reads the current drawing and writes it under <paramref name="folder"/>. Returns the path, or a Persian message why there is none.</summary>
    public static async Task<(string? Path, string Message)> PrepareAsync(ItemDrawingService drawings, Principal? principal, long itemId, int? revision, string folder, CancellationToken cancellationToken = default)
    {
        var outcome = await drawings.OpenAsync(principal, itemId, revision, cancellationToken);
        if (!outcome.Succeeded)
        {
            return (null, outcome.Status switch
            {
                OperationStatus.NotFound => "برای این آیتم نقشه‌ای بارگذاری نشده است.",
                OperationStatus.NotAuthorized => Messages.For(outcome),
                _ => Messages.For(outcome),
            });
        }

        var file = outcome.Value!;
        Directory.CreateDirectory(folder);
        var name = $"item-{itemId}-r{file.Info.Revision}-{file.Info.ContentHash[..8]}.pdf";
        var path = Path.Combine(folder, name);
        if (!File.Exists(path))
        {
            await File.WriteAllBytesAsync(path, file.Content, cancellationToken);
        }

        return (path, string.Empty);
    }
}

/// <summary>
/// The drawings screen (Decision Log #125 Q14): pick a request and an item, see the drawing revisions, open one explicitly, and (Drawing only) upload or replace the PDF. It relays to
/// <see cref="ItemDrawingService"/>, which checks the Drawing permission, the PDF rules and the size again.
/// </summary>
public sealed class ItemDrawingsViewModel(RequestService requests, ItemDrawingService drawings, IJalaliDateService dates, Func<Principal> currentUser) : ObservableObject
{
    private readonly ObservableCollection<DrawingRequestRow> _requests = [];
    private readonly ObservableCollection<DrawingItemRow> _items = [];
    private readonly ObservableCollection<DrawingRevisionRow> _revisions = [];
    private DrawingRequestRow? _selectedRequest;
    private DrawingItemRow? _selectedItem;
    private string _message = string.Empty;
    private bool _isError;
    private bool _canManage;
    private bool _isBusy;

    public ObservableCollection<DrawingRequestRow> Requests => _requests;

    public ObservableCollection<DrawingItemRow> Items => _items;

    public ObservableCollection<DrawingRevisionRow> Revisions => _revisions;

    public DrawingRequestRow? SelectedRequest
    {
        get => _selectedRequest;
        set => SetProperty(ref _selectedRequest, value);
    }

    public DrawingItemRow? SelectedItem
    {
        get => _selectedItem;
        set => SetProperty(ref _selectedItem, value);
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

    /// <summary>True for Drawing (the upload buttons are enabled; the Application checks again).</summary>
    public bool CanManage
    {
        get => _canManage;
        private set => SetProperty(ref _canManage, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default) => await RunAsync(async () =>
    {
        CanManage = await drawings.CanManageAsync(currentUser(), cancellationToken);
        var outcome = await requests.ListAsync(currentUser(), null, cancellationToken);
        _requests.Clear();
        if (!outcome.Succeeded)
        {
            Fail(Messages.For(outcome));
            return;
        }

        foreach (var summary in outcome.Value!)
        {
            _requests.Add(new DrawingRequestRow(summary.RequestId, summary.RequestNo.Value, summary.CustomerName));
        }
    });

    public async Task SelectRequestAsync(DrawingRequestRow? request, CancellationToken cancellationToken = default) => await RunAsync(async () =>
    {
        SelectedRequest = request;
        _items.Clear();
        _revisions.Clear();
        SelectedItem = null;
        if (request is null)
        {
            return;
        }

        var detail = await requests.GetAsync(currentUser(), request.RequestId, cancellationToken);
        if (!detail.Succeeded)
        {
            Fail(Messages.For(detail));
            return;
        }

        var items = detail.Value!.Request.Value.Items;
        var current = await drawings.ListCurrentAsync(currentUser(), items.Select(i => i.Id).ToList(), cancellationToken);
        var byItem = current.Succeeded ? current.Value!.ToDictionary(d => d.ItemId) : [];
        foreach (var item in items.OrderBy(i => i.ItemNo, StringComparer.Ordinal))
        {
            _items.Add(new DrawingItemRow(item.Id, item.ItemNo, item.PartName, byItem.TryGetValue(item.Id, out var info) ? $"نسخه {info.Revision}: {info.FileName}" : "بدون نقشه"));
        }
    });

    public async Task SelectItemAsync(DrawingItemRow? item, CancellationToken cancellationToken = default) => await RunAsync(async () =>
    {
        SelectedItem = item;
        await ReloadRevisionsAsync(cancellationToken);
    });

    /// <summary>Uploads the first drawing of the selected item or replaces its current one. <paramref name="content"/> is what the view read from the chosen file.</summary>
    public async Task<bool> UploadAsync(string? fileName, byte[]? content, CancellationToken cancellationToken = default)
    {
        if (_selectedItem is not { } item)
        {
            Fail(Messages.NothingSelected);
            return false;
        }

        var saved = false;
        await RunAsync(async () =>
        {
            var outcome = await drawings.UploadAsync(currentUser(), item.ItemId, fileName, content, cancellationToken);
            if (!outcome.Succeeded)
            {
                Fail(Messages.For(outcome));
                return;
            }

            saved = true;
            await RefreshSelectedAsync(item.ItemId, cancellationToken);
            Succeed($"نقشه ذخیره شد (نسخهٔ {outcome.Value!.Revision}). نسخه‌های قبلی در تاریخچه می‌ماند.");
        });
        return saved;
    }

    /// <summary>Tells the user a chosen file is over the size limit without reading it.</summary>
    public void ReportTooLarge() => Fail(Messages.ForIssue(ValidationIssue.PdfTooLarge, JalaliDateError.None));

    /// <summary>The path of the selected item's drawing (current, or <paramref name="revision"/>) in a temporary folder, ready for the viewer; null with a message when there is none.</summary>
    public async Task<string?> PrepareSelectedAsync(int? revision, string folder, CancellationToken cancellationToken = default)
    {
        if (_selectedItem is not { } item)
        {
            Fail(Messages.NothingSelected);
            return null;
        }

        string? path = null;
        await RunAsync(async () =>
        {
            var prepared = await DrawingOpening.PrepareAsync(drawings, currentUser(), item.ItemId, revision, folder, cancellationToken);
            if (prepared.Path is null)
            {
                Fail(prepared.Message);
                return;
            }

            path = prepared.Path;
            Succeed("نقشه باز شد.");
        });
        return path;
    }

    private async Task RefreshSelectedAsync(long itemId, CancellationToken cancellationToken)
    {
        var index = _items.ToList().FindIndex(i => i.ItemId == itemId);
        var current = await drawings.ListCurrentAsync(currentUser(), [itemId], cancellationToken);
        if (index >= 0 && current.Succeeded)
        {
            var row = _items[index];
            var info = current.Value!.FirstOrDefault();
            _items[index] = row with { DrawingText = info is null ? "بدون نقشه" : $"نسخه {info.Revision}: {info.FileName}" };
            SelectedItem = _items[index];
        }

        await ReloadRevisionsAsync(cancellationToken);
    }

    private async Task ReloadRevisionsAsync(CancellationToken cancellationToken)
    {
        _revisions.Clear();
        if (_selectedItem is not { } item)
        {
            return;
        }

        var outcome = await drawings.ListRevisionsAsync(currentUser(), item.ItemId, cancellationToken);
        if (!outcome.Succeeded)
        {
            Fail(Messages.For(outcome));
            return;
        }

        foreach (var revision in outcome.Value!)
        {
            _revisions.Add(new DrawingRevisionRow(revision.Revision, revision.FileName, SizeText(revision.ContentLength), dates.FormatDateTime(revision.UploadedAtUtc), revision.IsCurrent ? "فعلی" : "قدیمی"));
        }
    }

    private static string SizeText(long bytes) => bytes >= 1024 * 1024 ? (bytes / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB" : Math.Max(1, bytes / 1024).ToString(System.Globalization.CultureInfo.InvariantCulture) + " KB";

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
