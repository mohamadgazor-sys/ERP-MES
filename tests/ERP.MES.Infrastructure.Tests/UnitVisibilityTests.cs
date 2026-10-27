using ERP.MES.Application.Production;
using ERP.MES.Application.Requests;
using ERP.MES.Domain.Contracts;
using ERP.MES.Domain.Requests;

namespace ERP.MES.Infrastructure.Tests;

// Per-unit queues after Planning releases an item (owner direction of 2026-10-27): every unit of the route sees its own stage, a future stage is visible with its reason, and visibility is never permission to start.
public class UnitVisibilityTests
{
    private static CancellationToken Token => KitWorld.Token;

    [Fact]
    public async Task Each_unit_of_the_route_sees_only_its_own_stage_and_a_future_stage_is_pending_with_an_explicit_reason()
    {
        using var s = await SessionScene.CreateAsync();

        var milling = (await s.W.Services.UnitQueue.LoadAsync(s.Supervisor, s.Milling, Token)).Value!;
        var turning = (await s.W.Services.UnitQueue.LoadAsync(s.SupervisorTurning, s.Turning, Token)).Value!;

        Assert.Equal([(s.ItemA, 1), (s.ItemB, 1)], milling.Queue.Select(e => (e.ItemId, e.StageNo)).Order().ToArray());
        Assert.Empty(milling.Pending);
        Assert.Empty(turning.Queue);
        var future = Assert.Single(turning.Pending);
        Assert.Equal((s.ItemA, 2, QueueBlock.WaitingPreviousQc), (future.ItemId, future.StageNo, future.Block));
        Assert.False(future.IsActionable);
    }

    [Fact]
    public async Task A_unit_cannot_load_another_units_queue_and_a_future_stage_cannot_be_dispatched_by_its_unit_before_QC_approval()
    {
        using var s = await SessionScene.CreateAsync();

        Assert.Equal(OperationStatus.NotAuthorized, (await s.W.Services.UnitQueue.LoadAsync(s.SupervisorTurning, s.Milling, Token)).Status);
        var dispatch = await s.W.Services.WorkSessions.DispatchAsync(s.SupervisorTurning, s.Dispatch(s.ItemA, s.T1, s.Op3, stage: 2), Token);

        Assert.Equal(ValidationIssue.SessionStageNotOperational, dispatch.Issue);
        Assert.Equal(0, s.W.Count("WorkSessions"));
    }

    [Fact]
    public async Task Seeing_a_released_stage_is_not_permission_for_a_non_supervisor_or_another_department_to_start_work()
    {
        using var s = await SessionScene.CreateAsync();
        var seen = (await s.W.Services.UnitQueue.LoadAsync(s.Operator1, s.Milling, Token)).Value!;
        Assert.Contains(seen.Queue, e => e.ItemId == s.ItemA && e.IsActionable);

        Assert.Equal(ValidationIssue.SessionNotSupervisor, (await s.W.Services.WorkSessions.DispatchAsync(s.Operator1, s.Dispatch(s.ItemA, s.M1, s.Op1), Token)).Issue);
        Assert.Equal(OperationStatus.NotAuthorized, (await s.W.Services.WorkSessions.DispatchAsync(s.W.Manager, s.Dispatch(s.ItemA, s.M1, s.Op1), Token)).Status);
        Assert.Equal(0, s.W.Count("WorkSessions"));
    }

    [Fact]
    public async Task A_stage_held_by_the_items_contract_names_the_exact_gate_and_cannot_be_dispatched_until_the_item_is_released_from_it()
    {
        using var s = await SessionScene.CreateAsync();
        var (requestId, _) = await s.W.ReadyRequestAsync("R-9", contractRequired: true, approveContract: false);
        var item = await s.W.DefineItemAsync(requestId, "H", [s.Milling], release: false);

        var entry = (await s.W.Services.UnitQueue.LoadAsync(s.Supervisor, s.Milling, Token)).Value!.Pending.Single(e => e.ItemId == item);
        Assert.Equal(QueueBlock.GateNotSatisfied, entry.Block);
        Assert.Equal(DefinitionBlock.ContractPendingManagement, entry.Gate!.DefinitionBlock);
        Assert.Equal(ValidationIssue.StageGateNotSatisfied, (await s.W.Services.StageWork.PerformAsync(s.W.Planning, StageAction.Release, await s.W.TargetAsync(requestId, item, 1), Token)).Issue);

        Assert.True((await s.W.Services.Contracts.DecideItemAsync(s.W.Planning, requestId, item, ContractRequirement.NotRequired, "بدون قرارداد", Token)).Succeeded);
        Assert.True((await s.W.Services.StageWork.PerformAsync(s.W.Planning, StageAction.Release, await s.W.TargetAsync(requestId, item, 1), Token)).Succeeded);

        var released = (await s.W.Services.UnitQueue.LoadAsync(s.Supervisor, s.Milling, Token)).Value!.Queue.Single(e => e.ItemId == item);
        Assert.True(released.IsActionable);
        Assert.Null(released.Gate);
    }

    [Fact]
    public async Task Queue_ordering_planning_position_and_management_override_still_apply_with_sessions_present()
    {
        using var s = await SessionScene.CreateAsync();
        await s.StartedAsync(s.ItemA, s.M1, s.Op1);

        var queue = (await s.W.Services.UnitQueue.LoadAsync(s.Supervisor, s.Milling, Token)).Value!.Queue;

        Assert.Equal([s.ItemA, s.ItemB], queue.Select(e => e.ItemId).ToArray());
        Assert.Equal(QueueCategory.InProgress, queue[0].Category);
        Assert.Equal(QueueCategory.Waiting, queue[1].Category);
        var row = new QueueRowRef(s.RequestId, s.ItemB, 1, queue[1].StageVersion, queue[1].OverrideVersion);
        var overridden = await s.W.Services.UnitQueue.OverrideAsync(s.W.Manager, s.Milling, "فوری", [row], Token);
        Assert.True(overridden.Succeeded, overridden.Status + " " + overridden.Issue);
        Assert.Equal(1, (await s.W.Services.UnitQueue.LoadAsync(s.Supervisor, s.Milling, Token)).Value!.Queue.Single(e => e.ItemId == s.ItemB).OverrideRank);
    }
}
