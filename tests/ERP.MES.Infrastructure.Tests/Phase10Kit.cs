using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Requests;
using ERP.MES.Infrastructure.Security;

namespace ERP.MES.Infrastructure.Tests;

/// <summary>Shared fixture for the phase-10 tests (item contract decisions, work sessions, dispatch, actual time): every department's user, services over a real SQLite file, and request/item/stage builders.</summary>
internal sealed class KitWorld : IDisposable
{
    private const string InitialPassword = "Staff-Pass-Phrase-2";
    private const string OwnPassword = "My-Own-Pass-Phrase-3";

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public required SecurityFixture Fixture { get; init; }

    public required Principal Admin { get; init; }

    public required Principal Central { get; init; }

    public required Principal Manager { get; init; }

    public required Principal Planning { get; init; }

    public required Principal Drawing { get; init; }

    public required Principal Warehouse { get; init; }

    public required Principal Qc { get; init; }

    public required Principal Production { get; init; }

    public required Principal Supervisor { get; init; }

    public required Principal OtherOperator { get; init; }

    public required WorkflowServices Services { get; init; }

    public void Dispose() => Fixture.Dispose();

    public void Tick() => Fixture.Clock.Advance(TimeSpan.FromMinutes(1));

    public long Count(string tableAndWhere) => long.Parse(Fixture.Db.Query("SELECT COUNT(*) FROM " + tableAndWhere).Single(), System.Globalization.CultureInfo.InvariantCulture);

    public static async Task<KitWorld> StartAsync()
    {
        var fixture = await SecurityFixture.CreateFastAsync();
        var admin = await fixture.BootstrapAdminAsync(cancellationToken: Token);
        return new KitWorld
        {
            Fixture = fixture,
            Admin = admin,
            Central = await UserAsync(fixture, admin, "central.one", DepartmentCodes.Central, RoleCodes.Staff),
            Manager = await UserAsync(fixture, admin, "manager.one", DepartmentCodes.Management, RoleCodes.Manager),
            Planning = await UserAsync(fixture, admin, "planning.one", DepartmentCodes.Planning, RoleCodes.Staff),
            Drawing = await UserAsync(fixture, admin, "drawing.one", DepartmentCodes.Drawing, RoleCodes.Staff),
            Warehouse = await UserAsync(fixture, admin, "warehouse.one", DepartmentCodes.Warehouse, RoleCodes.Staff),
            Qc = await UserAsync(fixture, admin, "qc.one", DepartmentCodes.Qc, RoleCodes.Staff),
            Production = await UserAsync(fixture, admin, "production.one", DepartmentCodes.Production, RoleCodes.Staff),
            Supervisor = await UserAsync(fixture, admin, "supervisor.one", DepartmentCodes.Production, RoleCodes.Staff),
            OtherOperator = await UserAsync(fixture, admin, "production.two", DepartmentCodes.Production, RoleCodes.Staff),
            Services = WorkflowServices.Create(fixture.Db.ContextFactory, fixture.Services.Authorization, PersistenceTestData.Dates, fixture.Clock, new FixedAuditContext()),
        };
    }

    private static async Task<Principal> UserAsync(SecurityFixture fixture, Principal admin, string name, string department, string role)
    {
        await fixture.CreateUserAsync(admin, name, InitialPassword, department, role, Token);
        var first = await fixture.SignInAsync(name, InitialPassword, Token);
        await fixture.Services.Accounts.ChangeOwnPasswordAsync(first, InitialPassword, OwnPassword, Token);
        return await fixture.SignInAsync(name, OwnPassword, Token);
    }

    private static byte[] Pdf(string text) => System.Text.Encoding.ASCII.GetBytes("%PDF-1.4\n% " + text + "\n%%EOF\n");

    /// <summary>A MAKE request taken through Central, Management and Drawing, with Planning's contract decision (and, when required, an approved contract).</summary>
    public async Task<(long Id, long Version)> ReadyRequestAsync(string requestNo, bool contractRequired, bool approveContract = true)
    {
        var customer = (await Services.Customers.CreateAsync(Central, "Fictional Customer " + requestNo, Token)).Value!;
        var created = (await Services.Requests.CreateAsync(Central, new CreateRequestCommand(customer.Value.Id, requestNo, WorkType.Make, "1405/07/20", null, null), Token)).Value!;
        Tick();
        var withPdf = (await Services.Documents.AttachRequestPdfAsync(Central, created.Value.Id, created.Version, "a.pdf", Pdf("req"), Token)).Value!;
        Tick();
        var sent = (await Services.Requests.SubmitToManagementAsync(Central, withPdf.Value.Id, withPdf.Version, Token)).Value!;
        Tick();
        var approved = (await Services.Lifecycle.ApproveAsync(Manager, sent.Value.Id, sent.Version, Token)).Value!;
        Tick();
        var forwarded = (await Services.Lifecycle.ForwardToDrawingAsync(Planning, approved.Value.Id, approved.Version, Token)).Value!;
        Tick();
        var returned = await Services.Lifecycle.RecordDrawingResultAsync(Drawing, forwarded.Value.Id, forwarded.Version, DrawingOutcome.FullyAccepted, null, Token);
        Assert.True(returned.Succeeded, returned.Status + " " + returned.Issue);
        Tick();
        var id = created.Value.Id;
        var decided = await Services.Contracts.DecideRequirementAsync(Planning, id, contractRequired ? ContractRequirement.Required : ContractRequirement.NotRequired, null, Token);
        Assert.True(decided.Succeeded, decided.Status + " " + decided.Issue);
        Tick();
        if (contractRequired)
        {
            var draft = (await Services.Contracts.SaveDraftAsync(Planning, id, decided.Value!.Version, "CN-100", "1405/07/25", "قرارداد ساخت", null, Token)).Value!;
            Tick();
            var withContractPdf = (await Services.Contracts.AttachPdfAsync(Planning, id, draft.Version, "contract.pdf", Pdf("contract"), Token)).Value!;
            Tick();
            var submitted = (await Services.Contracts.SubmitAsync(Planning, id, withContractPdf.Version, Token)).Value!;
            Tick();
            if (approveContract)
            {
                Assert.True((await Services.Contracts.ApproveAsync(Manager, id, submitted.Version, null, Token)).Succeeded);
                Tick();
            }
        }

        return (id, await RequestVersionAsync(id));
    }

    public async Task<long> RequestVersionAsync(long id) => (await Services.Requests.GetAsync(Qc, id, Token)).Value!.Request.Version;

    public async Task<long> MaterialAsync(string code = "ST37-R30")
    {
        var outcome = await Services.Materials.AddMaterialAsync(Warehouse, code, "میلگرد ST37", "ST37", "میلگرد", Token);
        Assert.True(outcome.Succeeded, outcome.Status + " " + outcome.Issue);
        return outcome.Value!.Value.Id;
    }

    public async Task<long> PieceTypeAsync() =>
        (await Services.QcMaster.LoadAsync(Qc, Token)).Value!.QuantityTypes.First(t => t.Value.Code == "PIECE").Value.Id;

    public async Task<long> AddUnitAsync(string code)
    {
        var unit = await Services.PlanningMaster.AddUnitAsync(Planning, code, "واحد " + code, Token);
        Assert.True(unit.Succeeded, unit.Status + " " + unit.Issue);
        return unit.Value!.Value.Id;
    }

    public async Task<long> AddMachineAsync(string code, long unit)
    {
        var machine = await Services.PlanningMaster.AddMachineAsync(Planning, code, "ماشین " + code, unit, Token);
        Assert.True(machine.Succeeded, machine.Status + " " + machine.Issue);
        return machine.Value!.Value.Id;
    }

    public async Task<StageTarget> TargetAsync(long requestId, long itemId, int stageNo)
    {
        var current = (await Services.Requests.GetAsync(Qc, requestId, Token)).Value!;
        return new StageTarget(requestId, itemId, stageNo, current.Request.Version, current.Versions.ItemVersion(itemId), current.Versions.Stages.Where(s => s.Key.ItemId == itemId).ToDictionary(s => s.Key.StageNo, s => s.Value));
    }

    /// <summary>Defines an item with the given stage units in a request that has passed every gate; the first stage is released when asked.</summary>
    public async Task<long> DefineItemAsync(long requestId, string itemNo, long[] stageUnits, bool release = false)
    {
        var version = await RequestVersionAsync(requestId);
        var material = long.Parse(Fixture.Db.Query("SELECT Id FROM Materials LIMIT 1").Single(), System.Globalization.CultureInfo.InvariantCulture);
        var defined = await Services.ItemDefinition.DefineItemAsync(Planning, requestId, version, new DefineItemCommand(itemNo, "Part " + itemNo, 10m, Priority.Normal, await PieceTypeAsync(), material, 1m, null, null), Token);
        Assert.True(defined.Succeeded, defined.Status + " " + defined.Issue);
        var itemId = defined.Value!.Value.Items.Single(i => i.ItemNo == itemNo).Id;
        for (var i = 0; i < stageUnits.Length; i++)
        {
            var current = (await Services.Requests.GetAsync(Planning, requestId, Token)).Value!;
            var staged = await Services.ItemDefinition.DefineStageAsync(Planning, requestId, current.Request.Version, itemId, current.Versions.ItemVersion(itemId), new DefineStageCommand("مرحله " + (i + 1), 2m, stageUnits[i], null), Token);
            Assert.True(staged.Succeeded, staged.Status + " " + staged.Issue);
        }

        if (release)
        {
            var released = await Services.StageWork.PerformAsync(Planning, StageAction.Release, await TargetAsync(requestId, itemId, 1), Token);
            Assert.True(released.Succeeded, released.Status + " " + released.Issue);
        }

        Tick();
        return itemId;
    }
}
