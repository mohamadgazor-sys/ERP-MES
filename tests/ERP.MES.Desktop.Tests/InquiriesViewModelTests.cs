using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The raw-material inquiry screen against the real Application services (no window is opened).
public class InquiriesViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional\n%%EOF\n");

    private static InquiriesViewModel For(WorkflowHarness h, Principal user) => new(h.Workflow.Inquiries, h.Workflow.QcMaster, h.Dates, () => user);

    private static async Task<long> UnderDrawingAsync(WorkflowHarness h)
    {
        var customer = (await h.Workflow.Customers.CreateAsync(h.Central, "Fictional Customer", Token)).Value!;
        var created = (await h.Workflow.Requests.CreateAsync(h.Central, new CreateRequestCommand(customer.Value.Id, "I-1", WorkType.Make, "1405/07/20", null, null), Token)).Value!;
        var withPdf = (await h.Workflow.Documents.AttachRequestPdfAsync(h.Central, created.Value.Id, created.Version, "a.pdf", Pdf(), Token)).Value!;
        var sent = (await h.Workflow.Requests.SubmitToManagementAsync(h.Central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        var approved = (await h.Workflow.Lifecycle.ApproveAsync(h.Manager, sent.Value.Id, sent.Version, Token)).Value!;
        Assert.True((await h.Workflow.Lifecycle.ForwardToDrawingAsync(h.Planning, approved.Value.Id, approved.Version, Token)).Succeeded);
        return created.Value.Id;
    }

    [Fact]
    public async Task Drawing_builds_and_sends_an_inquiry_and_warehouse_answers_in_persian_text()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await UnderDrawingAsync(h);
        var drawing = For(h, h.Drawing);

        await drawing.LoadAsync(Token);
        Assert.Equal("بدون استعلام", drawing.Requests.Single().InquiryText);
        drawing.SelectedRequest = drawing.Requests.Single();
        await drawing.SelectRequestAsync(id, Token);
        Assert.True(drawing.CanCreate);
        Assert.True(await drawing.CreateAsync("فوری", Token));
        Assert.True(drawing.HasInquiry);
        Assert.True(drawing.CanEditLines);
        Assert.False(drawing.CanSend);

        var kg = drawing.UnitChoices.First(u => u.Text.StartsWith("KG", StringComparison.Ordinal)).Value;
        Assert.False(await drawing.AddLineAsync("ST37", "میلگرد", "abc", "6000", null, "۱۲", kg, null, null, Token));
        Assert.True(await drawing.AddLineAsync("ST37", "میلگرد", "۳۰", "۶۰۰۰", null, "۱۲٫۵", kg, "DIN 1013", null, Token));
        Assert.Equal("ST37 / میلگرد", drawing.Lines.Single().MaterialText);
        Assert.Contains("قطر 30 mm", drawing.Lines.Single().DimensionsText, StringComparison.Ordinal);
        Assert.Equal("پاسخ داده نشده", drawing.Lines.Single().AnswerText);
        Assert.True(drawing.CanSend);
        Assert.True(await drawing.SendAsync(Token));
        Assert.Contains("ارسال‌شده", drawing.InquiryHeading, StringComparison.Ordinal);
        Assert.False(drawing.CanEditLines);

        var warehouse = For(h, h.Warehouse);
        await warehouse.LoadAsync(Token);
        await warehouse.SelectRequestAsync(id, Token);
        await warehouse.SelectInquiryAsync(warehouse.Inquiries.Single().InquiryId, Token);
        Assert.True(warehouse.CanAnswer);
        Assert.False(warehouse.CanEditLines);
        Assert.False(await warehouse.AnswerSelectedLineAsync(true, null, Token));
        Assert.Equal(Messages.NothingSelected, warehouse.Message);
        warehouse.SelectedLine = warehouse.Lines.Single();
        Assert.True(await warehouse.AnswerSelectedLineAsync(false, "موجود نیست", Token));
        Assert.Equal("خیر", warehouse.Lines.Single().AnswerText);
        Assert.Equal("موجود نیست", warehouse.Lines.Single().AnswerNoteText);
        Assert.Contains("پاسخ داده شده", warehouse.InquiryHeading, StringComparison.Ordinal);

        await drawing.SelectInquiryAsync(drawing.Inquiries.Single().InquiryId, Token);
        Assert.True(drawing.CanClose);
        Assert.True(await drawing.CloseAsync("بررسی شد", Token));
        Assert.False(drawing.CanAnswer);
        Assert.False(drawing.CanCancel);
    }

    [Fact]
    public async Task Cancel_needs_a_reason_and_the_screen_shows_the_persian_message()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await UnderDrawingAsync(h);
        var drawing = For(h, h.Drawing);
        await drawing.LoadAsync(Token);
        await drawing.SelectRequestAsync(id, Token);
        await drawing.CreateAsync(null, Token);

        Assert.False(await drawing.CancelAsync(" ", Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.ReasonRequired, ERP.MES.Domain.Calendar.JalaliDateError.None), drawing.Message);
        Assert.True(await drawing.CancelAsync("اشتباه", Token));
        Assert.Contains("لغوشده", drawing.Inquiries.Single().StatusText, StringComparison.Ordinal);
        Assert.False(drawing.CanEditLines);
    }

    [Fact]
    public async Task Planning_management_and_others_see_nothing_until_decision_D8()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        await UnderDrawingAsync(h);

        foreach (var user in new[] { h.Planning, h.Management, h.Qc, h.Central, h.Production })
        {
            var vm = For(h, user);
            await vm.LoadAsync(Token);
            Assert.Empty(vm.Requests);
            Assert.False(vm.CanView);
            Assert.False(vm.CanCreate);
            Assert.Equal(Messages.NotAuthorized, vm.Message);
        }
    }
}
