using ERP.MES.Domain.Requests;

namespace ERP.MES.Domain.Reporting;

public enum JobReportStatus
{
    Draft = 1,
    Submitted,
    Returned,
    Approved,
}

/// <summary>Why a report line needs the reviewer's attention (Decision Log #101, docs/JOB_REPORTS_KPI_AND_OVERTIME_DESIGN.md sections 4 and 6). A flagged line is not rejected; the reviewer decides.</summary>
[Flags]
public enum JobLineFlags
{
    None = 0,

    /// <summary>The line is not inside the unit's shift of that day.</summary>
    OutsideShift = 1,

    /// <summary>The operator was not planned on this unit and shift that day.</summary>
    NotScheduled = 2,

    /// <summary>The operator has an off-day recorded for the date.</summary>
    OperatorOffDay = 4,

    /// <summary>The machine differs from the machine Planning planned for the stage.</summary>
    MachineDiffersFromPlan = 8,

    /// <summary>Another operator's submitted or approved line uses the same machine at an overlapping time.</summary>
    MachineOverlap = 16,
}

/// <summary>One line of a daily job report: reported work on a machine for an item's stage. Operators report processed base units and time only; accepted / rework / scrap belong to QC.</summary>
public sealed class JobReportLine
{
    public const int MaxNoteLength = 300;

    private JobReportLine()
    {
    }

    public JobReportLine(
        int lineNo, long machineId, long requestId, long requestItemId, int stageNo, DateTime startUtc, DateTime endUtc, int downtimeMinutes, string? downtimeReason, long processedBaseUnits, string? note)
    {
        var start = Customer.RequireUtc(startUtc, nameof(startUtc));
        var end = Customer.RequireUtc(endUtc, nameof(endUtc));
        if (end <= start || end - start > TimeSpan.FromHours(24))
        {
            throw new ArgumentException("A line needs an end after its start and at most 24 hours long.", nameof(endUtc));
        }

        var worked = (int)(end - start).TotalMinutes;
        if (downtimeMinutes < 0 || downtimeMinutes > worked)
        {
            throw new ArgumentOutOfRangeException(nameof(downtimeMinutes), downtimeMinutes, "Downtime is between zero and the worked minutes.");
        }

        var reason = downtimeReason?.Trim();
        if (downtimeMinutes > 0 && (string.IsNullOrEmpty(reason) || reason.Length > MaxNoteLength))
        {
            throw new ArgumentException("A downtime reason of 1 to " + MaxNoteLength + " characters is required.", nameof(downtimeReason));
        }

        if (processedBaseUnits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processedBaseUnits), processedBaseUnits, "Processed base units cannot be negative.");
        }

        var text = note?.Trim();
        if (text is { Length: > MaxNoteLength })
        {
            throw new ArgumentException("The note is at most " + MaxNoteLength + " characters.", nameof(note));
        }

        LineNo = lineNo;
        MachineId = machineId;
        RequestId = requestId;
        RequestItemId = requestItemId;
        StageNo = stageNo;
        StartUtc = start;
        EndUtc = end;
        DowntimeMinutes = downtimeMinutes;
        DowntimeReason = downtimeMinutes > 0 ? reason : null;
        ProcessedBaseUnits = processedBaseUnits;
        Note = string.IsNullOrEmpty(text) ? null : text;
    }

    public long Id { get; private set; }

    public int LineNo { get; private set; }

    public long MachineId { get; private set; }

    public long RequestId { get; private set; }

    public long RequestItemId { get; private set; }

    public int StageNo { get; private set; }

    public DateTime StartUtc { get; private set; }

    public DateTime EndUtc { get; private set; }

    public int DowntimeMinutes { get; private set; }

    public string? DowntimeReason { get; private set; }

    public long ProcessedBaseUnits { get; private set; }

    public string? Note { get; private set; }

    public JobLineFlags Flags { get; private set; }

    /// <summary>Minutes between start and end (the worked time of the line).</summary>
    public int WorkedMinutes => (int)(EndUtc - StartUtc).TotalMinutes;

    /// <summary>Worked minutes less downtime: the productive minutes (integer; never rounded for ranking).</summary>
    public int ProductiveMinutes => WorkedMinutes - DowntimeMinutes;

    public bool Overlaps(JobReportLine other) => StartUtc < other.EndUtc && other.StartUtc < EndUtc;

    internal void SetFlags(JobLineFlags flags) => Flags = flags;
}

public enum JobReportEventKind
{
    Created = 1,
    Submitted,
    Returned,
    Approved,
}

/// <summary>Append-only history of a report: who did what and when, with the reason of a return and whether Planning reviewed as the fallback.</summary>
public sealed class JobReportEvent
{
    private JobReportEvent()
    {
    }

    public JobReportEvent(JobReportEventKind kind, long userId, DateTime atUtc, string? reason, bool planningFallback)
    {
        Kind = kind;
        UserId = userId;
        AtUtc = Customer.RequireUtc(atUtc, nameof(atUtc));
        Reason = reason;
        PlanningFallback = planningFallback;
    }

    public long Id { get; private set; }

    public JobReportEventKind Kind { get; private set; }

    public long UserId { get; private set; }

    public DateTime AtUtc { get; private set; }

    public string? Reason { get; private set; }

    public bool PlanningFallback { get; private set; }
}

/// <summary>
/// A daily job report (Decision Log #82 R5, #95 D8, #102): one operator, one work date, one unit and shift. DRAFT -> SUBMITTED -> APPROVED, or SUBMITTED -> RETURNED (reason) -> corrected and
/// SUBMITTED again. Only approved lines will feed costing and KPIs. The operator's own user is the actor; nobody reports on behalf of another operator. Lines never change after submission.
/// </summary>
public sealed class DailyJobReport
{
    private readonly List<JobReportLine> _lines = [];
    private readonly List<JobReportEvent> _events = [];

    private DailyJobReport()
    {
    }

    public DailyJobReport(long id, long operatorId, DateOnly workDate, long productionUnitId, long shiftTemplateId, long createdByUserId, DateTime createdAtUtc)
    {
        Id = id;
        OperatorId = operatorId;
        WorkDate = workDate;
        ProductionUnitId = productionUnitId;
        ShiftTemplateId = shiftTemplateId;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = Customer.RequireUtc(createdAtUtc, nameof(createdAtUtc));
        Status = JobReportStatus.Draft;
        RevisionNo = 1;
        _events.Add(new JobReportEvent(JobReportEventKind.Created, createdByUserId, createdAtUtc, null, planningFallback: false));
    }

    public long Id { get; private set; }

    public long OperatorId { get; private set; }

    public DateOnly WorkDate { get; private set; }

    public long ProductionUnitId { get; private set; }

    public long ShiftTemplateId { get; private set; }

    public JobReportStatus Status { get; private set; }

    public int RevisionNo { get; private set; }

    public long CreatedByUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? SubmittedAtUtc { get; private set; }

    public long? ReviewedByUserId { get; private set; }

    public DateTime? ReviewedAtUtc { get; private set; }

    public string? ReturnReason { get; private set; }

    /// <summary>True when Planning reviewed because the unit had no designated supervisor (flagged, Decision Log #95 D8).</summary>
    public bool ReviewedByPlanningFallback { get; private set; }

    public IReadOnlyList<JobReportLine> Lines => _lines;

    public IReadOnlyList<JobReportEvent> Events => _events;

    public bool IsEditable => Status is JobReportStatus.Draft or JobReportStatus.Returned;

    public bool HasFlaggedLines => _lines.Any(l => l.Flags != JobLineFlags.None);

    /// <summary>Replaces the lines of a draft or returned report. The operator's own lines must not overlap each other in time.</summary>
    public void ReplaceLines(IEnumerable<JobReportLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (!IsEditable)
        {
            throw new InvalidOperationException("A " + Status + " report cannot be edited.");
        }

        var list = lines.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            for (var j = i + 1; j < list.Count; j++)
            {
                if (list[i].Overlaps(list[j]))
                {
                    throw new JobReportOverlapException("Two lines of the report overlap in time.");
                }
            }
        }

        _lines.Clear();
        _lines.AddRange(list.OrderBy(l => l.StartUtc).Select((l, i) => Renumber(l, i + 1)));
    }

    /// <summary>Submits the report with the flags the Application computed for each line number.</summary>
    public void Submit(long userId, DateTime atUtc, IReadOnlyDictionary<int, JobLineFlags> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        if (!IsEditable)
        {
            throw new InvalidOperationException("A " + Status + " report cannot be submitted.");
        }

        if (_lines.Count == 0)
        {
            throw new InvalidOperationException("A report without lines cannot be submitted.");
        }

        foreach (var line in _lines)
        {
            line.SetFlags(flags.GetValueOrDefault(line.LineNo, JobLineFlags.None));
        }

        Status = JobReportStatus.Submitted;
        SubmittedAtUtc = Customer.RequireUtc(atUtc, nameof(atUtc));
        ReturnReason = null;
        _events.Add(new JobReportEvent(JobReportEventKind.Submitted, userId, atUtc, null, planningFallback: false));
    }

    public void Approve(long reviewerUserId, DateTime atUtc, bool planningFallback)
    {
        RequireSubmitted("approved");
        Status = JobReportStatus.Approved;
        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = Customer.RequireUtc(atUtc, nameof(atUtc));
        ReviewedByPlanningFallback = planningFallback;
        _events.Add(new JobReportEvent(JobReportEventKind.Approved, reviewerUserId, atUtc, null, planningFallback));
    }

    public void Return(long reviewerUserId, DateTime atUtc, string? reason, bool planningFallback)
    {
        RequireSubmitted("returned");
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > JobReportLine.MaxNoteLength)
        {
            throw new ArgumentException("A return reason of 1 to " + JobReportLine.MaxNoteLength + " characters is required.", nameof(reason));
        }

        Status = JobReportStatus.Returned;
        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = Customer.RequireUtc(atUtc, nameof(atUtc));
        ReviewedByPlanningFallback = planningFallback;
        ReturnReason = text;
        RevisionNo++;
        _events.Add(new JobReportEvent(JobReportEventKind.Returned, reviewerUserId, atUtc, text, planningFallback));
    }

    private void RequireSubmitted(string action)
    {
        if (Status != JobReportStatus.Submitted)
        {
            throw new InvalidOperationException("A " + Status + " report cannot be " + action + ".");
        }
    }

    private static JobReportLine Renumber(JobReportLine line, int no) =>
        new(no, line.MachineId, line.RequestId, line.RequestItemId, line.StageNo, line.StartUtc, line.EndUtc, line.DowntimeMinutes, line.DowntimeReason, line.ProcessedBaseUnits, line.Note);
}

/// <summary>Two lines of one report overlap in time.</summary>
public sealed class JobReportOverlapException(string message) : InvalidOperationException(message);
