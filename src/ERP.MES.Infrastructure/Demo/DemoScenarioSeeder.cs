using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Requests;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Production;
using ERP.MES.Domain.Scheduling;
using ERP.MES.Infrastructure.Persistence;
using ERP.MES.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Demo;

/// <summary>What the management scenario created (all numbers are zero when it was already present).</summary>
public sealed record DemoScenarioSummary(bool Seeded, int RequestsCreated, string? Warning);

/// <summary>
/// The FICTIONAL management-demonstration scenario (owner request of 2026-11-13). Every record is created through the real application services under the real demonstration accounts, so the
/// permission rules, the gates, the workflow history and the audit trail are those of normal operation; nothing is written around them. It shows, with consistent data: a new customer, a MAKE
/// request that is approved, drawn and decided (no contract), whose items have routes (internal and external), are released with the single "send all items" action, run on a machine with a work
/// session and a pause, are inspected by QC, and an external stage with a partial return and pieces the contractor never returned that wait for Management's shortfall decision; a REPAIR request in
/// the unit queue; a contract request waiting for Management; a machine downtime (accepted and awaiting review), a calendar closure and an overtime assignment.
///
/// Repeatable: a request is created only when its business key is absent, so running the scenario on a seeded database does nothing. The accounts' "must change password" flag is lifted for the
/// duration of the scenario and restored afterwards for exactly the accounts that had it.
/// </summary>
public sealed class DemoScenarioSeeder(IDbContextFactory<ErpMesDbContext> contexts, WorkflowServices services, IClock clock)
{
    public const string CustomerName = "شرکت صنعتی نمونه فرضی د " + DemoDataSeeder.DemoNameSuffix;
    public const string MakeRequestNo = DemoDataSeeder.RequestNoPrefix + "1405-101";
    public const string RepairRequestNo = DemoDataSeeder.RequestNoPrefix + "1405-102";
    public const string ContractRequestNo = DemoDataSeeder.RequestNoPrefix + "1405-103";

    private static readonly CancellationToken None = CancellationToken.None;

    private static byte[] Pdf(string text) => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% Fictional demonstration document: " + text + "\n%%EOF\n");

    private static T Ok<T>(Outcome<T> outcome, string what)
    {
        if (!outcome.Succeeded)
        {
            throw new InvalidOperationException("Demo scenario step failed: " + what + " (" + outcome.Status + " " + outcome.Issue + ")");
        }

        return outcome.Value!;
    }

    private async Task<Dictionary<string, Principal>> PrincipalsAsync(CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var users = await context.Users.Include(u => u.Department).Include(u => u.Role).AsNoTracking().ToListAsync(cancellationToken);
        return users.ToDictionary(u => u.Username.Value, u => new Principal(u.Id, u.Username.Value, u.DisplayName, u.Department.Code, u.Role.Code, MustChangePassword: false), StringComparer.Ordinal);
    }

    public async Task<DemoScenarioSummary> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await ScenarioExistsAsync(cancellationToken))
        {
            return new DemoScenarioSummary(false, 0, null);
        }

        await using (var context = await contexts.CreateDbContextAsync(cancellationToken))
        {
            // Remember who must still change the password, lift the flag for the scenario and put it back afterwards (the accounts and their one-time passwords are otherwise untouched).
            var pending = await context.Set<ERP.MES.Application.Security.UserCredential>().Where(c => c.MustChangePassword).Select(c => c.UserId).ToListAsync(cancellationToken);
            await context.Set<ERP.MES.Application.Security.UserCredential>().ExecuteUpdateAsync(s => s.SetProperty(c => c.MustChangePassword, false), cancellationToken);
            try
            {
                var created = await RunAsync(cancellationToken);
                return new DemoScenarioSummary(true, created, null);
            }
            finally
            {
                await using var restore = await contexts.CreateDbContextAsync(CancellationToken.None);
                await restore.Set<ERP.MES.Application.Security.UserCredential>().Where(c => pending.Contains(c.UserId)).ExecuteUpdateAsync(s => s.SetProperty(c => c.MustChangePassword, true), CancellationToken.None);
            }
        }
    }

    private async Task<bool> ScenarioExistsAsync(CancellationToken cancellationToken)
    {
        var all = await new EfRequestRepository(contexts).ListAsync(null, cancellationToken);
        return all.Any(r => r.RequestNo.Value == MakeRequestNo);
    }

    private async Task<int> RunAsync(CancellationToken ct)
    {
        var p = await PrincipalsAsync(ct);
        var secretary = p["demo.secretary"];
        var management = p["demo.management"];
        var planning = p["demo.planning"];
        var drawing = p["demo.drawing"];
        var qc = p["demo.qc"];
        var warehouse = p["demo.warehouse"];
        var production = p["demo.production"];
        var supervisor = p["demo.supervisor"];
        var admin = p["demo.admin"];
        var maintenance = p["demo.maintenance"];
        var productionManager = p["demo.productionmanager"];
        var w = services;

        // ---- master data the scenario needs: an external plating route, its stage title and the units/machines/operator/material that already exist ----
        var data = Ok(await w.PlanningMaster.LoadAsync(planning, ct), "load planning master data");
        var milling = data.Units.Single(u => u.Value.Code == "MILLING").Value.Id;
        var turning = data.Units.Single(u => u.Value.Code == "TURNING").Value.Id;
        var fcc = data.Machines.Single(m => m.Value.Code == "FCC-06").Value.Id;
        var plating = data.Units.FirstOrDefault(u => u.Value.Code == "PLATING-CO")?.Value.Id
            ?? Ok(await w.PlanningMaster.AddUnitAsync(admin, "PLATING-CO", "آبکاری بیرونی " + DemoDataSeeder.DemoNameSuffix, ct, ProductionUnitCategory.ExternalPlating), "add the external plating route").Value.Id;
        var titles = Ok(await w.PlanningMaster.ListStageTitlesAsync(planning, ct), "list stage titles");
        if (!titles.Any(t => t.Value.Title == "آبکاری بیرونی"))
        {
            Ok(await w.PlanningMaster.AddStageTitleAsync(planning, "آبکاری بیرونی", ct), "add the plating stage title");
        }

        var piece = Ok(await w.QcMaster.LoadAsync(qc, ct), "load units of measure").QuantityTypes.First(t => t.Value.Code == "PIECE").Value.Id;
        var material = Ok(await w.Materials.LoadAsync(warehouse, ct), "load materials").Materials.First(m => m.Value.Code == "ST37-R30").Value.Id;
        var operatorId = Ok(await w.Operators.LoadAsync(planning, ct), "load operators").Operators.First(o => o.Value.Code == "OP-DEMO").Value.Id;

        var start = clock.UtcNow.Date.AddDays(-1).AddHours(5);
        var scenarioClock = clock as SeedClock;
        void At(int hour, int minute = 0)
        {
            if (scenarioClock is not null)
            {
                scenarioClock.Set(start.AddHours(hour).AddMinutes(minute));
            }
        }

        async Task<long> NewCustomerAsync()
        {
            var existing = Ok(await w.Customers.ListAsync(secretary, true, ct), "list customers").FirstOrDefault(c => c.Value.Name == CustomerName);
            return existing?.Value.Id ?? Ok(await w.Customers.CreateAsync(secretary, CustomerName, ct), "create the customer").Value.Id;
        }

        async Task<(long Id, long Version)> ApprovedAsync(long customerId, string requestNo, WorkType type, string title, string drawingNote)
        {
            var created = Ok(await w.Requests.CreateAsync(secretary, new CreateRequestCommand(customerId, requestNo, type, "1405/08/01", DemoDataSeeder.TitlePrefix + title, drawingNote), ct), "create request " + requestNo);
            var withPdf = Ok(await w.Documents.AttachRequestPdfAsync(secretary, created.Value.Id, created.Version, requestNo + ".pdf", Pdf(requestNo), ct), "attach the request PDF");
            var sent = Ok(await w.Requests.SubmitToManagementAsync(secretary, withPdf.Value.Id, withPdf.Version, ct), "submit to Management");
            var approved = Ok(await w.Lifecycle.ApproveAsync(management, sent.Value.Id, sent.Version, ct), "Management approval");
            var forwarded = Ok(await w.Lifecycle.ForwardToDrawingAsync(planning, approved.Value.Id, approved.Version, ct), "forward to Drawing");
            Ok(await w.Lifecycle.RecordDrawingResultAsync(drawing, forwarded.Value.Id, forwarded.Version, DrawingOutcome.FullyAccepted, "نقشه‌ها بررسی شد و قابل ساخت است.", ct), "Drawing result");
            return (created.Value.Id, (await RequestVersionAsync(planning, created.Value.Id, ct)));
        }

        async Task<long> DefineItemAsync(long requestId, DefineItemCommand command)
        {
            var version = await RequestVersionAsync(planning, requestId, ct);
            var saved = Ok(await w.ItemDefinition.DefineItemAsync(planning, requestId, version, command, ct), "define item " + command.CustomerItemRef);
            return saved.Value.Items.Single(i => i.CustomerItemRef == command.CustomerItemRef).Id;
        }

        async Task DefineStageAsync(long requestId, long itemId, DefineStageCommand command)
        {
            var current = Ok(await w.Requests.GetAsync(planning, requestId, ct), "read the request");
            Ok(await w.ItemDefinition.DefineStageAsync(planning, requestId, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), command, ct), "define stage " + command.StageTitle);
        }

        async Task<StageTarget> TargetAsync(Principal reader, long requestId, long itemId, int stageNo)
        {
            var current = Ok(await w.Requests.GetAsync(reader, requestId, ct), "read the request");
            return new StageTarget(requestId, itemId, stageNo, current.Request.Version, current.Versions.ItemVersion(itemId), current.Versions.Stages.Where(s => s.Key.ItemId == itemId).ToDictionary(s => s.Key.StageNo, s => s.Value));
        }

        var customer = await NewCustomerAsync();
        var created = 0;

        // ======== 1. MAKE request: approved, drawn, no contract; items with internal and external routes ========
        At(1);
        var make = await ApprovedAsync(customer, MakeRequestNo, WorkType.Make, "ساخت شفت، بوش و صفحه نگهدارنده (نمونه مدیریتی)", "داده فرضی برای نمایش به مدیریت.");
        created++;
        At(2);
        Ok(await w.Contracts.DecideRequirementAsync(planning, make.Id, ContractRequirement.NotRequired, "این درخواست قرارداد جداگانه ندارد.", ct), "contract decision (not required)");

        At(3);
        var shaft = await DefineItemAsync(make.Id, new DefineItemCommand("S-1", "شفت خروجی آبکاری‌شده", 10m, Priority.Urgent, piece, material, 2.4m, 350m, 40m));
        var bush = await DefineItemAsync(make.Id, new DefineItemCommand("B-1", "بوش فولادی", 20m, Priority.Normal, piece, material, 0.6m, 80m, 50m));
        var holder = await DefineItemAsync(make.Id, new DefineItemCommand("H-1", "صفحه نگهدارنده", 5m, Priority.VeryUrgent, piece, material, 3.1m, 200m, 120m));
        await DefineStageAsync(make.Id, shaft, new DefineStageCommand("آبکاری بیرونی", 0m, plating, null, null, 5));
        await DefineStageAsync(make.Id, shaft, new DefineStageCommand("تراشکاری", 3m, turning, null));
        await DefineStageAsync(make.Id, shaft, new DefineStageCommand("کنترل نهایی", 1m, milling, null));
        await DefineStageAsync(make.Id, bush, new DefineStageCommand("فرزکاری", 2m, milling, fcc));
        await DefineStageAsync(make.Id, holder, new DefineStageCommand("فرزکاری", 4m, milling, fcc));

        // ---- the single "send all items to the work queue" action by Planning (workflow history + audit) ----
        At(4);
        var release = Ok(await w.StageWork.ReleaseAllAsync(planning, make.Id, ct), "release all items");
        if (release.Released.Count != 3)
        {
            throw new InvalidOperationException("Demo scenario: expected three items to be released, got " + release.Released.Count + ".");
        }

        // ---- the bush: dispatched to the machine and the operator, a pause for missing material, finished, time approved, submitted, accepted by QC ----
        At(5);
        var started = Ok(await w.WorkSessions.DispatchAsync(supervisor, new DispatchCommand(bush, 1, fcc, operatorId, null, "نوبت نمایشی خارج از جدول شیفت", true), ct), "dispatch the bush");
        At(7);
        var pauseReason = Ok(await w.WorkSessions.LoadUnitAsync(supervisor, milling, 7, ct), "load unit work").Reasons.First(r => r.Value.Code == PauseReason.MaterialUnavailable).Value.Id;
        var paused = Ok(await w.WorkSessions.PauseAsync(production, started.Value.Id, started.Version, pauseReason, "مواد با تأخیر رسید", ct), "pause the session");
        At(7, 30);
        var resumed = Ok(await w.WorkSessions.ResumeAsync(production, paused.Value.Id, paused.Version, null, ct), "resume the session");
        At(10);
        var finished = Ok(await w.WorkSessions.FinishAsync(supervisor, resumed.Value.Id, resumed.Version, "پایان کار", ct), "finish the session");
        Ok(await w.WorkSessions.ApproveTimeAsync(planning, finished.Value.Id, finished.Version, ct), "approve the session time");
        At(10, 20);
        Ok(await w.StageWork.PerformAsync(production, StageAction.SubmitForQc, await TargetAsync(production, make.Id, bush, 1), ct), "submit the bush for QC");
        At(11);
        Ok(await w.QcInspection.RecordInspectionAsync(qc, await TargetAsync(qc, make.Id, bush, 1), [new InspectionEntry(InspectionDisposition.Accepted, 20, null)], ct), "QC accepts the bush");

        // ---- the shaft: external plating, partial return, QC acceptance, pieces never returned -> waiting for Management's shortfall decision ----
        At(11, 30);
        var external = w.ExternalWork;
        async Task<ExternalWorkEntry> ShaftEntryAsync(Principal reader) => Ok(await external.LoadAsync(reader, true, ct), "load external work").Entries.Single(e => e.ItemId == shaft);
        var dispatch = Ok(await external.DispatchAsync(warehouse, (await ShaftEntryAsync(warehouse)).ToStageTarget(), 10, "شفت‌های خام برای آبکاری", "ارسال با کامیون پیمانکار", ct), "dispatch to the contractor");
        At(13);
        Ok(await external.ReturnAsync(warehouse, (await ShaftEntryAsync(warehouse)).ToStageTarget(), dispatch.Id, 7, "بازگشت جزئی", ct), "partial return");
        var qcEntry = Ok(await w.QcInspection.LoadAsync(qc, ct), "load the QC worklist").Entries.Single(e => e.ItemId == shaft);
        Ok(await w.QcInspection.RecordInspectionAsync(qc, qcEntry.ToStageTarget(), [new InspectionEntry(InspectionDisposition.Accepted, 7, null)], ct), "QC accepts the returned shafts");
        At(14);
        Ok(await external.MarkMissingAsync(warehouse, (await ShaftEntryAsync(warehouse)).ToStageTarget(), dispatch.Id, 3, "پیمانکار سه قطعه را تحویل نداد و اعلام مفقودی کرد", ct), "record the pieces lost at the contractor");

        // ======== 2. REPAIR request: approved and drawn; one item released into the milling queue ========
        At(15);
        var repair = await ApprovedAsync(customer, RepairRequestNo, WorkType.Repair, "تعمیر گلند آب‌بندی (نمونه مدیریتی)", "داده فرضی برای نمایش به مدیریت.");
        created++;
        var gland = await DefineItemAsync(repair.Id, new DefineItemCommand("G-1", "گلند آب‌بندی", 8m, Priority.Urgent, null, null, null, null, null, DeliveryDate: DeliveryDate(), WarehouseItemCode: "WH-DEMO-GL-101"));
        await DefineStageAsync(repair.Id, gland, new DefineStageCommand("فرزکاری", 3m, milling, fcc));
        Ok(await w.StageWork.ReleaseAllAsync(planning, repair.Id, ct), "release the repair request");

        // ======== 3. MAKE request with a contract that waits for Management's approval ========
        At(16);
        var contract = await ApprovedAsync(customer, ContractRequestNo, WorkType.Make, "ساخت چرخ‌دنده (قرارداد در انتظار تأیید مدیریت)", "داده فرضی برای نمایش به مدیریت.");
        created++;
        var decided = Ok(await w.Contracts.DecideRequirementAsync(planning, contract.Id, ContractRequirement.Required, "مشتری قرارداد کتبی خواسته است.", ct), "contract decision (required)");
        var draft = Ok(await w.Contracts.SaveDraftAsync(planning, contract.Id, decided.Version, "CN-DEMO-103", "1405/08/10", "قرارداد ساخت چرخ‌دنده", null, ct), "contract draft");
        var withPdf = Ok(await w.Contracts.AttachPdfAsync(planning, contract.Id, draft.Version, "contract-103.pdf", Pdf("contract 103"), ct), "contract PDF");
        Ok(await w.Contracts.SubmitAsync(planning, contract.Id, withPdf.Version, ct), "submit the contract");
        var gear = await DefineItemAsync(contract.Id, new DefineItemCommand("C-1", "چرخ‌دنده مارپیچ", 12m, Priority.Normal, piece, material, 1.8m, 120m, 90m));
        await DefineStageAsync(contract.Id, gear, new DefineStageCommand("فرزکاری", 5m, milling, fcc));

        // ======== 4. Machine downtime, a calendar closure and overtime ========
        var today = DateOnly.FromDateTime(clock.UtcNow.Date);
        var nextDay = today.AddDays(2);
        var reported = Ok(await w.Downtime.ReportAsync(maintenance, fcc, DowntimeReason.PreventiveMaintenance, nextDay.ToDateTime(new TimeOnly(8, 0)), nextDay.ToDateTime(new TimeOnly(12, 0)), "سرویس دوره‌ای و تعویض روغن", ct), "report downtime");
        Ok(await w.Downtime.DecideAsync(planning, reported.Value.Id, reported.Version, true, "با برنامهٔ تولید هماهنگ شد.", ct), "accept downtime");
        Ok(await w.Downtime.ReportAsync(maintenance, fcc, DowntimeReason.Breakdown, nextDay.AddDays(3).ToDateTime(new TimeOnly(8, 0)), nextDay.AddDays(3).ToDateTime(new TimeOnly(10, 0)), "نشت هیدرولیک؛ در انتظار بررسی برنامه‌ریزی", ct), "report a downtime awaiting review");
        Ok(await w.Calendar.AddExceptionAsync(planning, today.AddDays(9), CalendarExceptionKind.Off, null, null, "تعطیلی رسمی نمونه", ct), "calendar closure");
        var limits = Ok(await w.Overtime.LoadAsync(management, today, today.AddDays(14), ct), "load overtime");
        Ok(await w.Overtime.SetLimitsAsync(management, limits.Limits?.Version ?? 0, 120, 600, ct), "overtime example limits");
        var overtimeDay = today.AddDays(1);
        var proposal = Ok(await w.Overtime.ProposeAsync(supervisor, operatorId, milling, fcc, overtimeDay.ToDateTime(new TimeOnly(17, 30)), overtimeDay.ToDateTime(new TimeOnly(19, 0)), "تکمیل سفارش فوری بوش‌ها", ct), "propose overtime");
        Ok(await w.Overtime.DecideAsync(productionManager, proposal.Value.Id, proposal.Version, true, "تأیید شد.", ct), "approve overtime");
        return created;
    }

    private static DateOnly DeliveryDate() => DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(30));

    private async Task<long> RequestVersionAsync(Principal reader, long requestId, CancellationToken ct) =>
        Ok(await services.Requests.GetAsync(reader, requestId, ct), "read the request").Request.Version;
}

/// <summary>A clock the scenario moves forward by hand so the sample records carry believable, ordered times.</summary>
public sealed class SeedClock(DateTime start) : IClock
{
    public DateTime UtcNow { get; private set; } = start;

    public void Set(DateTime utc) => UtcNow = utc > UtcNow ? utc : UtcNow.AddSeconds(30);
}
