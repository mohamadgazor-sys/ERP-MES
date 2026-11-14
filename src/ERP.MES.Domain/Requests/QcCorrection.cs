namespace ERP.MES.Domain.Requests;

/// <summary>
/// One earlier accepted QC decision (an inspection line) that a correction supersedes, with how many of its units (owner decision Q2, Decision Log #142). The original line is never changed; this row is the
/// "relationship to the original decision" that Master Specification 1.10 requires.
/// </summary>
public sealed class QcCorrectionSource
{
    private QcCorrectionSource()
    {
    }

    public QcCorrectionSource(long inspectionLineId, long supersededBaseUnits)
    {
        if (supersededBaseUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(supersededBaseUnits), supersededBaseUnits, "A superseded quantity is positive.");
        }

        InspectionLineId = inspectionLineId;
        SupersededBaseUnits = supersededBaseUnits;
    }

    public long Id { get; private set; }

    public long InspectionLineId { get; private set; }

    public long SupersededBaseUnits { get; private set; }
}

/// <summary>
/// A QC correction of a final (QC_APPROVED) stage decision (Master Specification 1.10, 15; owner decisions Q1-Q8, Decision Log #142). Append-only: the old QC lines, the approval, the work sessions and the
/// costs stay; this row records who corrected, when and why, which accepted units (<see cref="Sources"/>) are now reclassified as rework or scrap, and what the stage looked like before and after.
/// </summary>
public sealed class QcCorrection
{
    public const int MaxReasonLength = 300;

    private readonly List<QcCorrectionSource> _sources = [];

    private QcCorrection()
    {
        Reason = string.Empty;
    }

    internal QcCorrection(
        long requestItemId, int stageNo, long reworkBaseUnits, long scrapBaseUnits, string reason, long acceptedBefore, ItemStageStatus resultingStatus, bool afterCustomerDispatch,
        IEnumerable<QcCorrectionSource> sources, long correctedByUserId, DateTime correctedAtUtc)
    {
        RequestItemId = requestItemId;
        StageNo = stageNo;
        ReworkBaseUnits = reworkBaseUnits;
        ScrapBaseUnits = scrapBaseUnits;
        Reason = reason;
        AcceptedBeforeBaseUnits = acceptedBefore;
        ResultingStatus = resultingStatus;
        AfterCustomerDispatch = afterCustomerDispatch;
        _sources.AddRange(sources);
        CorrectedByUserId = correctedByUserId;
        CorrectedAtUtc = Customer.RequireUtc(correctedAtUtc, nameof(correctedAtUtc));
    }

    public long Id { get; private set; }

    public long RequestItemId { get; private set; }

    public int StageNo { get; private set; }

    /// <summary>Previously accepted base units that QC now classifies as rework (they return to the stage as executable work and go through QC again).</summary>
    public long ReworkBaseUnits { get; private set; }

    /// <summary>Previously accepted base units that QC now classifies as scrap (closed through the existing shortfall decision).</summary>
    public long ScrapBaseUnits { get; private set; }

    public long CorrectedBaseUnits => ReworkBaseUnits + ScrapBaseUnits;

    public string Reason { get; private set; }

    /// <summary>The valid accepted base units of the stage before the correction; the quantity still accepted afterwards is this minus <see cref="CorrectedBaseUnits"/>.</summary>
    public long AcceptedBeforeBaseUnits { get; private set; }

    public long AcceptedAfterBaseUnits => AcceptedBeforeBaseUnits - CorrectedBaseUnits;

    /// <summary>The stage status the correction left: IN_PROGRESS (or WAITING_EXTERNAL_SEND) for rework, WAITING_SHORTFALL_DECISION for scrap alone.</summary>
    public ItemStageStatus ResultingStatus { get; private set; }

    /// <summary>True when a customer dispatch of the item existed when QC corrected: Management sees the reason and history prominently (Q8). The dispatch itself is never undone.</summary>
    public bool AfterCustomerDispatch { get; private set; }

    public IReadOnlyList<QcCorrectionSource> Sources => _sources;

    public long CorrectedByUserId { get; private set; }

    public DateTime CorrectedAtUtc { get; private set; }
}
