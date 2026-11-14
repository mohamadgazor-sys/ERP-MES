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
public sealed record JobReportItem(Stored<DailyJobReport> Report, string OperatorName, bool CanEdit, bool CanReview, bool ReviewWouldBePlanningFallback, int LateWorkingDays = 0)
{
    /// <summary>True once an amendment of this approved report was itself approved: the report stays as history but no longer feeds KPIs or costing (owner decision B2).</summary>
    public bool IsSuperseded { get; init; }

    /// <summary>True while an amendment of this report exists (open or approved): the report cannot be reopened again.</summary>
    public bool HasAmendment { get; init; }

    /// <summary>The signed-in user may reopen this approved report for amendment (Planning, the Production Manager).</summary>
    public bool CanReopen { get; init; }

    /// <summary>True when the report was (or, for a draft, would be today) entered more than <see cref="JobReportService.LateAfterWorkingDays"/> working days after its work date (Decision Log #142 B1). A warning only; a late report is never refused.</summary>
    public bool IsLate => LateWorkingDays > JobReportService.LateAfterWorkingDays;
}

public sealed record JobReportCapabilities(bool CanView, bool CanSubmit, bool CanReview, bool CanReopen = false)
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
    IWorkSessionRepository workSessions,
    IJalaliDateService dates,
    CurrentAuthorization authorization,
    IClock clock,
    IAuditContext auditContext)
{
    /// <summary>A report entered more than this many working days after its work date shows a late-report warning (owner decision B1, 2026-11-14: N = 5). Never a refusal.</summary>
    public const int LateAfterWorkingDays = 5;

    private static readonly Permission[] All = [Permission.ViewJobReports, Permission.SubmitJobReport, Permission.ReviewJobReports, Permission.ReopenJobReport];

    /// <summary>
    /// The working days between the report's work date and the day it was submitted (today for a draft), counted on the production calendar of the report's unit and shift: the days after the work date
    /// up to and including the submission day on which that shift works. The work date and the submission time stay separate facts on the report.
    /// </summary>
    public static int WorkingDaysLate(DailyJobReport report, WorkingCalendar calendar, IJalaliDateService dates, DateTime nowUtc)
    {
        var enteredLocal = dates.UtcToLocal(report.SubmittedAtUtc ?? nowUtc).Date;
        var count = 0;
        for (var day = report.WorkDate.AddDays(1); day <= enteredLocal; day = day.AddDays(1))
        {
            if (calendar.IsWorking(report.ProductionUnitId, report.ShiftTemplateId, day))
            {
                count++;
            }
        }

        return count;
    }

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
            var broad = user.DepartmentCode is DepartmentCodes.Planning or DepartmentCodes.Management or DepartmentCodes.ProductionManager;
            var names = people.ToDictionary(o => o.Value.Id, o => o.Value.FullName);
            var calendar = (await scheduling.LoadConfigurationAsync(cancellationToken)).ToCalendar();
            var nowUtc = clock.UtcNow;
            var all = await store.ListAsync(from, toInclusive, cancellationToken);
            var amended = all.Where(r => r.Value.PreviousRevisionReportId is not null).Select(r => r.Value.PreviousRevisionReportId!.Value).ToHashSet();
            var superseded = all.Where(r => r.Value is { PreviousRevisionReportId: not null, Status: JobReportStatus.Approved }).Select(r => r.Value.PreviousRevisionReportId!.Value).ToHashSet();
            var items = all
                .Where(r => broad || r.Value.OperatorId == own || supervised.Contains(r.Value.ProductionUnitId))
                .Select(r =>
                {
                    var isOwn = r.Value.OperatorId == own;
                    var mayReview = set.Has(Permission.ReviewJobReports) && !isOwn && r.Value.Status == JobReportStatus.Submitted
                        && (supervised.Contains(r.Value.ProductionUnitId) || user.DepartmentCode == DepartmentCodes.ProductionManager || (user.DepartmentCode == DepartmentCodes.Planning && !unitsWithSupervisor.Contains(r.Value.ProductionUnitId)));
                    return new JobReportItem(r, names.GetValueOrDefault(r.Value.OperatorId, string.Empty), set.Has(Permission.SubmitJobReport) && isOwn && r.Value.IsEditable, mayReview, user.DepartmentCode != DepartmentCodes.ProductionManager && !supervised.Contains(r.Value.ProductionUnitId), r.Value.IsAmendment ? 0 : WorkingDaysLate(r.Value, calendar, dates, nowUtc))
                    {
                        IsSuperseded = superseded.Contains(r.Value.Id),
                        HasAmendment = amended.Contains(r.Value.Id),
                        CanReopen = set.Has(Permission.ReopenJobReport) && r.Value.Status == JobReportStatus.Approved && !amended.Contains(r.Value.Id),
                    };
                })
                .OrderByDescending(i => i.Report.Value.WorkDate).ThenBy(i => i.OperatorName, StringComparer.Ordinal).ToList();
            return Outcome<JobReportList>.Ok(new JobReportList(items, new JobReportCapabilities(true, set.Has(Permission.SubmitJobReport) && own is not null, set.Has(Permission.ReviewJobReports), set.Has(Permission.ReopenJobReport)), own));
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

                // An amendment replaces the report it amends: that report, and every older revision already replaced by an approved one, are not "other" reports.
                var replaced = others.Where(o => o.Value is { PreviousRevisionReportId: not null, Status: JobReportStatus.Approved }).Select(o => o.Value.PreviousRevisionReportId!.Value).ToHashSet();
                if (report.Value.PreviousRevisionReportId is { } amends)
                {
                    replaced.Add(amends);
                }

                others = others.Where(o => !replaced.Contains(o.Value.Id)).ToList();
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
                var isSupervisor = user.DepartmentCode == DepartmentCodes.ProductionManager || designations.Any(d => d.Value.UserId == user.UserId && d.Value.IsSupervisor);
                var unitHasSupervisor = designations.Any(d => d.Value.IsSupervisor);
                var fallback = !isSupervisor && user.DepartmentCode == DepartmentCodes.Planning && !unitHasSupervisor;
                if ((!isSupervisor && !fallback) || person?.Value.UserId == user.UserId)
                {
                    return Outcome<Stored<DailyJobReport>>.Invalid(ValidationIssue.JobReportReviewerNotAllowed);
                }

                var text = reason?.Trim();

                // Approving an amendment (owner decision B2): when it changes the machine time costing used, the latest cost snapshot and price approval of the affected items become stale in the same transaction.
                StaleCostMarking? staleCost = null;
                var staleItems = new List<long>();
                if (approve && report.Value.PreviousRevisionReportId is { } previousId && await store.FindAsync(previousId, cancellationToken) is { } previous)
                {
                    staleItems = ChangedCostingItems(previous.Value, report.Value);
                    if (staleItems.Count > 0)
                    {
                        staleCost = new StaleCostMarking(staleItems, $"Approved amendment of job report {previousId} changed the machine time costing used", user.UserId, now);
                    }
                }

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
                        $"Job report {reportId} {(approve ? "approved" : "returned: " + text)}{(fallback ? " (reviewed by Planning because the unit has no supervisor)" : string.Empty)}"
                        + (staleItems.Count > 0 ? $"; the cost snapshot of item(s) {string.Join(", ", staleItems)} is no longer current" : string.Empty)),
                    cancellationToken,
                    staleCost));
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

            // Work sessions (Decision Log #112): a stage is tracked by sessions or by job reports, never by both.
            if (await workSessions.StageHasSessionsAsync(input.ItemId, input.StageNo, cancellationToken))
            {
                return new Built(null, ValidationIssue.SessionTimeBasisConflict);
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

    /// <summary>
    /// Planning or the Production Manager reopens an APPROVED report for amendment with a mandatory reason (owner decision B2, Decision Log #142). The approved original is not changed (one event is appended);
    /// a new revision is created as a draft copy for the operator to correct, submit and have approved again. Until then the original stays the latest valid approved revision for KPIs and costing.
    /// </summary>
    public async Task<Outcome<Stored<DailyJobReport>>> ReopenAsync(Principal? principal, long reportId, long expectedVersion, string? reason, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ReopenJobReport, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<DailyJobReport>>.Fail(OperationStatus.NotAuthorized);
        }

        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > JobReportLine.MaxNoteLength)
        {
            return Outcome<Stored<DailyJobReport>>.Invalid(ValidationIssue.JobReportReasonRequired);
        }

        var user = decision.Current!;
        var now = clock.UtcNow;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var report = await store.FindAsync(reportId, cancellationToken);
                if (report is null)
                {
                    return Outcome<Stored<DailyJobReport>>.Fail(OperationStatus.NotFound);
                }

                if (report.Value.Status != JobReportStatus.Approved)
                {
                    return Outcome<Stored<DailyJobReport>>.Invalid(ValidationIssue.JobReportReopenNotAllowed);
                }

                return Outcome<Stored<DailyJobReport>>.Ok(await store.ReopenAsync(
                    reportId, expectedVersion, r => r.Reopen(user.UserId, text, now),
                    Audit(user, now, AuditActions.JobReportReopened, reportId.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Approved job report {reportId} reopened for amendment: {text}"), cancellationToken));
            });
        }
        catch (InvalidOperationException)
        {
            return Outcome<Stored<DailyJobReport>>.Invalid(ValidationIssue.JobReportReopenNotAllowed);
        }
    }

    /// <summary>The items whose machine time that costing uses (machine and productive minutes of their lines) differs between the amended report and its amendment.</summary>
    private static List<long> ChangedCostingItems(DailyJobReport before, DailyJobReport after)
    {
        static Dictionary<long, List<(long Machine, int Minutes)>> Signature(DailyJobReport report) =>
            report.Lines.GroupBy(l => l.RequestItemId).ToDictionary(g => g.Key, g => g.Select(l => (l.MachineId, l.ProductiveMinutes)).OrderBy(x => x.MachineId).ThenBy(x => x.ProductiveMinutes).ToList());

        var was = Signature(before);
        var now = Signature(after);
        return was.Keys.Union(now.Keys).Where(id => !was.GetValueOrDefault(id, []).SequenceEqual(now.GetValueOrDefault(id, []))).OrderBy(id => id).ToList();
    }

    private AuditLogEntry Audit(Principal user, DateTime now, string code, string targetId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, "DailyJobReport", targetId, summary);
}
