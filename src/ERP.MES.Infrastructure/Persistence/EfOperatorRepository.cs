using ERP.MES.Application.Persistence;
using ERP.MES.Application.Security;
using ERP.MES.Domain.Planning;
using Microsoft.EntityFrameworkCore;

namespace ERP.MES.Infrastructure.Persistence;

/// <summary>Operator persistence on <see cref="ErpMesDbContext"/> (provider-neutral). Each call uses its own short-lived context and one transaction; audit entries are saved with the change.</summary>
public sealed class EfOperatorRepository(IDbContextFactory<ErpMesDbContext> contextFactory) : IOperatorRepository
{
    public async Task<IReadOnlyList<Stored<Operator>>> ListOperatorsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await context.Operators.OrderBy(o => o.Id).ToListAsync(cancellationToken);
        return list.Select(o => new Stored<Operator>(o, context.GetRowVersion(o))).ToList();
    }

    public async Task<Stored<Operator>> AddOperatorAsync(Operator entity, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(audit);
        RequireNew(entity.Id);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        context.Operators.Add(entity);
        context.AuditLog.AddRange(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<Operator>(entity, context.GetRowVersion(entity));
    }

    public async Task<Stored<Operator>> UpdateOperatorAsync(long operatorId, long expectedVersion, Action<Operator> change, IReadOnlyList<AuditLogEntry>? audit = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.Operators.SingleOrDefaultAsync(o => o.Id == operatorId, cancellationToken) ?? throw new RecordNotFoundException("operator", operatorId);
        PersistenceSave.CheckExpectedVersion(context, entity, expectedVersion);
        change(entity);
        if (audit is not null)
        {
            context.AuditLog.AddRange(audit);
        }

        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<Operator>(entity, context.GetRowVersion(entity));
    }

    public async Task<LinkedUserInfo?> FindUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await context.Users.Where(u => u.Id == userId).Select(u => new { u.Id, u.DisplayName, u.IsActive }).SingleOrDefaultAsync(cancellationToken);
        return user is null ? null : new LinkedUserInfo(user.Id, user.DisplayName, user.IsActive);
    }

    public async Task<IReadOnlyList<Stored<OperatorMachineAssignment>>> ListAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await context.OperatorMachineAssignments.OrderBy(a => a.Id).ToListAsync(cancellationToken);
        return list.Select(a => new Stored<OperatorMachineAssignment>(a, context.GetRowVersion(a))).ToList();
    }

    public async Task<Stored<OperatorMachineAssignment>> AddAssignmentAsync(OperatorMachineAssignment assignment, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(audit);
        RequireNew(assignment.Id);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // The "one open assignment per operator and machine" rule is checked in the saving context too, so a concurrent duplicate is seen here.
        if (await context.OperatorMachineAssignments.AnyAsync(a => a.OperatorId == assignment.OperatorId && a.MachineId == assignment.MachineId && a.EffectiveTo == null, cancellationToken))
        {
            throw new ConcurrencyConflictException("An open assignment of this operator to this machine already exists. Nothing was saved; reload and try again.");
        }

        context.OperatorMachineAssignments.Add(assignment);
        context.AuditLog.AddRange(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<OperatorMachineAssignment>(assignment, context.GetRowVersion(assignment));
    }

    public async Task<Stored<OperatorMachineAssignment>> UpdateAssignmentAsync(
        long assignmentId, long expectedVersion, Action<OperatorMachineAssignment> change, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.OperatorMachineAssignments.SingleOrDefaultAsync(a => a.Id == assignmentId, cancellationToken) ?? throw new RecordNotFoundException("operator assignment", assignmentId);
        PersistenceSave.CheckExpectedVersion(context, entity, expectedVersion);
        change(entity);
        context.AuditLog.AddRange(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<OperatorMachineAssignment>(entity, context.GetRowVersion(entity));
    }

    public async Task<IReadOnlyList<Stored<OperatorMachineQualification>>> ListQualificationsAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var list = await context.OperatorMachineQualifications.OrderBy(q => q.Id).ToListAsync(cancellationToken);
        return list.Select(q => new Stored<OperatorMachineQualification>(q, context.GetRowVersion(q))).ToList();
    }

    public async Task<Stored<OperatorMachineQualification>> AddQualificationAsync(OperatorMachineQualification qualification, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(qualification);
        ArgumentNullException.ThrowIfNull(audit);
        RequireNew(qualification.Id);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await context.OperatorMachineQualifications.AnyAsync(q => q.OperatorId == qualification.OperatorId && q.MachineId == qualification.MachineId && q.QualifiedTo == null, cancellationToken))
        {
            throw new ConcurrencyConflictException("An open qualification of this operator on this machine already exists. Nothing was saved; reload and try again.");
        }

        context.OperatorMachineQualifications.Add(qualification);
        context.AuditLog.AddRange(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<OperatorMachineQualification>(qualification, context.GetRowVersion(qualification));
    }

    public async Task<Stored<OperatorMachineQualification>> UpdateQualificationAsync(
        long qualificationId, long expectedVersion, Action<OperatorMachineQualification> change, IReadOnlyList<AuditLogEntry> audit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(audit);
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await context.OperatorMachineQualifications.SingleOrDefaultAsync(q => q.Id == qualificationId, cancellationToken) ?? throw new RecordNotFoundException("operator qualification", qualificationId);
        PersistenceSave.CheckExpectedVersion(context, entity, expectedVersion);
        change(entity);
        context.AuditLog.AddRange(audit);
        await PersistenceSave.SaveAsync(context, cancellationToken);
        return new Stored<OperatorMachineQualification>(entity, context.GetRowVersion(entity));
    }

    private static void RequireNew(long id)
    {
        if (id != 0)
        {
            throw new ArgumentException("The record already has an id; use the update method to change a stored record.");
        }
    }
}
