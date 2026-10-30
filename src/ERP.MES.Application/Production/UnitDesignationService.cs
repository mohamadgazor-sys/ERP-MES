using ERP.MES.Application.Persistence;
using ERP.MES.Application.Requests;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Identity;
using ERP.MES.Domain.Production;

namespace ERP.MES.Application.Production;

/// <summary>One designation row for display: who is designated to which unit (active or withdrawn).</summary>
public sealed record DesignationRow(long DesignationId, long UserId, string UserDisplayName, long UnitId, string UnitCode, bool IsActive, long Version, bool IsSupervisor = false);

public sealed record DesignationOverview(IReadOnlyList<DesignationRow> Rows, IReadOnlyList<ProductionUserInfo> ProductionUsers, IReadOnlyList<UnitInfo> Units, bool CanManage, bool CanAssign = false);

/// <summary>
/// Designates Production users to production units (Decision Log #99): a Production user reads and acts on the queues of the units they are designated to and no others (owner decision D8:
/// "Production reads own unit queue"). Since Decision Log #122 the row is the user's operational WORKING UNIT: Program Admin assigns and withdraws it (<see cref="Permission.AssignWorkingUnits"/>)
/// and the Production Manager marks the supervisor of a unit (<see cref="Permission.ManageUnitDesignations"/>); neither is a department or role permission. Designations are never deleted: a
/// withdrawal keeps the row, who designated and who withdrew. Every change is audited and uses the row version.
/// </summary>
public sealed class UnitDesignationService(IUnitQueueRepository store, IPlanningMasterRepository planning, CurrentAuthorization authorization, IClock clock, IAuditContext auditContext)
{
    public async Task<Outcome<DesignationOverview>> LoadAsync(Principal? principal, CancellationToken cancellationToken = default)
    {
        var set = await authorization.EvaluateAsync(principal, [Permission.ManageUnitDesignations, Permission.AssignWorkingUnits, Permission.ViewUnitQueue], cancellationToken);
        var canSeeAll = set.Has(Permission.ManageUnitDesignations) || set.Has(Permission.AssignWorkingUnits);
        if (!canSeeAll && !set.Has(Permission.ViewUnitQueue))
        {
            return Outcome<DesignationOverview>.Fail(OperationStatus.NotAuthorized);
        }

        return await CustomerService.GuardAsync(async () =>
        {
            var users = (await store.ListProductionUsersAsync(cancellationToken)).ToDictionary(u => u.UserId);
            var units = (await planning.ListUnitsAsync(cancellationToken)).ToDictionary(u => u.Value.Id);
            var rows = (await store.ListDesignationsAsync(cancellationToken))
                .Where(d => canSeeAll || d.Value.UserId == set.Current!.UserId)
                .Where(d => users.ContainsKey(d.Value.UserId) && units.ContainsKey(d.Value.ProductionUnitId))
                .Select(d => new DesignationRow(d.Value.Id, d.Value.UserId, users[d.Value.UserId].DisplayName, d.Value.ProductionUnitId, units[d.Value.ProductionUnitId].Value.Code, d.Value.IsActive, d.Version, d.Value.IsSupervisor))
                .OrderBy(r => r.UnitCode, StringComparer.Ordinal).ThenBy(r => r.UserDisplayName, StringComparer.Ordinal).ToList();
            var canManage = set.Has(Permission.ManageUnitDesignations);
            var canAssign = set.Has(Permission.AssignWorkingUnits);
            return Outcome<DesignationOverview>.Ok(new DesignationOverview(
                rows,
                canSeeAll ? users.Values.Where(u => u.IsActive).OrderBy(u => u.DisplayName, StringComparer.Ordinal).ToList() : [],
                units.Values.Select(u => new UnitInfo(u.Value.Id, u.Value.Code, u.Value.Name, u.Value.IsActive)).OrderBy(u => u.Code, StringComparer.Ordinal).ToList(),
                canManage,
                canAssign));
        });
    }

    public async Task<Outcome<DesignationRow>> AssignAsync(Principal? principal, long userId, long unitId, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.AssignWorkingUnits, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<DesignationRow>.Fail(OperationStatus.NotAuthorized);
        }

        var actor = decision.Current!;
        return await CustomerService.GuardAsync(async () =>
        {
            var user = (await store.ListProductionUsersAsync(cancellationToken)).FirstOrDefault(u => u.UserId == userId);
            if (user is not { IsActive: true })
            {
                return Outcome<DesignationRow>.Invalid(ValidationIssue.DesignationUserInvalid);
            }

            var unit = (await planning.ListUnitsAsync(cancellationToken)).FirstOrDefault(u => u.Value.Id == unitId);
            if (unit is not { Value.IsActive: true })
            {
                return Outcome<DesignationRow>.Invalid(ValidationIssue.QueueUnitInvalid);
            }

            var now = clock.UtcNow;
            var stored = await store.AssignDesignationAsync(userId, unitId, actor.UserId, now,
                Audit(actor, now, AuditActions.UnitDesignationAssigned, userId, $"User {user.Username} designated to unit {unit.Value.Code}"), cancellationToken);
            return Outcome<DesignationRow>.Ok(new DesignationRow(stored.Value.Id, userId, user.DisplayName, unitId, unit.Value.Code, stored.Value.IsActive, stored.Version));
        });
    }

    /// <summary>The Production Manager marks a designated Production user as the supervisor of the unit (or removes the mark). A supervisor reviews the unit's daily job reports (Decision Log #102).</summary>
    public async Task<Outcome<DesignationRow>> SetSupervisorAsync(Principal? principal, long designationId, long expectedVersion, bool isSupervisor, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.ManageUnitDesignations, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<DesignationRow>.Fail(OperationStatus.NotAuthorized);
        }

        var actor = decision.Current!;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var current = (await store.ListDesignationsAsync(cancellationToken)).FirstOrDefault(d => d.Value.Id == designationId);
                if (current is null)
                {
                    return Outcome<DesignationRow>.Fail(OperationStatus.NotFound);
                }

                var user = (await store.ListProductionUsersAsync(cancellationToken)).FirstOrDefault(u => u.UserId == current.Value.UserId);
                var unit = (await planning.ListUnitsAsync(cancellationToken)).FirstOrDefault(u => u.Value.Id == current.Value.ProductionUnitId);
                var now = clock.UtcNow;
                var stored = await store.SetSupervisorAsync(designationId, expectedVersion, isSupervisor,
                    Audit(actor, now, AuditActions.UnitSupervisorChanged, current.Value.UserId, $"User {user?.Username ?? current.Value.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)} is {(isSupervisor ? "now" : "no longer")} supervisor of unit {unit?.Value.Code}"), cancellationToken);
                return Outcome<DesignationRow>.Ok(new DesignationRow(stored.Value.Id, stored.Value.UserId, user?.DisplayName ?? string.Empty, stored.Value.ProductionUnitId, unit?.Value.Code ?? string.Empty, stored.Value.IsActive, stored.Version, stored.Value.IsSupervisor));
            });
        }
        catch (InvalidOperationException)
        {
            return Outcome<DesignationRow>.Fail(OperationStatus.InvalidState);
        }
    }

    public async Task<Outcome<DesignationRow>> RevokeAsync(Principal? principal, long designationId, long expectedVersion, CancellationToken cancellationToken = default)
    {
        var decision = await authorization.CheckAsync(principal, Permission.AssignWorkingUnits, cancellationToken);
        if (!decision.IsAllowed)
        {
            return Outcome<DesignationRow>.Fail(OperationStatus.NotAuthorized);
        }

        var actor = decision.Current!;
        try
        {
            return await CustomerService.GuardAsync(async () =>
            {
                var current = (await store.ListDesignationsAsync(cancellationToken)).FirstOrDefault(d => d.Value.Id == designationId);
                if (current is null)
                {
                    return Outcome<DesignationRow>.Fail(OperationStatus.NotFound);
                }

                var users = await store.ListProductionUsersAsync(cancellationToken);
                var user = users.FirstOrDefault(u => u.UserId == current.Value.UserId);
                var unit = (await planning.ListUnitsAsync(cancellationToken)).FirstOrDefault(u => u.Value.Id == current.Value.ProductionUnitId);
                var now = clock.UtcNow;
                var stored = await store.RevokeDesignationAsync(designationId, expectedVersion, actor.UserId, now,
                    Audit(actor, now, AuditActions.UnitDesignationRevoked, current.Value.UserId, $"User {user?.Username ?? current.Value.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)} withdrawn from unit {unit?.Value.Code}"), cancellationToken);
                return Outcome<DesignationRow>.Ok(new DesignationRow(stored.Value.Id, stored.Value.UserId, user?.DisplayName ?? string.Empty, stored.Value.ProductionUnitId, unit?.Value.Code ?? string.Empty, stored.Value.IsActive, stored.Version));
            });
        }
        catch (InvalidOperationException)
        {
            return Outcome<DesignationRow>.Fail(OperationStatus.InvalidState);
        }
    }

    private AuditLogEntry Audit(Principal actor, DateTime now, string code, long userId, string summary) =>
        new(now, actor.UserId, auditContext.MachineName, auditContext.OsUserName, auditContext.DatabaseLogin, code, "UserUnitDesignation", userId.ToString(System.Globalization.CultureInfo.InvariantCulture), summary);
}
