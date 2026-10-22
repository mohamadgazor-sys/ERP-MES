using ERP.MES.Domain.Calendar;
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
