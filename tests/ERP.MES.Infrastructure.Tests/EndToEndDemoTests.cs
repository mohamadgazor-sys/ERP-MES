using ERP.MES.Application.Persistence;
using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Costing;
using ERP.MES.Domain.Requests;
using ERP.MES.Domain.Production;
using ERP.MES.Infrastructure.Security;
using ERP.MES.Infrastructure.Sqlite.Demo;

namespace ERP.MES.Infrastructure.Tests;

// The end-to-end acceptance test (Decision Log #108): a freshly initialised DEMONSTRATION database, the demonstration accounts that replace their one-time passwords, and the real application services
// carry one new contract request from the customer's request to the customer dispatch and the unit metrics. Nothing is placed directly: every step is a use case under the real authorization.
public class EndToEndDemoTests
{
    private const string NewPassword = "My-Own-Pass-Phrase-3";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Dictionary<string, string> ReadCredentials(string path) =>
        File.ReadAllLines(path).Where(line => line.Contains('\t')).Select(line => line.Split('\t')).ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

    private static byte[] Pdf(string text) => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% " + text + "\n%%EOF\n");

    private static long Count(TempDatabase db, string tableAndWhere) => long.Parse(db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<Principal> SignedInAsync(SecurityServices security, string username, string oneTimePassword)
    {
        var first = await security.Authentication.SignInAsync(username, oneTimePassword, Token);
        Assert.Equal(SignInStatus.Succeeded, first.Status);
        Assert.True(first.Principal!.MustChangePassword);
        Assert.Equal(AccountStatus.Succeeded, (await security.Accounts.ChangeOwnPasswordAsync(first.Principal, oneTimePassword, NewPassword, Token)).Status);
        var again = await security.Authentication.SignInAsync(username, NewPassword, Token);
        Assert.Equal(SignInStatus.Succeeded, again.Status);
        Assert.False(again.Principal!.MustChangePassword);
        return again.Principal;
    }

    [Fact]
    public async Task One_contract_request_runs_from_the_customer_to_the_dispatch_and_the_metrics_on_the_seeded_demonstration_database()
    {
        using var db = new TempDatabase();
        var dates = PersistenceTestData.Dates;
        var result = await DemoDatabase.InitializeAsync(db.Path, dates, db.NextCredentialPath(), SecurityFixture.FastPolicy, Token);
        var credentials = ReadCredentials(result.CredentialFile!);
        var security = SecurityServices.Create(db.ContextFactory, SecurityFixture.FastPolicy, hasher: new LowCostHasher());
        var clock = new FakeClock { UtcNow = new DateTime(2026, 10, 24, 7, 30, 0, DateTimeKind.Utc) }; // Saturday 1405/08/02, a working day; the display zone of the test is UTC
        var w = WorkflowServices.Create(db.ContextFactory, security.Authorization, dates, clock);

        var central = await SignedInAsync(security, "demo.central", credentials["demo.central"]);
        var manager = await SignedInAsync(security, "demo.management", credentials["demo.management"]);
        var planning = await SignedInAsync(security, "demo.planning", credentials["demo.planning"]);
        var drawing = await SignedInAsync(security, "demo.drawing", credentials["demo.drawing"]);
        var qc = await SignedInAsync(security, "demo.qc", credentials["demo.qc"]);
        var warehouse = await SignedInAsync(security, "demo.warehouse", credentials["demo.warehouse"]);
        var production = await SignedInAsync(security, "demo.production", credentials["demo.production"]);
        var supervisor = await SignedInAsync(security, "demo.supervisor", credentials["demo.supervisor"]);
        void Tick() => clock.Advance(TimeSpan.FromMinutes(1));
        var sampleRequests = Count(db, "Requests");

        // 1. Central records the customer's request; Management approves it; Planning sends it to Drawing, which accepts it.
        var customer = (await w.Customers.CreateAsync(central, "مشتری آزمون سرتاسری", Token)).Value!;
        var created = (await w.Requests.CreateAsync(central, new CreateRequestCommand(customer.Value.Id, "E2E-1", WorkType.Make, "1405/08/01", null, null), Token)).Value!;
        var withPdf = (await w.Documents.AttachRequestPdfAsync(central, created.Value.Id, created.Version, "request.pdf", Pdf("request"), Token)).Value!;
        var sent = (await w.Requests.SubmitToManagementAsync(central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        Tick();
        var approved = (await w.Lifecycle.ApproveAsync(manager, sent.Value.Id, sent.Version, Token)).Value!;
        var forwarded = (await w.Lifecycle.ForwardToDrawingAsync(planning, approved.Value.Id, approved.Version, Token)).Value!;
        Assert.True((await w.Lifecycle.RecordDrawingResultAsync(drawing, forwarded.Value.Id, forwarded.Version, DrawingOutcome.FullyAccepted, null, Token)).Succeeded);
        var requestId = created.Value.Id;

        // 2. Planning requires a contract for the request, prepares and submits it with its PDF; Management approves it.
        var decided = (await w.Contracts.DecideRequirementAsync(planning, requestId, ContractRequirement.Required, null, Token)).Value!;
        var draft = (await w.Contracts.SaveDraftAsync(planning, requestId, decided.Version, "CN-E2E-1", "1405/08/02", "قرارداد آزمون", null, Token)).Value!;
        var contractPdf = (await w.Contracts.AttachPdfAsync(planning, requestId, draft.Version, "contract.pdf", Pdf("contract"), Token)).Value!;
        var contractSent = (await w.Contracts.SubmitAsync(planning, requestId, contractPdf.Version, Token)).Value!;
        Assert.True((await w.Contracts.ApproveAsync(manager, requestId, contractSent.Version, null, Token)).Succeeded);
        Tick();

        // 3. Planning defines the item (seeded material, piece quantity type) and its single stage in the seeded milling unit, and releases it.
        var piece = (await w.QcMaster.LoadAsync(qc, Token)).Value!.QuantityTypes.First(t => t.Value.Code == "PIECE").Value.Id;
        var material = (await w.Materials.LoadAsync(warehouse, Token)).Value!.Materials.Single(m => m.Value.Code == "ST37-R30").Value.Id;
        var milling = (await w.PlanningMaster.LoadAsync(planning, Token)).Value!.Units.Single(u => u.Value.Code == "MILLING").Value.Id;
        var requestVersion = (await w.Requests.GetAsync(planning, requestId, Token)).Value!.Request.Version;
        var defined = await w.ItemDefinition.DefineItemAsync(planning, requestId, requestVersion, new DefineItemCommand("E2E.10", "قطعه آزمون", 10m, Priority.Urgent, piece, material, 1m, null, null), Token);
        Assert.True(defined.Succeeded, defined.Status + " " + defined.Issue);
        var itemId = defined.Value!.Value.Items.Single().Id;
        var current = (await w.Requests.GetAsync(planning, requestId, Token)).Value!;
        var staged = await w.ItemDefinition.DefineStageAsync(planning, requestId, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand("فرزکاری", 2m, milling, null), Token);
        Assert.True(staged.Succeeded, staged.Status + " " + staged.Issue);

        async Task<StageTarget> TargetAsync()
        {
            var latest = (await w.Requests.GetAsync(planning, requestId, Token)).Value!;
            return new StageTarget(requestId, itemId, 1, latest.Request.Version, latest.Versions.ItemVersion(itemId), latest.Versions.Stages.Where(s => s.Key.ItemId == itemId).ToDictionary(s => s.Key.StageNo, s => s.Value));
        }

        Assert.True((await w.StageWork.PerformAsync(planning, StageAction.Release, await TargetAsync(), Token)).Succeeded);
        Tick();

        // 4. Planning schedules the operator for the day; the milling supervisor sees the stage in the unit queue, dispatches it to the unit's machine and the operator, and starts the work session.
        var queue = (await w.UnitQueue.LoadAsync(supervisor, milling, Token)).Value!;
        var entry = Assert.Single(queue.Queue, e => e.RequestNo == "E2E-1" && e.ItemNo == "E2E.10");
        Assert.True(entry.IsActionable);
        var template = (await w.Calendar.LoadAsync(planning, Token)).Value!.Configuration.Templates.Single(t => t.Value.Code == "D").Value.Id;
        var machine = (await w.PlanningMaster.LoadAsync(planning, Token)).Value!.Machines.Single(m => m.Value.Code == "FCC-06").Value.Id;
        var operatorId = (await w.Operators.LoadAsync(planning, Token)).Value!.Operators.Single(o => o.Value.Code == "OP-DEMO").Value.Id;
        Assert.True((await w.WorkerSchedule.ScheduleAsync(planning, operatorId, new DateOnly(2026, 10, 24), milling, template, null, null, null, Token)).Succeeded);
        Assert.Equal(ValidationIssue.SessionNotSupervisor, (await w.WorkSessions.DispatchAsync(production, new DispatchCommand(itemId, 1, machine, operatorId, null, null, true), Token)).Issue); // the operator cannot dispatch
        clock.UtcNow = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);
        var started = await w.WorkSessions.DispatchAsync(supervisor, new DispatchCommand(itemId, 1, machine, operatorId, entry.StageVersion, null, true), Token);
        Assert.True(started.Succeeded, started.Status + " " + started.Issue);
        Assert.False(started.Value!.Value.QualificationWarning);

        // 5. The operator pauses for missing material from 10:00 to 10:30 and resumes; the supervisor finishes the session at 12:00. The stage is not complete yet.
        clock.UtcNow = new DateTime(2026, 10, 24, 10, 0, 0, DateTimeKind.Utc);
        var reasonId = (await w.WorkSessions.LoadUnitAsync(supervisor, milling, 7, Token)).Value!.Reasons.Single(r => r.Value.Code == PauseReason.MaterialUnavailable).Value.Id;
        var paused = await w.WorkSessions.PauseAsync(production, started.Value.Value.Id, started.Value.Version, reasonId, "مواد نرسید", Token);
        Assert.True(paused.Succeeded, paused.Status + " " + paused.Issue);
        Assert.Equal(ValidationIssue.SubmitBlockedBySessions, (await w.StageWork.PerformAsync(production, StageAction.SubmitForQc, await TargetAsync(), Token)).Issue);
        clock.UtcNow = new DateTime(2026, 10, 24, 10, 30, 0, DateTimeKind.Utc);
        var resumed = await w.WorkSessions.ResumeAsync(production, paused.Value!.Value.Id, paused.Value.Version, null, Token);
        Assert.True(resumed.Succeeded, resumed.Status + " " + resumed.Issue);
        clock.UtcNow = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
        var sessionDone = await w.WorkSessions.FinishAsync(supervisor, resumed.Value!.Value.Id, resumed.Value.Version, null, Token);
        Assert.True(sessionDone.Succeeded, sessionDone.Status + " " + sessionDone.Issue);
        var metrics = (await w.WorkSessions.LoadStageMetricsAsync(supervisor, itemId, 1, false, Token)).Value!;
        Assert.Equal((210d, 210d, 240d, 30d), (metrics.MachineHours.Single().Runtime.TotalMinutes, metrics.LaborHours.Single().Time.TotalMinutes, metrics.Elapsed.Single().Elapsed.TotalMinutes, metrics.PausedByReason.Single().Time.TotalMinutes));

        // 6. Planning approves the session's actual time (not the person who dispatched it); only approved time reaches the cost.
        Assert.True((await w.WorkSessions.ApproveTimeAsync(planning, sessionDone.Value!.Value.Id, sessionDone.Value.Version, Token)).Succeeded);
        Tick();

        // 7. The stage goes to QC, which accepts all ten base units: the route is complete.
        Assert.True((await w.StageWork.PerformAsync(production, StageAction.SubmitForQc, await TargetAsync(), Token)).Succeeded);
        var inspected = await w.QcInspection.RecordInspectionAsync(qc, await TargetAsync(), [new InspectionEntry(InspectionDisposition.Accepted, 10, null)], Token);
        Assert.True(inspected.Succeeded, inspected.Status + " " + inspected.Issue);
        Tick();

        // 8. Warehouse receives the item and calculates its cost from the seeded rate and price; the contract item takes its price from the contract, which Management records with the contract number.
        Assert.True((await w.Completion.ReceiveAsync(warehouse, itemId, "تحویل از کنترل کیفیت", Token)).Succeeded);
        var cost = await w.Costing.CalculateAsync(warehouse, itemId, Token);
        Assert.True(cost.Succeeded, cost.Status + " " + cost.Issue + " " + string.Join(",", cost.WarningList));
        Assert.Equal((4_200_000m, 900_000m, 5_100_000m), (cost.Value!.Calculation.Value.MachiningTotalRial, cost.Value.Calculation.Value.MaterialTotalRial, cost.Value.Calculation.Value.TotalRial));
        Assert.Equal(PriceBasis.Contract, cost.Value.Decision.Value.Basis);
        Assert.Equal(ValidationIssue.FinishedSentNeedsPrice, (await w.Completion.MarkFinishedSentAsync(warehouse, itemId, Token)).Issue);
        var priced = (await w.Costing.SubmitAsync(warehouse, cost.Value.Decision.Value.Id, cost.Value.Decision.Version, Token)).Value!;
        var final = await w.Costing.ApproveAsync(manager, priced.Value.Id, priced.Version, 8_000_000m, null, "CN-E2E-1", Token);
        Assert.True(final.Succeeded, final.Status + " " + final.Issue);

        // 9. Warehouse marks the item finished/sent and records the customer dispatch.
        var finished = await w.Completion.MarkFinishedSentAsync(warehouse, itemId, Token);
        Assert.True(finished.Succeeded, finished.Status + " " + finished.Issue);
        Assert.Equal(8_000_000m, finished.Value!.ApprovedPriceRial);
        Assert.True((await w.Completion.RecordDispatchAsync(warehouse, itemId, "1405/08/02", "WB-E2E-1", null, Token)).Succeeded);

        // 10. Management reads the unit metrics of the day: only approved time counts (the operator's labour time, pause excluded) and nothing is waiting for approval.
        var kpi = (await w.Kpi.ReportAsync(manager, new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 24), Token)).Value!;
        var unit = kpi.Units.Single(u => u.Code == "MILLING");
        Assert.Equal((210, 10L, 1m, 1, true), (unit.ProductiveMinutes, unit.AcceptedBaseUnits, unit.AcceptanceRate, unit.CompletedStages, unit.IsAuthoritative));
        Assert.Equal("OP-DEMO", Assert.Single(kpi.Operators).Code);

        // The history is complete and the sample data was not touched.
        Assert.Equal(sampleRequests + 1, Count(db, "Requests"));
        foreach (var code in new[] { "WarehouseReceiptRecorded", "ItemCostCalculated", "ItemPriceSubmitted", "ItemPriceApproved", "ItemFinishedSent", "CustomerDispatchRecorded", "WorkSessionDispatched", "WorkSessionStarted", "WorkSessionPaused", "WorkSessionResumed", "WorkSessionFinished", "WorkSessionTimeApproved", "QcInspectionRecorded" })
        {
            Assert.Equal(1, Count(db, $"AuditLog WHERE ActionCode = '{code}'"));
        }

        Assert.Equal(0, Count(db, "OvertimeLimits")); // the owner-decision settings stay empty until Planning configures them
        Assert.Equal(0, Count(db, "DailyJobReports")); // the stage is tracked by its session, not by a job report
    }
}
