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
        long requestId = 100) =>
        Request.Create(
            requestId,
            NewCustomer(customerId),
            RequestNo.Create(requestNo),
            requestDate ?? new DateOnly(2026, 10, 12),
            title: "Fictional request",
            description: null,
            createdByUserId: 1,
            Now,
            Dates);

    public static RequestItem AddMake(this Request request, string itemNo = "1.10", decimal quantity = 10, Priority priority = Priority.Normal, long itemId = 0) =>
        request.AddItem(itemId, itemNo, "Ring A", WorkType.Make, quantity, priority, createdByUserId: 1, Now);

    public static RequestItem AddRepair(this Request request, string itemNo = "2.10", decimal quantity = 4, Priority priority = Priority.Urgent, long itemId = 0) =>
        request.AddItem(itemId, itemNo, "Shaft B", WorkType.Repair, quantity, priority, createdByUserId: 1, Now);

    /// <summary>An item with a seeded route of three internal stages.</summary>
    public static RequestItem ItemWithThreeStages(decimal quantity = 10)
    {
        var item = NewRequest().AddMake(quantity: quantity);
        item.AddStage("Turning", 5m);
        item.AddStage("Grinding", 3m);
        item.AddStage("Inspection prep", 1.5m);
        return item;
    }
}
