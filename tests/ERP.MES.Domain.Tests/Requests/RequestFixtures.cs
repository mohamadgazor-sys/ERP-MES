using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Documents;
using ERP.MES.Domain.Requests;
using ERP.MES.Domain.Tests.Calendar;

namespace ERP.MES.Domain.Tests.Requests;

internal static class RequestFixtures
{
    public static readonly DateTime Now = new(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc);

    public static JalaliDateService Dates { get; } = TestServices.Latin();

    public static Customer NewCustomer(long id = 1, string name = "Fictional Customer A") => new(id, name, createdByUserId: 1, Now);

    public static Request NewRequest(
        string requestNo = "P-565",
        long customerId = 1,
        DateOnly? requestDate = null,
        long requestId = 100,
        WorkType workType = WorkType.Make) =>
        Request.Create(
            requestId,
            NewCustomer(customerId),
            RequestNo.Create(requestNo),
            workType,
            requestDate ?? new DateOnly(2026, 10, 12),
            title: "Fictional request",
            description: null,
            createdByUserId: 1,
            Now,
            Dates);

    public static byte[] PdfBytes(int size = 64)
    {
        var bytes = new byte[size];
        Array.Fill(bytes, (byte)' ');
        var head = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n");
        head.AsSpan(0, Math.Min(head.Length, size)).CopyTo(bytes);
        return bytes;
    }

    public static string Hash(byte[] content) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));

    public static StoredDocument NewPdf(string fileName = "request.pdf", int size = 64)
    {
        Assert.True(StoredDocument.TryCreateRequestPdf(fileName, PdfBytes(size), Hash(PdfBytes(size)), long.MaxValue, 1, Now, out var document, out var error), error.ToString());
        return document!;
    }

    /// <summary>Attaches a fictional request PDF so the request can be submitted (Decision Log #87).</summary>
    public static Request WithPdf(this Request request)
    {
        request.AttachRequestPdf(NewPdf(), 1, Now);
        return request;
    }

    public static RequestItem AddRing(this Request request, string itemNo = "1.10", decimal quantity = 10, Priority priority = Priority.Normal, long itemId = 0) =>
        request.AddItem(itemId, itemNo, "Ring A", quantity, 1, Now, priority);

    public static RequestItem AddShaft(this Request request, string itemNo = "2.10", decimal quantity = 4, Priority priority = Priority.Urgent, long itemId = 0) =>
        request.AddItem(itemId, itemNo, "Shaft B", quantity, 1, Now, priority);

    /// <summary>An item with a seeded route of three internal stages.</summary>
    public static RequestItem ItemWithThreeStages(decimal quantity = 10)
    {
        var item = NewRequest().AddRing(quantity: quantity);
        item.DefineInitialStage("Turning", 5m);
        item.DefineInitialStage("Grinding", 3m);
        item.DefineInitialStage("Inspection prep", 1.5m);
        return item;
    }
}
