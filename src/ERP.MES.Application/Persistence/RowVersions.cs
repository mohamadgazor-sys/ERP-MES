using ERP.MES.Domain.Requests;

namespace ERP.MES.Application.Persistence;

/// <summary>Names one stage of one item: stages have no identity of their own in the Domain, so a stage is the item's id and the stage number.</summary>
public readonly record struct StageRef(long ItemId, int StageNo);

/// <summary>
/// The row versions of a loaded request (Decision Log #23, #77): the request row, every item row and every stage row, each at the version the row had when it was
/// loaded. A caller keeps them with what it showed the user and passes back the ones belonging to the rows it intends to change.
/// </summary>
public sealed record RequestVersions(long Request, IReadOnlyDictionary<long, long> Items, IReadOnlyDictionary<StageRef, long> Stages)
{
    public long ItemVersion(long itemId) =>
        Items.TryGetValue(itemId, out var version) ? version : throw new KeyNotFoundException("The loaded request has no item with id " + itemId + ".");

    public long StageVersion(long itemId, int stageNo) =>
        Stages.TryGetValue(new StageRef(itemId, stageNo), out var version)
            ? version
            : throw new KeyNotFoundException("The loaded request has no stage " + stageNo + " of item " + itemId + ".");
}

/// <summary>A request with its items, stages and approvals, and the row version of every request, item and stage row it was loaded with.</summary>
public sealed record VersionedRequest(Request Value, RequestVersions Versions);

/// <summary>
/// The versions a caller expects the rows it is about to change to have, per row: the request row, specific item rows, specific stage rows. Every named row is
/// compared with the stored row BEFORE the change runs; one mismatch rejects the whole operation with <see cref="ConcurrencyConflictException"/> and nothing is
/// changed. A row that is not named is not compared against the caller's view (it is still protected for the short time between loading and saving, because every row
/// the change touches is saved under its own version). <see cref="None"/> names no row.
/// </summary>
public sealed record RowVersionExpectations
{
    private static readonly IReadOnlyDictionary<long, long> NoItems = new Dictionary<long, long>();
    private static readonly IReadOnlyDictionary<StageRef, long> NoStages = new Dictionary<StageRef, long>();

    public static RowVersionExpectations None { get; } = new();

    public long? Request { get; init; }

    public IReadOnlyDictionary<long, long> Items { get; init; } = NoItems;

    public IReadOnlyDictionary<StageRef, long> Stages { get; init; } = NoStages;

    public static RowVersionExpectations ForRequest(long version) => None.WithRequest(version);

    public static RowVersionExpectations ForItem(long itemId, long version) => None.WithItem(itemId, version);

    public static RowVersionExpectations ForStage(long itemId, int stageNo, long version) => None.WithStage(itemId, stageNo, version);

    public RowVersionExpectations WithRequest(long version) => this with { Request = version };

    public RowVersionExpectations WithItem(long itemId, long version) =>
        this with { Items = new Dictionary<long, long>(Items) { [itemId] = version } };

    public RowVersionExpectations WithStage(long itemId, int stageNo, long version) =>
        this with { Stages = new Dictionary<StageRef, long>(Stages) { [new StageRef(itemId, stageNo)] = version } };

    /// <summary>Everything the loaded request's rows had, so the whole view the caller holds is checked.</summary>
    public static RowVersionExpectations FromLoaded(RequestVersions versions)
    {
        ArgumentNullException.ThrowIfNull(versions);
        return new RowVersionExpectations
        {
            Request = versions.Request,
            Items = new Dictionary<long, long>(versions.Items),
            Stages = new Dictionary<StageRef, long>(versions.Stages),
        };
    }
}
