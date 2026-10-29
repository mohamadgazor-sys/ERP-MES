using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The request screen's state machine against the real Application services on a temporary SQLite database (no window is opened).
public class RequestsViewModelTests
{
    private const string DateText = "۱۴۰۵/۰۷/۲۰";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<long> CustomerAsync(WorkflowHarness h, string name = "مشتری نمونه (فرضی)")
    {
        var created = await h.Workflow.Customers.CreateAsync(h.Central, name, Token);
        Assert.True(created.Succeeded);
        return created.Value!.Value.Id;
    }

    [Fact]
    public async Task With_no_requests_the_list_shows_the_empty_state_and_the_buttons_follow_the_users_rights()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await CustomerAsync(h);

        var central = h.RequestsFor(h.Central);
        await central.LoadAsync(Token);
        Assert.True(central.IsEmpty);
        Assert.True(central.CanCreateRequest);
        Assert.False(central.CanSubmit);
        Assert.Single(central.Customers);

        var drawing = h.RequestsFor(h.Drawing);
        await drawing.LoadAsync(Token);
        Assert.False(drawing.CanCreateRequest);

        var qc = h.RequestsFor(h.Qc);
        await qc.LoadAsync(Token);
        Assert.False(qc.CanCreateRequest);
        Assert.False(qc.CanSetPriority);
    }

    [Fact]
    public async Task The_work_types_are_make_and_repair_and_today_is_shown_in_jalali()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var vm = h.RequestsFor(h.Central);

        Assert.Equal(new[] { WorkType.Make, WorkType.Repair }, vm.WorkTypes.Select(t => t.Value).ToArray());
        Assert.Equal(new[] { "ساخت", "تعمیر" }, vm.WorkTypes.Select(t => t.Text).ToArray());
        // 2026-10-16 09:00 UTC is 12:30 in the site zone: 1405/07/24.
        Assert.Equal("۱۴۰۵/۰۷/۲۴", vm.TodayText);
    }

    [Fact]
    public async Task Central_creates_a_request_that_shows_in_the_list_with_jalali_text_and_persists()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var customerId = await CustomerAsync(h);
        var vm = h.RequestsFor(h.Central);
        await vm.LoadAsync(Token);

        var saved = await vm.CreateAsync(customerId, "wsj-898", WorkType.Make, DateText, "عنوان نمونه", null, Token);

        Assert.True(saved);
        Assert.StartsWith("درخواست ثبت شد", vm.Message, StringComparison.Ordinal);
        var row = Assert.Single(vm.Requests);
        Assert.Equal("wsj-898", row.RequestNo);
        Assert.Equal("مشتری نمونه (فرضی)", row.CustomerName);
        Assert.Equal(DateText, row.DateText);
        Assert.Equal("۱۴۰۵", row.YearText);
        Assert.Equal(Messages.StatusText(RequestStatus.Created), row.StatusText);
        Assert.Equal(0, row.ItemCount);
        Assert.True(vm.HasDetail);
        Assert.True(vm.HasNoItems);
        Assert.Contains(Messages.Ltr("wsj-898"), vm.DetailHeading, StringComparison.Ordinal);
        Assert.False(vm.CanSubmit);
        Assert.True(vm.CanAttachPdf);

        var afterRestart = h.RequestsFor(h.Qc, h.Restarted());
        await afterRestart.LoadAsync(Token);
        Assert.Equal("wsj-898", Assert.Single(afterRestart.Requests).RequestNo);
    }

    [Theory]
    [InlineData("", DateText, "NoRequired")]
    [InlineData(" P-1", DateText, "NoWhitespace")]
    [InlineData("P-1", "", "DateRequired")]
    [InlineData("P-1", "۱۴۰۵-۰۷-۲۰", "DateCharacter")]
    [InlineData("P-1", "۱۴۰۴/۱۲/۳۰", "DateDay")]
    [InlineData("P-1", "۱۴۰۵/۱۳/۰۱", "DateMonth")]
    public async Task An_invalid_request_shows_the_persian_reason_and_saves_nothing(string requestNo, string date, string kind)
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var customerId = await CustomerAsync(h);
        var vm = h.RequestsFor(h.Central);
        await vm.LoadAsync(Token);
        var expected = kind switch
        {
            "NoRequired" => Messages.ForIssue(ValidationIssue.RequestNoRequired, JalaliDateError.None),
            "NoWhitespace" => Messages.ForIssue(ValidationIssue.RequestNoWhitespace, JalaliDateError.None),
            "DateRequired" => Messages.ForIssue(ValidationIssue.RequestDateRequired, JalaliDateError.None),
            "DateCharacter" => Messages.ForDateError(JalaliDateError.InvalidCharacter),
            "DateDay" => Messages.ForDateError(JalaliDateError.DayOutOfRange),
            _ => Messages.ForDateError(JalaliDateError.MonthOutOfRange),
        };

        var saved = await vm.CreateAsync(customerId, requestNo, WorkType.Make, date, null, null, Token);

        Assert.False(saved);
        Assert.Equal(expected, vm.Message);
        Assert.NotEqual(string.Empty, vm.Message);
        Assert.Empty(vm.Requests);
        Assert.Empty((await h.Workflow.Requests.ListAsync(h.Central, null, Token)).Value!);
    }

    [Fact]
    public async Task The_date_error_texts_are_all_different_and_persian()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var texts = new[]
        {
            Messages.ForDateError(JalaliDateError.InvalidCharacter), Messages.ForDateError(JalaliDateError.InvalidFormat), Messages.ForDateError(JalaliDateError.YearOutOfRange),
            Messages.ForDateError(JalaliDateError.MonthOutOfRange), Messages.ForDateError(JalaliDateError.DayOutOfRange),
        };

        Assert.Equal(texts.Length, texts.Distinct().Count());
        Assert.All(texts, t => Assert.Contains(t, c => c is >= '\u0600' and <= '\u06FF'));
    }

    [Fact]
    public async Task No_customer_selected_a_duplicate_number_and_a_case_only_near_duplicate_are_each_reported()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var customerId = await CustomerAsync(h);
        var vm = h.RequestsFor(h.Central);
        await vm.LoadAsync(Token);

        Assert.False(await vm.CreateAsync(0, "P-1", WorkType.Make, DateText, null, null, Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.CustomerRequired, JalaliDateError.None), vm.Message);

        Assert.True(await vm.CreateAsync(customerId, "wsj-898", WorkType.Make, DateText, null, null, Token));
        Assert.False(await vm.CreateAsync(customerId, "wsj-898", WorkType.Make, DateText, null, null, Token));
        Assert.Equal(Messages.Duplicate, vm.Message);

        Assert.True(await vm.CreateAsync(customerId, "WSJ-898", WorkType.Make, DateText, null, null, Token));
        Assert.Contains("هشدار", vm.Message, StringComparison.Ordinal);
        Assert.Contains("wsj-898", vm.Message, StringComparison.Ordinal);
        Assert.Equal(2, vm.Requests.Count);
    }

    [Fact]
    public async Task A_customer_deactivated_after_the_dropdown_was_filled_is_refused_with_a_clear_message_and_the_list_reloads()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var staying = await CustomerAsync(h, "مشتری فعال");
        var leaving = await CustomerAsync(h, "مشتری در حال غیرفعال شدن");
        var vm = h.RequestsFor(h.Central);
        await vm.LoadAsync(Token);
        Assert.Equal(2, vm.Customers.Count);

        var stored = (await h.Workflow.Customers.ListAsync(h.Central, false, Token)).Value!.Single(c => c.Value.Id == leaving);
        Assert.True((await h.Workflow.Customers.SetActiveAsync(h.Planning, leaving, stored.Version, false, Token)).Succeeded);

        var saved = await vm.CreateAsync(leaving, "P-1", WorkType.Make, DateText, null, null, Token);

        Assert.False(saved);
        Assert.Equal(Messages.ForIssue(ValidationIssue.CustomerInactive, JalaliDateError.None), vm.Message);
        Assert.Contains("غیرفعال", vm.Message, StringComparison.Ordinal);
        Assert.Empty(vm.Requests);
        Assert.Equal(new[] { staying }, vm.Customers.Select(c => c.Id).ToArray());
        Assert.Empty((await h.Workflow.Requests.ListAsync(h.Central, null, Token)).Value!);

        // A request for the other, active customer still works.
        Assert.True(await vm.CreateAsync(staying, "P-1", WorkType.Make, DateText, null, null, Token));
    }

    [Fact]
    public async Task A_customer_that_no_longer_exists_is_refused_with_its_own_message()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await CustomerAsync(h);
        var vm = h.RequestsFor(h.Central);
        await vm.LoadAsync(Token);

        Assert.False(await vm.CreateAsync(4242, "P-1", WorkType.Make, DateText, null, null, Token));

        Assert.Equal(Messages.ForIssue(ValidationIssue.CustomerNotFound, JalaliDateError.None), vm.Message);
        Assert.NotEqual(Messages.ForIssue(ValidationIssue.CustomerInactive, JalaliDateError.None), vm.Message);
        Assert.Empty(vm.Requests);
    }

    [Fact]
    public async Task Departments_without_the_right_are_told_so_and_nothing_is_created()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var customerId = await CustomerAsync(h);
        var vm = h.RequestsFor(h.Planning);
        await vm.LoadAsync(Token);

        Assert.False(await vm.CreateAsync(customerId, "P-1", WorkType.Make, DateText, null, null, Token));

        Assert.Equal(Messages.NotAuthorized, vm.Message);
        Assert.Empty(vm.Requests);
    }

    private static async Task SeedItemAsync(WorkflowHarness h, long requestId, string itemNo, string partName, decimal quantity, Priority priority = Priority.Normal)
    {
        // Fixture seeding through the repository: item definition is Planning's gated workflow (ItemDefinitionViewModelTests); these tests are about how items are shown.
        await h.RequestRepository.UpdateRowsAsync(requestId, RowVersionExpectations.None, r => r.AddItem(0, itemNo, partName, quantity, h.Planning.UserId, h.Clock.UtcNow, priority), Token);
        h.Clock.Advance(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task The_items_show_the_requests_type_quantity_and_priority_labels()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var customerId = await CustomerAsync(h);
        var central = h.RequestsFor(h.Central);
        await central.CreateAsync(customerId, "P-565", WorkType.Make, DateText, null, null, Token);
        var requestId = central.Requests.Single().RequestId;
        Assert.Equal("ساخت", central.Requests.Single().WorkTypeText);
        await SeedItemAsync(h, requestId, "1.10", "حلقه", 10m);
        await SeedItemAsync(h, requestId, "2.10", "شفت", 2.5m, Priority.Urgent);

        var planning = h.RequestsFor(h.Planning);
        await planning.LoadAsync(Token);
        await planning.SelectAsync(planning.Requests.Single(), Token);
        Assert.True(planning.CanSetPriority);

        // Both items inherit the request's one work type (Decision Log #84).
        Assert.Equal(
            new[] { new ItemRow("1.10", "حلقه", "ساخت", "۱۰", "عادی"), new ItemRow("2.10", "شفت", "ساخت", "۲.۵", "فوری") },
            planning.Items.ToArray());
        Assert.Equal(2, planning.Requests.Single().ItemCount);

        var restarted = h.RequestsFor(h.Qc, h.Restarted());
        await restarted.LoadAsync(Token);
        await restarted.SelectAsync(restarted.Requests.Single(), Token);
        Assert.Equal(2, restarted.Items.Count);
        Assert.Equal(requestId, restarted.Requests.Single().RequestId);
    }

    [Theory]
    [InlineData("10", "۱۰")]
    [InlineData("10.0", "۱۰")]
    [InlineData("2.50", "۲.۵")]
    [InlineData("2.0001", "۲.۰۰۰۱")]
    [InlineData("0.5", "۰.۵")]
    public async Task Quantities_are_shown_without_insignificant_zeros_and_without_rounding(string typed, string shown)
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var customerId = await CustomerAsync(h);
        await h.RequestsFor(h.Central).CreateAsync(customerId, "P-1", WorkType.Make, DateText, null, null, Token);
        await SeedItemAsync(h, (await h.Workflow.Requests.ListAsync(h.Central, null, Token)).Value!.Single().RequestId, "1.10", "حلقه", decimal.Parse(typed, System.Globalization.CultureInfo.InvariantCulture));
        var drawing = h.RequestsFor(h.Drawing);
        await drawing.LoadAsync(Token);
        await drawing.SelectAsync(drawing.Requests.Single(), Token);

        Assert.Equal(shown, drawing.Items.Single().QuantityText);
        var restarted = h.RequestsFor(h.Qc, h.Restarted());
        await restarted.LoadAsync(Token);
        await restarted.SelectAsync(restarted.Requests.Single(), Token);
        Assert.Equal(shown, restarted.Items.Single().QuantityText);
    }

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional test document\n%%EOF\n");

    [Fact]
    public async Task The_request_pdf_must_be_attached_before_submitting_is_offered_and_shows_its_name_and_size()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var customerId = await CustomerAsync(h);
        var central = h.RequestsFor(h.Central);
        await central.CreateAsync(customerId, "P-1", WorkType.Make, DateText, null, null, Token);

        Assert.False(central.HasPdf);
        Assert.True(central.CanAttachPdf);
        Assert.False(central.CanSubmit);
        Assert.Contains("پیوست نشده", central.PdfText, StringComparison.Ordinal);
        Assert.False(await central.SubmitAsync(Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.RequestPdfRequired, JalaliDateError.None), central.Message);

        Assert.True(await central.AttachPdfAsync(@"C:\x\Order 7.pdf", Pdf(), Token));

        Assert.True(central.HasPdf);
        Assert.Equal("فایل PDF درخواست پیوست شد.", central.Message);
        Assert.Contains(Messages.Ltr("Order 7.pdf"), central.PdfText, StringComparison.Ordinal);
        Assert.Contains("کیلوبایت", central.PdfText, StringComparison.Ordinal);
        var file = await central.LoadPdfAsync(Token);
        Assert.NotNull(file);
        Assert.Equal("Order 7.pdf", file!.FileName);
        Assert.Equal(Pdf(), file.Content);
    }

    [Fact]
    public async Task A_wrong_file_is_refused_with_a_persian_message_and_replacing_the_pdf_works_until_the_request_is_submitted()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var customerId = await CustomerAsync(h);
        var central = h.RequestsFor(h.Central);
        await central.CreateAsync(customerId, "P-1", WorkType.Make, DateText, null, null, Token);

        Assert.False(await central.AttachPdfAsync("notes.pdf", System.Text.Encoding.ASCII.GetBytes("plain text"), Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.PdfNotPdf, JalaliDateError.None), central.Message);
        Assert.False(await central.AttachPdfAsync("empty.pdf", [], Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.PdfEmpty, JalaliDateError.None), central.Message);
        Assert.False(await central.AttachPdfAsync("notes.txt", Pdf(), Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.PdfFileNameInvalid, JalaliDateError.None), central.Message);
        Assert.False(central.HasPdf);

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await central.AttachPdfAsync("one.pdf", Pdf(), Token));
        h.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await central.AttachPdfAsync("two.pdf", Pdf(), Token));
        Assert.Contains(Messages.Ltr("two.pdf"), central.PdfText, StringComparison.Ordinal);

        Assert.True(await central.SubmitAsync(Token));
        Assert.False(central.CanAttachPdf);
        Assert.False(await central.AttachPdfAsync("three.pdf", Pdf(), Token));
    }

    [Fact]
    public async Task Other_departments_cannot_attach_a_pdf_but_can_view_it()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var customerId = await CustomerAsync(h);
        var central = h.RequestsFor(h.Central);
        await central.CreateAsync(customerId, "P-1", WorkType.Make, DateText, null, null, Token);
        await central.AttachPdfAsync("request.pdf", Pdf(), Token);
        var planning = h.RequestsFor(h.Planning);
        await planning.LoadAsync(Token);
        await planning.SelectAsync(planning.Requests.Single(), Token);

        Assert.False(planning.CanAttachPdf);
        Assert.True(planning.HasPdf);
        Assert.False(await planning.AttachPdfAsync("other.pdf", Pdf(), Token));
        Assert.Equal(Messages.NotAuthorized, planning.Message);
        Assert.NotNull(await planning.LoadPdfAsync(Token));
    }

    [Fact]
    public async Task Central_submits_to_management_once_and_the_button_then_disappears()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var customerId = await CustomerAsync(h);
        var central = h.RequestsFor(h.Central);
        await central.CreateAsync(customerId, "P-1", WorkType.Make, DateText, null, null, Token);
        Assert.False(central.CanSubmit);
        Assert.True(await central.AttachPdfAsync("request.pdf", Pdf(), Token));
        Assert.True(central.CanSubmit);

        Assert.True(await central.SubmitAsync(Token));

        Assert.Equal("درخواست به مدیریت ارسال شد.", central.Message);
        Assert.Equal(Messages.StatusText(RequestStatus.SentToManagement), central.Requests.Single().StatusText);
        Assert.False(central.CanSubmit);
        Assert.Equal(RequestStatus.SentToManagement, (await h.Restarted().Requests.GetAsync(h.Qc, central.Requests.Single().RequestId, Token)).Value!.Request.Value.Status);

        Assert.False(await central.SubmitAsync(Token));
        Assert.Equal(Messages.InvalidState, central.Message);
    }

    [Fact]
    public async Task Planning_cannot_submit_even_if_it_tries_and_other_departments_do_not_get_the_button()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var customerId = await CustomerAsync(h);
        await h.RequestsFor(h.Central).CreateAsync(customerId, "P-1", WorkType.Make, DateText, null, null, Token);
        var planning = h.RequestsFor(h.Planning);
        await planning.LoadAsync(Token);
        await planning.SelectAsync(planning.Requests.Single(), Token);

        Assert.False(planning.CanSubmit);
        Assert.False(await planning.SubmitAsync(Token));
        Assert.Equal(Messages.NotAuthorized, planning.Message);
        Assert.Equal(Messages.StatusText(RequestStatus.Created), planning.Requests.Single().StatusText);
    }

    [Fact]
    public async Task Selecting_nothing_clears_the_detail_and_actions_without_a_request_say_so()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var customerId = await CustomerAsync(h);
        var central = h.RequestsFor(h.Central);
        await central.CreateAsync(customerId, "P-1", WorkType.Make, DateText, null, null, Token);
        Assert.True(central.HasDetail);

        await central.SelectAsync(null, Token);
        Assert.False(central.HasDetail);
        Assert.False(central.CanSubmit);
        Assert.False(await central.SubmitAsync(Token));
        Assert.Equal(Messages.NothingSelected, central.Message);
    }
}
