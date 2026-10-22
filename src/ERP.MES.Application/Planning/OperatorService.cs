using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Calendar;
using ERP.MES.Domain.Planning;
using ERP.MES.Domain.Quality;

namespace ERP.MES.Application.Planning;

/// <summary>Operators with their assignments and qualifications as loaded at one moment.</summary>
public sealed record OperatorData(
    IReadOnlyList<Stored<Operator>> Operators, IReadOnlyList<Stored<OperatorMachineAssignment>> Assignments, IReadOnlyList<Stored<OperatorMachineQualification>> Qualifications);

/// <summary>
/// Operators, their master assignments to machines and their qualifications (Decision Log #82 R2, #89). Planning registers and assigns (supervisors cannot); Planning, Management and
/// Production read. An assignment is NOT a qualification: assigning an operator who holds no current qualification on the machine succeeds with a WARNING (a labelled default). Assignments
/// and qualifications are history - closed or ended, never edited or deleted. Every command reloads the user from the database (Decision Log #74) and every write is audited in the same
/// transaction.
/// </summary>
public sealed class OperatorService(IOperatorRepository operators, IPlanningMasterRepository planning, CurrentAuthorization authorization, IClock clock, IJalaliDateService dates, IAuditContext auditContext)
{
    public async Task<bool> CanMaintainAsync(Principal? principal, CancellationToken cancellationToken = default) =>
        (await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken)).IsAllowed;

    public async Task<Outcome<OperatorData>> LoadAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.ViewOperators, cancellationToken)).IsAllowed)
        {
            return Outcome<OperatorData>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () => Outcome<OperatorData>.Ok(new OperatorData(
            await operators.ListOperatorsAsync(cancellationToken), await operators.ListAssignmentsAsync(cancellationToken), await operators.ListQualificationsAsync(cancellationToken))));
    }

    // ---- operators ----

    public async Task<Outcome<Stored<Operator>>> RegisterAsync(Principal? principal, string? code, string? fullName, long? userId, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<Operator>>.Fail(OperationStatus.NotAuthorized);
        }

        if (!UnitOfMeasure.IsValidCode(code))
        {
            return Outcome<Stored<Operator>>.Invalid(ValidationIssue.CodeInvalid);
        }

        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > Operator.MaxNameLength)
        {
            return Outcome<Stored<Operator>>.Invalid(ValidationIssue.OperatorNameInvalid);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var all = await operators.ListOperatorsAsync(cancellationToken);
            if (all.Any(o => string.Equals(o.Value.Code, code, StringComparison.Ordinal)))
            {
                return Outcome<Stored<Operator>>.Invalid(ValidationIssue.CodeDuplicate);
            }

            var userIssue = await CheckUserAsync(userId, all, exceptOperatorId: null, cancellationToken);
            if (userIssue != ValidationIssue.None)
            {
                return Outcome<Stored<Operator>>.Invalid(userIssue);
            }

            var entity = new Operator(0, code!, fullName, userId, decision.Current!.UserId, now);
            return Outcome<Stored<Operator>>.Ok(await operators.AddOperatorAsync(
                entity, [Audit(decision.Current, now, AuditActions.OperatorRegistered, "Operator", code!, $"Operator {code} registered" + (userId is null ? string.Empty : " and linked to a user account"))], cancellationToken));
        });
    }

    public async Task<Outcome<Stored<Operator>>> RenameAsync(Principal? principal, long operatorId, long expectedVersion, string? fullName, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken)).IsAllowed)
        {
            return Outcome<Stored<Operator>>.Fail(OperationStatus.NotAuthorized);
        }

        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length > Operator.MaxNameLength)
        {
            return Outcome<Stored<Operator>>.Invalid(ValidationIssue.OperatorNameInvalid);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () => Outcome<Stored<Operator>>.Ok(await operators.UpdateOperatorAsync(operatorId, expectedVersion, o => o.Rename(fullName, now), cancellationToken: cancellationToken)));
    }

    public async Task<Outcome<Stored<Operator>>> SetActiveAsync(Principal? principal, long operatorId, long expectedVersion, bool isActive, CancellationToken cancellationToken = default)
    {
        if (!(await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken)).IsAllowed)
        {
            return Outcome<Stored<Operator>>.Fail(OperationStatus.NotAuthorized);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () => Outcome<Stored<Operator>>.Ok(await operators.UpdateOperatorAsync(operatorId, expectedVersion, o => o.SetActive(isActive, now), cancellationToken: cancellationToken)));
    }

    /// <summary>Links the operator to a user account (or unlinks with null). The account must exist, be active and not belong to another operator.</summary>
    public async Task<Outcome<Stored<Operator>>> LinkUserAsync(Principal? principal, long operatorId, long expectedVersion, long? userId, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<Operator>>.Fail(OperationStatus.NotAuthorized);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var all = await operators.ListOperatorsAsync(cancellationToken);
            var current = all.FirstOrDefault(o => o.Value.Id == operatorId);
            if (current is null)
            {
                return Outcome<Stored<Operator>>.Fail(OperationStatus.NotFound);
            }

            var issue = await CheckUserAsync(userId, all, operatorId, cancellationToken);
            if (issue != ValidationIssue.None)
            {
                return Outcome<Stored<Operator>>.Invalid(issue);
            }

            var saved = await operators.UpdateOperatorAsync(
                operatorId, expectedVersion, o => o.LinkUser(userId, now),
                [Audit(decision.Current!, now, AuditActions.OperatorLinkedToUser, "Operator", current.Value.Code, $"Operator {current.Value.Code} " + (userId is null ? "unlinked from its user account" : "linked to a user account"))],
                cancellationToken);
            return Outcome<Stored<Operator>>.Ok(saved);
        });
    }

    private async Task<ValidationIssue> CheckUserAsync(long? userId, IReadOnlyList<Stored<Operator>> all, long? exceptOperatorId, CancellationToken cancellationToken)
    {
        if (userId is not { } id)
        {
            return ValidationIssue.None;
        }

        var user = await operators.FindUserAsync(id, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return ValidationIssue.UserInvalid;
        }

        return all.Any(o => o.Value.UserId == id && o.Value.Id != exceptOperatorId) ? ValidationIssue.UserAlreadyLinked : ValidationIssue.None;
    }

    // ---- master assignments ----

    /// <summary>
    /// Assigns an operator to a machine from a Jalali date. Both must be active; only one OPEN assignment per operator and machine. When the operator holds no qualification on the
    /// machine on that date the assignment is still made and the outcome carries a warning.
    /// </summary>
    public async Task<Outcome<Stored<OperatorMachineAssignment>>> AssignAsync(
        Principal? principal, long operatorId, long machineId, string? effectiveFromJalali, bool isPrimary, string? reason, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<OperatorMachineAssignment>>.Fail(OperationStatus.NotAuthorized);
        }

        var dateIssue = ParseDate(effectiveFromJalali, out var from, out var dateError);
        if (dateIssue != ValidationIssue.None)
        {
            return Outcome<Stored<OperatorMachineAssignment>>.Invalid(dateIssue, dateError);
        }

        if (reason is not null && reason.Trim().Length > OperatorMachineAssignment.MaxReasonLength)
        {
            return Outcome<Stored<OperatorMachineAssignment>>.Invalid(ValidationIssue.ReasonTooLong);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var worker = (await operators.ListOperatorsAsync(cancellationToken)).FirstOrDefault(o => o.Value.Id == operatorId);
            if (worker is null || !worker.Value.IsActive)
            {
                return Outcome<Stored<OperatorMachineAssignment>>.Invalid(ValidationIssue.OperatorInvalid);
            }

            var machine = (await planning.ListMachinesAsync(cancellationToken)).FirstOrDefault(m => m.Value.Id == machineId);
            if (machine is null || !machine.Value.IsActive)
            {
                return Outcome<Stored<OperatorMachineAssignment>>.Invalid(ValidationIssue.MachineInvalid);
            }

            if ((await operators.ListAssignmentsAsync(cancellationToken)).Any(a => a.Value.OperatorId == operatorId && a.Value.MachineId == machineId && a.Value.IsOpen))
            {
                return Outcome<Stored<OperatorMachineAssignment>>.Invalid(ValidationIssue.AssignmentExists);
            }

            var qualified = OperatorMachineRules.IsQualified((await operators.ListQualificationsAsync(cancellationToken)).Select(q => q.Value), operatorId, machineId, from);
            var assignment = new OperatorMachineAssignment(0, operatorId, machineId, from, isPrimary, reason, decision.Current!.UserId, now);
            var saved = await operators.AddAssignmentAsync(
                assignment,
                [Audit(decision.Current, now, AuditActions.OperatorMachineAssigned, "Operator", worker.Value.Code, $"Operator {worker.Value.Code} assigned to machine {machine.Value.Code} from {from:yyyy-MM-dd}" + (qualified ? string.Empty : " without a current qualification"))],
                cancellationToken);
            return Outcome<Stored<OperatorMachineAssignment>>.Ok(saved, qualified ? null : [NoQualificationWarning]);
        });
    }

    /// <summary>The warning text returned with an assignment that has no current qualification behind it.</summary>
    public const string NoQualificationWarning = "NoCurrentQualification";

    public async Task<Outcome<Stored<OperatorMachineAssignment>>> CloseAssignmentAsync(
        Principal? principal, long assignmentId, long expectedVersion, string? lastDayJalali, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<OperatorMachineAssignment>>.Fail(OperationStatus.NotAuthorized);
        }

        var dateIssue = ParseDate(lastDayJalali, out var lastDay, out var dateError);
        if (dateIssue != ValidationIssue.None)
        {
            return Outcome<Stored<OperatorMachineAssignment>>.Invalid(dateIssue, dateError);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var current = (await operators.ListAssignmentsAsync(cancellationToken)).FirstOrDefault(a => a.Value.Id == assignmentId);
            if (current is null)
            {
                return Outcome<Stored<OperatorMachineAssignment>>.Fail(OperationStatus.NotFound);
            }

            if (!current.Value.IsOpen)
            {
                return Outcome<Stored<OperatorMachineAssignment>>.Invalid(ValidationIssue.AssignmentClosed);
            }

            if (lastDay < current.Value.EffectiveFrom)
            {
                return Outcome<Stored<OperatorMachineAssignment>>.Invalid(ValidationIssue.EndBeforeStart);
            }

            var saved = await operators.UpdateAssignmentAsync(
                assignmentId, expectedVersion, a => a.Close(lastDay, decision.Current!.UserId, now),
                [Audit(decision.Current!, now, AuditActions.OperatorMachineAssignmentClosed, "Operator", assignmentId.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Operator assignment {assignmentId} closed on {lastDay:yyyy-MM-dd}")],
                cancellationToken);
            return Outcome<Stored<OperatorMachineAssignment>>.Ok(saved);
        });
    }

    // ---- qualifications ----

    public async Task<Outcome<Stored<OperatorMachineQualification>>> RecordQualificationAsync(
        Principal? principal, long operatorId, long machineId, string? qualifiedFromJalali, string? note, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<OperatorMachineQualification>>.Fail(OperationStatus.NotAuthorized);
        }

        var dateIssue = ParseDate(qualifiedFromJalali, out var from, out var dateError);
        if (dateIssue != ValidationIssue.None)
        {
            return Outcome<Stored<OperatorMachineQualification>>.Invalid(dateIssue, dateError);
        }

        if (note is not null && note.Trim().Length > OperatorMachineQualification.MaxNoteLength)
        {
            return Outcome<Stored<OperatorMachineQualification>>.Invalid(ValidationIssue.ReasonTooLong);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var worker = (await operators.ListOperatorsAsync(cancellationToken)).FirstOrDefault(o => o.Value.Id == operatorId);
            if (worker is null || !worker.Value.IsActive)
            {
                return Outcome<Stored<OperatorMachineQualification>>.Invalid(ValidationIssue.OperatorInvalid);
            }

            var machine = (await planning.ListMachinesAsync(cancellationToken)).FirstOrDefault(m => m.Value.Id == machineId);
            if (machine is null || !machine.Value.IsActive)
            {
                return Outcome<Stored<OperatorMachineQualification>>.Invalid(ValidationIssue.MachineInvalid);
            }

            if ((await operators.ListQualificationsAsync(cancellationToken)).Any(q => q.Value.OperatorId == operatorId && q.Value.MachineId == machineId && q.Value.IsOpen))
            {
                return Outcome<Stored<OperatorMachineQualification>>.Invalid(ValidationIssue.QualificationExists);
            }

            var qualification = new OperatorMachineQualification(0, operatorId, machineId, from, note, decision.Current!.UserId, now);
            return Outcome<Stored<OperatorMachineQualification>>.Ok(await operators.AddQualificationAsync(
                qualification, [Audit(decision.Current, now, AuditActions.OperatorQualificationRecorded, "Operator", worker.Value.Code, $"Operator {worker.Value.Code} qualified on machine {machine.Value.Code} from {from:yyyy-MM-dd}")], cancellationToken));
        });
    }

    public async Task<Outcome<Stored<OperatorMachineQualification>>> EndQualificationAsync(
        Principal? principal, long qualificationId, long expectedVersion, string? lastDayJalali, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.MaintainProductionResources, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<Stored<OperatorMachineQualification>>.Fail(OperationStatus.NotAuthorized);
        }

        var dateIssue = ParseDate(lastDayJalali, out var lastDay, out var dateError);
        if (dateIssue != ValidationIssue.None)
        {
            return Outcome<Stored<OperatorMachineQualification>>.Invalid(dateIssue, dateError);
        }

        var now = clock.UtcNow;
        return await CustomerService.GuardAsync(async () =>
        {
            var current = (await operators.ListQualificationsAsync(cancellationToken)).FirstOrDefault(q => q.Value.Id == qualificationId);
            if (current is null)
            {
                return Outcome<Stored<OperatorMachineQualification>>.Fail(OperationStatus.NotFound);
            }

            if (!current.Value.IsOpen)
            {
                return Outcome<Stored<OperatorMachineQualification>>.Invalid(ValidationIssue.QualificationEnded);
            }

            if (lastDay < current.Value.QualifiedFrom)
            {
                return Outcome<Stored<OperatorMachineQualification>>.Invalid(ValidationIssue.EndBeforeStart);
            }

            var saved = await operators.UpdateQualificationAsync(
                qualificationId, expectedVersion, q => q.End(lastDay),
                [Audit(decision.Current!, now, AuditActions.OperatorQualificationEnded, "Operator", qualificationId.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Operator qualification {qualificationId} ended on {lastDay:yyyy-MM-dd}")],
                cancellationToken);
            return Outcome<Stored<OperatorMachineQualification>>.Ok(saved);
        });
    }

    private ValidationIssue ParseDate(string? text, out DateOnly date, out JalaliDateError error)
    {
        date = default;
        error = JalaliDateError.None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return ValidationIssue.DateRequired;
        }

        if (!dates.TryParseDate(text, out var jalali, out error))
        {
            return ValidationIssue.DateInvalid;
        }

        date = dates.ToGregorian(jalali);
        return ValidationIssue.None;
    }

    private AuditLogEntry Audit(Principal user, DateTime now, string action, string targetType, string targetId, string summary) =>
        new(now, user.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, action, targetType, targetId, summary);
}
