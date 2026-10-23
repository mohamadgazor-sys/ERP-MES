using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Desktop.ViewModels;
using ERP.MES.Domain.Inquiries;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Desktop.Tests;

// The clarification screen and the No-answer disposition actions against the real Application services (no window is opened).
public class ClarificationsViewModelTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static byte[] Pdf() => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% fictional\n%%EOF\n");

    private static ClarificationsViewModel For(WorkflowHarness h, Principal user) => new(h.Workflow.Clarifications, h.Dates, () => user);

    private static InquiriesViewModel InquiriesFor(WorkflowHarness h, Principal user) => new(h.Workflow.Inquiries, h.Workflow.QcMaster, h.Workflow.Gates, h.Dates, () => user);

    private static async Task<long> UnderDrawingAsync(WorkflowHarness h)
    {
        var customer = (await h.Workflow.Customers.CreateAsync(h.Central, "Fictional Customer", Token)).Value!;
        var created = (await h.Workflow.Requests.CreateAsync(h.Central, new CreateRequestCommand(customer.Value.Id, "K-1", WorkType.Make, "1405/07/20", null, null), Token)).Value!;
        var withPdf = (await h.Workflow.Documents.AttachRequestPdfAsync(h.Central, created.Value.Id, created.Version, "a.pdf", Pdf(), Token)).Value!;
        var sent = (await h.Workflow.Requests.SubmitToManagementAsync(h.Central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        var approved = (await h.Workflow.Lifecycle.ApproveAsync(h.Manager, sent.Value.Id, sent.Version, Token)).Value!;
        Assert.True((await h.Workflow.Lifecycle.ForwardToDrawingAsync(h.Planning, approved.Value.Id, approved.Version, Token)).Succeeded);
        return created.Value.Id;
    }

    [Fact]
    public async Task Drawing_asks_planning_sees_and_answers_drawing_closes()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await UnderDrawingAsync(h);
        var drawing = For(h, h.Drawing);

        await drawing.LoadAsync(Token);
        Assert.True(drawing.CanAsk);
        Assert.Equal("K-1 — Fictional Customer", drawing.RequestChoices.Single().Text);
        Assert.False(await drawing.AskAsync(id, " ", Token));
        Assert.Equal(Messages.ForIssue(ValidationIssue.ClarificationTextInvalid, ERP.MES.Domain.Calendar.JalaliDateError.None), drawing.Message);
        Assert.True(await drawing.AskAsync(id, "قطر چند است؟", Token));
        Assert.Contains("باز", drawing.Rows.Single().StatusText, StringComparison.Ordinal);

        var planning = For(h, h.Planning);
        await planning.LoadAsync(Token);
        Assert.Equal("قطر چند است؟", planning.Rows.Single().QuestionText);
        Assert.False(planning.CanAsk);
        Assert.False(planning.CanAnswer);
        planning.SelectedRow = planning.Rows.Single();
        Assert.True(planning.CanAnswer);
        Assert.False(planning.CanClose);
        Assert.True(await planning.AnswerSelectedAsync("۳۰ میلی‌متر", Token));
        Assert.Contains("منتظر بستن", planning.Rows.Single().StatusText, StringComparison.Ordinal);

        await drawing.LoadAsync(Token);
        drawing.SelectedRow = drawing.Rows.Single();
        Assert.False(drawing.CanAnswer);
        Assert.True(drawing.CanClose);
        Assert.True(await drawing.CloseSelectedAsync(null, Token));
        Assert.Empty(drawing.Rows);
    }

    [Fact]
    public async Task Other_departments_see_no_questions()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await UnderDrawingAsync(h);
        await For(h, h.Drawing).AskAsync(id, "q", Token);

        foreach (var user in new[] { h.Management, h.Warehouse, h.Qc, h.Central, h.Production })
        {
            var vm = For(h, user);
            await vm.LoadAsync(Token);
            Assert.Empty(vm.Rows);
            Assert.False(vm.CanView);
            Assert.Equal(Messages.NotAuthorized, vm.Message);
        }
    }

    [Fact]
    public async Task A_no_answer_shows_as_without_disposition_blocks_the_gate_text_and_drawing_records_the_disposition()
    {
        using var h = await WorkflowHarness.CreateAsync(Token);
        var id = await UnderDrawingAsync(h);
        var drawing = InquiriesFor(h, h.Drawing);
        await drawing.LoadAsync(Token);
        await drawing.SelectRequestAsync(id, Token);
        await drawing.CreateAsync(null, Token);
        var kg = drawing.UnitChoices.First(u => u.Text.StartsWith("KG", StringComparison.Ordinal)).Value;
        await drawing.AddLineAsync("ST37", "میلگرد", "30", "6000", null, "12", kg, null, null, Token);
        await drawing.SendAsync(Token);

        var warehouse = InquiriesFor(h, h.Warehouse);
        await warehouse.LoadAsync(Token);
        await warehouse.SelectRequestAsync(id, Token);
        await warehouse.SelectInquiryAsync(warehouse.Inquiries.Single().InquiryId, Token);
        warehouse.SelectedLine = warehouse.Lines.Single();
        Assert.True(await warehouse.AnswerSelectedLineAsync(false, "نیست", Token));
        Assert.Contains("بدون تکلیف", warehouse.Lines.Single().DispositionText, StringComparison.Ordinal);
        Assert.False(warehouse.CanDispose);

        await drawing.SelectInquiryAsync(drawing.Inquiries.Single().InquiryId, Token);
        drawing.SelectedLine = drawing.Lines.Single();
        Assert.True(drawing.CanDispose);
        Assert.Contains("پاسخ «خیر» بدون تعیین تکلیف", drawing.GateText, StringComparison.Ordinal);
        Assert.False(await drawing.DisposeSelectedLineAsync(DispositionKind.ReturnedUnchanged, " ", null, Token));
        Assert.True(await drawing.DisposeSelectedLineAsync(DispositionKind.RecordedAsCondition, "ماده جایگزین", null, Token));
        Assert.Contains("ثبت به‌عنوان شرط", drawing.Lines.Single().DispositionText, StringComparison.Ordinal);
        Assert.False(drawing.CanDispose);

        var planning = InquiriesFor(h, h.Planning);
        await planning.LoadAsync(Token);
        await planning.SelectRequestAsync(id, Token);
        await planning.SelectInquiryAsync(planning.Inquiries.Single().InquiryId, Token);
        planning.SelectedLine = planning.Lines.Single();
        Assert.True(planning.CanRecordSupplyRoute);
        Assert.False(planning.CanDispose);
        Assert.True(await planning.RecordSupplyRouteAsync("خرید از بازار", true, Token));
        Assert.Contains("خرید بیرونی", planning.Lines.Single().SupplyRouteText, StringComparison.Ordinal);
    }
}
