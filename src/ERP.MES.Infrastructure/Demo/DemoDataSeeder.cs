using ERP.MES.Application.Security;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Documents;
using ERP.MES.Application.Persistence;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Infrastructure.Demo;

/// <summary>
/// The real demonstration accounts that the sample records are attributed to: the Central user who "created" the customers, requests and items (the QC user is resolved too, for the walkthrough's QC step). They are provisioned (through the production bootstrap and account use cases) before the seed runs, so every created-by / approved-by value in the sample data refers to an
/// existing user row.
/// </summary>
public sealed record DemoSeedActors(long CreatedByUserId, long QcApproverUserId, long DrawingUserId = 0);

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
public sealed class DemoDataSeeder(ICustomerRepository customers, IRequestRepository requests, IJalaliDateService dates, DemoSeedActors actors, IItemDrawingRepository? drawings = null)
{
    public const string DemoNameSuffix = "(نمونه آزمایشی)";

    public const string RequestNoPrefix = "DEMO-";

    public const string TitlePrefix = "[نمونه] ";

    /// <summary>An item of a sample request. <c>ParentRef</c> names the customer reference of its assembly (the parent comes first in the list); a REPAIR item carries its delivery date (Jalali year, month, day) and warehouse code; <c>Drawing</c> gives it a fictional drawing.</summary>
    private sealed record ItemDef(string ItemNo, string PartName, decimal Quantity, Priority Priority, string? ParentRef = null, bool Assembly = false, (int Year, int Month, int Day)? Delivery = null, string? WarehouseCode = null, bool Drawing = false);

    private sealed record RequestDef(int CustomerIndex, string RequestNo, WorkType Type, int Year, int Month, int Day, string Title, string Description, RequestStatus Status, ItemDef[] Items)
    {
        /// <summary>Every sample request carries a fictional PDF except the freshly created one, so the demonstration can show the rule "attach the PDF before submitting" (Decision Log #87).</summary>
        public bool HasPdf => Status != RequestStatus.Created;
    }

    private static readonly string[] CustomerNames =
    [
        "شرکت نمونه فرضی الف " + DemoNameSuffix,
        "کارخانه نمونه فرضی ب " + DemoNameSuffix,
        "گروه صنعتی نمونه فرضی ج " + DemoNameSuffix,
    ];

    // Fictional requests. Only requests that are valid under the current workflow are seeded: one waiting for Management's decision and one just created (no PDF yet). They have items but NO stages:
    // stages belong to the production-unit workflow (Planning defines and releases them per unit after approval, contract and Drawing gates), which the walkthrough performs through the real services.
    // The earlier sample requests with unit-less stage progress (retired workflow, Decision Log #98/#116) are no longer seeded. Numbers, names and quantities are invented.
    private static readonly RequestDef[] Requests =
    [
        new(1, RequestNoPrefix + "1405-014", WorkType.Make, 1405, 7, 15, TitlePrefix + "ساخت چرخ‌دنده و غلتک (در انتظار مدیریت)", "داده فرضی برای نمایش؛ هنوز تأیید مدیریت ندارد.", RequestStatus.SentToManagement,
        [
            new("3.10", "چرخ‌دنده مارپیچ", 100m, Priority.VeryUrgent),
            new("3.20", "غلتک چاپ", 3m, Priority.Normal),
        ]),
        new(1, RequestNoPrefix + "1405-015", WorkType.Repair, 1405, 7, 18, TitlePrefix + "تعمیر گلند و میل‌لنگ (تازه ثبت‌شده)", "داده فرضی برای نمایش؛ تازه ثبت شده است.", RequestStatus.Created,
        [
            new("4.10", "گلند آب‌بندی", 200m, Priority.Urgent, Delivery: (1405, 8, 20), WarehouseCode: "WH-DEMO-GL-200", Drawing: true),
            new("4.20", "میل‌لنگ", 1m, Priority.Normal, Delivery: (1405, 8, 25), WarehouseCode: "WH-DEMO-CR-001"),
        ]),

        // A nested assembly (Decision Log #121): the gearbox contains a housing and a shaft sub-assembly that contains two parts. The system numbers read DEMO-1405-016-001, -001.001, -001.002, -001.002.001 and -001.002.002.
        new(2, RequestNoPrefix + "1405-016", WorkType.Make, 1405, 7, 20, TitlePrefix + "ساخت مجموعهٔ گیربکس (مونتاژ تو در تو، در انتظار مدیریت)", "داده فرضی برای نمایش ساختار مونتاژ؛ هنوز تأیید مدیریت ندارد.", RequestStatus.SentToManagement,
        [
            new("A.00", "مجموعهٔ گیربکس", 2m, Priority.Normal, Assembly: true, Drawing: true),
            new("A.10", "پوستهٔ گیربکس", 2m, Priority.Normal, ParentRef: "A.00", Drawing: true),
            new("A.20", "مجموعهٔ شفت", 2m, Priority.Urgent, ParentRef: "A.00", Assembly: true),
            new("A.21", "شفت اصلی", 2m, Priority.Normal, ParentRef: "A.20"),
            new("A.22", "چرخ‌دنده مارپیچ", 2m, Priority.Normal, ParentRef: "A.20", Drawing: true),
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
            var saved = await requests.AddAsync(Build(definition, customer, requestDate), cancellationToken);
            await AddDrawingsAsync(definition, saved.Value, cancellationToken);
            requestsAdded++;
        }

        return new DemoSeedSummary(customersAdded, customersExisting, requestsAdded, requestsExisting);
    }

    private Request Build(RequestDef definition, Customer customer, DateOnly requestDate)
    {
        var createdAt = AtUtc(definition.Year, definition.Month, definition.Day, 0);
        var request = Request.Create(
            0, customer, RequestNo.Create(definition.RequestNo), definition.Type, requestDate, definition.Title, definition.Description, actors.CreatedByUserId, createdAt, dates);

        if (definition.HasPdf)
        {
            var pdfName = definition.RequestNo + ".pdf";
            var pdfBytes = FictionalPdf(definition.RequestNo);
            if (!StoredDocument.TryCreateRequestPdf(pdfName, pdfBytes, DocumentHash.Sha256Hex(pdfBytes), long.MaxValue, actors.CreatedByUserId, createdAt, out var pdf, out var pdfError))
            {
                throw new InvalidOperationException("The fictional demonstration PDF was refused: " + pdfError);
            }

            request.AttachRequestPdf(pdf!, actors.CreatedByUserId, createdAt);
        }

        foreach (var itemDefinition in definition.Items)
        {
            var parent = itemDefinition.ParentRef is null ? null : request.FindByCustomerItemRef(itemDefinition.ParentRef) ?? throw new InvalidOperationException("The demonstration parent item '" + itemDefinition.ParentRef + "' does not exist.");
            if (definition.Type == WorkType.Repair)
            {
                // A REPAIR item carries its delivery date and the warehouse's code (Decision Log #124 B5): the sample repair items are valid, complete items.
                var delivery = itemDefinition.Delivery ?? throw new InvalidOperationException("A demonstration REPAIR item needs a delivery date.");
                request.DefineItem(
                    0, itemDefinition.ItemNo, itemDefinition.PartName, itemDefinition.Quantity, new ItemSpecification(null, null, null, null, null, 1, GregorianDate(delivery.Year, delivery.Month, delivery.Day), itemDefinition.WarehouseCode),
                    actors.CreatedByUserId, createdAt, itemDefinition.Priority, parent, itemDefinition.Assembly);
            }
            else
            {
                request.AddItem(0, itemDefinition.ItemNo, itemDefinition.PartName, itemDefinition.Quantity, actors.CreatedByUserId, createdAt, itemDefinition.Priority, parent, itemDefinition.Assembly);
            }
        }

        request.PlaceInStatusForDemonstration(definition.Status, createdAt.AddDays(1));
        return request;
    }

    // Fictional technical drawings (Decision Log #125 Q14) for the items marked with a drawing, attributed to the Drawing account; nothing is seeded when the repository or the account is not given.
    private async Task AddDrawingsAsync(RequestDef definition, Request saved, CancellationToken cancellationToken)
    {
        if (drawings is null || actors.DrawingUserId == 0)
        {
            return;
        }

        var at = AtUtc(definition.Year, definition.Month, definition.Day, 2);
        foreach (var itemDefinition in definition.Items.Where(i => i.Drawing))
        {
            var item = saved.FindByCustomerItemRef(itemDefinition.ItemNo) ?? throw new InvalidOperationException("The demonstration item '" + itemDefinition.ItemNo + "' was not saved.");
            var bytes = FictionalPdf("drawing " + item.ItemNo);
            if (!StoredDocument.TryCreateItemDrawing(item.ItemNo + ".pdf", bytes, DocumentHash.Sha256Hex(bytes), long.MaxValue, actors.DrawingUserId, at, out var document, out var error))
            {
                throw new InvalidOperationException("The fictional demonstration drawing was refused: " + error);
            }

            await drawings.AddRevisionAsync(
                item.Id, document!, actors.DrawingUserId, at,
                (added, _) => new AuditLogEntry(at, actors.DrawingUserId, "DEMO-SEED", "demo.seed", null, AuditActions.ItemDrawingUploaded, "RequestItem", item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"Item {item.ItemNo}: fictional drawing '{added.FileName}' revision {added.Revision} (demonstration)"),
                cancellationToken);
        }
    }

    // A tiny, valid, FICTIONAL one-page PDF (no customer data): the text only says that it is a demonstration document.
    private static byte[] FictionalPdf(string requestNo)
    {
        var text = "Fictional demonstration request " + requestNo + " - not a real document";
        var stream = "BT /F1 11 Tf 20 100 Td (" + text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + ") Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 420 144] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            "<< /Length " + stream.Length + " >>\nstream\n" + stream + "\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };

        var builder = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(builder.Length);
            builder.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        var xref = builder.Length;
        builder.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        builder.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return System.Text.Encoding.ASCII.GetBytes(builder.ToString());
    }

    private DateOnly GregorianDate(int year, int month, int day) =>
        dates.TryCreateDate(year, month, day, out var date, out var error)
            ? dates.ToGregorian(date)
            : throw new InvalidOperationException("Demo date " + year + "/" + month + "/" + day + " is invalid: " + error + ".");

    // 05:00 UTC plus the given hours: a fixed, repeatable moment on the Jalali day.
    private DateTime AtUtc(int year, int month, int day, int hours) =>
        GregorianDate(year, month, day).ToDateTime(new TimeOnly(5, 0), DateTimeKind.Utc).AddHours(hours);
}
