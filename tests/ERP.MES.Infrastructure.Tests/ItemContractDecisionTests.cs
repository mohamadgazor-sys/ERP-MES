using ERP.MES.Application.Requests;
using ERP.MES.Domain.Contracts;

namespace ERP.MES.Infrastructure.Tests;

// Item-scoped contract decisions (Decision Log #111): the contract document and approval stay at request level; each item may be released from the contract gate by Planning, with a reason, as history.
public class ItemContractDecisionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_item_without_a_decision_follows_the_request_contract()
    {
        using var w = await KitWorld.StartAsync();
        var (id, _) = await w.ReadyRequestAsync("R-1", contractRequired: false);
        await w.MaterialAsync();
        var unit = await w.AddUnitAsync("MILLING");

        var item = await w.DefineItemAsync(id, "A", [unit]);

        Assert.Equal(0, w.Count("ItemContractDecisions"));
        Assert.True((await w.Services.Gates.EvaluateAsync(id, item, Token))!.Satisfied);
    }

    [Fact]
    public async Task Planning_can_release_one_item_from_a_required_contract_that_is_not_yet_approved_for_the_others()
    {
        using var w = await KitWorld.StartAsync();
        var (id, _) = await w.ReadyRequestAsync("R-2", contractRequired: true);
        await w.MaterialAsync();
        var unit = await w.AddUnitAsync("MILLING");
        var a = await w.DefineItemAsync(id, "A", [unit]);
        var b = await w.DefineItemAsync(id, "B", [unit]);

        var decided = await w.Services.Contracts.DecideItemAsync(w.Planning, id, b, ContractRequirement.NotRequired, "قطعه بدون قرارداد", Token);

        Assert.True(decided.Succeeded, decided.Status + " " + decided.Issue);
        Assert.Equal(1, w.Count("ItemContractDecisions"));
        Assert.Equal("ItemContractDecided", Assert.Single(w.Fixture.Db.Query("SELECT ActionCode FROM AuditLog WHERE ActionCode LIKE 'ItemContract%'")));
        Assert.Equal(ContractStatus.NotRequired, (await w.Services.Gates.EvaluateAsync(id, b, Token))!.ContractStatus);
        Assert.Equal(ContractStatus.Approved, (await w.Services.Gates.EvaluateAsync(id, a, Token))!.ContractStatus);
    }

    [Fact]
    public async Task The_gate_is_per_item_so_an_item_that_needs_the_contract_stays_blocked_while_another_is_free()
    {
        using var w = await KitWorld.StartAsync();
        // Required but not approved: the request is returned, the contract is a draft.
        var (id, _) = await StartDraftContractAsync(w, "R-3");
        await w.MaterialAsync();
        var unit = await w.AddUnitAsync("MILLING");
        var free = await w.DefineItemAsync(id, "FREE", [unit], release: false);
        var held = await w.DefineItemAsync(id, "HELD", [unit], release: false);
        Assert.True((await w.Services.Contracts.DecideItemAsync(w.Planning, id, free, ContractRequirement.NotRequired, "بدون قرارداد", Token)).Succeeded);

        Assert.True((await w.Services.Gates.EvaluateAsync(id, free, Token))!.Satisfied);
        Assert.Contains(GateBlocker.DefinitionGate, (await w.Services.Gates.EvaluateAsync(id, held, Token))!.Blockers);
        // The request-level state is still blocked: one item still needs the contract.
        Assert.False((await w.Services.Gates.EvaluateAsync(id, Token))!.Satisfied);
    }

    [Fact]
    public async Task A_decision_needs_a_reason_authorization_and_a_real_change()
    {
        using var w = await KitWorld.StartAsync();
        var (id, _) = await w.ReadyRequestAsync("R-4", contractRequired: true);
        await w.MaterialAsync();
        var unit = await w.AddUnitAsync("MILLING");
        var item = await w.DefineItemAsync(id, "A", [unit]);

        Assert.Equal(ValidationIssue.ReasonRequired, (await w.Services.Contracts.DecideItemAsync(w.Planning, id, item, ContractRequirement.NotRequired, "  ", Token)).Issue);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.Contracts.DecideItemAsync(w.Manager, id, item, ContractRequirement.NotRequired, "x", Token)).Status);
        Assert.Equal(OperationStatus.NotAuthorized, (await w.Services.Contracts.DecideItemAsync(w.Production, id, item, ContractRequirement.NotRequired, "x", Token)).Status);
        Assert.Equal(ValidationIssue.ItemContractUnchanged, (await w.Services.Contracts.DecideItemAsync(w.Planning, id, item, ContractRequirement.Required, "x", Token)).Issue);
        Assert.Equal(ValidationIssue.ItemNotFound, (await w.Services.Contracts.DecideItemAsync(w.Planning, id, 99999, ContractRequirement.NotRequired, "x", Token)).Issue);
        Assert.Equal(0, w.Count("ItemContractDecisions"));
    }

    [Fact]
    public async Task An_item_cannot_be_made_to_require_a_contract_the_request_does_not_have()
    {
        using var w = await KitWorld.StartAsync();
        var (id, _) = await w.ReadyRequestAsync("R-5", contractRequired: false);
        await w.MaterialAsync();
        var unit = await w.AddUnitAsync("MILLING");
        var item = await w.DefineItemAsync(id, "A", [unit]);

        var outcome = await w.Services.Contracts.DecideItemAsync(w.Planning, id, item, ContractRequirement.Required, "نیاز به قرارداد", Token);

        Assert.Equal(ValidationIssue.ItemContractRequestNotRequired, outcome.Issue);
    }

    [Fact]
    public async Task A_decision_is_refused_once_a_stage_of_the_item_was_released_and_history_is_append_only()
    {
        using var w = await KitWorld.StartAsync();
        var (id, _) = await w.ReadyRequestAsync("R-7", contractRequired: true);
        await w.MaterialAsync();
        var unit = await w.AddUnitAsync("MILLING");
        var item = await w.DefineItemAsync(id, "A", [unit]);
        Assert.True((await w.Services.Contracts.DecideItemAsync(w.Planning, id, item, ContractRequirement.NotRequired, "اول", Token)).Succeeded);
        w.Tick();
        Assert.True((await w.Services.Contracts.DecideItemAsync(w.Planning, id, item, ContractRequirement.Required, "تغییر نظر", Token)).Succeeded);
        w.Tick();

        var released = await w.Services.StageWork.PerformAsync(w.Planning, StageAction.Release, await w.TargetAsync(id, item, 1), Token);
        Assert.True(released.Succeeded, released.Status + " " + released.Issue);

        Assert.Equal(ValidationIssue.ItemContractInProduction, (await w.Services.Contracts.DecideItemAsync(w.Planning, id, item, ContractRequirement.NotRequired, "دیر", Token)).Issue);
        var history = (await w.Services.Contracts.ListItemDecisionsAsync(w.Planning, id, Token)).Value!;
        Assert.Equal([ContractRequirement.NotRequired, ContractRequirement.Required], history.Select(h => h.Requirement).ToArray());
        Assert.Equal(2, w.Count("ItemContractDecisions"));
    }

    [Fact]
    public async Task A_stage_of_an_item_held_by_an_unapproved_contract_cannot_be_released_but_a_released_item_can()
    {
        using var w = await KitWorld.StartAsync();
        var (id, _) = await StartDraftContractAsync(w, "R-8");
        await w.MaterialAsync();
        var unit = await w.AddUnitAsync("MILLING");
        var held = await w.DefineItemAsync(id, "HELD", [unit]);
        var free = await w.DefineItemAsync(id, "FREE", [unit]);
        Assert.True((await w.Services.Contracts.DecideItemAsync(w.Planning, id, free, ContractRequirement.NotRequired, "بدون قرارداد", Token)).Succeeded);

        var heldRelease = await w.Services.StageWork.PerformAsync(w.Planning, StageAction.Release, await w.TargetAsync(id, held, 1), Token);
        var freeRelease = await w.Services.StageWork.PerformAsync(w.Planning, StageAction.Release, await w.TargetAsync(id, free, 1), Token);

        Assert.Equal(ValidationIssue.StageGateNotSatisfied, heldRelease.Issue);
        Assert.True(freeRelease.Succeeded, freeRelease.Status + " " + freeRelease.Issue);
    }

    // A required contract that Management has not approved yet.
    private static Task<(long Id, long Version)> StartDraftContractAsync(KitWorld w, string requestNo) => w.ReadyRequestAsync(requestNo, contractRequired: true, approveContract: false);
}
