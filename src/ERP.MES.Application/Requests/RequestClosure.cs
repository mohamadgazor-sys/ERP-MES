using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Requests;

public enum ClosureBlockReason
{
    /// <summary>The request has no items (a request that never produced anything is cancelled, not closed).</summary>
    NoItems = 1,

    /// <summary>A stage of the item is not QC approved yet (the route is not complete).</summary>
    RouteNotComplete,

    /// <summary>Units are waiting for a QC decision.</summary>
    PendingQcDecision,

    /// <summary>A shortfall (scrap or pieces lost at a contractor) waits for Management's or Planning's decision.</summary>
    ShortfallDecisionPending,

    /// <summary>An external stage still has units to send, at the contractor or to be returned.</summary>
    ExternalQuantityOutstanding,

    /// <summary>The route is complete but the item is not finished/sent by the Warehouse yet.</summary>
    NotFinishedSent,
}

/// <summary>One reason the request cannot be closed, naming the item (empty for a request-level reason).</summary>
public sealed record ClosureBlocker(string ItemNo, ClosureBlockReason Reason);

/// <summary>
/// The conditions of the manual closure of a request (owner decision B10, Decision Log #142): nothing may be unresolved. Pure function of the loaded request and the set of items the Warehouse marked finished/sent.
/// An item whose last stage accepted no unit at all (everything scrapped and the shortfall decided) needs no finished/sent mark, because nothing exists to mark (labelled interpretation).
/// </summary>
public static class RequestClosure
{
    public static IReadOnlyList<ClosureBlocker> Evaluate(Request request, IReadOnlySet<long> finishedSentItemIds)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(finishedSentItemIds);
        var blockers = new List<ClosureBlocker>();
        if (request.Items.Count == 0)
        {
            blockers.Add(new ClosureBlocker(string.Empty, ClosureBlockReason.NoItems));
            return blockers;
        }

        foreach (var item in request.Items.OrderBy(i => i.ItemNo, StringComparer.Ordinal))
        {
            if (item.Stages.Count == 0)
            {
                blockers.Add(new ClosureBlocker(item.ItemNo, ClosureBlockReason.RouteNotComplete));
                continue;
            }

            if (item.Stages.Any(s => s.Status == ItemStageStatus.WaitingQc || s.AwaitingQcBaseUnits > 0))
            {
                blockers.Add(new ClosureBlocker(item.ItemNo, ClosureBlockReason.PendingQcDecision));
            }

            if (item.Stages.Any(s => s.Status == ItemStageStatus.WaitingShortfallDecision))
            {
                blockers.Add(new ClosureBlocker(item.ItemNo, ClosureBlockReason.ShortfallDecisionPending));
            }

            if (item.Stages.Any(s => s.Status is ItemStageStatus.WaitingExternalSend or ItemStageStatus.ExternalSent || (s.IsExternal && s.AtContractorBaseUnits > 0)))
            {
                blockers.Add(new ClosureBlocker(item.ItemNo, ClosureBlockReason.ExternalQuantityOutstanding));
            }

            if (!item.IsRouteCompleted)
            {
                if (!blockers.Any(b => b.ItemNo == item.ItemNo))
                {
                    blockers.Add(new ClosureBlocker(item.ItemNo, ClosureBlockReason.RouteNotComplete));
                }

                continue;
            }

            var lastAccepted = item.Stages[^1].AcceptedBaseUnits;
            if (lastAccepted > 0 && !finishedSentItemIds.Contains(item.Id))
            {
                blockers.Add(new ClosureBlocker(item.ItemNo, ClosureBlockReason.NotFinishedSent));
            }
        }

        return blockers;
    }
}
