using ERP.MES.Application.Persistence;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Infrastructure.Demo;

/// <summary>
/// The real demonstration accounts that the sample records are attributed to: the Central user who "created" the customers, requests and items, and the QC user who "approved" the
/// stages. They are provisioned (through the production bootstrap and account use cases) before the seed runs, so every created-by / approved-by value in the sample data refers to an
/// existing user row.
/// </summary>
public sealed record DemoSeedActors(long CreatedByUserId, long QcApproverUserId);

/// <summary>What one seeding run did. Running the seed again on a seeded database adds nothing.</summary>
public sealed record DemoSeedSummary(int CustomersAdded, int CustomersExisting, int RequestsAdded, int RequestsExisting);

/// <summary>
/// Populates a database with the FICTIONAL demonstration dataset (docs/FOUR_DAY_DEMO_PLAN.md, Decision Log #57). Everything goes through the
/// accepted repositories and the Domain methods, so the Domain's rules (including the QC gate) run on every object; nothing is written around
/// them. The dataset is fixed: no clock, random value or machine setting is read, so a reset followed by a seed always produces the same
/// customers, requests, items, stages and approvals.
///
/// Demo-only data stays recognisable: customer names end with <see cref="DemoNameSuffix"/>, request numbers start with
/// <see cref="RequestNoPrefix"/> and request titles start with <see cref="TitlePrefix"/>. The seed is repeatable: a customer is found by its exact
/// name and a request by its business key, and anything already present is left unchanged (never updated, never duplicated).
///
/// The seed does not know whether the database it is given is a demonstration database. Marking and the safe-reset guard belong to the
/// provider project (the demo-database class in ERP.MES.Infrastructure.Sqlite), and only that entry point should be used on a real file.
/// </summary>
public sealed class DemoDataSeeder(ICustomerRepository customers, IRequestRepository requests, IJalaliDateService dates, DemoSeedActors actors)
{
    public const string DemoNameSuffix = "(نمونه آزمایشی)";

    public const string RequestNoPrefix = "DEMO-";

    public const string TitlePrefix = "[نمونه] ";

    private enum StageState
    {
        /// <summary>Not released by Planning yet (valid only when no stage has been approved).</summary>
        Planned,
        Waiting,
        InProgress,
        WaitingQc,
    }

    private sealed record StageDef(string Title, decimal MinutesPerPiece);

    // Approved: number of leading stages that QC has approved. Next: state of the stage after them (ignored when every stage is approved).
    private sealed record ItemDef(string ItemNo, string PartName, decimal Quantity, Priority Priority, StageDef[] Stages, int Approved, StageState Next);

    private sealed record RequestDef(int CustomerIndex, string RequestNo, WorkType Type, int Year, int Month, int Day, string Title, string Description, RequestStatus Status, ItemDef[] Items);

    private static readonly string[] CustomerNames =
    [
        "شرکت نمونه فرضی الف " + DemoNameSuffix,
        "کارخانه نمونه فرضی ب " + DemoNameSuffix,
        "گروه صنعتی نمونه فرضی ج " + DemoNameSuffix,
    ];

    // Fictional requests: each has ONE work type (MAKE or REPAIR; Decision Log #84) that its items inherit, two or three stages per item, a spread of the approved statuses and priorities, and
    // stages at different points of the QC gate (not released, waiting, in progress, waiting for QC, approved). Numbers, names and
    // quantities are invented.
    private static readonly RequestDef[] Requests =
    [
        new(0, RequestNoPrefix + "1405-001", WorkType.Make, 1405, 7, 5, TitlePrefix + "ساخت قطعات پمپ", "داده فرضی برای نمایش؛ مشتری و قطعات واقعی نیستند.", RequestStatus.MgmtApprovedAtPlanning,
        [
            new("1.10", "شفت پمپ", 20m, Priority.VeryUrgent, [new("تراشکاری", 5m), new("سنگ‌زنی", 3m), new("پولیش", 1.5m)], 1, StageState.WaitingQc),
            new("1.20", "بوش برنزی", 6m, Priority.Urgent, [new("جوشکاری ترمیمی", 12m), new("ماشین‌کاری نهایی", 4m)], 0, StageState.InProgress),
        ]),
        new(0, RequestNoPrefix + "1405-002", WorkType.Repair, 1405, 7, 12, TitlePrefix + "تعمیر فلنج و گیربکس", "داده فرضی برای نمایش.", RequestStatus.MgmtApprovedAtPlanning,
        [
            new("2.10", "فلنج ورودی", 50m, Priority.Normal, [new("برش", 2m), new("سوراخکاری", 1.5m)], 0, StageState.WaitingQc),
            new("2.20", "پوسته گیربکس", 2m, Priority.Normal, [new("بازرسی و ترمیم", 30m), new("مونتاژ و تست", 20m)], 0, StageState.Waiting),
        ]),
        new(1, RequestNoPrefix + "1405-014", WorkType.Make, 1405, 7, 15, TitlePrefix + "ساخت چرخ‌دنده و غلتک (در انتظار مدیریت)", "داده فرضی برای نمایش؛ هنوز تأیید مدیریت ندارد.", RequestStatus.SentToManagement,
        [
            new("3.10", "چرخ‌دنده مارپیچ", 100m, Priority.VeryUrgent, [new("تراشکاری", 4m), new("دنده‌زنی", 8m), new("سنگ‌زنی", 5m)], 0, StageState.Planned),
            new("3.20", "غلتک چاپ", 3m, Priority.Normal, [new("ماشین‌کاری", 15m), new("پرداخت", 10m)], 0, StageState.Planned),
        ]),
        new(1, RequestNoPrefix + "1405-015", WorkType.Repair, 1405, 7, 18, TitlePrefix + "تعمیر گلند و میل‌لنگ (تازه ثبت‌شده)", "داده فرضی برای نمایش؛ تازه ثبت شده است.", RequestStatus.Created,
        [
            new("4.10", "گلند آب‌بندی", 200m, Priority.Urgent, [new("تراشکاری", 2m), new("پرداخت", 1m)], 0, StageState.Planned),
            new("4.20", "میل‌لنگ", 1m, Priority.Normal, [new("سنگ‌زنی", 40m), new("پولیش", 15m)], 0, StageState.Planned),
        ]),
        new(2, RequestNoPrefix + "1405-003", WorkType.Make, 1405, 6, 28, TitlePrefix + "ساخت پین و یاتاقان", "داده فرضی برای نمایش؛ یک قلم مسیر کامل دارد.", RequestStatus.MgmtApprovedAtPlanning,
        [
            new("5.10", "پین اتصال", 500m, Priority.Normal, [new("تراشکاری", 1m), new("سنگ‌زنی", 0.8m), new("پولیش", 0.5m)], 3, StageState.Waiting),
            new("5.20", "یاتاقان ثابت", 8m, Priority.Urgent, [new("ماشین‌کاری", 6m), new("پرداخت", 3m)], 1, StageState.Waiting),
        ]),
        new(2, RequestNoPrefix + "1404-087", WorkType.Repair, 1404, 12, 20, TitlePrefix + "تعمیر درپوش و شفت (سال قبل، بسته‌شده)", "داده فرضی برای نمایش؛ سال شمسی ۱۴۰۴.", RequestStatus.Closed,
        [
            new("6.10", "درپوش انتهایی", 40m, Priority.Normal, [new("تراشکاری", 3m), new("پرداخت", 1m)], 2, StageState.Planned),
            new("6.20", "شفت خمیده", 4m, Priority.Normal, [new("صاف‌کاری", 20m), new("ماشین‌کاری", 8m)], 2, StageState.Planned),
        ]),
    ];

    public async Task<DemoSeedSummary> SeedAsync(CancellationToken cancellationToken = default)
    {
        var storedCustomers = await customers.ListAsync(activeOnly: false, cancellationToken);
        var customerIds = new long[CustomerNames.Length];
        var customersAdded = 0;
        var customersExisting = 0;
        for (var i = 0; i < CustomerNames.Length; i++)
        {
            var existing = storedCustomers.FirstOrDefault(c => string.Equals(c.Value.Name, CustomerNames[i], StringComparison.Ordinal));
            if (existing is not null)
            {
                customerIds[i] = existing.Value.Id;
                customersExisting++;
                continue;
            }

            var added = await customers.AddAsync(new Customer(0, CustomerNames[i], actors.CreatedByUserId, AtUtc(1405, 6, 20, 0)), cancellationToken);
            customerIds[i] = added.Value.Id;
            customersAdded++;
        }

        var requestsAdded = 0;
        var requestsExisting = 0;
        foreach (var definition in Requests)
        {
            var customerId = customerIds[definition.CustomerIndex];
            var requestDate = GregorianDate(definition.Year, definition.Month, definition.Day);
            var key = new RequestKey(customerId, definition.RequestNo, dates.GetJalaliYear(requestDate));
            if (await requests.ExistsAsync(key, cancellationToken))
            {
                requestsExisting++;
                continue;
            }

            var customer = (await customers.FindAsync(customerId, cancellationToken))?.Value
                ?? throw new RecordNotFoundException("customer", customerId);
            await requests.AddAsync(Build(definition, customer, requestDate), cancellationToken);
            requestsAdded++;
        }

        return new DemoSeedSummary(customersAdded, customersExisting, requestsAdded, requestsExisting);
    }

    private Request Build(RequestDef definition, Customer customer, DateOnly requestDate)
    {
        var createdAt = AtUtc(definition.Year, definition.Month, definition.Day, 0);
        var request = Request.Create(
            0, customer, RequestNo.Create(definition.RequestNo), definition.Type, requestDate, definition.Title, definition.Description, actors.CreatedByUserId, createdAt, dates);

        foreach (var itemDefinition in definition.Items)
        {
            var item = request.AddItem(0, itemDefinition.ItemNo, itemDefinition.PartName, itemDefinition.Quantity, actors.CreatedByUserId, createdAt, itemDefinition.Priority);
            foreach (var stage in itemDefinition.Stages)
            {
                item.DefineInitialStage(stage.Title, stage.MinutesPerPiece);
            }

            Advance(item, itemDefinition, createdAt.AddDays(1));
        }

        request.PlaceInStatusForDemonstration(definition.Status, createdAt.AddDays(1));
        return request;
    }

    // Moves an item through the Domain's own demonstration transitions, so the QC gate is enforced while the sample data is built.
    private void Advance(RequestItem item, ItemDef definition, DateTime activityStartUtc)
    {
        var stageCount = definition.Stages.Length;
        if (definition.Approved < 0 || definition.Approved > stageCount)
        {
            throw new InvalidOperationException("Demo item " + definition.ItemNo + " approves a number of stages the route does not have.");
        }

        var allApproved = definition.Approved == stageCount;
        if (definition.Approved > 0 && !allApproved && definition.Next == StageState.Planned)
        {
            throw new InvalidOperationException("Demo item " + definition.ItemNo + ": a stage after an approved stage is released by the QC approval, so it cannot stay Planned.");
        }

        if (definition.Approved > 0 || (!allApproved && definition.Next != StageState.Planned))
        {
            item.ReleaseFirstStage();
        }

        for (var stageNo = 1; stageNo <= definition.Approved; stageNo++)
        {
            item.StartStage(stageNo);
            item.SubmitStageForQc(stageNo);
            item.ApproveStageWholeQuantity(stageNo, actors.QcApproverUserId, activityStartUtc.AddHours(stageNo * 4));
        }

        if (allApproved)
        {
            return;
        }

        var next = definition.Approved + 1;
        switch (definition.Next)
        {
            case StageState.InProgress:
                item.StartStage(next);
                break;
            case StageState.WaitingQc:
                item.StartStage(next);
                item.SubmitStageForQc(next);
                break;
        }
    }

    private DateOnly GregorianDate(int year, int month, int day) =>
        dates.TryCreateDate(year, month, day, out var date, out var error)
            ? dates.ToGregorian(date)
            : throw new InvalidOperationException("Demo date " + year + "/" + month + "/" + day + " is invalid: " + error + ".");

    // 05:00 UTC plus the given hours: a fixed, repeatable moment on the Jalali day.
    private DateTime AtUtc(int year, int month, int day, int hours) =>
        GregorianDate(year, month, day).ToDateTime(new TimeOnly(5, 0), DateTimeKind.Utc).AddHours(hours);
}
