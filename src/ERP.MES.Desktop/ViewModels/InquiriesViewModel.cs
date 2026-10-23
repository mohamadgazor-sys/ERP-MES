using System.Collections.ObjectModel;
using ERP.MES.Application.Persistence;
using ERP.MES.Application.Quality;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Inquiries;

namespace ERP.MES.Desktop.ViewModels;

public sealed record InquiryRequestRow(long RequestId, string RequestNo, string CustomerName, string RequestStatusText, string InquiryText);

public sealed record InquiryRow(long InquiryId, int RoundNo, string StatusText, int LineCount, int AnsweredCount);

public sealed record InquiryLineRow(int LineNo, string MaterialText, string DimensionsText, string QuantityText, string AnswerText, string AnswerNoteText, string ResponderText, string DispositionText, string SupplyRouteText);

public sealed record InquiryHistoryRow(string DateText, string ActionText, string LineText, string DepartmentText, string NoteText);

/// <summary>
/// State and actions of the raw-material inquiry screen (Decision Log #86, #88). Drawing creates, fills, sends, closes and cancels; Warehouse answers each line Yes or No; both read.
/// The inquiry is information only: it changes no request status and blocks nothing. The clarification thread and the effect of a "No" wait for owner decision D1 and are not offered.
/// The screen mirrors what the Application allows; every command is checked again there.
/// </summary>
public sealed class InquiriesViewModel(InquiryService service, QcMasterService qc, RequestGateService gates, IJalaliDateService dates, Func<Principal> currentUser) : ObservableObject
{
    private readonly ObservableCollection<InquiryRequestRow> _requests = [];
    private readonly ObservableCollection<InquiryRow> _inquiries = [];
    private readonly ObservableCollection<InquiryLineRow> _lines = [];
    private readonly ObservableCollection<InquiryHistoryRow> _history = [];
    private IReadOnlyList<Choice<long>> _unitChoices = [];
    private InquiryCapabilities _capabilities = InquiryCapabilities.None;
    private InquiryRequestRow? _selectedRequest;
    private InquiryRow? _selectedInquiry;
    private InquiryLineRow? _selectedLine;
    private InquiryDetail? _detail;
    private ProductionGateStatus? _gate;
    private IReadOnlyList<Choice<long>> _roundChoices = [];
    private string _message = string.Empty;
    private bool _isBusy;
    private bool _isLoading;

    public ObservableCollection<InquiryRequestRow> Requests => _requests;

    public ObservableCollection<InquiryRow> Inquiries => _inquiries;

    public ObservableCollection<InquiryLineRow> Lines => _lines;

    public ObservableCollection<InquiryHistoryRow> History => _history;

    public IReadOnlyList<Choice<long>> UnitChoices => _unitChoices;

    public InquiryRequestRow? SelectedRequest
    {
        get => _selectedRequest;
        set => SetProperty(ref _selectedRequest, value);
    }

    public InquiryRow? SelectedInquiry
    {
        get => _selectedInquiry;
        set => SetProperty(ref _selectedInquiry, value);
    }

    public InquiryLineRow? SelectedLine
    {
        get => _selectedLine;
        set => SetProperty(ref _selectedLine, value);
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

    public bool CanView => _capabilities.CanView;

    public bool HasInquiry => _detail is not null;

    public string InquiryHeading => _detail is null
        ? string.Empty
        : $"درخواست {_detail.RequestNo} — {_detail.CustomerName} — دور {_detail.Inquiry.Value.RoundNo} — {Messages.InquiryStatusText(_detail.Inquiry.Value.Status)}";

    private InquiryStatus? Status => _detail?.Inquiry.Value.Status;

    public bool CanCreate => _capabilities.CanManage && _selectedRequest is not null;

    public bool CanEditLines => _capabilities.CanManage && Status == InquiryStatus.Draft;

    public bool CanSend => CanEditLines && _detail!.Inquiry.Value.Lines.Count > 0;

    public bool CanClose => _capabilities.CanManage && Status == InquiryStatus.Answered;

    public bool CanCancel => _capabilities.CanManage && Status is InquiryStatus.Draft or InquiryStatus.Sent or InquiryStatus.PartiallyAnswered or InquiryStatus.Answered;

    public IReadOnlyList<Choice<long>> RoundChoices => _roundChoices;

    /// <summary>The production-gate state of the selected request (read-only information; the Application enforces the gate).</summary>
    public string GateText => _gate is null
        ? string.Empty
        : _gate.Satisfied
            ? "دروازه‌های مدیریت، نقشه‌کشی، قرارداد، استعلام و پرسش‌ها برقرار است (این به‌تنهایی آزادسازی تولید نیست)."
            : "موانع آزادسازی: " + string.Join("؛ ", _gate.Blockers.Select(Messages.GateBlockerText))
                + (_gate.HasUnresolvedMaterialState ? " — وضعیت حل‌نشده: " + $"ردیف بی‌پاسخ {_gate.UnansweredInquiryLines}، «خیر» بدون تکلیف {_gate.PendingDispositions}، پرسش باز {_gate.UnresolvedClarifications}" : string.Empty);

    public bool CanDispose => _capabilities.CanManage && Status is not (null or InquiryStatus.Draft) && _detail!.Inquiry.Value.PendingDispositionCount > 0;

    public bool CanRecordSupplyRoute => _capabilities.CanRecordSupplyRoute && Status is not (null or InquiryStatus.Draft);

    public bool CanAnswer => _capabilities.CanAnswer && Status is InquiryStatus.Sent or InquiryStatus.PartiallyAnswered or InquiryStatus.Answered;

    public async Task LoadAsync(CancellationToken cancellationToken = default) =>
        await RunAsync(async () =>
        {
            Message = string.Empty;
            await ReloadAsync(_selectedRequest?.RequestId, _selectedInquiry?.InquiryId, cancellationToken);
        }, showLoading: true);

    public async Task SelectRequestAsync(long requestId, CancellationToken cancellationToken = default) =>
        await RunAsync(async () => await ReloadAsync(requestId, null, cancellationToken));

    public async Task SelectInquiryAsync(long inquiryId, CancellationToken cancellationToken = default) =>
        await RunAsync(async () => await ReloadAsync(_selectedRequest?.RequestId, inquiryId, cancellationToken));

    public Task<bool> CreateAsync(string? note, CancellationToken cancellationToken = default) =>
        _selectedRequest is { } r
            ? ChangeAsync(() => service.CreateAsync(currentUser(), r.RequestId, note, cancellationToken), "استعلام جدید ایجاد شد.", selectResult: true)
            : Nothing();

    public Task<bool> AddLineAsync(
        string? grade, string? form, string? diameterText, string? lengthText, string? otherDimension, string? quantityText, long unitId, string? standard, string? remarks, CancellationToken cancellationToken = default)
    {
        if (!QuantityText.TryParse(quantityText, out var quantity))
        {
            Message = Messages.ForIssue(ValidationIssue.QuantityInvalid, JalaliDateError.None);
            return Task.FromResult(false);
        }

        if (!TryDimension(diameterText, out var diameter) || !TryDimension(lengthText, out var length))
        {
            Message = Messages.ForIssue(ValidationIssue.InquiryLineInvalid, JalaliDateError.None);
            return Task.FromResult(false);
        }

        return WithInquiry((id, version) => service.AddLineAsync(currentUser(), id, version, new InquiryLineInput(grade, form, diameter, length, otherDimension, quantity, unitId, standard, remarks), cancellationToken), "ردیف افزوده شد.");
    }

    public Task<bool> RemoveSelectedLineAsync(CancellationToken cancellationToken = default) =>
        _selectedLine is { } l ? WithInquiry((id, version) => service.RemoveLineAsync(currentUser(), id, version, l.LineNo, cancellationToken), "ردیف از پیش‌نویس حذف شد.") : Nothing();

    public Task<bool> SendAsync(CancellationToken cancellationToken = default) =>
        WithInquiry((id, version) => service.SendAsync(currentUser(), id, version, cancellationToken), "استعلام برای انبار ارسال شد.");

    public Task<bool> CloseAsync(string? note, CancellationToken cancellationToken = default) =>
        WithInquiry((id, version) => service.CloseAsync(currentUser(), id, version, note, cancellationToken), "استعلام پس از بررسی بسته شد.");

    public Task<bool> CancelAsync(string? reason, CancellationToken cancellationToken = default) =>
        WithInquiry((id, version) => service.CancelAsync(currentUser(), id, version, reason, cancellationToken), "استعلام لغو شد.");

    public Task<bool> AnswerSelectedLineAsync(bool yes, string? note, CancellationToken cancellationToken = default) =>
        _selectedLine is { } l
            ? WithInquiry((id, version) => service.AnswerAsync(currentUser(), id, version, l.LineNo, yes ? InquiryAnswer.Yes : InquiryAnswer.No, note, cancellationToken), "پاسخ ثبت شد.")
            : Nothing();

    private static bool TryDimension(string? text, out decimal? value)
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

    public Task<bool> DisposeSelectedLineAsync(DispositionKind kind, string? note, long? newRoundInquiryId, CancellationToken cancellationToken = default) =>
        _selectedLine is { } l
            ? WithInquiry((id, version) => service.RecordDispositionAsync(currentUser(), id, version, l.LineNo, kind, note, newRoundInquiryId, cancellationToken), "تکلیف پاسخ «خیر» ثبت شد.")
            : Nothing();

    public Task<bool> RecordSupplyRouteAsync(string? note, bool externalPurchase, CancellationToken cancellationToken = default) =>
        _selectedLine is { } l
            ? WithInquiry((id, version) => service.RecordSupplyRouteAsync(currentUser(), id, version, l.LineNo, note, externalPurchase, cancellationToken), "مسیر تأمین ثبت شد.")
            : Nothing();

    private Task<bool> Nothing()
    {
        Message = Messages.NothingSelected;
        return Task.FromResult(false);
    }

    private Task<bool> WithInquiry(Func<long, long, Task<Outcome<Stored<RawMaterialInquiry>>>> call, string success)
    {
        if (_detail is null)
        {
            return Nothing();
        }

        var id = _detail.Inquiry.Value.Id;
        var version = _detail.Inquiry.Version;
        return ChangeAsync(() => call(id, version), success, selectResult: false);
    }

    private async Task<bool> ChangeAsync(Func<Task<Outcome<Stored<RawMaterialInquiry>>>> call, string success, bool selectResult)
    {
        var saved = false;
        await RunAsync(async () =>
        {
            var requestId = _selectedRequest?.RequestId;
            var inquiryId = _detail?.Inquiry.Value.Id;
            var outcome = await call();
            if (!outcome.Succeeded)
            {
                var text = Messages.For(outcome);
                if (outcome.Status != OperationStatus.NotAuthorized)
                {
                    await ReloadAsync(requestId, inquiryId, CancellationToken.None);
                }

                Message = text;
                return;
            }

            saved = true;
            await ReloadAsync(requestId, selectResult ? outcome.Value!.Value.Id : inquiryId, CancellationToken.None);
            Message = success;
        });
        return saved;
    }

    private async Task ReloadAsync(long? keepRequestId, long? keepInquiryId, CancellationToken cancellationToken)
    {
        _capabilities = await service.GetCapabilitiesAsync(currentUser(), cancellationToken);
        var units = await qc.LoadAsync(currentUser(), cancellationToken);
        _unitChoices = units.Succeeded ? units.Value!.Units.Where(u => u.Value.IsActive).Select(u => new Choice<long>(u.Value.Id, u.Value.Code + " — " + u.Value.Name)).ToList() : [];
        OnPropertyChanged(nameof(UnitChoices));

        var list = await service.ListRequestsAsync(currentUser(), cancellationToken);
        _requests.Clear();
        _inquiries.Clear();
        ClearDetail();
        if (!list.Succeeded)
        {
            Message = Messages.For(list);
            NotifyAll();
            return;
        }

        foreach (var r in list.Value!)
        {
            _requests.Add(new InquiryRequestRow(
                r.RequestId, r.RequestNo, r.CustomerName, Messages.StatusText(r.RequestStatus),
                r.InquiryCount == 0 ? "بدون استعلام" : $"{r.InquiryCount} دور؛ آخرین: {Messages.InquiryStatusText(r.LatestStatus!.Value)}"));
        }

        _selectedRequest = _requests.FirstOrDefault(r => r.RequestId == keepRequestId);
        OnPropertyChanged(nameof(SelectedRequest));
        if (_selectedRequest is not null)
        {
            var inquiries = await service.ListForRequestAsync(currentUser(), _selectedRequest.RequestId, cancellationToken);
            if (inquiries.Succeeded)
            {
                foreach (var i in inquiries.Value!)
                {
                    _inquiries.Add(new InquiryRow(i.Value.Id, i.Value.RoundNo, Messages.InquiryStatusText(i.Value.Status), i.Value.Lines.Count, i.Value.AnsweredLineCount));
                }
            }
        }

        _gate = null;
        _roundChoices = [];
        if (_selectedRequest is not null)
        {
            var gate = await gates.GetAsync(currentUser(), _selectedRequest.RequestId, cancellationToken);
            _gate = gate.Succeeded ? gate.Value : null;
        }

        _selectedInquiry = _inquiries.FirstOrDefault(i => i.InquiryId == keepInquiryId);
        OnPropertyChanged(nameof(SelectedInquiry));
        if (_selectedInquiry is not null)
        {
            await LoadDetailAsync(_selectedInquiry.InquiryId, cancellationToken);
        }

        NotifyAll();
    }

    private async Task LoadDetailAsync(long inquiryId, CancellationToken cancellationToken)
    {
        var keepLine = _selectedLine?.LineNo;
        var outcome = await service.GetAsync(currentUser(), inquiryId, cancellationToken);
        if (!outcome.Succeeded)
        {
            Message = Messages.For(outcome);
            return;
        }

        _detail = outcome.Value;
        var units = _unitChoices.ToDictionary(c => c.Value, c => c.Text);
        foreach (var l in _detail!.Inquiry.Value.Lines)
        {
            var current = l.CurrentResponse;
            _lines.Add(new InquiryLineRow(
                l.LineNo, $"{l.MaterialGrade} / {l.MaterialForm}",
                string.Join("  ", new[] { l.DiameterMm is { } d ? $"قطر {d:0.##} mm" : null, l.LengthMm is { } len ? $"طول {len:0.##} mm" : null, l.OtherDimension }.Where(x => x is not null)),
                $"{l.RequiredQuantity:0.####} {units.GetValueOrDefault(l.QuantityUnitId, "?").Split(' ')[0]}",
                current is null ? "پاسخ داده نشده" : current.Answer == InquiryAnswer.Yes ? "بله" : "خیر",
                current?.Note ?? string.Empty,
                current is null ? string.Empty : Messages.DepartmentText(current.RespondedByDepartmentCode) + " — " + dates.FormatDateTime(current.RespondedAtUtc),
                l.CurrentDisposition is { } disposition
                    ? Messages.DispositionKindText(disposition.Kind) + (string.IsNullOrEmpty(disposition.Note) ? string.Empty : ": " + disposition.Note)
                    : current is { Answer: InquiryAnswer.No } ? "بدون تکلیف (مانع آزادسازی)" : string.Empty,
                l.CurrentSupplyRoute is { } route ? (route.ExternalPurchase ? "خرید بیرونی: " : "مسیر تأمین: ") + route.Note : string.Empty));
        }

        foreach (var h in _detail.Inquiry.Value.History.Reverse())
        {
            _history.Add(new InquiryHistoryRow(
                dates.FormatDateTime(h.ActionAtUtc), Messages.InquiryActionText(h.Action) + (h.Answer is { } a ? (a == InquiryAnswer.Yes ? " (بله)" : " (خیر)") : string.Empty),
                h.LineNo is { } n ? $"ردیف {n}" : string.Empty, Messages.DepartmentText(h.ActionByDepartmentCode), h.Note ?? string.Empty));
        }

        _selectedLine = _lines.FirstOrDefault(l => l.LineNo == keepLine);
        OnPropertyChanged(nameof(SelectedLine));
        var thisRound = _detail.Inquiry.Value.RoundNo;
        var siblings = await service.ListForRequestAsync(currentUser(), _detail.Inquiry.Value.RequestId, cancellationToken);
        _roundChoices = siblings.Succeeded
            ? siblings.Value!.Where(s => s.Value.RoundNo > thisRound && s.Value.Status != InquiryStatus.Cancelled).Select(s => new Choice<long>(s.Value.Id, $"دور {s.Value.RoundNo}")).ToList()
            : [];
    }

    private void ClearDetail()
    {
        _detail = null;
        _lines.Clear();
        _history.Clear();
    }

    private void NotifyAll()
    {
        foreach (var name in new[] { nameof(CanView), nameof(HasInquiry), nameof(InquiryHeading), nameof(CanCreate), nameof(CanEditLines), nameof(CanSend), nameof(CanClose), nameof(CanCancel), nameof(CanAnswer), nameof(CanDispose), nameof(CanRecordSupplyRoute), nameof(GateText), nameof(RoundChoices) })
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
