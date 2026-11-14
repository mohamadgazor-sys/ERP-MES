using ERP.MES.Application.Requests;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;
using ERP.MES.Tests.Shared;

namespace ERP.MES.Desktop.Tests;

// The QC correction action («اصلاح تأیید قبلی»), the Requests-screen indicator and the Management warning, against the real Application services (no window is opened).
public class QcCorrectionViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static QcInspectionViewModel QcScreen(WorkflowHarness h, ERP.MES.Application.Security.Principal user) => new(h.Workflow.QcInspection, h.Dates, DigitStyle.Persian, () => user);

    private static async Task<(long RequestId, long ItemId)> ApprovedItemAsync(WorkflowHarness h)
    {
        var (requestId, itemId) = await QcInspectionViewModelTests.SubmittedPairItemAsync(h);
        var qc = QcScreen(h, h.Qc);
        await qc.LoadAsync(Token);
        await qc.SelectAsync(qc.Rows.Single());
        await qc.InspectAsync("20", null, null, null); // 10 pairs = 20 base units, all accepted
        Assert.False(qc.IsErrorMessage, qc.Message);
        return (requestId, itemId);
    }

    private static async Task<StageTarget> TargetAsync(WorkflowHarness h, long requestId, long itemId)
    {
        var latest = (await h.Workflow.Requests.GetAsync(h.Planning, requestId, Token)).Value!;
        return new StageTarget(requestId, itemId, 1, latest.Request.Version, latest.Versions.ItemVersion(itemId), latest.Versions.Stages.Where(s => s.Key.ItemId == itemId).ToDictionary(s => s.Key.StageNo, s => s.Value));
    }

    [Fact]
    public async Task An_approved_stage_is_listed_for_QC_and_the_correction_validates_then_records_while_the_history_stays()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await ApprovedItemAsync(h);
        var qc = QcScreen(h, h.Qc);
        await qc.LoadAsync(Token);

        var row = Assert.Single(qc.Rows);
        Assert.Equal("تأییدشده (قابل اصلاح توسط کنترل کیفیت)", row.StatusText);
        Assert.True(qc.CanInspect);
        await qc.SelectAsync(row);

        await qc.CorrectAsync("5", null, "  "); // no reason
        Assert.True(qc.IsErrorMessage);
        Assert.Equal(Messages.ForIssue(ValidationIssue.CorrectionInvalid, JalaliDateError.None), qc.Message);
        await qc.CorrectAsync("x", null, "دلیل");
        Assert.True(qc.IsErrorMessage);
        await qc.CorrectAsync("21", null, "دلیل"); // more than was accepted
        Assert.True(qc.IsErrorMessage);
        Assert.Equal(Messages.ForIssue(ValidationIssue.CorrectionExceedsAccepted, JalaliDateError.None), qc.Message);

        await qc.SelectAsync(qc.Rows.Single());
        await qc.CorrectAsync("5", null, "ابعاد پنج واحد خارج از تلرانس بود");
        Assert.False(qc.IsErrorMessage, qc.Message);
        var after = Assert.Single(qc.Rows);
        Assert.Equal("در بازکاری (تولید)", after.StatusText);
        Assert.Equal("۱۵", after.AcceptedText); // the original accepted line is kept; the valid accepted quantity shrinks
    }

    [Fact]
    public async Task Only_QC_has_the_action_and_the_service_refuses_every_other_role_independently_of_the_screen()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var (requestId, itemId) = await ApprovedItemAsync(h);

        foreach (var other in new[] { h.Planning, h.Manager, h.Warehouse, h.Production, h.ProductionManager, h.Central, h.Drawing, h.Admin })
        {
            var screen = QcScreen(h, other);
            await screen.LoadAsync(Token);
            Assert.False(screen.CanInspect, "the correction action must not be offered to this role");

            // A screen that was tampered with (or any other caller) is still refused by the service.
            var refused = await h.Workflow.QcInspection.CorrectAsync(other, await TargetAsync(h, requestId, itemId), 1, 0, "x", Token);
            Assert.Equal(OperationStatus.NotAuthorized, refused.Status);
        }

        var allowed = await h.Workflow.QcInspection.CorrectAsync(h.Qc, await TargetAsync(h, requestId, itemId), 1, 0, "x", Token);
        Assert.True(allowed.Succeeded, allowed.Status + " " + allowed.Issue);
    }

    [Fact]
    public async Task The_Requests_screen_distinguishes_an_ordinary_approved_item_from_a_corrected_one_without_opening_a_history_page()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var (requestId, itemId) = await ApprovedItemAsync(h);
        var requests = h.RequestsFor(h.Central);
        await requests.LoadAsync(Token);
        await requests.SelectAsync(requests.Requests.Single(), Token);

        var before = Assert.Single(requests.Items);
        Assert.Equal((Messages.CorrectionNone, 0), (before.CorrectionText, before.CorrectionLevel)); // ordinary QC-approved item
        Assert.False(requests.HasDispatchCorrectionWarning);

        Assert.True((await h.Workflow.QcInspection.CorrectAsync(h.Qc, await TargetAsync(h, requestId, itemId), 4, 0, "ترک سطحی", Token)).Succeeded);
        await requests.SelectAsync(null, Token); // the detail is read again when the request is opened again
        await requests.SelectAsync(requests.Requests.Single(), Token);

        var corrected = Assert.Single(requests.Items);
        Assert.Equal((Messages.CorrectionCorrected, 1), (corrected.CorrectionText, corrected.CorrectionLevel));
        Assert.False(requests.HasDispatchCorrectionWarning); // no customer dispatch happened
        Assert.NotEqual(Messages.CorrectionNone, corrected.CorrectionText);
        Assert.NotEqual(Messages.CorrectionNotCurrent, corrected.CorrectionText);
    }

    [Fact]
    public void The_Management_warning_after_a_customer_dispatch_states_what_happened_and_never_claims_a_reversal()
    {
        Assert.Equal(string.Empty, Messages.DispatchCorrectionWarningText([]));
        var text = Messages.DispatchCorrectionWarningText(["A-10", "B-20"]);

        Assert.Contains("هشدار برای مدیریت", text, StringComparison.Ordinal);
        Assert.Contains("پس از ارسال به مشتری", text, StringComparison.Ordinal);       // the correction occurred after customer dispatch
        Assert.Contains("A-10", text, StringComparison.Ordinal);
        Assert.Contains("B-20", text, StringComparison.Ordinal);
        Assert.Contains("به‌عنوان سابقه حفظ شده", text, StringComparison.Ordinal);      // the dispatch is retained as a historical record
        Assert.Contains("برگشت داده نشده است", text, StringComparison.Ordinal);          // not reversed automatically
        Assert.Contains("ممکن است دیگر جاری نباشند", text, StringComparison.Ordinal);    // downstream records may not be current
        Assert.Contains("تأیید جدید", text, StringComparison.Ordinal);                   // a new approval may be required
        Assert.DoesNotContain("برگشت داده شد", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_indicator_texts_are_three_distinct_Persian_states()
    {
        var states = new[]
        {
            Messages.CorrectionIndicatorText(ItemCorrectionIndicator.None(1)),
            Messages.CorrectionIndicatorText(new ItemCorrectionIndicator(1, ItemCorrectionState.Corrected, 1, false, "x", false)),
            Messages.CorrectionIndicatorText(new ItemCorrectionIndicator(1, ItemCorrectionState.DownstreamNotCurrent, 1, true, "x", true)),
        };
        Assert.Equal(3, states.Distinct().Count());
        Assert.Equal([Messages.CorrectionNone, Messages.CorrectionCorrected, Messages.CorrectionNotCurrent], states);
    }

    [Fact]
    public void The_correction_button_sits_inside_the_panel_that_is_disabled_without_the_QC_permission_and_the_Requests_screen_has_the_indicator_and_warning()
    {
        var qcXaml = File.ReadAllText(Path.Combine(SolutionLayout.Root, "src", "ERP.MES.Desktop", "Views", "QcInspectionView.xaml"));
        var panel = qcXaml.IndexOf("IsEnabled=\"{Binding CanInspect}\"", StringComparison.Ordinal);
        var button = qcXaml.IndexOf("اصلاح تأیید قبلی", StringComparison.Ordinal);
        var next = qcXaml.IndexOf("</WrapPanel>", panel, StringComparison.Ordinal);
        Assert.True(panel >= 0 && button > panel && button < next, "the correction button must be inside the CanInspect-gated panel");

        var requestsXaml = File.ReadAllText(Path.Combine(SolutionLayout.Root, "src", "ERP.MES.Desktop", "Views", "RequestsView.xaml"));
        Assert.Contains("Binding CorrectionText", requestsXaml, StringComparison.Ordinal);
        Assert.Contains("DispatchCorrectionWarning", requestsXaml, StringComparison.Ordinal);
    }
}
