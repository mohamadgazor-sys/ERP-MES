namespace ERP.MES.Domain.Quality;

/// <summary>One part of a set: what it is and how many base units it counts (Decision Log #90: a set's composition is defined explicitly and never assumed).</summary>
public sealed class QuantityTypeMember
{
    public QuantityTypeMember(int memberNo, string description, int baseUnits)
    {
        if (memberNo < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(memberNo), memberNo, "Member numbers start at 1.");
        }

        if (baseUnits < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(baseUnits), baseUnits, "A member counts at least one base unit.");
        }

        MemberNo = memberNo;
        Description = UnitOfMeasure.RequireName(description);
        BaseUnits = baseUnits;
    }

    public int MemberNo { get; }

    public string Description { get; }

    public int BaseUnits { get; }
}

/// <summary>
/// A quantity type (piece, pair, set...): what countable thing is ordered, produced and inspected (Decision Log #90, R3). It is NOT a unit of measure. Every type maps through a
/// controlled mapping to the base counting unit (a <see cref="UnitKind.Count"/> unit such as PCS): piece = 1 base unit, pair = 2, and a set has NO standard composition - its members
/// are listed explicitly and <see cref="BaseUnitsPerType"/> is their sum. QC inspects at the level of base units, so a pair can be inspected as individual pieces.
///
/// The definition (code, base unit, mapping, members) is immutable: a different mapping is a different quantity type, so quantities already recorded can never change meaning. Only
/// the Persian name and the active flag can change. Definitions are retired, never deleted.
/// </summary>
public sealed class QuantityType
{
    private readonly List<QuantityTypeMember> _members;

    // For the persistence layer.
    private QuantityType()
    {
        Code = string.Empty;
        Name = string.Empty;
        _members = [];
    }

    private QuantityType(long id, string code, string name, long baseUnitId, int baseUnitsPerType, List<QuantityTypeMember> members, long? createdByUserId, DateTime createdAtUtc)
    {
        Id = id;
        Code = UnitOfMeasure.RequireCode(code);
        Name = UnitOfMeasure.RequireName(name);
        BaseUnitId = baseUnitId;
        BaseUnitsPerType = baseUnitsPerType;
        _members = members;
        IsActive = true;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Requirements.Utc(createdAtUtc, nameof(createdAtUtc));
        UpdatedAtUtc = createdAtUtc;
    }

    public long Id { get; }

    public string Code { get; }

    public string Name { get; private set; }

    /// <summary>The unit of measure (of kind Count) that QC counts in. The caller has checked the kind; the Domain cannot see other aggregates.</summary>
    public long BaseUnitId { get; }

    /// <summary>How many base units one of this type counts (piece 1, pair 2; for a set the sum of its members).</summary>
    public int BaseUnitsPerType { get; }

    /// <summary>The members of a set, in order; empty for a piece-like type.</summary>
    public IReadOnlyList<QuantityTypeMember> Members => _members;

    public bool IsSet => _members.Count > 0;

    public bool IsActive { get; private set; }

    public long? CreatedByUserId { get; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>A type that counts <paramref name="baseUnitsPerType"/> identical base units (piece = 1, pair = 2, ...).</summary>
    public static QuantityType CreateSimple(long id, string code, string name, long baseUnitId, int baseUnitsPerType, long? createdByUserId, DateTime createdAtUtc)
    {
        if (baseUnitsPerType < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(baseUnitsPerType), baseUnitsPerType, "A quantity type counts at least one base unit.");
        }

        return new QuantityType(id, code, name, baseUnitId, baseUnitsPerType, [], createdByUserId, createdAtUtc);
    }

    /// <summary>
    /// A set with an explicit composition. There must be at least two members (a one-member "set" is a simple type); every member is described and counts at least one base unit;
    /// the base units of the set are their sum.
    /// </summary>
    public static QuantityType CreateSet(long id, string code, string name, long baseUnitId, IEnumerable<(string Description, int BaseUnits)> members, long? createdByUserId, DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(members);
        var list = members.Select((m, i) => new QuantityTypeMember(i + 1, m.Description, m.BaseUnits)).ToList();
        if (list.Count < 2)
        {
            throw new ArgumentException("A set lists at least two members; a type with one kind of part is a simple quantity type.", nameof(members));
        }

        return new QuantityType(id, code, name, baseUnitId, list.Sum(m => m.BaseUnits), list, createdByUserId, createdAtUtc);
    }

    public void Rename(string name, DateTime changedAtUtc)
    {
        Name = UnitOfMeasure.RequireName(name);
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }

    public void SetActive(bool isActive, DateTime changedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = Requirements.Utc(changedAtUtc, nameof(changedAtUtc));
    }
}
