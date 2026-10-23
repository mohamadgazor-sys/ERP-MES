using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Reporting;
using ERP.MES.Domain.Requests;
using ERP.MES.Domain.Scheduling;

namespace ERP.MES.Application.Reporting;

/// <summary>One line the operator typed. Times are UTC instants (the screen converts the typed local times).</summary>
public sealed record JobLineInput(
    long RequestId, long ItemId, int StageNo, long MachineId, DateTime StartUtc, DateTime EndUtc, int DowntimeMinutes, string? DowntimeReason, long ProcessedBaseUnits, string? Note);

/// <summary>A report with the operator's name and what the signed-in user may do with it.</summary>
public sealed record JobReportItem(Stored<DailyJobReport> Report, string OperatorName, bool CanEdit, bool CanReview, bool ReviewWouldBePlanningFallback);

public sealed record JobReportCapabilities(bool CanView, bool CanSubmit, bool CanReview)
{
    public static JobReportCapabilities None { get; } = new(false, false, false);
}

public sealed record JobReportList(IReadOnlyList<JobReportItem> Items, JobReportCapabilities Capabilities, long? OwnOperatorId);

/// <summary>
/// Daily job reports (Decision Log #82 R5, #95 D8, #102). The operator (a Production user linked to an active operator record) creates and submits only their OWN report - nobody reports on behalf of
/// another operator. The supervisor of the report's unit (a Production user whom Planning marked as supervisor) approves or returns it; Planning reviews only when the unit has no supervisor, and the
/// report is then flagged as reviewed by the fallback; nobody reviews their own report. Submission blocks overlapping time and marks lines that are outside the shift, not scheduled, on an absent day,
/// on another machine than planned or overlapping another operator on the machine: flagged for the reviewer, never silently rejected. Reports never change QC results or stage statuses.
/// </summary>
public sealed class JobReportService(
    IJobReportRepository store,
    IOperatorRepository operators,
    IPlanningMasterRepository planning,
    ISchedulingRepository scheduling,
    IUnitQueueRepository unitQueue,
    IRequestRepository requests,
    IJalaliDateService dates,
    CurrentAuthorization authorization,
    IClock clock,
    IAuditContext auditContext)
{
    private static readonly Permission[] All = [Permission.ViewJobReports, Permission.SubmitJobReport, Permission.ReviewJobReports];

    public async Task<Outcome<JobReportList>> LoadAsync(Principal? principal, DateOnly from, DateOnly toInclusive, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, All, cancellationToken);
        if (!set.Has(Permission.ViewJobReports) || set.Current is null)
        {
            return Outcome<JobReportList>.Fail(OperationStatus.NotAuthorized);
        }

        if (toInclusive < from || toInclusive.DayNumber - from.DayNumber > 400)
        {
            return Outcome<JobReportList>.Invalid(ValidationIssue.EndBeforeStart);
        }

        var user = set.Current;
        return await CustomerService.GuardAsync(async () =>
        {
            var people = await operators.ListOperatorsAsync(cancellationToken);
            var own = people.FirstOrDefault(o => o.Value.UserId == user.UserId && o.Value.IsActive)?.Value.Id;
            var designations = (await unitQueue.ListDesignationsAsync(cancellationToken)).Where(d => d.Value.IsActive).ToList();
            var supervised = designations.Where(d => d.Value.UserId == user.UserId && d.Value.IsSupervisor).Select(d => d.Value.ProductionUnitId).ToHashSet();
            var unitsWithSupervisor = designations.Where(d => d.Value.IsSupervisor).Select(d => d.Value.ProductionUnitId).ToHashSet();
            var broad = user.DepartmentCode is DepartmentCodes.Planning or DepartmentCodes.Management;
            var names = people.ToDictionary(o => o.Value.Id, o => o.Value.FullName);
            var items = (await store.ListAsync(from, toInclusive, cancellationToken))
                .Where(r => broad || r.Value.OperatorId == own || supervised.Contains(r.Value.ProductionUnitId))
                .Select(r =>
                {
                    var isOwn = r.Value.OperatorId == own;
                    var mayReview = set.Has(Permission.ReviewJobReports) && !isOwn && r.Value.Status == JobReportStatus.Submitted
                        && (supervised.Contains(r.Value.ProductionUnitId) || (user.DepartmentCode == DepartmentCodes.Planning && !unitsWithSupervisor.Contains(r.Value.ProductionUnitId)));
                    return new JobReportItem(r, names.GetValueOrDefault(r.Value.OperatorId, string.Empty), set.Has(Permission.SubmitJobReport) && isOwn && r.Value.IsEditable, mayReview, !supervised.Contains(r.Value.ProductionUnitId));
                })
                .OrderByDescending(i => i.Report.Value.WorkDate).ThenBy(i => i.OperatorName, StringComparer.Ordinal).ToList();
            return Outcome<JobReportList>.Ok(new JobReportList(items, new JobReportCapabilities(true, set.Has(Permission.SubmitJobReport) && own is not null, set.Has(Permission.ReviewJobReports)), own));
        });
    }

    /// <summary>The operator saves a draft (new, or an existing draft/returned report under its version): the whole list of lines is replaced.</summary>
    public async Task<Outcome<Stored<DailyJobReport>>> SaveDraftAsync(
        Principal? principal, long? reportId, long? expectedVersion, DateOnly workDate, long unitId, long templateId, IReadOnlyList<JobLineInput> lines, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var decision = await authorization.CheckAsync(principal, Permission.SubmitJobReport, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<DailyJobReport>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var own = (await operators.ListOperatorsAsync(cancellationToken)).FirstOrDefault(o => o.Value.UserId == user.UserId && o.Value.IsActive);
                if (own is null)
                {
                    return Outcome<Stored<DailyJobReport>>.Invalid(ValidationIssue.JobReportOperatorNotLinked);
                }

                var (today, _) = dates.UtcToLocal(now);
                var config = await scheduling.LoadConfigurationAsync(cancellationToken);
                if (workDate > today || !config.UnitShifts.Any(u => u.Value.IsActive && u.Value.ProductionUnitId == unitId && u.Value.ShiftTemplateId == templateId))
                {
                    return Outcome<Stored<DailyJobReport>>.Invalid(ValidationIssue.JobReportDateInvalid);
                }

                var built = await BuildLinesAsync(unitId, lines, cancellationToken);
                if (built.Issue != ValidationIssue.None)
                {
                    return Outcome<Stored<DailyJobReport>>.Invalid(built.Issue);
                }

                var summary = $"operator {own.Value.Code}, {workDate:yyyy-MM-dd}, unit {unitId}, shift {templateId}, {built.Lines!.Count} lines";
                if (reportId is null)
                {
                    var report = new DailyJobReport(0, own.Value.Id, workDate, unitId, templateId, user.UserId, now);
                    report.ReplaceLines(built.Lines);
                    try
                    {
                        return Outcome<Stored<DailyJobReport>>.Ok(await store.AddAsync(report, Audit(user, now, AuditActions.JobReportSaved, "new", "Job report draft created: " + summary), cancellationToken));
                    }
                    catch (DuplicateJobReportException)
                    {
                        return Outcome<Stored<DailyJobReport>>.Invalid(ValidationIssue.JobReportDuplicate);
                    }
                }

                var existing = await store.FindAsync(reportId.Value, cancellationToken);
                if (existing is null)
                {
                    return Outcome<Stored<DailyJobReport>>.Fail(OperationStatus.NotFound);
                }

                if (existing.Value.OperatorId != own.Value.Id || expectedVersion is null)
                {
                    return Outcome<Stored<DailyJobReport>>.Fail(existing.Value.OperatorId != own.Value.Id ? OperationStatus.NotAuthorized : OperationStatus.ConcurrencyConflict);
                }

                return Outcome<Stored<DailyJobReport>>.Ok(await store.UpdateAsync(reportId.Value, expectedVersion.Value, r => r.ReplaceLines(built.Lines),
                    Audit(user, now, AuditActions.JobReportSaved, reportId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), "Job report draft changed: " + summary), cancellationToken));
            });
        }
        catch (JobReportOverlapException)
        {
            return Outcome<Stored<DailyJobReport>>.Invalid(ValidationIssue.JobReportLinesOverlap);
        }
        catch (InvalidOperationException)
        {
            return Outcome<Stored<DailyJobReport>>.Fail(OperationStatus.InvalidState);
        }
    }

    /// <summary>The operator submits their own report: blocking checks first, then the flags are computed and the report becomes SUBMITTED.</summary>
    public async Task<Outcome<Stored<DailyJobReport>>> SubmitAsync(Principal? principal, long reportId, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.SubmitJobReport, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<DailyJobReport>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var own = (await operators.ListOperatorsAsync(cancellationToken)).FirstOrDefault(o => o.Value.UserId == user.UserId && o.Value.IsActive);
                var report = await store.FindAsync(reportId, cancellationToken);
                if (report is null)
                {
                    return Outcome<Stored<DailyJobReport>>.Fail(OperationStatus.NotFound);
                }

                if (own is null || report.Value.OperatorId != own.Value.Id)
                {
                    return Outcome<Stored<DailyJobReport>>.Fail(OperationStatus.NotAuthorized);
                }

                if (!report.Value.IsEditable || report.Value.Lines.Count == 0)
                {
                    return Outcome<Stored<DailyJobReport>>.Fail(OperationStatus.InvalidState);
                }

                var window = report.Value.WorkDate;
                var others = (await store.ListAsync(window.AddDays(-1), window.AddDays(1), cancellationToken))
                    .Where(r => r.Value.Id != report.Value.Id && r.Value.Status is JobReportStatus.Submitted or JobReportStatus.Approved).ToList();
                if (others.Any(o => o.Value.OperatorId == own.Value.Id && o.Value.Lines.Any(ol => report.Value.Lines.Any(l => l.Overlaps(ol)))))
                {
                    return Outcome<Stored<DailyJobReport>>.Invalid(ValidationIssue.JobReportLinesOverlap);
                }

                var flags = await ComputeFlagsAsync(report.Value, others.Where(o => o.Value.OperatorId != own.Value.Id).ToList(), cancellationToken);
                return Outcome<Stored<DailyJobReport>>.Ok(await store.UpdateAsync(reportId, expectedVersion, r => r.Submit(user.UserId, now, flags),
                    Audit(user, now, AuditActions.JobReportSubmitted, reportId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        $"Job report {reportId} submitted by operator {own.Value.Code} ({flags.Count(f => f.Value != JobLineFlags.None)} of {flags.Count} lines flagged)"), cancellationToken));
            });
        }
        catch (InvalidOperationException)
        {
            return Outcome<Stored<DailyJobReport>>.Fail(OperationStatus.InvalidState);
        }
    }

    /// <summary>The supervisor of the unit (or Planning when the unit has none, flagged) approves or returns a submitted report. Nobody reviews their own report.</summary>
    public async Task<Outcome<Stored<DailyJobReport>>> ReviewAsync(Principal? principal, long reportId, long expectedVersion, bool approve, string? reason, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ReviewJobReports, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<DailyJobReport>>.Fail(OperationStatus.NotAuthorized);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        if (!approve && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > JobReportLine.MaxNoteLength))
        {
            return Outcome<Stored<DailyJobReport>>.Invalid(ValidationIssue.JobReportReasonRequired);
        }

        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var report = await store.FindAsync(reportId, cancellationToken);
                if (report is null)
                {
                    return Outcome<Stored<DailyJobReport>>.Fail(OperationStatus.NotFound);
                }

                var person = (await operators.ListOperatorsAsync(cancellationToken)).FirstOrDefault(o => o.Value.Id == report.Value.OperatorId);
                var designations = (await unitQueue.ListDesignationsAsync(cancellationToken)).Where(d => d.Value.IsActive && d.Value.ProductionUnitId == report.Value.ProductionUnitId).ToList();
                var isSupervisor = designations.Any(d => d.Value.UserId == user.UserId && d.Value.IsSupervisor);
                var unitHasSupervisor = designations.Any(d => d.Value.IsSupervisor);
                var fallback = !isSupervisor && user.DepartmentCode == DepartmentCodes.Planning && !unitHasSupervisor;
                if ((!isSupervisor && !fallback) || person?.Value.UserId == user.UserId)
                {
                    return Outcome<Stored<DailyJobReport>>.Invalid(ValidationIssue.JobReportReviewerNotAllowed);
                }

                var text = reason?.Trim();
                return Outcome<Stored<DailyJobReport>>.Ok(await store.UpdateAsync(
                    reportId,
                    expectedVersion,
                    r =>
                    {
                        if (approve)
                        {
                            r.Approve(user.UserId, now, fallback);
                        }
                        else
                        {
                            r.Return(user.UserId, now, text, fallback);
                        }
                    },
                    Audit(user, now, approve ? AuditActions.JobReportApproved : AuditActions.JobReportReturned, reportId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        $"Job report {reportId} {(approve ? "approved" : "returned: " + text)}{(fallback ? " (reviewed by Planning because the unit has no supervisor)" : string.Empty)}"),
                    cancellationToken));
            });
        }
        catch (InvalidOperationException)
        {
            return Outcome<Stored<DailyJobReport>>.Fail(OperationStatus.InvalidState);
        }
    }

    private sealed record Built(IReadOnlyList<JobReportLine>? Lines, ValidationIssue Issue);

    private async Task<Built> BuildLinesAsync(long unitId, IReadOnlyList<JobLineInput> inputs, CancellationToken cancellationToken)
    {
        var machines = (await planning.ListMachinesAsync(cancellationToken)).ToDictionary(m => m.Value.Id, m => m.Value);
        var requestCache = new Dictionary<long, Request?>();
        var lines = new List<JobReportLine>();
        foreach (var (input, index) in inputs.Select((l, i) => (l, i)))
        {
            if (!machines.TryGetValue(input.MachineId, out var machine) || !machine.IsActive || machine.ProductionUnitId != unitId)
            {
                return new Built(null, ValidationIssue.JobReportLineInvalid);
            }

            if (!requestCache.TryGetValue(input.RequestId, out var request))
            {
                request = (await requests.FindAsync(input.RequestId, cancellationToken))?.Value;
                requestCache[input.RequestId] = request;
            }

            var stage = request?.Items.FirstOrDefault(i => i.Id == input.ItemId)?.Stages.FirstOrDefault(s => s.StageNo == input.StageNo);
            if (stage is null || stage.ProductionUnitId != unitId || stage.Status is ItemStageStatus.Planned or ItemStageStatus.WaitingPreviousQc)
            {
                return new Built(null, ValidationIssue.JobReportLineInvalid);
            }

            try
            {
                lines.Add(new JobReportLine(index + 1, input.MachineId, input.RequestId, input.ItemId, input.StageNo, input.StartUtc, input.EndUtc, input.DowntimeMinutes, input.DowntimeReason, input.ProcessedBaseUnits, input.Note));
            }
            catch (ArgumentException)
            {
                return new Built(null, ValidationIssue.JobReportLineInvalid);
            }
        }

        return new Built(lines, ValidationIssue.None);
    }

    private async Task<Dictionary<int, JobLineFlags>> ComputeFlagsAsync(DailyJobReport report, IReadOnlyList<Stored<DailyJobReport>> otherOperators, CancellationToken cancellationToken)
    {
        var config = await scheduling.LoadConfigurationAsync(cancellationToken);
        var template = config.Templates.Select(t => t.Value).First(t => t.Id == report.ShiftTemplateId);
        var shiftStart = report.WorkDate.ToDateTime(template.StartTime);
        var shiftEnd = shiftStart.AddMinutes(template.TotalMinutes);
        var workingToday = config.ToCalendar().IsWorking(report.ProductionUnitId, template.Id, report.WorkDate);
        var scheduled = (await scheduling.ListSchedulesAsync(report.WorkDate, report.WorkDate, cancellationToken))
            .Any(r => r.Value.IsPlanned && r.Value.OperatorId == report.OperatorId && r.Value.ProductionUnitId == report.ProductionUnitId && r.Value.ShiftTemplateId == report.ShiftTemplateId);
        var absent = (await scheduling.ListOffDaysAsync(cancellationToken)).Any(o => o.Value.OperatorId == report.OperatorId && o.Value.Covers(report.WorkDate));
        var requestCache = new Dictionary<long, Request?>();
        var flags = new Dictionary<int, JobLineFlags>();
        foreach (var line in report.Lines)
        {
            var result = JobLineFlags.None;
            var (startDate, startTime) = dates.UtcToLocal(line.StartUtc);
            var (endDate, endTime) = dates.UtcToLocal(line.EndUtc);
            var localStart = startDate.ToDateTime(startTime);
            var localEnd = endDate.ToDateTime(endTime);
            if (!workingToday || localStart < shiftStart || localEnd > shiftEnd)
            {
                result |= JobLineFlags.OutsideShift;
            }

            if (!scheduled)
            {
                result |= JobLineFlags.NotScheduled;
            }

            if (absent)
            {
                result |= JobLineFlags.OperatorOffDay;
            }

            if (!requestCache.TryGetValue(line.RequestId, out var request))
            {
                request = (await requests.FindAsync(line.RequestId, cancellationToken))?.Value;
                requestCache[line.RequestId] = request;
            }

            var planned = request?.Items.FirstOrDefault(i => i.Id == line.RequestItemId)?.Stages.FirstOrDefault(s => s.StageNo == line.StageNo)?.MachineId;
            if (planned is { } plannedMachine && plannedMachine != line.MachineId)
            {
                result |= JobLineFlags.MachineDiffersFromPlan;
            }

            if (otherOperators.Any(o => o.Value.Lines.Any(ol => ol.MachineId == line.MachineId && ol.Overlaps(line))))
            {
                result |= JobLineFlags.MachineOverlap;
            }

            flags[line.LineNo] = result;
        }

        return flags;
    }

    private AuditLogEntry Audit(Principal user, DateTime now, string code, string targetId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, "DailyJobReport", targetId, summary);
}
