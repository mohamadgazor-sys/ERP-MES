using System.Globalization;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.ViewModels;

/// <summary>Turns a queue entry into display text in the screen's digit shape. Shared by the read-only overview and the stage-work screen; it decides nothing.</summary>
internal sealed class QueueRowFormatter(DigitStyle digitStyle)
{
    public QueueRow Format(int position, ProductionQueueEntry e) => new(
        Digits(position.ToString(CultureInfo.InvariantCulture)),
        e.RequestNo,
        e.CustomerName,
        Messages.StatusText(e.RequestStatus),
        e.ItemNo,
        e.PartName,
        Messages.WorkTypeText(e.WorkType),
        Number(e.RequestedQuantity),
        e.Priority.PersianName(),
        CurrentStageText(e),
        e.CurrentStage is null ? ProductionQueueViewModel.NotAvailableText : ProductionQueueViewModel.StageStatusText(e.CurrentStage.Status),
        QcStatusText(e),
        e.CurrentStage is null ? ProductionQueueViewModel.NotAvailableText : Number(e.CurrentStage.EstimatedMinutes),
        e.TotalEstimatedMinutes is null ? ProductionQueueViewModel.NotAvailableText : Number(e.TotalEstimatedMinutes.Value));

    private string CurrentStageText(ProductionQueueEntry e) => e.RouteState switch
    {
        QueueRouteState.NoRoute => "مسیر تعریف نشده",
        QueueRouteState.Completed => "همهٔ مراحل تکمیل شد",
        _ => $"مرحلهٔ {Digits(e.CurrentStage!.StageNo.ToString(CultureInfo.InvariantCulture))} از {Digits(e.StageCount.ToString(CultureInfo.InvariantCulture))}: {e.CurrentStage.Title}",
    };

    /// <summary>The QC position of the item: a stage waiting for QC, the whole route approved, or how many stages QC has approved so far.</summary>
    private string QcStatusText(ProductionQueueEntry e)
    {
        if (e.RouteState == QueueRouteState.NoRoute)
        {
            return ProductionQueueViewModel.NotAvailableText;
        }

        if (e.RouteState == QueueRouteState.Completed)
        {
            return "همهٔ مراحل توسط کنترل کیفیت تأیید شده است";
        }

        var approved = $"{Digits(e.ApprovedStageCount.ToString(CultureInfo.InvariantCulture))} از {Digits(e.StageCount.ToString(CultureInfo.InvariantCulture))} مرحله تأیید شده";
        return e.CurrentStage!.Status == ItemStageStatus.WaitingQc ? "در انتظار تأیید کنترل کیفیت؛ " + approved : approved;
    }

    // Exact decimals as stored (no rounding), trailing zeros not shown, in the digit shape of the screen.
    private string Number(decimal value) => Digits(value.ToString("0.############################", CultureInfo.InvariantCulture));

    private string Digits(string text) =>
        digitStyle == DigitStyle.Persian ? new string(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c).ToArray()) : text;
}
