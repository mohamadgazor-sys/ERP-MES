using ERP.MES.Domain.Quality;

namespace ERP.MES.Domain.Costing;

public enum CostLineKind
{
    /// <summary>Approved reported machine time times the machine rate in force on the work date.</summary>
    Machining = 1,

    /// <summary>Raw material: weight per base unit times the base units that entered production times the price per kg in force on the price date.</summary>
    Material = 2,
}

/// <summary>Where the price comes from (Decision Log #95 D4): the formula result, or the contract amount of an approved contract.</summary>
public enum PriceBasis
{
    Calculated = 1,
    Contract = 2,
}

public enum PriceDecisionStatus
{
    /// <summary>Calculated by Warehouse, not yet sent to Management.</summary>
    Draft = 1,

    Submitted = 2,

    Approved = 3,
}

/// <summary>One line of a cost snapshot with every input that produced it, so a reviewer can recompute the figure. Money is whole Iranian Rial.</summary>
public sealed class ItemCostLine
{
    private ItemCostLine()
    {
    }

    public ItemCostLine(int lineNo, CostLineKind kind, long? jobReportLineId, long? machineId, long? rateId, DateOnly? workDate, int? minutes, decimal? weightKg, decimal unitPriceRial, decimal amountRial)
    {
        if (amountRial < 0 || amountRial != decimal.Round(amountRial, 0, MidpointRounding.AwayFromZero))
        {
            throw new ArgumentOutOfRangeException(nameof(amountRial), amountRial, "A cost line is a whole, non-negative number of Rial.");
        }

        LineNo = lineNo;
        Kind = kind;
        JobReportLineId = jobReportLineId;
        MachineId = machineId;
        RateId = rateId;
        WorkDate = workDate;
        Minutes = minutes;
        WeightKg = weightKg;
        UnitPriceRial = unitPriceRial;
        AmountRial = amountRial;
    }

    public long Id { get; private set; }

    public int LineNo { get; private set; }

    public CostLineKind Kind { get; private set; }

    /// <summary>The approved report line the machining time came from (machining lines).</summary>
    public long? JobReportLineId { get; private set; }

    public long? MachineId { get; private set; }

    /// <summary>The machine rate (or, for material, the price row) that was used; see the snapshot for the material price id.</summary>
    public long? RateId { get; private set; }

    public DateOnly? WorkDate { get; private set; }

    public int? Minutes { get; private set; }

    /// <summary>Kilograms of raw material charged (material line).</summary>
    public decimal? WeightKg { get; private set; }

    /// <summary>Rial per hour (machining) or Rial per kg (material).</summary>
    public decimal UnitPriceRial { get; private set; }

    public decimal AmountRial { get; private set; }
}

/// <summary>
/// An immutable cost snapshot of one item (docs/COSTING_DESIGN.md, Decision Log #91, #95 D3, #104): approved actual report time times the rate in force on each line's work date, plus the raw material
/// of the base units that entered production, absorbed over the QC-accepted base units. Recalculation adds a new snapshot; nothing is ever edited or deleted.
/// </summary>
public sealed class ItemCostCalculation
{
    public const string FormulaVersion = "v1";

    private readonly List<ItemCostLine> _lines = [];

    private ItemCostCalculation()
    {
    }

    public ItemCostCalculation(
        long requestItemId, int sequenceNo, long acceptedBaseUnits, long consumedBaseUnits, long? materialId, long? materialPriceId, DateOnly? priceDate, IEnumerable<ItemCostLine> lines, long calculatedByUserId, DateTime calculatedAtUtc)
    {
        if (acceptedBaseUnits <= 0 || consumedBaseUnits < acceptedBaseUnits)
        {
            throw new ArgumentOutOfRangeException(nameof(acceptedBaseUnits), acceptedBaseUnits, "A cost needs accepted base units, and no more may be accepted than entered production.");
        }

        RequestItemId = requestItemId;
        SequenceNo = sequenceNo;
        Formula = FormulaVersion;
        AcceptedBaseUnits = acceptedBaseUnits;
        ConsumedBaseUnits = consumedBaseUnits;
        MaterialId = materialId;
        MaterialPriceId = materialPriceId;
        PriceDate = priceDate;
        _lines.AddRange(lines);
        if (_lines.Count == 0 || _lines.Select((l, i) => l.LineNo == i + 1).Any(ok => !ok))
        {
            throw new ArgumentException("A snapshot has at least one line, numbered from 1.", nameof(lines));
        }

        MachiningTotalRial = _lines.Where(l => l.Kind == CostLineKind.Machining).Sum(l => l.AmountRial);
        MaterialTotalRial = _lines.Where(l => l.Kind == CostLineKind.Material).Sum(l => l.AmountRial);
        TotalRial = MachiningTotalRial + MaterialTotalRial;
        UnitCostRial = decimal.Round(TotalRial / acceptedBaseUnits, 0, MidpointRounding.AwayFromZero);
        CalculatedByUserId = calculatedByUserId;
        CalculatedAtUtc = Requirements.Utc(calculatedAtUtc, nameof(calculatedAtUtc));
    }

    public long Id { get; private set; }

    public long RequestItemId { get; private set; }

    /// <summary>1 for the first calculation of the item, then 2, 3 ... The latest one is the current one.</summary>
    public int SequenceNo { get; private set; }

    public string Formula { get; private set; } = FormulaVersion;

    /// <summary>The produced quantity: base units accepted by QC at the last stage (Decision Log #95 D3).</summary>
    public long AcceptedBaseUnits { get; private set; }

    /// <summary>The base units that entered production (the first stage's input); scrap and rework are absorbed in the accepted units' cost.</summary>
    public long ConsumedBaseUnits { get; private set; }

    public long? MaterialId { get; private set; }

    public long? MaterialPriceId { get; private set; }

    /// <summary>The date the material price was taken for (a labelled default: the day the first stage was released).</summary>
    public DateOnly? PriceDate { get; private set; }

    public decimal MachiningTotalRial { get; private set; }

    public decimal MaterialTotalRial { get; private set; }

    /// <summary>The sum of the rounded lines. The total is authoritative; the unit cost is derived.</summary>
    public decimal TotalRial { get; private set; }

    /// <summary>The total divided over the accepted base units, to whole Rial.</summary>
    public decimal UnitCostRial { get; private set; }

    public long CalculatedByUserId { get; private set; }

    public DateTime CalculatedAtUtc { get; private set; }

    public IReadOnlyList<ItemCostLine> Lines => _lines;
}

/// <summary>
/// The price decision on one cost snapshot (Decision Log #95 D4, #104): Warehouse submits it, Management approves it. The calculated price proposes the snapshot's total and Management may change it
/// with a mandatory reason; for an item of an approved contract the contract amount is the price - the application stores no contract amount, so Management records it with the contract reference, and
/// the calculated cost stays information. The proposal is kept next to the approved price.
/// </summary>
public sealed class ItemPriceDecision
{
    public const int MaxReasonLength = 500;

    public const int MaxReferenceLength = 100;

    public const decimal MaxPriceRial = 10_000_000_000_000_000m;

    private ItemPriceDecision()
    {
    }

    public ItemPriceDecision(ItemCostCalculation calculation, PriceBasis basis)
    {
        ArgumentNullException.ThrowIfNull(calculation);
        Calculation = calculation;
        RequestItemId = calculation.RequestItemId;
        Basis = basis;
        ProposedPriceRial = basis == PriceBasis.Calculated ? calculation.TotalRial : null;
        Status = PriceDecisionStatus.Draft;
    }

    public long Id { get; private set; }

    public long CalculationId { get; private set; }

    public ItemCostCalculation? Calculation { get; private set; }

    public long RequestItemId { get; private set; }

    public PriceBasis Basis { get; private set; }

    /// <summary>The calculated total as proposed (null for a contract item: the contract amount is not calculated).</summary>
    public decimal? ProposedPriceRial { get; private set; }

    public PriceDecisionStatus Status { get; private set; }

    public long? SubmittedByUserId { get; private set; }

    public DateTime? SubmittedAtUtc { get; private set; }

    public decimal? ApprovedPriceRial { get; private set; }

    /// <summary>Mandatory when the approved price differs from the proposal.</summary>
    public string? Reason { get; private set; }

    /// <summary>The contract number or reference the contract amount was taken from (contract items).</summary>
    public string? ContractReference { get; private set; }

    public long? ApprovedByUserId { get; private set; }

    public DateTime? ApprovedAtUtc { get; private set; }

    public void Submit(long userId, DateTime atUtc)
    {
        Require(PriceDecisionStatus.Draft);
        SubmittedByUserId = userId;
        SubmittedAtUtc = Requirements.Utc(atUtc, nameof(atUtc));
        Status = PriceDecisionStatus.Submitted;
    }

    /// <summary>Approves the price; <paramref name="priceRial"/> null accepts the proposal (calculated basis only).</summary>
    public void Approve(decimal? priceRial, string? reason, string? contractReference, long userId, DateTime atUtc)
    {
        Require(PriceDecisionStatus.Submitted);
        var price = priceRial ?? ProposedPriceRial ?? throw new ArgumentException("A contract price must be entered.", nameof(priceRial));
        if (price <= 0 || price > MaxPriceRial || price != decimal.Round(price, 0, MidpointRounding.AwayFromZero))
        {
            throw new ArgumentOutOfRangeException(nameof(priceRial), price, "The price is a positive, whole number of Rial.");
        }

        var note = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var reference = string.IsNullOrWhiteSpace(contractReference) ? null : contractReference.Trim();
        if ((note is { Length: > MaxReasonLength }) || (reference is { Length: > MaxReferenceLength }))
        {
            throw new ArgumentException("The reason or the contract reference is too long.", nameof(reason));
        }

        if (Basis == PriceBasis.Contract)
        {
            if (priceRial is null || reference is null)
            {
                throw new ArgumentException("The contract amount and the contract reference are mandatory.", nameof(contractReference));
            }
        }
        else if (price != ProposedPriceRial && note is null)
        {
            throw new ArgumentException("Changing the calculated price needs a reason.", nameof(reason));
        }

        ApprovedPriceRial = price;
        Reason = note;
        ContractReference = Basis == PriceBasis.Contract ? reference : null;
        ApprovedByUserId = userId;
        ApprovedAtUtc = Requirements.Utc(atUtc, nameof(atUtc));
        Status = PriceDecisionStatus.Approved;
    }

    private void Require(PriceDecisionStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"The price decision is {Status}, not {expected}.");
        }
    }
}
