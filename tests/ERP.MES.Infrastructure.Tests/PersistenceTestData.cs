using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Persistence;

namespace ERP.MES.Infrastructure.Tests;

/// <summary>Fictional data for the persistence tests.</summary>
internal static class PersistenceTestData
{
    public static readonly DateTime Now = new DateTime(2026, 10, 15, 9, 30, 15, DateTimeKind.Utc).AddTicks(1234567);

    public static readonly JalaliDateService Dates = new(DigitStyle.Latin, TimeZoneInfo.Utc);

    public static Customer NewCustomer(string name = "Fictional Customer A") => new(0, name, 1, Now);

    public static Request NewRequest(Customer customer, string requestNo = "P-565", DateOnly? date = null, string? title = null, WorkType workType = WorkType.Make) =>
        Request.Create(0, customer, RequestNo.Create(requestNo), workType, date ?? new DateOnly(2026, 10, 12), title, null, 1, Now, Dates);

    /// <summary>Saves a customer and a <see cref="NewSampleRequest"/> in the database and returns the request id.</summary>
    public static async Task<long> SeedSampleRequestAsync(TempDatabase db)
    {
        await using var context = db.NewContext();
        var customer = NewCustomer();
        context.Customers.Add(customer);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var request = NewSampleRequest(customer);
        context.Requests.Add(request);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return request.Id;
    }

    /// <summary>A MAKE request (one authoritative type, Decision Log #84) with an item of three stages, the first already QC approved, and a second item with one stage.</summary>
    public static Request NewSampleRequest(Customer customer)
    {
        // Persian text (written with code points): "fictional request".
        var request = NewRequest(customer, "P-565", title: "درخواست فرضی");

        var make = request.AddItem(0, "1.10", "Ring A", 10.123456789m, 1, Now, Priority.Urgent);
        make.DefineInitialStage("Turning", 5m);
        make.DefineInitialStage("Grinding", 3m);
        make.DefineInitialStage("Polishing", 1.5m);
        make.ReleaseFirstStage();
        make.StartStage(1);
        make.SubmitStageForQc(1);
        make.ApproveStageWholeQuantity(1, 9, Now.AddHours(2));

        var repair = request.AddItem(0, "2.10", "Shaft B", 4m, 1, Now);
        repair.DefineInitialStage("Welding", 12m);

        return request;
    }
}
