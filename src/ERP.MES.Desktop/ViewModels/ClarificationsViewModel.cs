using System.Collections.ObjectModel;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Inquiries;

namespace ERP.MES.Desktop.ViewModels;

public sealed record ClarificationRow(long ClarificationId, long RequestId, string RequestNo, string CustomerName, string StatusText, string AskedText, string QuestionText, string AnswerText, bool IsAnswerable, bool IsClosable, long Version);

/// <summary>
/// State and actions of the clarification screen (owner decision D1, Decision Log #95). Drawing asks Planning a question about a request it is reviewing and closes its own questions; Planning
/// sees the open questions and answers them. The request status never changes; an unclosed question blocks production release (the Application enforces it). The screen mirrors the
/// Application's capabilities and makes no authorization decision.
/// </summary>
public sealed class ClarificationsViewModel(ClarificationService service, IJalaliDateService dates, Func<Principal> currentUser) : ObservableObject
{
    private readonly ObservableCollection<ClarificationRow> _rows = [];
    private ClarificationCapabilities _capabilities = ClarificationCapabilities.None;
    private ClarificationRow? _selected;
    private IReadOnlyList<Choice<long>> _requestChoices = [];
    private string _message = string.Empty;
    private bool _isBusy;
    private bool _isLoading;

    public ObservableCollection<ClarificationRow> Rows => _rows;

    public ClarificationRow? SelectedRow
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

    public IReadOnlyList<Choice<long>> RequestChoices => _requestChoices;

    public bool CanView => _capabilities.CanView;

    public bool CanAsk => _capabilities.CanAsk;

    public bool CanAnswer => _capabilities.CanAnswer && _selected is { IsAnswerable: true };

    public bool CanClose => _capabilities.CanAsk && _selected is { IsClosable: true };

    public async Task LoadAsync(CancellationToken cancellationToken = default) =>
        await RunAsync(async () =>
        {
            Message = string.Empty;
            await ReloadAsync(_selected?.ClarificationId, cancellationToken);
        }, showLoading: true);

    /// <summary>Drawing asks about a request (by its id; the request must be under Drawing review).</summary>
    public Task<bool> AskAsync(long requestId, string? question, CancellationToken cancellationToken = default) =>
        ChangeAsync(() => service.AskAsync(currentUser(), requestId, question, cancellationToken), "پرسش برای برنامه‌ریزی ثبت شد.");

    public Task<bool> AnswerSelectedAsync(string? answer, CancellationToken cancellationToken = default) =>
        _selected is { } r ? ChangeAsync(() => service.AnswerAsync(currentUser(), r.ClarificationId, r.Version, answer, cancellationToken), "پاسخ ثبت شد.") : Nothing();

    public Task<bool> CloseSelectedAsync(string? note, CancellationToken cancellationToken = default) =>
        _selected is { } r ? ChangeAsync(() => service.CloseAsync(currentUser(), r.ClarificationId, r.Version, note, cancellationToken), "پرسش بسته شد.") : Nothing();

    private Task<bool> Nothing()
    {
        Message = Messages.NothingSelected;
        return Task.FromResult(false);
    }

    private async Task<bool> ChangeAsync(Func<Task<Outcome<Stored<DrawingClarification>>>> call, string success)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            var outcome = await call();
            if (!outcome.Succeeded)
            {
                var text = Messages.For(outcome);
                if (outcome.Status != OperationStatus.NotAuthorized)
                {
                    await ReloadAsync(_selected?.ClarificationId, CancellationToken.None);
                }

                Message = text;
                return;
            }

            saved = true;
            await ReloadAsync(outcome.Value!.Value.Id, CancellationToken.None);
            Message = success;
        });
        return saved;
    }

    private async Task ReloadAsync(long? keepId, CancellationToken cancellationToken)
    {
        _capabilities = await service.GetCapabilitiesAsync(currentUser(), cancellationToken);
        var list = await service.ListUnresolvedAsync(currentUser(), cancellationToken);
        _rows.Clear();
        if (!list.Succeeded)
        {
            Message = Messages.For(list);
        }
        else
        {
            foreach (var item in list.Value!)
            {
                var c = item.Clarification.Value;
                _rows.Add(new ClarificationRow(
                    c.Id, c.RequestId, item.RequestNo, item.CustomerName, Messages.ClarificationStatusText(c.Status),
                    Messages.DepartmentText(c.AskedByDepartmentCode) + " — " + dates.FormatDateTime(c.AskedAtUtc), c.Question, c.Answer ?? string.Empty,
                    c.Status == ClarificationStatus.Open, c.Status != ClarificationStatus.Closed, item.Clarification.Version));
            }
        }

        var review = await service.ListUnderReviewAsync(currentUser(), cancellationToken);
        _requestChoices = review.Succeeded ? review.Value!.Select(r => new Choice<long>(r.RequestId, Messages.Ltr(r.RequestNo) + " — " + r.CustomerName)).ToList() : [];
        OnPropertyChanged(nameof(RequestChoices));
        _selected = _rows.FirstOrDefault(r => r.ClarificationId == keepId);
        OnPropertyChanged(nameof(SelectedRow));
        foreach (var name in new[] { nameof(CanView), nameof(CanAsk), nameof(CanAnswer), nameof(CanClose) })
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
